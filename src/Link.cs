// Valyx Jarvis plugin: the link to the helper program (speech, voice, AI calls), through small files in
// %LOCALAPPDATA%\ValyxJarvis\link. All file work happens on one background thread; the game thread only reads snapshots.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ValyxJarvis
{
    /// <summary>A small JSON reader and writer (the game's Mono has no JavaScriptSerializer). No Unity calls, so tests run it outside the game.</summary>
    public static class Json
    {
        public static object Parse(string s) { int i = 0; var v = Value(s, ref i); return v; }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("end of text");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++;
                Ws(s, ref i); if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i); var k = Str(s, ref i); Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException("expected :"); i++;
                    d[k] = Value(s, ref i); Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    throw new FormatException("expected , or }");
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++;
                Ws(s, ref i); if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i)); Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return l; }
                    throw new FormatException("expected , or ]");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (st == i) throw new FormatException("unexpected '" + c + "'");
            var num = s.Substring(st, i - st);
            long n; if (long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return double.Parse(num, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static string Str(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') throw new FormatException("expected a string");
            var sb = new StringBuilder(); i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break; case 't': sb.Append('\t'); break; case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break; case 'f': sb.Append('\f'); break;
                    case 'u': if (i + 4 > s.Length) throw new FormatException("bad \\u"); sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("unterminated string");
        }

        public static string Write(object v)
        {
            var sb = new StringBuilder(); W(sb, v); return sb.ToString();
        }

        static void W(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { Q(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is int || v is long) { sb.Append(Convert.ToInt64(v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is float || v is double) { sb.Append(Convert.ToDouble(v).ToString("R", CultureInfo.InvariantCulture)); return; }
            var d = v as IDictionary<string, object>;
            if (d != null)
            {
                sb.Append('{'); bool first = true;
                foreach (var kv in d) { if (!first) sb.Append(','); first = false; Q(sb, kv.Key); sb.Append(':'); W(sb, kv.Value); }
                sb.Append('}'); return;
            }
            var l = v as System.Collections.IEnumerable;
            if (l != null) { sb.Append('['); bool first = true; foreach (var x in l) { if (!first) sb.Append(','); first = false; W(sb, x); } sb.Append(']'); return; }
            Q(sb, v.ToString());
        }

        static void Q(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            sb.Append('"');
        }

        // Typed reads with defaults, for snapshots.
        public static string S(IDictionary<string, object> d, string k, string def = "") { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : def; }
        public static bool B(IDictionary<string, object> d, string k, bool def = false) { object v; return d != null && d.TryGetValue(k, out v) && v is bool ? (bool)v : def; }
        public static long N(IDictionary<string, object> d, string k, long def = 0) { object v; return d != null && d.TryGetValue(k, out v) && (v is long || v is double) ? Convert.ToInt64(v) : def; }
        public static IDictionary<string, object> O(IDictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) ? v as IDictionary<string, object> : null; }
        public static List<object> L(IDictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) ? v as List<object> : null; }
    }

    /// <summary>One action the helper asked the game to do.</summary>
    public sealed class GameAction { public string Id, Type, Value, Say; }

    public sealed class Link : IDisposable
    {
        public readonly string Dir, ToGame, ToApp, StateFile, HelperDir;
        public volatile IDictionary<string, object> State;     // the helper's latest state.json, parsed
        public volatile string Problem = "";                   // why the helper is not there, for the overlay
        public volatile string GameSnapshot;                   // game.json: the room as the player sees it (counts, names, mode), set by the plugin
        string writtenSnapshot;
        readonly Queue<GameAction> actions = new Queue<GameAction>();
        readonly Queue<KeyValuePair<string, string>> outbox = new Queue<KeyValuePair<string, string>>();
        readonly Action<string> log;
        readonly int gamePid = Process.GetCurrentProcess().Id;
        readonly Thread worker;
        volatile bool run = true;
        DateTime stateTime = DateTime.MinValue, lastLaunch = DateTime.MinValue;
        int launches; long sent;

        public Link(Action<string> logger)
        {
            log = logger;
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ValyxJarvis");
            Dir = Path.Combine(root, "link"); ToGame = Path.Combine(Dir, "to-game"); ToApp = Path.Combine(Dir, "to-app");
            StateFile = Path.Combine(Dir, "state.json"); HelperDir = Path.Combine(root, "helper");
            worker = new Thread(Loop) { IsBackground = true, Name = "ValyxJarvis link" };
            worker.Start();
        }

        /// <summary>True when the helper wrote its heartbeat in the last few seconds.</summary>
        public bool Alive
        {
            get
            {
                var st = State; if (st == null) return false;
                long t = Json.N(st, "t");
                return Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t) < 5000 && Json.S(st, "state") != "closed";
            }
        }

        public GameAction NextAction() { lock (actions) return actions.Count > 0 ? actions.Dequeue() : null; }

        /// <summary>Queue a message for the helper (typed text, push-to-talk, settings, test). Written by the worker thread.</summary>
        public void Send(string type, object value)
        {
            var msg = new Dictionary<string, object> { { "type", type } };
            if (value is IDictionary<string, object>) msg["data"] = value; else msg["value"] = value ?? "";
            var name = DateTime.UtcNow.Ticks.ToString("D19") + "-" + Interlocked.Increment(ref sent) + ".json";
            lock (outbox) outbox.Enqueue(new KeyValuePair<string, string>(name, Json.Write(msg)));
        }

        void Loop()
        {
            try { Directory.CreateDirectory(ToGame); Directory.CreateDirectory(ToApp); } catch (Exception e) { Problem = "link folder: " + e.Message; }
            try { foreach (var f in Directory.GetFiles(ToGame, "*.json")) File.Delete(f); } catch { }   // nothing from an older session runs now
            EnsureHelper();
            while (run)
            {
                try
                {
                    ReadState();
                    ReadActions();
                    Flush();
                    var snap = GameSnapshot;
                    if (snap != null && snap != writtenSnapshot)
                    {
                        var path = Path.Combine(Dir, "game.json");
                        File.WriteAllText(path + ".tmp", snap, new UTF8Encoding(false));
                        if (File.Exists(path)) File.Delete(path);
                        File.Move(path + ".tmp", path);
                        writtenSnapshot = snap;
                    }
                    if (!Alive && (DateTime.UtcNow - lastLaunch).TotalSeconds > 8) EnsureHelper();
                }
                catch (Exception e) { log("link: " + e.Message); }
                Thread.Sleep(120);
            }
        }

        void ReadState()
        {
            if (!File.Exists(StateFile)) return;
            var t = File.GetLastWriteTimeUtc(StateFile);
            if (t == stateTime) return;
            string text;
            try { text = File.ReadAllText(StateFile, Encoding.UTF8); } catch (IOException) { return; }   // mid-rename: next round
            try { State = Json.Parse(text) as IDictionary<string, object>; stateTime = t; Problem = ""; }
            catch (FormatException) { }
        }

        void ReadActions()
        {
            string[] files;
            try { files = Directory.GetFiles(ToGame, "*.json"); } catch { return; }
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var f in files)
            {
                try
                {
                    var d = Json.Parse(File.ReadAllText(f, Encoding.UTF8)) as IDictionary<string, object>;
                    File.Delete(f);
                    if (d == null) continue;
                    lock (actions) actions.Enqueue(new GameAction { Id = Json.S(d, "id"), Type = Json.S(d, "type"), Value = Json.S(d, "value"), Say = Json.S(d, "say") });
                }
                catch (IOException) { }               // still being written: next round
                catch (FormatException) { try { File.Delete(f); } catch { } }
            }
        }

        void Flush()
        {
            while (true)
            {
                KeyValuePair<string, string> m;
                lock (outbox) { if (outbox.Count == 0) return; m = outbox.Dequeue(); }
                var path = Path.Combine(ToApp, m.Key);
                File.WriteAllText(path + ".tmp", m.Value, new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(path + ".tmp", path);
            }
        }

        // ---- the helper program ------------------------------------------------------------------------
        /// <summary>Unpacks the helper embedded in this DLL (when it changed) and starts it. At most a few tries per session.</summary>
        void EnsureHelper()
        {
            lastLaunch = DateTime.UtcNow;
            if (launches >= 4) { Problem = "the helper keeps stopping (see %APPDATA%\\ValyxJarvis\\error.log)"; return; }
            try
            {
                var exe = Path.Combine(HelperDir, "ValyxJarvis.exe");
                byte[] bytes;
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ValyxJarvis.Helper.exe"))
                {
                    if (s == null) { if (!File.Exists(exe)) { Problem = "this build has no helper inside"; return; } bytes = null; }
                    else { bytes = new byte[s.Length]; int r = 0; while (r < bytes.Length) r += s.Read(bytes, r, bytes.Length - r); }
                }
                if (bytes != null)
                {
                    string hash;
                    using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                    var stamp = Path.Combine(HelperDir, "stamp.txt");
                    if (!File.Exists(exe) || !File.Exists(stamp) || File.ReadAllText(stamp).Trim() != hash)
                    {
                        Directory.CreateDirectory(HelperDir);
                        if (File.Exists(stamp)) File.Delete(stamp);
                        // A helper from an older version may still run and lock its exe: ask it to quit first.
                        if (File.Exists(exe)) { Send("quit", ""); Flush(); for (int i = 0; i < 30 && IsLocked(exe); i++) Thread.Sleep(100); }
                        File.WriteAllBytes(exe + ".new", bytes);
                        if (File.Exists(exe)) File.Delete(exe);
                        File.Move(exe + ".new", exe);
                        File.WriteAllText(stamp, hash);   // last, so a half-done unpack is redone next time
                        log("helper unpacked (" + bytes.Length / 1024 + " KB)");
                    }
                }
                var psi = new ProcessStartInfo(exe, "--game-pid " + gamePid) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = HelperDir };
                Process.Start(psi);
                launches++;
                log("helper started (try " + launches + ")");
            }
            catch (Exception e)
            {
                Problem = "could not start the helper: " + e.Message + " (antivirus?)";
                log(Problem);
            }
        }

        static bool IsLocked(string path)
        {
            try { using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return false; }
            catch (IOException) { return true; }
            catch { return false; }
        }

        // The helper is NOT asked to quit here: a hot reload would kill and restart it every time. It quits by itself when the
        // game closes, and EnsureHelper asks it to quit only when a new version replaces its exe.
        public void Dispose() { run = false; }
    }
}