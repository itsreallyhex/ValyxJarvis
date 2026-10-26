// Valyx Jarvis plugin: the floating dot and the small side panel. In VR they float at the lower right of your view (layer 19:
// in the headset, not on the stream). In Desktop Mode they are a screen canvas in the lower right corner: a world object "in front
// of the screen camera" ended up 1.7 m behind the player in game (the wrong camera was picked), and a screen canvas cannot miss.
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValyxJarvis
{
    sealed class Overlay : MonoBehaviour
    {
        const int FirstPersonOnly = 19;   // kit: the Shoulder Camera (the stream) does not draw this layer
        static readonly Color Violet = new Color(0.49f, 0.36f, 1f), Teal = new Color(0.18f, 0.83f, 0.75f), Amber = new Color(0.96f, 0.65f, 0.14f),
            Rose = new Color(0.96f, 0.25f, 0.37f), Grey = new Color(0.55f, 0.57f, 0.62f), PanelBg = new Color(0.06f, 0.07f, 0.1f, 0.82f),
            TextMain = new Color(0.93f, 0.94f, 0.97f), TextDim = new Color(0.62f, 0.65f, 0.72f);

        public Link Link;
        public Func<bool> ShowAll = () => true;
        Transform root, dot, glow, core, moon, panel;
        Material glowMat, coreMat, moonMat, panelMat;
        TextMeshPro heardText, sayText;
        bool built, vr; int layer = -1;
        float panelAlpha, panelUntil, pulse;
        string shownHeard = "", shownSay = "", result = "";
        long lastSeq = -1, lastSayId = -1; string lastState = "";
        Transform panelBg;
        const float VrWidth = 18f, ScreenWidth = 516f;   // text widths in each text's own units (VR: cm at 0.01 scale; screen: canvas pixels minus padding)
        Vector3 smoothPos; Quaternion smoothRot; bool placed;
        // Desktop Mode: the same dot and panel on a screen canvas
        GameObject screen; RectTransform sDot, sPanel; Image sGlow, sCore, sMoon, sPanelBg; TextMeshProUGUI sHeard, sSay;

        /// <summary>Called by the plugin when an action finished, so the panel shows what really happened ("Trying on: PARTY HAT").</summary>
        public void ShowResult(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            result = text; panelUntil = Time.unscaledTime + 7f;
            Ui.SetWrapped(sayText, text, VrWidth); Ui.SetWrapped(sSay, text, ScreenWidth);
            LayoutVr();
        }

        public void Flash(string heard, string say, float seconds)
        {
            Build();
            shownHeard = heard ?? ""; shownSay = say ?? "";
            var quoted = shownHeard.Length > 0 ? "“" + shownHeard + "”" : "";
            Ui.SetWrapped(heardText, quoted, VrWidth); Ui.SetWrapped(sayText, shownSay, VrWidth);
            Ui.SetWrapped(sHeard, quoted, ScreenWidth); Ui.SetWrapped(sSay, shownSay, ScreenWidth);
            if (sHeard != null) sHeard.gameObject.SetActive(shownHeard.Length > 0);
            panelUntil = Time.unscaledTime + seconds;
            LayoutVr();
        }

        /// <summary>VR panel: the whole answer wraps (up to 6 lines) and the background grows upward to fit; the bottom edge stays put.</summary>
        void LayoutVr()
        {
            if (heardText == null || sayText == null || panelBg == null) return;
            const float W = 0.18f, bottom = -0.028f, pad = 0.008f, gap = 0.003f;
            float hh = 0f;
            if (shownHeard.Length > 0) { heardText.ForceMeshUpdate(); hh = Mathf.Min(heardText.preferredHeight, 3.2f) * 0.01f; }
            sayText.ForceMeshUpdate(); float sh = sayText.preferredHeight * 0.01f;
            heardText.rectTransform.sizeDelta = new Vector2(W * 100f, hh * 100f);
            sayText.rectTransform.sizeDelta = new Vector2(W * 100f, sh * 100f);
            float total = pad * 2 + sh + (hh > 0 ? hh + gap : 0f);
            panelBg.localScale = new Vector3(0.2f, total, 1f);
            panelBg.localPosition = new Vector3(0, bottom + total / 2f, 0);
            float top = bottom + total - pad;
            heardText.transform.localPosition = new Vector3(0, top - hh / 2f, -0.001f);
            sayText.transform.localPosition = new Vector3(0, top - (hh > 0 ? hh + gap : 0f) - sh / 2f, -0.001f);
        }

        void Build()
        {
            if (built) return;
            built = true;
            root = new GameObject("ValyxJarvis Overlay").transform;
            DontDestroyOnLoad(root.gameObject);
            dot = new GameObject("Dot").transform; dot.SetParent(root, false);
            glowMat = Ui.Mat(Violet, Shapes.Glow(128), true);
            coreMat = Ui.Mat(Violet, Shapes.Disc(96), true);
            moonMat = Ui.Mat(Color.white, Shapes.Disc(48), true);
            glow = Ui.Panel(dot, "Glow", new Vector2(0.05f, 0.05f), new Vector3(0, 0, 0.001f), glowMat, 10).transform;
            core = Ui.Panel(dot, "Core", new Vector2(0.018f, 0.018f), Vector3.zero, coreMat, 11).transform;
            moon = Ui.Panel(dot, "Moon", new Vector2(0.006f, 0.006f), Vector3.zero, moonMat, 12).transform;

            panel = new GameObject("Panel").transform; panel.SetParent(root, false);
            panel.localPosition = new Vector3(-0.125f, 0f, 0.002f);
            panelMat = Ui.Mat(PanelBg, Shapes.RoundRect(512, 144, 0.3f), true);
            panelBg = Ui.Panel(panel, "Bg", new Vector2(0.2f, 0.056f), Vector3.zero, panelMat, 9).transform;
            heardText = Ui.Text(panel, "Heard", 0.0105f, 0.18f, TextDim, 13, TextAlignmentOptions.TopRight);
            sayText = Ui.Text(panel, "Say", 0.0135f, 0.18f, TextMain, 13, TextAlignmentOptions.TopRight);
            foreach (var tx in new[] { heardText, sayText })
                if (tx != null) { tx.textWrappingMode = TextWrappingModes.Normal; tx.overflowMode = TextOverflowModes.Truncate; }
            if (heardText != null) heardText.maxVisibleLines = 2;
            if (sayText != null) sayText.maxVisibleLines = 6;
        }

        static Sprite Sprite(Texture2D t) { var s = UnityEngine.Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f)); Ui.Track(s); return s; }

        Image Img(Transform parent, string name, Sprite s, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.sprite = s; img.raycastTarget = false;
            ((RectTransform)go.transform).sizeDelta = size;
            return img;
        }

        /// <summary>A wrapped line inside the panel's vertical layout (the panel grows with it).</summary>
        TextMeshProUGUI Line(Transform parent, string name, float size, Color color, int maxLines)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>(); t.font = Ui.GameFont; t.fontSize = size; t.color = color; t.raycastTarget = false;
            t.alignment = TextAlignmentOptions.TopRight; t.textWrappingMode = TextWrappingModes.Normal; t.overflowMode = TextOverflowModes.Truncate;
            t.maxVisibleLines = maxLines;
            return t;
        }

        /// <summary>Desktop Mode: lower right of the window, under the menu (which sits on top).</summary>
        void BuildScreen()
        {
            if (screen != null) return;
            screen = new GameObject("ValyxJarvis Screen Overlay");
            DontDestroyOnLoad(screen);
            var cv = screen.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 29000;
            var sc = screen.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;

            var dotGo = new GameObject("Dot", typeof(RectTransform)); dotGo.transform.SetParent(screen.transform, false);
            sDot = (RectTransform)dotGo.transform; sDot.anchorMin = sDot.anchorMax = new Vector2(1, 0); sDot.anchoredPosition = new Vector2(-70, 70); sDot.sizeDelta = new Vector2(90, 90);
            sGlow = Img(sDot, "Glow", Sprite(Shapes.Glow(128)), new Vector2(110, 110));
            sCore = Img(sDot, "Core", Sprite(Shapes.Disc(96)), new Vector2(36, 36));
            sMoon = Img(sDot, "Moon", Sprite(Shapes.Disc(48)), new Vector2(11, 11));

            var round = Shapes.RoundRect(64, 64, 0.3f);
            var panelSprite = UnityEngine.Sprite.Create(round, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(20, 20, 20, 20)); Ui.Track(panelSprite);
            sPanelBg = Img(screen.transform, "Panel", panelSprite, new Vector2(560, 92)); sPanelBg.type = Image.Type.Sliced;
            // bottom-right pivot: the panel keeps its bottom edge beside the dot and grows upward with longer answers
            sPanel = sPanelBg.rectTransform; sPanel.anchorMin = sPanel.anchorMax = new Vector2(1, 0); sPanel.pivot = new Vector2(1, 0); sPanel.anchoredPosition = new Vector2(-130, 26);
            var lay = sPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            lay.padding = new RectOffset(20, 20, 12, 14); lay.spacing = 4; lay.childAlignment = TextAnchor.LowerRight;
            lay.childControlWidth = true; lay.childControlHeight = true; lay.childForceExpandWidth = true; lay.childForceExpandHeight = false;
            var fit = sPanel.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sHeard = Line(sPanel, "Heard", 20, TextDim, 2);
            sSay = Line(sPanel, "Say", 26, TextMain, 6);
            Ui.SetWrapped(sHeard, shownHeard.Length > 0 ? "“" + shownHeard + "”" : "", ScreenWidth); Ui.SetWrapped(sSay, shownSay, ScreenWidth);
        }

        void LateUpdate()
        {
            if (!GorillaTagger.hasInstance || Link == null) return;
            try { Tick(); }
            catch (Exception e) { if (Time.frameCount % 600 == 0) Debug.LogWarning("[Valyx Jarvis] overlay: " + e.Message); }
        }

        void Tick()
        {
            Build();
            var gt = GorillaTagger.Instance;
            vr = gt.activeXRDisplay != null;
            int want = vr ? FirstPersonOnly : 0;
            if (want != layer) { layer = want; Ui.SetLayer(root.gameObject, layer); }

            // ---- where: VR follows the head with a soft lag; Desktop draws on a screen canvas instead
            if (vr)
            {
                var anchor = gt.headCollider.transform;
                var targetPos = anchor.position + anchor.rotation * new Vector3(0.17f, -0.12f, 0.5f);
                var targetRot = Quaternion.LookRotation(targetPos - anchor.position, anchor.up);
                float k = 1f - Mathf.Exp(-9f * Time.unscaledDeltaTime);
                if (!placed || (smoothPos - targetPos).sqrMagnitude > 1f) { smoothPos = targetPos; smoothRot = targetRot; placed = true; }
                smoothPos = Vector3.Lerp(smoothPos, targetPos, k);
                smoothRot = Quaternion.Slerp(smoothRot, targetRot, k);
                root.SetPositionAndRotation(smoothPos, smoothRot);
            }
            else BuildScreen();

            // ---- what: the helper's state
            var st = Link.State;
            bool alive = Link.Alive;
            string state = alive ? Json.S(st, "state", "idle") : "offline";
            long seq = Json.N(st, "seq", -1);
            if (alive && seq != lastSeq)
            {
                lastSeq = seq;
                string heard = Json.S(st, "heard"), say = Json.S(st, "say");
                long sayId = Json.N(st, "sayId", 0);
                // Follow the helper live: "Listening..." at the key, the new words the moment they are heard, then the answer. Every
                // answer has its own id, so a repeated line ("Day it is." twice) still shows (it used to look like nothing happened).
                if (state == "listening") { if (lastState != "listening") { result = ""; Flash("", Listening(st), 30f); } }
                else if (state == "thinking") { if (lastState != "thinking" || heard != shownHeard) { result = ""; Flash(heard, Thinking(st), 30f); } }
                else if ((state == "speaking" || state == "idle") && lastSayId >= 0 && sayId != lastSayId && say.Length > 0) Flash(heard, result.Length > 0 ? result : say, 8f);
                else if (state == "nomic" && lastState != "nomic") Flash("", Json.S(st, "note"), 8f);
                if (state == "idle" && (lastState == "listening" || lastState == "thinking") && sayId == lastSayId && panelUntil > Time.unscaledTime + 8f) panelUntil = Time.unscaledTime + 2f;
                lastState = state; lastSayId = sayId;
            }
            if (!alive && lastState != "offline") { lastState = "offline"; var why = Link.Problem; Flash("", why.Length > 0 ? "Jarvis: " + why : Arabic(st) ? "جارفيس يشتغل…" : "Jarvis is starting…", 6f); }

            // ---- how it looks
            Color c; float scale = 1f, glowA = 0.35f, t = Time.unscaledTime;
            switch (state)
            {
                case "listening": c = Teal; pulse = Mathf.Lerp(pulse, 1f, 0.2f); scale = 1.15f + 0.18f * Mathf.Sin(t * 9f); glowA = 0.75f; break;
                case "thinking": c = Violet; scale = 1.05f; glowA = 0.6f; break;
                case "speaking": c = Violet; scale = 1.1f + 0.1f * Mathf.Sin(t * 6f); glowA = 0.7f; break;
                case "setup": c = Amber; glowA = 0.25f + 0.15f * Mathf.Sin(t * 2f); break;
                case "nomic": c = Rose; glowA = 0.3f; break;
                case "offline": c = Grey; glowA = 0.12f; break;
                default: c = Violet; scale = 1f + 0.04f * Mathf.Sin(t * 1.6f); glowA = 0.22f + 0.06f * Mathf.Sin(t * 1.6f); break;
            }
            Ui.SetColor(coreMat, new Color(c.r, c.g, c.b, state == "offline" ? 0.5f : 0.95f));
            Ui.SetColor(glowMat, new Color(c.r, c.g, c.b, glowA));
            core.localScale = new Vector3(0.018f, 0.018f, 1f) * scale;
            glow.localScale = new Vector3(0.05f, 0.05f, 1f) * (0.9f + 0.25f * scale);
            bool orbit = state == "thinking";
            moon.gameObject.SetActive(orbit);
            if (orbit) moon.localPosition = new Vector3(Mathf.Cos(t * 7f), Mathf.Sin(t * 7f), 0) * 0.014f + new Vector3(0, 0, -0.0005f);

            // the panel fades in and out; the dot hides when idle if the player turned it off
            float targetA = Time.unscaledTime < panelUntil ? 1f : 0f;
            panelAlpha = Mathf.MoveTowards(panelAlpha, targetA, Time.unscaledDeltaTime * 4f);
            panel.gameObject.SetActive(panelAlpha > 0.01f);
            Ui.SetColor(panelMat, new Color(PanelBg.r, PanelBg.g, PanelBg.b, PanelBg.a * panelAlpha));
            if (heardText != null) heardText.alpha = panelAlpha;
            if (sayText != null) sayText.alpha = panelAlpha;
            bool showDot = ShowAll() && (Json.B(Json.O(st, "cfg"), "dot", true) || state != "idle" || panelAlpha > 0.01f);
            dot.gameObject.SetActive(showDot);
            root.gameObject.SetActive(ShowAll() && vr);
            if (screen != null)
            {
                screen.SetActive(ShowAll() && !vr);
                if (!vr)
                {
                    sDot.gameObject.SetActive(showDot);
                    sCore.color = new Color(c.r, c.g, c.b, state == "offline" ? 0.5f : 0.95f);
                    sGlow.color = new Color(c.r, c.g, c.b, glowA);
                    sCore.rectTransform.localScale = Vector3.one * scale;
                    sGlow.rectTransform.localScale = Vector3.one * (0.9f + 0.25f * scale);
                    sMoon.gameObject.SetActive(orbit);
                    if (orbit) sMoon.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(t * 7f), Mathf.Sin(t * 7f)) * 15f;
                    sPanel.gameObject.SetActive(panelAlpha > 0.01f);
                    sPanelBg.color = new Color(PanelBg.r, PanelBg.g, PanelBg.b, PanelBg.a * panelAlpha);
                    sHeard.alpha = panelAlpha; sSay.alpha = panelAlpha;
                }
            }
        }

        static bool Arabic(IDictionary<string, object> st) { return Json.S(st, "lang") == "ar"; }
        static string Listening(IDictionary<string, object> st) { return Arabic(st) ? "أسمعك…" : "Listening…"; }
        static string Thinking(IDictionary<string, object> st) { return Arabic(st) ? "أفكر…" : "Thinking…"; }

        void OnDestroy() { if (root != null) Destroy(root.gameObject); if (screen != null) Destroy(screen); }
    }
}