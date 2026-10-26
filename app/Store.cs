// Valyx Jarvis desktop app: settings on disk. The API key is protected with Windows DPAPI (this Windows user only).
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ValyxJarvis
{
    public sealed class Settings
    {
        public int Version = 4;
        public string Provider = "groq";
        public string KeyProtected = "";   // base64 of DPAPI(key)
        public string BaseUrl = "";        // only used by the "custom" provider (and to override Ollama's port)
        public string Model = "";          // empty = picked from the provider's own list
        public string Language = "auto";   // auto | en | ar : the language Jarvis answers in
        public string SttMode = "auto";    // auto (Whisper when the provider has it) | local (offline recognizer) | off
        public string VoiceName = "";      // a Windows voice; empty = the Windows default voice for the language
        public string VoiceEngine = "ai";  // ai = a human-sounding voice from the provider when it has one, else Windows | windows
        public string AiVoice = "", AiVoiceAr = "";   // empty = the provider's first voice
        public int VoiceRate = 0;          // -10..10
        public int Volume = 80;            // 0..100
        public bool Speak = true;          // Jarvis answers out loud
        public bool Chime = true;          // a small tone when he starts and stops listening
        public bool WakeWord = true;       // always listen for "Jarvis" (default again, owner 2026-10-27: he answers only when named); off = only while the talk key is held
        public string MicName = "";        // empty = Automatic: every mic records while the key is held, the loudest wins
        public string PttKey = "V";        // hold to talk (owner, 2026-10-26); read by the game plugin, so it only counts while the game has focus
        public bool OverlayDot = true;     // the floating dot in the game
        public bool SetupDone = false;

        public string Key
        {
            get
            {
                if (string.IsNullOrEmpty(KeyProtected)) return "";
                try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(KeyProtected), null, DataProtectionScope.CurrentUser)); }
                catch { return ""; } // another Windows user or a copied file: the key is simply gone, never a crash
            }
            set
            {
                value = (value ?? "").Trim();
                KeyProtected = value.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
            }
        }

        public bool Arabic => Language == "ar";

        public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ValyxJarvis");
        public static string File_ => Path.Combine(Dir, "settings.json");

        /// <summary>Brings an older settings file up to date, once: a changed default never reaches an existing file otherwise.</summary>
        public static Settings Migrate(Settings s)
        {
            // Version 2: the owner found an always-listening Jarvis annoying; existing files switch to push-to-talk once.
            if (s.Version < 2) { s.WakeWord = false; s.PttKey = "V"; s.Version = 2; }
            // Version 3: a silent virtual mic was picked by accident (Virtual Desktop on a desktop); Automatic cannot pick a silent one.
            if (s.Version < 3) { s.MicName = ""; s.Version = 3; }
            // Version 4: always listening is the default again, now that only a capture that names Jarvis gets a reply (owner, 2026-10-27).
            if (s.Version < 4) { s.WakeWord = true; s.Version = 4; }
            return s;
        }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(File_))
                {
                    var s = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(File_, Encoding.UTF8));
                    if (s != null) return Migrate(s).Fix();
                }
            }
            catch { /* a broken file starts fresh rather than blocking the app */ }
            return new Settings();
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            var tmp = File_ + ".tmp";
            File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(Fix()), new UTF8Encoding(false));
            if (File.Exists(File_)) File.Delete(File_);
            File.Move(tmp, File_);
        }

        public Settings Fix()
        {
            VoiceRate = Math.Max(-10, Math.Min(10, VoiceRate));
            Volume = Math.Max(0, Math.Min(100, Volume));
            if (Language != "en" && Language != "ar") Language = "auto";
            if (SttMode != "local" && SttMode != "off") SttMode = "auto";
            if (VoiceEngine != "windows") VoiceEngine = "ai";
            if (string.IsNullOrEmpty(PttKey)) PttKey = "V";
            return this;
        }
    }
}