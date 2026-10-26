// Valyx Jarvis plugin: the settings menu, Desktop Mode only (a mouse-clickable screen overlay; there is no screen to click in a
// headset). Built like GorillaBuddy's (kit ref 04 "A mouse-clickable 2D screen overlay built from code"): a ScreenSpaceOverlay
// canvas, InputSystemUIInputModule, TMP_InputField with an explicit Text Area, buttons and steppers instead of dropdowns.
// Everything is sent to the helper, which stores it (the key with Windows DPAPI); this menu never writes the key anywhere.
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ValyxJarvis
{
    sealed class Menu : MonoBehaviour
    {
        static readonly Color Bg = new Color(0.07f, 0.08f, 0.1f, 0.97f), CardBg = new Color(0.11f, 0.12f, 0.16f, 1f), Field = new Color(0.15f, 0.17f, 0.22f, 1f),
            Accent = new Color(0.49f, 0.36f, 1f), AccentDim = new Color(0.27f, 0.22f, 0.5f), Good = new Color(0.18f, 0.83f, 0.75f),
            Warn = new Color(0.96f, 0.65f, 0.14f), Bad = new Color(0.96f, 0.25f, 0.37f), Text1 = new Color(0.93f, 0.94f, 0.97f), Dim = new Color(0.6f, 0.63f, 0.7f);
        static readonly string[] PttKeys = { "V", "B", "G", "T", "C", "X", "Z", "CapsLock", "LeftAlt", "Mouse4", "Mouse5", "F6", "F7", "F9", "F10", "F11", "F12", "Insert", "Home", "End", "None" };

        public Link Link;
        public Key OpenKey = Key.F8;
        public BepInEx.Configuration.ConfigEntry<string> DismissedNotices;   // voice-terms pages the player said "Later" to
        GameObject notice; string noticeFor = ""; float noticeSnooze;
        public bool IsOpen { get { return canvas != null; } }

        GameObject canvas, eventSystem;
        Sprite round;
        bool ar;
        Dictionary<string, object> draft;      // what the player is editing; sent on Save
        readonly List<Action> refresh = new List<Action>();
        TMP_InputField keyField, urlField, modelField, typeField;
        TextMeshProUGUI status, testText, providerNote;
        RectTransform chips, urlRow;
        string chipsFor = "";
        bool prevCursorVisible; CursorLockMode prevLock;

        string L(string en, string arabic) { return ar ? arabic : en; }

        void Update()
        {
            if (!GorillaTagger.hasInstance) return;
            try
            {
                // No VR check: with SteamVR running (a Quest over SteamVR) the game counts as VR even on the monitor, and F8 did
                // nothing (owner, 2026-10-27). The menu is on the game window; whoever presses F8 is looking at it.
                Notice();
                var kb = Keyboard.current; if (kb == null) return;
                if (kb[OpenKey].wasPressedThisFrame) { if (IsOpen) Close(); else Open(); }
                else if (IsOpen && kb.escapeKey.wasPressedThisFrame) Close();
                if (IsOpen) Live();
            }
            catch (Exception e) { Debug.LogWarning("[Valyx Jarvis] menu: " + e); }
        }

        /// <summary>"Jarvis, open settings": opens the menu in Desktop Mode; in VR there is no screen to click, so it says where to go.</summary>
        public string OpenFromVoice(bool arabic, out bool ok)
        {
            ok = false;
            if (!GorillaTagger.hasInstance) return "";
            if (!IsOpen) Open();
            else if ((D("language", Json.S(Link.State, "lang")) == "ar") != ar) Rebuild();   // the language changed since it opened
            ok = true;
            if (GorillaTagger.Instance.activeXRDisplay != null)   // in the headset the menu is not visible, it is on the monitor
                return arabic ? "فتحت لك الإعدادات على شاشة الكمبيوتر." : "Settings are open on your PC screen.";
            return arabic ? "فتحت لك الإعدادات." : "Settings are open.";
        }

        public string CloseFromVoice(bool arabic, out bool ok)
        {
            ok = IsOpen; if (IsOpen) Close();
            return ok ? (arabic ? "سكرت الإعدادات." : "Settings closed.") : "";
        }

        // ---- open / close ----------------------------------------------------------------------------
        void Open()
        {
            var st = Link.State;
            var cfg = Json.O(st, "cfg");
            draft = cfg != null ? new Dictionary<string, object>(cfg) : new Dictionary<string, object>();
            Build();
            prevCursorVisible = Cursor.visible; prevLock = Cursor.lockState;
            Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
        }

        void Close()
        {
            if (canvas != null) Destroy(canvas);
            canvas = null; refresh.Clear(); chipsFor = "";
            if (notice == null) { if (eventSystem != null) Destroy(eventSystem); eventSystem = null; Cursor.visible = prevCursorVisible; Cursor.lockState = prevLock; }
        }

        void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;   // another UI mod may have one already; only make our own if not
            eventSystem = new GameObject("ValyxJarvis EventSystem");
            eventSystem.AddComponent<EventSystem>(); eventSystem.AddComponent<InputSystemUIInputModule>();
        }

        // ---- the voice-terms popup ------------------------------------------------------------------------
        /// <summary>Groq gives the human voice only after its terms are accepted once per language (English and Arabic are separate models).
        /// When the helper finds a language waiting for that, this popup says so, once per page, until "Later" or the terms are accepted.</summary>
        void Notice()
        {
            var st = Link.State; if (st == null || Time.unscaledTime < noticeSnooze) return;
            var dismissed = new HashSet<string>((DismissedNotices != null ? DismissedNotices.Value : "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
            var open = (Json.L(st, "voiceTerms") ?? new List<object>()).OfType<IDictionary<string, object>>()
                .Where(t => !dismissed.Contains(Json.S(t, "url"))).ToList();
            var key = string.Join("|", open.Select(t => Json.S(t, "url")));
            if (key == noticeFor) return;
            HideNotice();
            noticeFor = key;
            if (open.Count == 0 || IsOpen) { noticeFor = IsOpen ? "" : key; return; }   // never on top of the menu; shown after it closes
            BuildNotice(open);
        }

        void HideNotice()
        {
            if (notice != null) Destroy(notice);
            notice = null; noticeFor = "";
            if (canvas == null && eventSystem != null) { Destroy(eventSystem); eventSystem = null; Cursor.visible = prevCursorVisible; Cursor.lockState = prevLock; }
        }

        void BuildNotice(List<IDictionary<string, object>> open)
        {
            ar = Json.S(Link.State, "lang") == "ar";
            EnsureEventSystem();
            prevCursorVisible = Cursor.visible; prevLock = Cursor.lockState; Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
            notice = new GameObject("ValyxJarvis Notice");
            var cv = notice.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 30001;
            var sc = notice.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            notice.AddComponent<GraphicRaycaster>();
            var panel = Box(notice.transform, "Panel", Bg);
            var prt = (RectTransform)panel.transform; prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(28, 28, 22, 22); v.spacing = 12; v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            var fit = panel.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize; fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            Size(panel.gameObject, 720, -1);
            Label(panel.transform, L("Give Jarvis a human voice", "خل جارفيس يتكلم بصوت بشري"), 28, Text1, 40).fontStyle = FontStyles.Bold;
            bool arabicOnly = open.All(t => Json.S(t, "lang") == "ar");
            Label(panel.transform, L("Groq gives Jarvis a natural voice for free, but asks you to accept its terms once for each language. Until then he uses a Windows voice" +
                    (open.Any(t => Json.S(t, "lang") == "ar") ? ", and Arabic answers are shown as text only." : "."),
                "Groq يعطي جارفيس صوت طبيعي مجاناً، بس يطلب توافق على شروطه مرة وحدة لكل لغة. لين توافق يتكلم بصوت ويندوز" +
                    (open.Any(t => Json.S(t, "lang") == "ar") ? "، والردود العربية تطلع كتابة بس." : ".")), 18, Dim, 84);
            Label(panel.transform, L("Open the page, sign in, press Accept, then come back.", "افتح الصفحة، سجل دخول، اضغط Accept، وارجع."), 16, Dim, 26);
            foreach (var t in open)
            {
                var url = Json.S(t, "url"); var lang = Json.S(t, "lang");
                var b = Btn(panel.transform, lang == "ar" ? L("Open the Arabic voice page", "افتح صفحة الصوت العربي") : L("Open the English voice page", "افتح صفحة الصوت الإنجليزي"),
                    () => { if (url.StartsWith("https://console.groq.com/")) Application.OpenURL(url); }, -1, 44);
                Paint(b, true);
            }
            var row = Group(panel.transform, false, 10);
            Btn(row, L("I accepted, test it", "وافقت، جربه"), () => { Link.Send("voicetest", ""); HideNotice(); noticeSnooze = Time.unscaledTime + 20f; }, -1, 42);   // the test reply clears it if accepted
            Btn(row, L("Later", "بعدين"), () =>
            {
                if (DismissedNotices != null)
                {
                    var all = new HashSet<string>(DismissedNotices.Value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
                    foreach (var t in open) all.Add(Json.S(t, "url"));
                    DismissedNotices.Value = string.Join("|", all);
                }
                HideNotice();
            }, 160, 42);
        }

        /// <summary>Builds the menu again (another language), keeping what was typed but not saved yet.</summary>
        void Rebuild()
        {
            string key = keyField != null ? keyField.text : "";
            if (urlField != null) draft["baseUrl"] = urlField.text;
            if (modelField != null) draft["model"] = modelField.text;
            if (canvas != null) Destroy(canvas);
            canvas = null; refresh.Clear(); chipsFor = "";
            Build();
            if (key.Length > 0 && keyField != null) keyField.text = key;
        }

        string D(string k, string def = "") { return Json.S(draft, k, def); }
        bool DB(string k, bool def = false) { return Json.B(draft, k, def); }
        long DN(string k, long def = 0) { return Json.N(draft, k, def); }
        void Set(string k, object v) { draft[k] = v; foreach (var r in refresh) r(); }

        // ---- building blocks -------------------------------------------------------------------------
        Sprite Round()
        {
            if (round != null) return round;
            var t = Shapes.RoundRect(64, 64, 0.22f);
            round = Sprite.Create(t, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(16, 16, 16, 16));
            Ui.Track(round);
            return round;
        }

        Image Box(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.sprite = Round(); img.type = Image.Type.Sliced; img.color = c;
            return img;
        }

        static LayoutElement Size(GameObject go, float w, float h, float flexW = -1)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (w > 0) le.preferredWidth = w; if (h > 0) { le.preferredHeight = h; le.minHeight = h; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            return le;
        }

        RectTransform Group(Transform parent, bool vertical, float spacing, RectOffset pad = null)
        {
            var go = new GameObject(vertical ? "Col" : "Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            HorizontalOrVerticalLayoutGroup g = vertical ? (HorizontalOrVerticalLayoutGroup)go.AddComponent<VerticalLayoutGroup>() : go.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing; g.childControlWidth = true; g.childControlHeight = true; g.childForceExpandWidth = vertical; g.childForceExpandHeight = false;
            g.childAlignment = vertical ? TextAnchor.UpperCenter : (ar ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
            if (!vertical) ((HorizontalLayoutGroup)g).reverseArrangement = ar;
            if (pad != null) g.padding = pad;
            return (RectTransform)go.transform;
        }

        TextMeshProUGUI Label(Transform parent, string text, float size, Color c, float h = -1)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = Ui.GameFont; t.fontSize = size; t.color = c; t.raycastTarget = false;
            t.alignment = ar ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
            t.textWrappingMode = TextWrappingModes.Normal;
            Ui.Set(t, text);
            Size(go, -1, h > 0 ? h : size * 1.5f, 1);
            return t;
        }

        Button Btn(Transform parent, string text, Action click, float w = -1, float h = 36)
        {
            var img = Box(parent, "Button", Field);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors; colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f); colors.pressedColor = new Color(0.85f, 0.85f, 0.85f); b.colors = colors;
            b.onClick.AddListener(() => { try { click(); } catch (Exception e) { Debug.LogWarning("[Valyx Jarvis] menu button: " + e); } });
            var lbl = new GameObject("Text", typeof(RectTransform)); lbl.transform.SetParent(img.transform, false);
            var rt = (RectTransform)lbl.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(10, 0); rt.offsetMax = new Vector2(-10, 0);
            var t = lbl.AddComponent<TextMeshProUGUI>(); t.font = Ui.GameFont; t.fontSize = 17; t.color = Text1; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
            Ui.Set(t, text);
            Size(img.gameObject, w, h, w > 0 ? 0 : 1);
            return b;
        }

        static void Paint(Button b, bool on) { b.GetComponent<Image>().color = on ? Accent : Field; }
        static void Caption(Button b, string text) { Ui.Set(b.GetComponentInChildren<TextMeshProUGUI>(), text); }

        TMP_InputField Input(Transform parent, string placeholder, bool password, Action<string> submit = null)
        {
            var img = Box(parent, "Input", Field);
            Size(img.gameObject, -1, 38, 1);
            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            area.transform.SetParent(img.transform, false);
            var art = (RectTransform)area.transform; art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one; art.offsetMin = new Vector2(12, 4); art.offsetMax = new Vector2(-12, -4);
            Func<string, Color, TextMeshProUGUI> make = (n, c) =>
            {
                var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(area.transform, false);
                var rt = (RectTransform)go.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
                var t = go.AddComponent<TextMeshProUGUI>(); t.font = Ui.GameFont; t.fontSize = 17; t.color = c;
                t.alignment = ar ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft; t.textWrappingMode = TextWrappingModes.NoWrap;
                return t;
            };
            var text = make("Text", Text1);
            var ph = make("Placeholder", Dim); ph.fontStyle = FontStyles.Italic; Ui.Set(ph, placeholder);
            var input = img.gameObject.AddComponent<TMP_InputField>();
            // In Arabic the field needs the Arabic font (it falls back to the game's for Latin): the game's font drew the placeholder as boxes.
            var fieldFont = ar && Ui.ArabicFont != null ? Ui.ArabicFont : Ui.GameFont;
            text.font = fieldFont; ph.font = fieldFont;
            input.textViewport = art; input.textComponent = text; input.placeholder = ph; input.fontAsset = fieldFont;
            input.pointSize = 17; input.characterLimit = 300;
            if (password) { input.contentType = TMP_InputField.ContentType.Password; input.asteriskChar = '•'; }
            if (submit != null) input.onSubmit.AddListener(s => submit(s));
            return input;
        }

        RectTransform Card(Transform parent, string title)
        {
            var img = Box(parent, "Card", CardBg);
            var v = img.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(16, 16, 12, 14); v.spacing = 8; v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            Label(img.transform, title, 20, Text1, 28).fontStyle = FontStyles.Bold;
            return (RectTransform)img.transform;
        }

        void Stepper(Transform parent, string title, Func<string> show, Action prev, Action next)
        {
            var row = Group(parent, false, 6);
            var lbl = Label(row, title, 16, Dim, 36); Size(lbl.gameObject, 150, 36, 0);
            Btn(row, ar ? "›" : "‹", () => { prev(); foreach (var r in refresh) r(); }, 40);
            var mid = Btn(row, show(), () => { next(); foreach (var r in refresh) r(); });
            Btn(row, ar ? "‹" : "›", () => { next(); foreach (var r in refresh) r(); }, 40);
            refresh.Add(() => Caption(mid, show()));
        }

        void Switch(Transform parent, string title, string key, bool def)
        {
            Button b = null;
            b = Btn(parent, "", () => Set(key, !DB(key, def)));
            Action paint = () => { bool on = DB(key, def); Paint(b, on); Caption(b, (on ? "● " : "○ ") + title); };
            refresh.Add(paint);
        }

        void Choice(Transform parent, string key, string[] values, string[] labels)
        {
            var row = Group(parent, false, 6);
            for (int i = 0; i < values.Length; i++)
            {
                var v = values[i];
                var b = Btn(row, labels[i], () => { bool changed = D(key) != v; Set(key, v); if (key == "language" && changed) Rebuild(); });   // the menu follows the language at once
                refresh.Add(() => Paint(b, D(key) == v));
            }
        }

        // ---- the menu ---------------------------------------------------------------------------------
        void Build()
        {
            var st = Link.State;
            ar = D("language", Json.S(st, "lang")) == "ar";
            EnsureEventSystem();
            canvas = new GameObject("ValyxJarvis Menu");
            var cv = canvas.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 30000;
            var scaler = canvas.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            canvas.AddComponent<GraphicRaycaster>();

            var panel = Box(canvas.transform, "Panel", Bg);
            var prt = (RectTransform)panel.transform; prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f); prt.pivot = new Vector2(0.5f, 0.5f);
            var pv = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            pv.padding = new RectOffset(24, 24, 18, 20); pv.spacing = 12; pv.childControlWidth = true; pv.childControlHeight = true; pv.childForceExpandWidth = true; pv.childForceExpandHeight = false;
            var fit = panel.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize; fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            Size(panel.gameObject, 1180, -1);

            // header
            var head = Group(panel.transform, false, 10);
            var title = Label(head, L("Jarvis", "جارفيس"), 34, Text1, 44); title.fontStyle = FontStyles.Bold; Size(title.gameObject, 170, 44, 0);
            var sub = Label(head, L("Free voice assistant · Valyx · nothing here is seen by other players", "مساعد صوتي مجاني · فاليكس · ما يشوفه غيرك"), 16, Dim, 44);
            Btn(head, "X", Close, 44, 40);   // the game font has no ✕
            status = Label(panel.transform, "", 16, Dim, 24);

            var cols = Group(panel.transform, false, 16);
            cols.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;   // both columns start at the top
            var left = Group(cols, true, 12); Size(left.gameObject, 560, -1, 1);
            var right = Group(cols, true, 12); Size(right.gameObject, 560, -1, 1);

            // AI connection
            var ai = Card(left, L("AI connection", "اتصال الذكاء الاصطناعي"));
            var provs = Json.L(st, "providers") ?? new List<object>();
            var free = provs.OfType<IDictionary<string, object>>().Where(p => Json.B(p, "free")).ToList();
            var paid = provs.OfType<IDictionary<string, object>>().Where(p => !Json.B(p, "free")).ToList();
            foreach (var set in new[] { free, paid })
            {
                var row = Group(ai, false, 6);
                foreach (var p in set)
                {
                    var id = Json.S(p, "id"); var name = Json.S(p, "name"); int cut = name.IndexOf(" ("); if (cut > 0) name = name.Substring(0, cut);
                    name = name.Replace("Google ", "").Replace("Anthropic ", "");
                    if (ar && id == "custom") name = "أخرى";
                    var b = Btn(row, name, () => { Set("provider", id); });
                    refresh.Add(() => Paint(b, D("provider", "groq") == id));
                }
            }
            providerNote = Label(ai, "", 15, Good, 22);
            var keyRow = Group(ai, false, 6);
            keyField = Input(keyRow, L("Paste your API key here", "الصق مفتاح API هنا"), true);
            Btn(keyRow, L("Get a free key", "مفتاح مجاني"), () => { var url = Json.S(Prov(), "keyUrl"); if (url.StartsWith("https://")) Application.OpenURL(url); }, 170);
            keyField.onValueChanged.AddListener(OnKeyTyped);
            urlRow = Group(ai, false, 6);
            var ul = Label(urlRow, L("Address", "العنوان"), 16, Dim, 38); Size(ul.gameObject, 110, 38, 0);
            urlField = Input(urlRow, "http://localhost:11434/v1", false); urlField.text = D("baseUrl");
            var modelRow = Group(ai, false, 6);
            var ml = Label(modelRow, L("Model", "النموذج"), 16, Dim, 38); Size(ml.gameObject, 110, 38, 0);
            modelField = Input(modelRow, L("auto (recommended)", "تلقائي (موصى به)"), false); modelField.text = D("model");
            chips = Group(ai, false, 6);
            var testRow = Group(ai, false, 8);
            Btn(testRow, L("Test connection", "اختبار الاتصال"), Test, 200);
            testText = Label(testRow, "", 15, Dim, 38);

            // Talk to him
            var talk = Card(left, L("Type to Jarvis", "اكتب لجارفيس"));
            var trow = Group(talk, false, 6);
            typeField = Input(trow, L("e.g. make it night, try on a party hat", "مثلاً: خله ليل، جرب قبعة"), false, s => SendTyped());
            Btn(trow, L("Send", "إرسال"), SendTyped, 110);
            Label(talk, L("Voice: hold the talk key and speak, then let go. In VR: hold the left stick in while you speak.",
                          "بالصوت: امسك زر التحدث وتكلم ثم اتركه. في الـVR: اضغط العصا اليسرى وأنت تتكلم."), 15, Dim, 44);

            // Voice and sound
            var voice = Card(right, L("Voice and sound", "الصوت"));
            Choice(voice, "engine", new[] { "ai", "windows" }, new[] { L("Human voice (AI)", "صوت بشري (ذكاء اصطناعي)"), L("Windows voice", "صوت ويندوز") });
            var voices = (Json.L(st, "voices") ?? new List<object>()).Select(o => Convert.ToString(o)).ToList(); voices.Insert(0, "");
            // Which list the stepper walks: the provider's human voices (Arabic ones when the language is Arabic), or Windows voices.
            Func<bool> human = () => D("engine", "ai") == "ai" && AiVoices().Count > 0;
            Func<string> vkey = () => human() ? (D("language") == "ar" ? "aiVoiceAr" : "aiVoice") : "voice";
            Func<List<string>> vlist = () => human() ? AiVoices() : voices;
            Stepper(voice, L("Voice", "الصوت"), () =>
                {
                    var v = D(vkey()); var list = vlist();
                    if (human()) return Cap(list.Contains(v) ? v : list[0]);
                    return v.Length == 0 ? L("Automatic", "تلقائي") : v.Replace("Microsoft ", "").Replace(" Desktop", "");
                },
                () => { var l = vlist(); Set(vkey(), l[(Math.Max(0, l.IndexOf(D(vkey()))) - 1 + l.Count) % l.Count]); },
                () => { var l = vlist(); Set(vkey(), l[(Math.Max(0, l.IndexOf(D(vkey()))) + 1) % l.Count]); });
            Stepper(voice, L("Speed", "السرعة"), () => DN("rate").ToString(), () => Set("rate", Math.Max(-10, DN("rate") - 1)), () => Set("rate", Math.Min(10, DN("rate") + 1)));
            Stepper(voice, L("Volume", "مستوى الصوت"), () => DN("volume", 80) + "%", () => Set("volume", Math.Max(0, DN("volume", 80) - 10)), () => Set("volume", Math.Min(100, DN("volume", 80) + 10)));
            var vr1 = Group(voice, false, 6);
            Switch(vr1, L("Answer out loud", "يرد بالصوت"), "speak", true);
            Switch(vr1, L("Little tones", "نغمات صغيرة"), "chime", true);
            var vr2 = Group(voice, false, 6);
            Btn(vr2, L("Test voice", "جرب الصوت"), () => { SendSettings(false); Link.Send("voicetest", ""); }, 180);
            termsBtn = Btn(vr2, L("Accept voice terms", "وافق على شروط الصوت"), () => { var url = Json.S(Link.State, "voiceUrl"); if (url.StartsWith("https://console.groq.com/")) Application.OpenURL(url); }, 230);
            voiceNote = Label(voice, "", 14, Warn, 40);
            refresh.Add(VoiceNote);

            // Listening
            var listen = Card(right, L("Listening", "الاستماع"));
            Choice(listen, "language", new[] { "auto", "en", "ar" }, new[] { L("Automatic", "تلقائي"), "English", "العربية" });
            var lr = Group(listen, false, 6);
            Switch(lr, L("Always listen for \"Jarvis\"", "يسمع \"Jarvis\" دائماً"), "wake", false);
            Switch(lr, L("Floating dot", "النقطة العائمة"), "dot", true);
            Stepper(listen, L("Hold to talk", "امسك وتكلم"), () => KeyLabel(D("ptt", "V")),
                () => Set("ptt", PttKeys[(Math.Max(0, Array.IndexOf(PttKeys, D("ptt", "V"))) - 1 + PttKeys.Length) % PttKeys.Length]),
                () => Set("ptt", PttKeys[(Math.Max(0, Array.IndexOf(PttKeys, D("ptt", "V"))) + 1) % PttKeys.Length]));
            var mics = (Json.L(st, "mics") ?? new List<object>()).Select(o => Convert.ToString(o)).ToList(); mics.Insert(0, "");
            Stepper(listen, L("Microphone", "المايك"), () => D("mic").Length == 0 ? L("Automatic", "تلقائي") : D("mic"),
                () => Set("mic", mics[(Math.Max(0, mics.IndexOf(D("mic"))) - 1 + mics.Count) % mics.Count]),
                () => Set("mic", mics[(Math.Max(0, mics.IndexOf(D("mic"))) + 1) % mics.Count]));
            var sl = Label(listen, L("Speech to text", "تحويل الصوت لنص"), 15, Dim, 22);
            Choice(listen, "stt", new[] { "auto", "local", "off" }, new[] { L("Best available", "الأفضل"), L("This PC only", "الجهاز فقط"), L("Off", "مطفأ") });
            var note = Label(listen, "", 14, Dim, 40);
            refresh.Add(() => Ui.Set(note, Json.B(Prov(), "stt") ? L("This provider also hears you (Arabic too).", "هذا المزود يسمعك بعد (حتى بالعربي).") : L("Speech is recognised on this PC (English). For Arabic voice, use Groq or OpenAI.", "الصوت يتعرف عليه الجهاز (إنجليزي). للعربي استخدم Groq أو OpenAI.")));

            // footer
            var foot = Group(panel.transform, false, 10);
            var hint = Label(foot, L("F8 opens this menu · Esc closes", "F8 يفتح القائمة · Esc يقفلها"), 15, Dim, 40);
            Btn(foot, L("Cancel", "إلغاء"), Close, 140, 40);
            var save = Btn(foot, L("Save", "حفظ"), () => { SendSettings(true); Close(); }, 180, 40); Paint(save, true);

            refresh.Add(() =>
            {
                var p = Prov();
                Ui.Set(providerNote, ar && Json.S(p, "noteAr").Length > 0 ? Json.S(p, "noteAr") : Json.S(p, "note"));
                bool url = D("provider") == "custom" || D("provider") == "ollama";
                urlRow.gameObject.SetActive(url);
                keyField.interactable = Json.B(p, "needsKey", true) || D("provider") == "custom";
                Ui.Set((TextMeshProUGUI)keyField.placeholder, DB("hasKey") && keyField.text.Length == 0 ? L("Key saved ", "المفتاح محفوظ ") + D("keyHint") + L(" · paste to replace", " · الصق لتغييره") : Json.B(p, "needsKey", true) ? L("Paste your API key here", "الصق مفتاح API هنا") : L("No key needed", "ما يحتاج مفتاح"));
            });
            foreach (var r in refresh) r();
            Live();
        }

        Button termsBtn; TextMeshProUGUI voiceNote;

        List<string> AiVoices()
        {
            var l = Json.L(Link.State, D("language") == "ar" ? "aiVoicesAr" : "aiVoices");
            // The helper publishes the voices of the SAVED provider; while another one is picked here, show none.
            if (D("provider") != Json.S(Json.O(Link.State, "cfg"), "provider")) return new List<string>();
            return (l ?? new List<object>()).Select(o => Convert.ToString(o)).ToList();
        }

        static string Cap(string s) { return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

        /// <summary>Why the human voice is not heard, and the one-time fix when there is one.</summary>
        void VoiceNote()
        {
            if (voiceNote == null) return;
            var problem = Json.S(Link.State, "voiceProblem");
            bool terms = problem == "terms" && Json.S(Link.State, "voiceUrl").Length > 0;
            termsBtn.gameObject.SetActive(terms);
            string text = "";
            if (D("engine", "ai") == "ai")
            {
                if (terms) text = L("Groq asks you to accept the voice's terms once (free). Press the button, accept, then Test voice.", "Groq يطلب توافق على شروط الصوت مرة وحدة (مجاني). اضغط الزر ووافق، بعدين جرب الصوت.");
                else if (problem.Length > 0) text = L("The human voice failed (" + problem + "); a Windows voice is used for now.", "الصوت البشري ما اشتغل (" + problem + ")، يستخدم صوت ويندوز حالياً.");
                else if (AiVoices().Count == 0) text = L("This provider has no human voice, so a Windows voice speaks. Groq (free) and OpenAI have one.", "هذا المزود ما عنده صوت بشري، فيتكلم صوت ويندوز. Groq (مجاني) و OpenAI عندهم.");
            }
            if (text.Length == 0 && D("language") == "ar" && !Json.B(Link.State, "arVoice") && (D("engine", "ai") == "windows" || AiVoices().Count == 0))
                text = L("No Arabic voice in Windows: answers show as text.", "ما فيه صوت عربي بويندوز: الردود تظهر كتابة.");
            Ui.Set(voiceNote, text);
        }

        string KeyLabel(string k) { return k == "Mouse4" ? L("Mouse side 1", "زر الماوس الجانبي 1") : k == "Mouse5" ? L("Mouse side 2", "زر الماوس الجانبي 2") : k == "None" ? L("Off", "مطفأ") : k; }

        IDictionary<string, object> Prov()
        {
            var id = D("provider", "groq");
            return (Json.L(Link.State, "providers") ?? new List<object>()).OfType<IDictionary<string, object>>().FirstOrDefault(p => Json.S(p, "id") == id);
        }

        /// <summary>The provider follows the key's prefix (gsk_ Groq, AIza Gemini, sk-or- OpenRouter, sk-ant- Anthropic, sk- OpenAI).</summary>
        void OnKeyTyped(string k)
        {
            k = (k ?? "").Trim();
            string id = k.StartsWith("sk-ant-") ? "anthropic" : k.StartsWith("sk-or-") ? "openrouter" : k.StartsWith("gsk_") ? "groq" : k.StartsWith("AIza") ? "gemini" : k.StartsWith("sk-") ? "openai" : null;
            if (id != null && k.Length >= 12 && id != D("provider")) Set("provider", id);
        }

        void SendSettings(bool rebuildIfLanguage)
        {
            var data = new Dictionary<string, object>(draft);
            data.Remove("hasKey"); data.Remove("keyHint"); data.Remove("setupDone");
            data["baseUrl"] = urlField != null ? urlField.text.Trim() : D("baseUrl");
            data["model"] = modelField != null ? modelField.text.Trim() : D("model");
            var key = keyField != null ? keyField.text.Trim() : "";
            if (key.Length > 0) { data["key"] = key; draft["hasKey"] = true; }
            Link.Send("settings", data);
        }

        void Test()
        {
            SendSettings(false);
            Link.Send("test", "");
            Ui.Set(testText, L("Checking…", "جاري الفحص…")); testText.color = Dim;
            chipsFor = "";
        }

        void SendTyped()
        {
            var t = typeField != null ? typeField.text.Trim() : "";
            if (t.Length == 0) return;
            Link.Send("text", t); typeField.text = "";
            typeField.ActivateInputField();
        }

        /// <summary>Parts that follow the helper while the menu is open: status, the test result, the model list.</summary>
        void Live()
        {
            if (status == null) return;
            var st = Link.State;
            if (!Link.Alive) { Ui.Set(status, "● " + (Link.Problem.Length > 0 ? Link.Problem : L("Starting the helper…", "يشغل المساعد…"))); status.color = Warn; }
            else
            {
                var mic = Json.S(st, "micError");
                var model = D("model"); var p = Json.S(Prov(), "name");
                if (ar) { int cut = p.IndexOf(" ("); if (cut > 0) p = p.Substring(0, cut); }   // brackets scramble in right-to-left text
                Ui.Set(status, "● " + L("Ready", "جاهز") + " · " + p + (mic.Length > 0 ? " · " + L("no microphone", "ما فيه مايك") : ""));
                status.color = mic.Length > 0 ? Warn : Good;
            }
            VoiceNote();
            var test = Json.S(st, "test");
            if (test.Length > 1 && testText != null)
            {
                Ui.Set(testText, test);
                testText.color = test.StartsWith("Connected") ? Good : test.StartsWith("Key works") ? Warn : test == "…" ? Dim : Bad;
            }
            var models = (Json.L(st, "models") ?? new List<object>()).Select(o => Convert.ToString(o)).Where(m => !m.Contains("whisper") && !m.Contains("tts") && !m.Contains("embed")).Take(5).ToList();
            var sig = string.Join("|", models);
            if (chips != null && sig != chipsFor)
            {
                chipsFor = sig;
                foreach (Transform c in chips) Destroy(c.gameObject);
                foreach (var m in models) { var name = m; Btn(chips, name, () => { modelField.text = name; }, -1, 30); }
            }
        }

        void OnDestroy() { Close(); HideNotice(); }
    }
}