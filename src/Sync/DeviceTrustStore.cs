using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DeskBox.Core;

namespace DeskBox.Sync
{
    public class PairingTicket { public string Code = ""; public long ExpiresAt; }
    public class DeviceCredential { public string DeviceId = ""; public string AccessToken = ""; public long ExpiresAt; }
    public class TrustedDevice
    {
        public string Id = ""; public string Name = ""; public string Platform = "ios"; public string TokenHash = "";
        public long CreatedAt; public long LastSeenAt; public long ExpiresAt; public bool Revoked;
    }

    /// <summary>One-time pairing and hashed device credentials. Raw tokens are returned once and never persisted.</summary>
    public class DeviceTrustStore
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, long> _pairing = new Dictionary<string, long>(StringComparer.Ordinal);
        private List<TrustedDevice> _devices;
        private string FilePath { get { return Path.Combine(AppPaths.DataDir, "trusted-devices.json"); } }

        public DeviceTrustStore() { _devices = Json.Load(FilePath, new List<TrustedDevice>()) ?? new List<TrustedDevice>(); }
        public List<TrustedDevice> Devices { get { lock (_lock) return _devices.Select(Clone).ToList(); } }

        public PairingTicket CreatePairingTicket(int validMinutes)
        {
            validMinutes = Math.Max(1, Math.Min(30, validMinutes));
            var code = RandomDigits(6); var expires = Now() + validMinutes * 60000L;
            lock (_lock) { RemoveExpiredPairings(); _pairing[Hash(code)] = expires; }
            return new PairingTicket { Code = code, ExpiresAt = expires };
        }

        public DeviceCredential CompletePairing(string code, string deviceName, string platform, int tokenDays)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(deviceName)) return null;
            lock (_lock)
            {
                var key = Hash(code.Trim()); long expires;
                if (!_pairing.TryGetValue(key, out expires) || expires < Now()) { _pairing.Remove(key); return null; }
                _pairing.Remove(key);
                tokenDays = Math.Max(1, Math.Min(365, tokenDays));
                var token = RandomToken(); var device = new TrustedDevice
                {
                    Id = "dev_" + Guid.NewGuid().ToString("N"), Name = deviceName.Trim(), Platform = string.IsNullOrWhiteSpace(platform) ? "ios" : platform.Trim(),
                    TokenHash = Hash(token), CreatedAt = Now(), LastSeenAt = Now(), ExpiresAt = Now() + tokenDays * 86400000L
                };
                _devices.Add(device); Save(); return new DeviceCredential { DeviceId = device.Id, AccessToken = token, ExpiresAt = device.ExpiresAt };
            }
        }

        public bool IsPairingValid(PairingTicket ticket)
        {
            if (ticket == null) return false;
            lock (_lock) { long expires; return _pairing.TryGetValue(Hash(ticket.Code), out expires) && expires > Now(); }
        }

        public void CancelPairing(PairingTicket ticket)
        {
            if (ticket != null) lock (_lock) _pairing.Remove(Hash(ticket.Code));
        }

        public void CancelAllPairings() { lock (_lock) _pairing.Clear(); }

        public bool Validate(string deviceId, string token)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(token)) return false;
            lock (_lock)
            {
                var d = _devices.FirstOrDefault(x => x.Id == deviceId);
                if (d == null || d.Revoked || d.ExpiresAt < Now() || !FixedEquals(d.TokenHash, Hash(token))) return false;
                d.LastSeenAt = Now(); Save(); return true;
            }
        }

        public DeviceCredential Refresh(string deviceId, string currentToken, int tokenDays)
        {
            if (!Validate(deviceId, currentToken)) return null;
            lock (_lock)
            {
                var d = _devices.First(x => x.Id == deviceId); var token = RandomToken(); tokenDays = Math.Max(1, Math.Min(365, tokenDays));
                d.TokenHash = Hash(token); d.ExpiresAt = Now() + tokenDays * 86400000L; d.LastSeenAt = Now(); Save();
                return new DeviceCredential { DeviceId = d.Id, AccessToken = token, ExpiresAt = d.ExpiresAt };
            }
        }
        public long ExtendTrust(string deviceId, string token)
        {
            lock (_lock)
            {
                if (!Validate(deviceId, token)) return 0;
                var device = _devices.First(x => x.Id == deviceId);
                device.ExpiresAt = Math.Max(device.ExpiresAt, Now() + 30L * 86400000L); Save();
                return device.ExpiresAt;
            }
        }

        public void Revoke(string deviceId) { lock (_lock) { var d = _devices.FirstOrDefault(x => x.Id == deviceId); if (d != null) { d.Revoked = true; d.TokenHash = ""; Save(); } } }
        public void Remove(string deviceId) { lock (_lock) { _devices.RemoveAll(x => x.Id == deviceId); Save(); } }

        private void RemoveExpiredPairings() { var now = Now(); foreach (var key in _pairing.Where(x => x.Value < now).Select(x => x.Key).ToList()) _pairing.Remove(key); }
        private void Save() { Json.Save(FilePath, _devices); }
        private static long Now() { return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }
        private static string RandomDigits(int count) { var sb = new StringBuilder(count); var bytes = new byte[16]; using (var rng = RandomNumberGenerator.Create()) while (sb.Length < count) { rng.GetBytes(bytes); foreach (var b in bytes) { if (b < 250) sb.Append((char)('0' + b % 10)); if (sb.Length == count) break; } } return sb.ToString(); }
        private static string RandomToken() { var bytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        private static string Hash(string value) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""))); }
        private static bool FixedEquals(string a, string b) { if (a == null || b == null || a.Length != b.Length) return false; int diff = 0; for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i]; return diff == 0; }
        private static TrustedDevice Clone(TrustedDevice x) { return new TrustedDevice { Id=x.Id, Name=x.Name, Platform=x.Platform, TokenHash=x.TokenHash, CreatedAt=x.CreatedAt, LastSeenAt=x.LastSeenAt, ExpiresAt=x.ExpiresAt, Revoked=x.Revoked }; }
    }
}
