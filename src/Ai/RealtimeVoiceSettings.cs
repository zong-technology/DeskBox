using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using DeskBox.Core;

namespace DeskBox.Ai
{
    internal sealed class RealtimeVoiceSettings
    {
        public string Provider = "gemini";
        public string Endpoint = GeminiLiveVoiceProvider.DefaultEndpoint;
        public string Model = "gemini-3.1-flash-live-preview";
        public string ApiKey = "";
        public string Appearance = "quantum";
        public bool ReducedMotion;

        public bool Ready {
            get { try { Validate(); return true; } catch (ArgumentException) { return false; } }
        }

        public void Validate()
        {
            if (Provider != "qwen" && Provider != "gemini") throw new ArgumentException("请选择实时语音服务。");
            if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(Model)) throw new ArgumentException("请填写实时模型与密钥。");
            QwenLiveVoiceProvider.Endpoint(Endpoint);
            if (Appearance != "quantum" && Appearance != "silverwing" && Appearance != "photon") throw new ArgumentException("投影外观无效。");
        }
    }

    internal sealed class RealtimeVoiceSettingsStore
    {
        private sealed class Saved
        {
            public Saved() { }
            public string Provider, Endpoint, Model, Secret, Appearance;
            public bool ReducedMotion;
        }
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DeskBox.RealtimeVoice.v1");
        private readonly string path;
        public RealtimeVoiceSettingsStore(string directory = null) { path = Path.Combine(directory ?? AppPaths.DataDir, "realtime-voice.json"); }

        public RealtimeVoiceSettings Load()
        {
            var profile = new RealtimeVoiceSettings();
            if (!File.Exists(path)) return profile;
            try {
                var value = new JavaScriptSerializer().Deserialize<Saved>(File.ReadAllText(path, Encoding.UTF8));
                if (value == null) return profile;
                profile.Provider = value.Provider ?? profile.Provider;
                profile.Endpoint = value.Endpoint ?? profile.Endpoint;
                profile.Model = value.Model ?? profile.Model;
                profile.Appearance = value.Appearance ?? profile.Appearance;
                profile.ReducedMotion = value.ReducedMotion;
                if (!string.IsNullOrEmpty(value.Secret)) {
                    try { profile.ApiKey = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value.Secret), Entropy, DataProtectionScope.CurrentUser)); }
                    catch (CryptographicException) { } // Another Windows user cannot decrypt this profile.
                }
            } catch (IOException) { } catch (ArgumentException) { } catch (InvalidOperationException) { } catch (FormatException) { }
            return profile;
        }

        public void Save(RealtimeVoiceSettings value)
        {
            if (!string.IsNullOrEmpty(value.ApiKey)) value.Validate();
            var saved = new Saved {
                Provider=value.Provider, Endpoint=value.Endpoint, Model=value.Model, Appearance=value.Appearance,
                ReducedMotion=value.ReducedMotion, Secret=string.IsNullOrEmpty(value.ApiKey) ? "" :
                    Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.ApiKey), Entropy, DataProtectionScope.CurrentUser))
            };
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(saved), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
