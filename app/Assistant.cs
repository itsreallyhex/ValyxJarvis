// Valyx Jarvis desktop app: the assistant. Ties hearing, the offline grammar, the AI provider, speech and the link to the game together.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ValyxJarvis
{
    /// <summary>Files the game plugin and this app pass to each other (in %LOCALAPPDATA%\ValyxJarvis\link).
    /// Files, not pipes: nothing to connect, either side can start first, and a crash leaves nothing hanging.</summary>
    public sealed class Link
    {
        public readonly string Dir, ToGame, ToApp, StateFile;
        long n;
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public Link(string dir)
        {
            Dir = dir; ToGame = Path.Combine(dir, "to-game"); ToApp = Path.Combine(dir, "to-app"); StateFile = Path.Combine(dir, "state.json");
            Directory.CreateDirectory(ToGame); Directory.CreateDirectory(ToApp);
        }

        static void WriteAtomic(string path, string text)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public void PublishState(Dictionary<string, object> state)
        {
            try { WriteAtomic(StateFile, Json.Serialize(state)); } catch { /* the game may be reading it: the next heartbeat rewrites it */ }
        }

        /// <summary>One small file per action; names sort by time so they run in order. Returns the action's id (the game reports back with it).</summary>
        public string SendAction(JAction a, string say)
        {
            var id = Guid.NewGuid().ToString("N").Substring(0, 12);
            var name = DateTime.UtcNow.Ticks.ToString("D19") + "-" + Interlocked.Increment(ref n) + ".json";
            try { WriteAtomic(Path.Combine(ToGame, name), Json.Serialize(new Dictionary<string, object> { { "id", id }, { "type", a.Type }, { "value", a.Value ?? "" }, { "say", say ?? "" } })); } catch { }
            return id;
        }

        /// <summary>The room as the game last described it (game.json), or null when the game has not written it for a while.</summary>
        public Dictionary<string, object> Game()
        {
            try
            {
                var path = Path.Combine(Dir, "game.json");
                if (!File.Exists(path) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalSeconds > 15) return null;
                return Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch { return null; }
        }

        /// <summary>Reads and removes what the plugin left for the app: typed text, push-to-talk, results.</summary>
        public List<Dictionary<string, object>> ReadInbox()
        {
            var list = new List<Dictionary<string, object>>();
            try
            {
                foreach (var f in Directory.GetFiles(ToApp, "*.json").OrderBy(x => x, StringComparer.Ordinal))
                {
                    try
                    {
                        if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalSeconds < 120)
                            list.Add(Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(f, Encoding.UTF8)));
                    }
                    catch { }
                    try { File.Delete(f); } catch { }
                }
                // Actions the plugin never picked up (game closed) must not run later, in a different match.
                foreach (var f in Directory.GetFiles(ToGame, "*.json"))
                    if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalSeconds > 30) try { File.Delete(f); } catch { }
            }
            catch { }
            return list;
        }
    }

    public sealed class Assistant : IDisposable
    {
        public Settings S;
        public readonly Link Link;
        readonly Func<IAudioSource> source;   // the microphone; tests pass a WAV file instead
        public event Action<string> Log;                 // lines for the settings window
        public event Action<float> Level;
        public string State = "idle", Heard = "", Said = "", Note = "";
        public string MicError;

        readonly Speaker speaker;
        Hearing hearing;
        readonly List<Api.Turn> history = new List<Api.Turn>();
        readonly SemaphoreSlim busy = new SemaphoreSlim(1, 1);
        readonly System.Threading.Timer tick;
        string model = "", testResult = "";
        List<string> models = new List<string>();
        readonly List<string> voices = Speaker.Voices(), mics = MicSource.Devices();
        long seq, sayId; volatile bool disposed;
        readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<Dictionary<string, object>>> pending =
            new System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<Dictionary<string, object>>>();
        public bool Persist = true;          // tests turn this off so they never touch the player's settings file
        public volatile bool QuitRequested;


        public Assistant(string linkDir, Settings settings = null, Func<IAudioSource> audio = null)
        {
            S = settings ?? Settings.Load();
            source = audio ?? (() => new MicSource(S.MicName));
            Link = new Link(linkDir);
            // the game's own list of maps (spawn points), so "take me to <anything the game has>" works, also after game updates
            LocalGrammar.MapCatalog = () =>
            {
                var g = Link.Game(); object v;
                var arr = g != null && g.TryGetValue("maps", out v) ? v as System.Collections.IEnumerable : null;
                if (arr == null) return null;
                var list = new List<string>(); foreach (var o in arr) { var n = Convert.ToString(o); if (n.Length > 0) list.Add(n); }
                return list.Count > 0 ? list : null;
            };
            speaker = new Speaker(S);
            speaker.Log = m => Say("voice: " + m);
            tick = new System.Threading.Timer(_ => Tick(), null, 500, 100);
        }

        public Speaker Speaker { get { return speaker; } }

        // ---- text the player sees and hears ---------------------------------------------------
        static readonly Dictionary<string, string[]> Msg = new Dictionary<string, string[]>
        {
            { "nokey",  new[] { "I need a free API key for that. Press F8 to add one.", "أحتاج مفتاح API مجاني لهذا. اضغط F8 وأضفه." } },
            { "hint",   new[] { "Try: Jarvis, make it night. Or add a free key with F8 to chat.", "جرب: Jarvis, make it night. أو أضف مفتاح مجاني من F8 للسوالف." } },
            { "badkey", new[] { "That key was refused. Check it in my settings.", "المفتاح مرفوض. تأكد منه في الإعدادات." } },
            { "limit",  new[] { "I've hit the free limit for a moment. Give me a minute and ask again!", "وصلت الحد المجاني شوي، عطني دقيقة واسألني مرة ثانية!" } },
            { "net",    new[] { "I can't reach the internet right now, but time, weather and try-ons still work.", "ما قدرت أوصل للإنترنت، بس الوقت والطقس والتجربة تشتغل." } },
            { "server", new[] { "The AI service is busy. Try again.", "الخدمة مشغولة، جرب مرة ثانية." } },
            { "bad",    new[] { "I didn't understand the service's answer.", "ما فهمت رد الخدمة." } },
            { "model",  new[] { "The AI service refused that model. Pick another one with F8.", "الخدمة رفضت هذا النموذج. اختر غيره من F8." } },
            { "didnt",  new[] { "Sorry, I didn't quite catch that. Say it once more?", "ما سمعتك زين، عيدها علي؟" } },
            { "done",   new[] { "Done.", "تم." } },
            { "yes",    new[] { "Yes?", "هلا؟" } },
            { "cantHear", new[] { "I can't hear you. Check your microphone in F8.", "ما أسمعك. تأكد من المايك في F8." } },
            { "nomic",  new[] { "I can't find a microphone. Type to me, or press the push-to-talk key.", "ما لقيت مايك. اكتب لي أو اضغط زر التحدث." } },
        };

        public string T(string key, bool ar) { return Msg[key][ar ? 1 : 0]; }

        static bool HasArabic(string s) { return s != null && s.Any(c => c >= 0x0600 && c <= 0x06FF); }
        bool IsArabic(string text) { return S.Language == "ar" || (S.Language == "auto" && HasArabic(text)); }

        void Say(string line) { var h = Log; if (h != null) h(DateTime.Now.ToString("HH:mm:ss") + "  " + line); }

        // ---- start / stop -----------------------------------------------------------------------
        public void Start() { StartHearing(); Publish(); ProbeVoices(); }

        /// <summary>Learn early which languages' human voice needs its terms accepted (the game shows a popup for those).</summary>
        void ProbeVoices()
        {
            var langs = S.Language == "ar" ? new[] { true } : S.Language == "en" ? new[] { false } : new[] { false, true };
            Task.Run(async () => { foreach (var ar in langs) await speaker.Probe(ar).ConfigureAwait(false); Publish(); });
        }

        public void Apply()
        {
            // Called after the settings window saved: a changed provider or key means a new model choice.
            model = ""; refused.Clear(); if (Persist) S.Save(); speaker.Stop();
            StartHearing(); Publish();
        }

        Hearing pttHearing;   // push-to-talk with one chosen mic: a Hearing that exists while the key is held
        MultiMicCapture multi; // push-to-talk with the Automatic mic: every mic records while the key is held
        public const double SilentRms = 250;   // below this the capture is silence (a speaking voice measured in the thousands)

        void StartHearing()
        {
            var old = hearing; hearing = null;
            if (old != null) old.Dispose();
            MicError = null; Note = "";
            // Without the wake word nothing listens until the talk key is pressed (owner, 2026-10-26: "not always listening").
            if (S.WakeWord) hearing = MakeHearing(true);
        }

        Hearing MakeHearing(bool wake)
        {
            var h = new Hearing(source(), wake);
            h.OfflineCommand += text => Run(() => Handle(text, true));
            h.ConfirmCommands = CloudStt;
            h.Utterance += (wav, why) => Run(() => HandleUtterance(wav, why));
            // Listening off without an utterance (a tap too short to hold words) must not leave the dot on "thinking".
            h.Listening += on => { if (on) { Heard = ""; speaker.Chime(true); SetState("listening"); } else SetState("idle"); };
            h.Level += l => { var ev = Level; if (ev != null) ev(l); };
            h.Trace += m => Say("[ear] " + m);
            if (h.Start()) return h;
            MicError = h.Error ?? "no microphone"; Note = T("nomic", S.Arabic); Say("microphone: " + MicError); h.Dispose();
            Publish();
            return null;
        }

        void PttDown()
        {
            speaker.Stop();   // pressing the talk key while Jarvis speaks cuts him off
            var h = hearing; if (h != null) { h.Muted = false; h.PttDown(); return; }
            if (pttHearing != null || multi != null) return;
            if (S.MicName.Length == 0)
            {
                var m = MultiMicCapture.Start(MicSource.Devices());
                if (m.Count == 0) { m.Dispose(); MicError = "no microphone could be opened"; Note = T("nomic", S.Arabic); Publish(); return; }
                m.Level += l => { var ev = Level; if (ev != null) ev(l); };
                multi = m; Heard = ""; speaker.Chime(true); SetState("listening");
                return;
            }
            var p = MakeHearing(false); if (p == null) return;
            pttHearing = p; p.PttDown();
        }

        void PttUp()
        {
            var h = hearing; if (h != null) { h.PttUp(); return; }
            var mm = multi;
            if (mm != null)
            {
                multi = null;
                Task.Run(() =>
                {
                    string name, report; double rms;
                    var wav = mm.Finish(out name, out rms, out report);
                    int ms = wav == null ? 0 : (wav.Length - 44) / Wav.BytesPerMs;
                    Say("captured " + ms + " ms; loudest: " + name + " (" + (int)rms + "); all: " + report);
                    if (wav == null || ms < 300) { SetState("idle"); return; }   // a tap, not a request
                    if (rms < SilentRms) { Run(() => Reply(new Reply { Say = T("cantHear", S.Arabic) }, S.Arabic)); return; }
                    Run(() => HandleUtterance(wav, "ptt"));
                });
                return;
            }
            var p = pttHearing; if (p == null) return;
            pttHearing = null;
            p.PttUp();                      // hands over the utterance (its work is queued, not run here)
            Task.Run(() => p.Dispose());    // then the mic closes
        }

        void Run(Func<Task> work)
        {
            Task.Run(async () =>
            {
                if (!await busy.WaitAsync(10000).ConfigureAwait(false)) return;   // one thing at a time; the next waits its turn instead of being lost
                try { await work().ConfigureAwait(false); }
                catch (Exception e) { Say("error: " + e.Message); SetState("idle"); }
                finally { busy.Release(); }
            });
        }

        // ---- the main path ------------------------------------------------------------------------
        async Task HandleUtterance(byte[] wav, string why)
        {
            Heard = ""; SetState("thinking");   // set here, not when listening stops: a too-short tap brings no utterance
            // Silence never goes to Whisper: it answers silence with stock lines ("Thank you.", Arabic subtitle credits).
            double level = -1;
            try { var pcm = Wav.Pcm(wav); level = Wav.Rms(pcm, pcm.Length); } catch { /* the recognizer's own WAV format: no level check */ }
            if (level >= 0 && level < SilentRms) { Say("silent capture (" + (int)level + "), not sent"); await Reply(new Reply { Say = T("cantHear", S.Arabic) }, S.Arabic).ConfigureAwait(false); return; }
            string text = "";
            var p = Providers.ById(S.Provider);
            var key = S.Key;
            if (CloudStt())
            {
                try
                {
                    // No forced language (forcing made mixed Arabic/English worse) and a vocabulary hint; a guess that is neither English nor
                    // Arabic (Arabic was heard as Persian) is retried in the player's language.
                    var clip = Wav.TrimSilence(wav);
                    var h = await Api.Transcribe(p, S, key, clip, null, CancellationToken.None, LocalGrammar.SttPrompt).ConfigureAwait(false);
                    text = h.Text;
                    if (h.Language.Length > 0 && h.Language != "en" && h.Language != "ar")
                    {
                        var retry = S.Language == "en" ? "en" : "ar";
                        Say("speech-to-text guessed " + h.Language + " for: " + text + " -> retrying as " + retry);
                        text = (await Api.Transcribe(p, S, key, clip, retry, CancellationToken.None, LocalGrammar.SttPrompt).ConfigureAwait(false)).Text;
                    }
                }
                catch (ApiException e) { Say("speech-to-text failed (" + e.Kind + "), using the offline recognizer"); }
            }
            if (string.IsNullOrWhiteSpace(text) && S.SttMode != "off") text = LocalStt.Recognize(wav);
            text = (text ?? "").Trim();
            Say("heard: " + text + (level >= 0 ? "  (level " + (int)level + ")" : ""));
            if (LocalGrammar.IsStockLine(text) && level >= 0 && level < 600)   // in game a real "Okay." came in at 882
            { Say("dropped a stock line from quiet audio"); await Reply(new Reply { Say = T("cantHear", S.Arabic) }, S.Arabic).ConfigureAwait(false); return; }
            // The wake word was matched acoustically, which can be wrong; a one-breath phrase must begin with Jarvis in the text too.
            // After a wake too: false wakes brought "Thank you for watching!", "Okay, bro." and replies to them (owner, 2026-10-27: "only
            // reply when hearing Jarvis"). The capture starts with the pre-roll, so a real wake has the name in it.
            if ((why == "phrase" || why == "after-wake") && !LocalGrammar.Mentions(text)) { Say("no Jarvis in what was heard, ignored"); SetState("idle"); return; }
            if (text.Length == 0) { await Reply(new Reply { Say = T("didnt", S.Arabic) }, S.Arabic).ConfigureAwait(false); return; }
            await Handle(text, true).ConfigureAwait(false);
        }

        bool CloudStt()
        {
            return S.SttMode != "local" && S.SttMode != "off" && !string.IsNullOrEmpty(Providers.ById(S.Provider).SttModel) && S.Key.Length > 0;
        }

        public void Submit(string text, bool fromGame = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Run(() => Handle(text.Trim(), false));
        }

        async Task Handle(string text, bool voice)
        {
            SetState("thinking");
            Heard = LocalGrammar.Strip(text); Publish();
            bool ar = IsArabic(text);
            var reply = LocalGrammar.Parse(text, ar);
            // Only "Jarvis": the AI got an empty message and guessed ("All set! ... maybe some weather?", 2026-10-26)
            if (reply == null && Heard.Length == 0) reply = new Reply { Say = T("yes", ar) };
            if (reply == null)
            {
                var p = Providers.ById(S.Provider);
                var key = S.Key;
                // Spoken without a key, offline dictation is too rough to guess from: give an example instead of "add a key".
                if (p.NeedsKey && key.Length == 0) reply = new Reply { Say = T(voice ? "hint" : "nokey", ar) };
                else
                {
                    try { reply = await Ask(p, key, LocalGrammar.Strip(text), ar).ConfigureAwait(false); }
                    catch (ApiException e)
                    {
                        Say("provider: " + e.Kind + " " + e.Message);
                        reply = new Reply { Say = T(e.Kind == ApiFail.KeyRejected ? "badkey" : e.Kind == ApiFail.RateLimited ? "limit" : e.Kind == ApiFail.Network ? "net" : e.Kind == ApiFail.Server ? "server" : e.Message.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0 ? "model" : "bad", ar) };
                    }
                }
            }
            await Reply(reply, ar).ConfigureAwait(false);
        }

        readonly HashSet<string> refused = new HashSet<string>();   // models this provider would not run for this key

        async Task<Reply> Ask(Provider p, string key, string text, bool ar)
        {
            var lang = S.Language == "ar" ? "Arabic" : S.Language == "en" ? "English" : "auto";
            string raw;
            for (int attempt = 0; ; attempt++)
            {
                if (model.Length == 0) model = S.Model.Length > 0 ? S.Model : await PickModel(p, key).ConfigureAwait(false);
                try
                {
                    raw = await Api.Chat(p, S, key, model, Brain.SystemPrompt(lang, GameContext()), history.Skip(Math.Max(0, history.Count - 6)).ToList(), text, CancellationToken.None).ConfigureAwait(false);
                    break;
                }
                // In game: Groq refused a model that needs terms acceptance (400). With "auto", move on to the next model.
                catch (ApiException e) when (e.Kind == ApiFail.BadReply && S.Model.Length == 0 && attempt < 3 && e.Message.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Say("model " + model + " refused, trying the next one");
                    refused.Add(model); model = "";
                }
            }
            var reply = Brain.ParseReply(raw);
            // Leaving a lobby on a guess is the worst mistake he can make: an AI travel runs only when the words asked for one.
            if (!LocalGrammar.AsksTravel(text) && reply.Actions.RemoveAll(a => a.Type == "travel") > 0)
            { Say("dropped a travel the player did not ask for"); reply.Say = T("didnt", ar); }
            history.Add(new Api.Turn { Role = "user", Text = text });
            history.Add(new Api.Turn { Role = "assistant", Text = raw.Length > 400 ? raw.Substring(0, 400) : raw });
            if (history.Count > 12) history.RemoveRange(0, history.Count - 12);
            return reply;
        }

        public async Task<string> PickModel(Provider p, string key)
        {
            var list = await Api.ListModels(p, S, key, CancellationToken.None).ConfigureAwait(false);
            var m = Providers.RankModels(p, list).FirstOrDefault(x => !refused.Contains(x)) ?? p.DefaultModel;
            Say("model: " + m + " (of " + list.Count + ")");
            return m;
        }

        async Task Reply(Reply r, bool ar)
        {
            // Questions about the room are answered here, from what the game wrote (counts and names, never positions).
            var info = r.Actions.Where(a => a.Type == "info").ToList();
            if (info.Count > 0) { r.Say = string.Join(" ", info.Select(a => Info(a.Value, ar))); r.Actions.RemoveAll(a => a.Type == "info"); }
            if (string.IsNullOrEmpty(r.Say) && r.Actions.Count == 0) r.Say = T("didnt", ar);

            // Send the actions; for a try-on wait for what the game really found and say that, not what the AI guessed.
            var waits = new List<Task<Dictionary<string, object>>>();
            foreach (var a in r.Actions)
            {
                var id = Link.SendAction(a, r.Say);
                if (a.Type == "tryon" || a.Type == "tryon_clear" || a.Type == "travel" || a.Type == "menu") waits.Add(Expect(id, 2500));   // say what the game really did
            }
            bool failed = false;
            if (waits.Count > 0)
            {
                var results = await Task.WhenAll(waits).ConfigureAwait(false);
                var texts = results.Where(x => x != null).Select(x => Convert.ToString(x.ContainsKey("text") ? x["text"] : "")).Where(x => x.Length > 0).ToList();
                failed = results.Any(x => x != null && x.ContainsKey("ok") && x["ok"] is bool && !(bool)x["ok"]);
                if (texts.Count > 0) r.Say = string.Join(" ", texts);
            }
            if (string.IsNullOrEmpty(r.Say)) r.Say = T("done", ar);
            Said = r.Say; Interlocked.Increment(ref sayId);
            Say("said: " + r.Say + (r.Actions.Count > 0 ? "   [" + string.Join(", ", r.Actions.Select(a => a.Type + (a.Value.Length > 0 ? " " + a.Value : ""))) + "]" : "") + (r.Local ? "  (offline)" : ""));

            // An offline command that worked needs no words: a short tone, and the panel shows the line.
            // Travel is always said out loud: leaving a lobby must never be a surprise.
            if (r.Local && info.Count == 0 && !failed && r.Actions.Count > 0 && !r.Actions.Any(a => a.Type == "travel"))
            {
                speaker.ChimeDone();
                SetState("idle");
                return;
            }
            SetState("speaking");
            var h = hearing; if (h != null) h.Muted = true;
            try { await speaker.Say(r.Say, ar).ConfigureAwait(false); }
            finally { await Task.Delay(150).ConfigureAwait(false); if (h != null) h.Muted = false; }
            SetState("idle");
        }

        Task<Dictionary<string, object>> Expect(string id, int ms)
        {
            var tcs = new TaskCompletionSource<Dictionary<string, object>>();
            pending[id] = tcs;
            Task.Delay(ms).ContinueWith(_ => { TaskCompletionSource<Dictionary<string, object>> t; if (pending.TryRemove(id, out t)) t.TrySetResult(null); });
            return tcs.Task;
        }

        static string Str(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : ""; }

        static List<string> Names(Dictionary<string, object> g, out bool youHere)
        {
            youHere = false; var list = new List<string>();
            object v; var arr = g != null && g.TryGetValue("names", out v) ? v as System.Collections.IEnumerable : null;
            if (arr != null) foreach (var o in arr) { var n = Convert.ToString(o); if (n.StartsWith("*")) youHere = true; else if (n.Length > 0) list.Add(n); }
            return list;
        }

        /// <summary>An answer about the room. Never where anyone is.</summary>
        public string Info(string kind, bool ar)
        {
            var g = Link.Game();
            if (g == null) return ar ? "ما أشوف اللعبة الحين." : "I can't see the game right now.";
            object inRoom;
            if (kind != "where" && !(g.TryGetValue("inRoom", out inRoom) && inRoom is bool && (bool)inRoom)) return ar ? "أنت مو في روم الحين." : "You're not in a room right now.";
            switch (kind)
            {
                case "players":
                    var n = Str(g, "players");
                    return ar ? "فيه " + n + " لاعبين في الروم." : n + (n == "1" ? " player" : " players") + " in this room.";
                case "who":
                    bool you; var names = Names(g, out you);
                    if (names.Count == 0) return ar ? "أنت لحالك." : "Just you.";
                    var list = names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + (ar ? " و" : " and ") + names.Last();
                    return (ar ? "معك: " : "With you: ") + list + ".";
                case "code":
                    var room = Str(g, "room"); bool priv = Str(g, "private") == "True";
                    return ar ? "كود الروم " + room + (priv ? " (خاص)." : " (عام).") : "Your room code is " + string.Join(" ", room.ToCharArray()) + (priv ? ", a private room." : ", a public room.");
                case "where":
                    var zone = MapName(Str(g, "zone"), ar);
                    return zone.Length == 0 ? (ar ? "ما أعرف وين أنت." : "I can't tell which map you're in.") : (ar ? "أنت في " + zone + "." : "You're in " + zone + ".");
                case "lava":
                case "me":
                    if (Str(g, "tagMode") != "True") return ar ? "هذا المود ما فيه لافا." : "This mode has no lava.";
                    int total, lava; int.TryParse(Str(g, "players"), out total); int.TryParse(Str(g, "lava"), out lava);
                    if (kind == "me") return Str(g, "youLava") == "True" ? (ar ? "إيه، أنت لافا." : "Yes, you're lava.") : (ar ? "لا، أنت سليم." : "No, you're safe.");
                    return ar ? lava + " لافا، و" + Math.Max(0, total - lava) + " باقين." : lava + " lava, " + Math.Max(0, total - lava) + " left.";
                case "mode":
                    var mode = Str(g, "mode");
                    if (mode.Length > 0 && !mode.Any(char.IsLower)) mode = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(mode.ToLowerInvariant());   // "CASUAL" -> "Casual"
                    return mode.Length == 0 ? (ar ? "ما أعرف المود." : "I can't tell the mode.") : (ar ? "المود: " + mode + "." : "This is " + mode + ".");
            }
            return "";
        }

        static readonly Dictionary<string, string[]> MapNames = new Dictionary<string, string[]>
        {
            { "forest", new[] { "the Forest", "الغابة" } }, { "cave", new[] { "the Caves", "الكهوف" } }, { "canyon", new[] { "Canyons", "الكانيون" } },
            { "city", new[] { "the City", "المدينة" } }, { "beach", new[] { "the Beach", "الشاطئ" } }, { "skyJungle", new[] { "the Clouds", "الغيوم" } },
            { "mountain", new[] { "the Mountains", "الجبال" } }, { "basement", new[] { "the Basement", "القبو" } }, { "Metropolis", new[] { "Metropolis", "المترو" } },
            { "arcade", new[] { "the Arcade", "الآركيد" } }, { "bayou", new[] { "the Bayou", "البايو" } }, { "rotating", new[] { "Rotating", "الدوار" } },
            { "critters", new[] { "Critters", "كريترز" } }, { "mall", new[] { "the Mall", "المول" } }, { "spaceMap", new[] { "Space", "الفضاء" } },
            { "hoverboard", new[] { "the Hoverpark", "الهوفر بارك" } }, { "monkeBlocks", new[] { "Monke Blocks", "مونكي بلوكس" } }, { "arena", new[] { "the Arena", "الساحة" } },
            { "ghostReactor", new[] { "Ghost Reactor", "قوست ريأكتور" } }, { "tutorial", new[] { "the Tutorial", "التعليمات" } }, { "ranked", new[] { "Ranked", "الرانكد" } },
        };

        /// <summary>The game's zone name as words ("skyJungle" -> "the Clouds"); an unknown zone from a later update gets its name spaced out.</summary>
        static string MapName(string zone, bool ar)
        {
            if (string.IsNullOrEmpty(zone) || zone == "none") return "";
            string[] n; if (MapNames.TryGetValue(zone, out n)) return n[ar ? 1 : 0];
            return System.Text.RegularExpressions.Regex.Replace(zone, "([a-z])([A-Z])", "$1 $2");
        }

        /// <summary>One line about the room for the AI: room, player count and names, mode. No positions.</summary>
        string GameContext()
        {
            var g = Link.Game();
            if (g == null) return "";
            var where = MapName(Str(g, "zone"), false);
            var maps = LocalGrammar.MapCatalog();
            var mapsLine = maps != null ? " Maps the player can travel to: " + string.Join(", ", maps) + "." : "";
            object inRoom; if (!(g.TryGetValue("inRoom", out inRoom) && inRoom is bool && (bool)inRoom)) return "the player is not in a room" + (where.Length > 0 ? ", on the map " + where : "") + "." + mapsLine;
            bool you; var names = Names(g, out you);
            return (where.Length > 0 ? "map " + where + ", " : "") + (Str(g, "private") == "True" ? "private" : "public") + " room " + Str(g, "room") + ", " + Str(g, "players") + " players (the player and: " +
                   (names.Count == 0 ? "nobody else" : string.Join(", ", names)) + "), game mode " + Str(g, "mode") +
                   (Str(g, "tagMode") == "True" ? ", " + Str(g, "lava") + " are lava (tagged)" + (Str(g, "youLava") == "True" ? " including the player" : "") : "") + "." + mapsLine;
        }
        // ---- state for the game ---------------------------------------------------------------------
        void SetState(string s) { State = s; Publish(); }

        void Publish()
        {
            Link.PublishState(new Dictionary<string, object>
            {
                { "v", 1 }, { "seq", Interlocked.Increment(ref seq) }, { "t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                { "state", MicError != null && State == "idle" ? "nomic" : S.Provider != "ollama" && S.Provider != "custom" && S.Key.Length == 0 && !S.SetupDone ? "setup" : State },
                { "heard", Heard ?? "" }, { "say", Said ?? "" }, { "sayId", Interlocked.Read(ref sayId) }, { "note", Note ?? "" },
                { "lang", S.Language == "ar" ? "ar" : "en" }, { "dot", S.OverlayDot },
                { "cfg", Cfg() }, { "test", testResult }, { "models", models }, { "voices", voices }, { "mics", mics },
                { "arVoice", Speaker.HasVoiceFor("ar") }, { "micError", MicError ?? "" },
                { "aiVoices", Providers.ById(S.Provider).Voices }, { "aiVoicesAr", Providers.ById(S.Provider).VoicesAr },
                { "voiceProblem", speaker.Problem }, { "voiceUrl", speaker.ProblemUrl },
                { "voiceTerms", speaker.TermsNeeded.Select(kv => new Dictionary<string, object> { { "lang", kv.Key }, { "url", kv.Value } }).ToList() },
                { "providers", Providers.All.Select(p => new Dictionary<string, object> { { "id", p.Id }, { "name", p.Name }, { "free", p.Free }, { "note", p.Note ?? "" }, { "noteAr", p.NoteAr ?? "" }, { "keyUrl", p.KeyUrl ?? "" }, { "needsKey", p.NeedsKey }, { "stt", !string.IsNullOrEmpty(p.SttModel) } }).ToList() },
            });
        }

        Dictionary<string, object> Cfg()
        {
            var k = S.Key;
            return new Dictionary<string, object>
            {
                { "provider", S.Provider }, { "hasKey", k.Length > 0 }, { "keyHint", k.Length >= 8 ? k.Substring(0, 4) + "…" + k.Substring(k.Length - 4) : "" },
                { "baseUrl", S.BaseUrl }, { "model", S.Model }, { "language", S.Language }, { "stt", S.SttMode }, { "voice", S.VoiceName },
                { "engine", S.VoiceEngine }, { "aiVoice", S.AiVoice }, { "aiVoiceAr", S.AiVoiceAr },
                { "rate", S.VoiceRate }, { "volume", S.Volume }, { "speak", S.Speak }, { "chime", S.Chime }, { "wake", S.WakeWord },
                { "ptt", S.PttKey }, { "mic", S.MicName }, { "dot", S.OverlayDot }, { "setupDone", S.SetupDone },
            };
        }

        /// <summary>Settings from the game's desktop menu. Only the fields present change; the key arrives once and is stored with DPAPI.</summary>
        public void ApplyRemote(Dictionary<string, object> d)
        {
            Func<string, string> str = k => { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : null; };
            Func<string, bool?> flag = k => { object v; return d.TryGetValue(k, out v) && v is bool ? (bool?)(bool)v : null; };
            Func<string, int?> num = k => { object v; return d.TryGetValue(k, out v) && v is int ? (int?)(int)v : null; };
            string oldMic = S.MicName; bool oldWake = S.WakeWord; string oldLang = S.Language, oldProv = S.Provider, oldEngine = S.VoiceEngine, oldKey = S.KeyProtected;
            var p = str("provider"); if (p != null && Providers.All.Any(x => x.Id == p)) S.Provider = p;
            var key = str("key"); if (key != null) S.Key = key.Length > 200 ? "" : key;
            var b = str("baseUrl"); if (b != null) S.BaseUrl = b.Trim();
            var m = str("model"); if (m != null) S.Model = m.Trim();
            var l = str("language"); if (l != null) S.Language = l;
            var st = str("stt"); if (st != null) S.SttMode = st;
            var vo = str("voice"); if (vo != null) S.VoiceName = vo.Length == 0 || voices.Contains(vo) ? vo : S.VoiceName;
            var en = str("engine"); if (en != null) S.VoiceEngine = en;
            var av = str("aiVoice"); if (av != null) S.AiVoice = av.Length <= 40 ? av : "";
            var ava = str("aiVoiceAr"); if (ava != null) S.AiVoiceAr = ava.Length <= 40 ? ava : "";
            var r = num("rate"); if (r.HasValue) S.VoiceRate = r.Value;
            var v2 = num("volume"); if (v2.HasValue) S.Volume = v2.Value;
            var sp = flag("speak"); if (sp.HasValue) S.Speak = sp.Value;
            var ch = flag("chime"); if (ch.HasValue) S.Chime = ch.Value;
            var w = flag("wake"); if (w.HasValue) S.WakeWord = w.Value;
            var pk = str("ptt"); if (pk != null) S.PttKey = pk;
            var mi = str("mic"); if (mi != null) S.MicName = mi.Length == 0 || mics.Contains(mi) ? mi : S.MicName;
            var dt = flag("dot"); if (dt.HasValue) S.OverlayDot = dt.Value;
            S.SetupDone = true; model = ""; refused.Clear(); testResult = ""; S.Fix();
            if (Persist) S.Save();
            if (S.MicName != oldMic || S.WakeWord != oldWake) StartHearing();
            if (S.Language != oldLang || S.Provider != oldProv || S.VoiceEngine != oldEngine || S.KeyProtected != oldKey) ProbeVoices();
            Say("settings changed in the game");
            Publish();
        }

        int ticks;
        void Tick()
        {
            if (disposed) return;
            try
            {
                // The talk key is read by the game plugin (only while the game has focus) and arrives as "ptt" messages.
                if (++ticks % 2 == 0)   // 200 ms: the talk key must feel immediate
                    foreach (var m in Link.ReadInbox())
                    {
                        object t, v;
                        m.TryGetValue("type", out t); m.TryGetValue("value", out v);
                        var type = t as string; var val = v as string ?? "";
                        if (type == "text") Submit(val, true);
                        else if (type == "ptt") { if (val == "down") PttDown(); else PttUp(); }
                        else if (type == "settings") { object data; if (m.TryGetValue("data", out data) && data is Dictionary<string, object>) ApplyRemote((Dictionary<string, object>)data); }
                        else if (type == "test") Task.Run(async () => { testResult = "…"; Publish(); testResult = await TestConnection(S).ConfigureAwait(false); try { if (testResult.StartsWith("Connected")) models = await Api.ListModels(Providers.ById(S.Provider), S, S.Key, CancellationToken.None).ConfigureAwait(false); } catch { } Publish(); });
                        else if (type == "voicetest") { bool tar = S.Language == "ar"; Task.Run(() => speaker.Say(tar ? "أنا جارفيس، جاهز." : "Hi, I'm Jarvis. Ready when you are.", tar)); }
                        else if (type == "quit") QuitRequested = true;
                        else if (type == "result")
                        {
                            object data; var d = m.TryGetValue("data", out data) ? data as Dictionary<string, object> : null;
                            object idv; TaskCompletionSource<Dictionary<string, object>> tcs;
                            if (d != null && d.TryGetValue("id", out idv) && idv is string && pending.TryRemove((string)idv, out tcs)) tcs.TrySetResult(d);
                        }
                    }
                if (ticks % 10 == 0) Publish();   // heartbeat once a second: the plugin treats a stale state as "app closed"
            }
            catch { }
        }

        /// <summary>For the settings window: does the key work, and which model will be used.</summary>
        public async Task<string> TestConnection(Settings s)
        {
            var p = Providers.ById(s.Provider); var key = s.Key;
            if (p.NeedsKey && key.Length == 0) return "No key yet.";
            try
            {
                var list = await Api.ListModels(p, s, key, CancellationToken.None).ConfigureAwait(false);
                var m = s.Model.Length > 0 ? s.Model : Providers.PickModel(p, list);
                return "Connected. " + list.Count + " models; Jarvis will use " + m + ".";
            }
            catch (ApiException e)
            {
                switch (e.Kind)
                {
                    case ApiFail.KeyRejected: return "The provider refused this key.";
                    case ApiFail.RateLimited: return "Key works, but the free limit is used up for now.";
                    case ApiFail.Network: return p.Id == "ollama" ? "Ollama is not running on this PC." : "Can't reach the provider (no internet?).";
                    default: return "The provider answered, but not as expected: " + e.Message;
                }
            }
        }

        public void Dispose()
        {
            disposed = true; tick.Dispose();
            var h = hearing; hearing = null; if (h != null) h.Dispose();
            speaker.Dispose();
            Note = ""; State = "closed"; Publish();
        }
    }
}