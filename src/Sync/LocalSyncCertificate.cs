using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using DeskBox.Core;

namespace DeskBox.Sync
{
    public static class LocalSyncCertificate
    {
        private class CertificateFile { public string ProtectedPfx = ""; public string Thumbprint = ""; public long CreatedAt; }

        public static X509Certificate2 LoadOrCreate(string dataDirectory)
        {
            var file = Path.Combine(dataDirectory, "local-sync-cert.json");
            var saved = Json.Load<CertificateFile>(file, null);
            if (saved != null && !string.IsNullOrEmpty(saved.ProtectedPfx))
            {
                try
                {
                    var protectedBytes = Convert.FromBase64String(saved.ProtectedPfx);
                    var pfx = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                    var cert = new X509Certificate2(pfx, "", X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet);
                    if (cert.HasPrivateKey && cert.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(7)) return cert;
                    cert.Dispose();
                }
                catch { }
            }
            Directory.CreateDirectory(dataDirectory);
            var created = CreateEphemeral("DeskBox Local Sync", 825);
            var bytes = created.Export(X509ContentType.Pfx);
            Json.Save(file, new CertificateFile { ProtectedPfx = Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)), Thumbprint = created.Thumbprint, CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
            return created;
        }

        public static X509Certificate2 CreateEphemeral(string name, int validDays)
        {
            validDays = Math.Max(1, Math.Min(3650, validDays));
            using (var rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest("CN=" + (string.IsNullOrWhiteSpace(name) ? "DeskBox Local Sync" : name), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                var usages = new OidCollection(); usages.Add(new Oid("1.3.6.1.5.5.7.3.1"));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
                using (var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(validDays)))
                    return new X509Certificate2(created.Export(X509ContentType.Pfx), "", X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet);
            }
        }
    }
}
