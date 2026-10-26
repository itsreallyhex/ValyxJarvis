using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace ValyxJarvis
{
    /// <summary>
    /// The building blocks of the overlay: quads with the game's own URP Unlit shader, world-space TextMeshPro text, and the fonts.
    /// English draws with the game's font; Arabic with the embedded Noto Sans Arabic, whose own fallback is the game's font (for
    /// digits and Latin inside an Arabic line). Nothing of the game's own fonts is changed.
    /// </summary>
    static class Ui
    {
        // In game (kit, 2026-09-19): a TextMeshPro line at a 0.01 object scale is about fontSize * 0.115 cm tall.
        const float CmPerFontSize = 0.115f;
        static readonly string[] ShaderNames = { "Universal Render Pipeline/Unlit", "Sprites/Default", "Unlit/Color" };

        static ManualLogSource log;
        static Shader shader;
        static Mesh quad;
        static TMP_FontAsset gameFont, arabicFont;
        static bool arabicTried;
        static readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();

        public static void Init(ManualLogSource logger) { log = logger; }

        public static Shader Shader
        {
            get
            {
                if (shader != null) return shader;
                foreach (string n in ShaderNames)
                {
                    shader = Shader.Find(n);
                    if (shader != null) { log.LogDebug("overlay shader: " + n); return shader; }
                }
                var t = GameObject.CreatePrimitive(PrimitiveType.Quad);
                shader = t.GetComponent<Renderer>().sharedMaterial.shader;
                UnityEngine.Object.DestroyImmediate(t);
                log.LogWarning("overlay shader: none of the usual shaders was found, using " + (shader != null ? shader.name : "nothing"));
                return shader;
            }
        }

        /// <summary>A unit quad facing -Z (seen from its -Z side), borrowed from a temporary primitive so it has no collider.</summary>
        public static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                var t = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad = t.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(t);
                return quad;
            }
        }

        public static Material Mat(Color color, Texture texture, bool transparent)
        {
            var m = new Material(Shader);
            if (transparent)
            {
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
                if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = 3000;
            }
            SetColor(m, color);
            SetTexture(m, texture);
            made.Add(m);
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static void SetTexture(Material m, Texture t)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
        }

        /// <summary>A flat quad of the given size (metres) at a local position, with no collider and no shadows.</summary>
        public static MeshRenderer Panel(Transform parent, string name, Vector2 size, Vector3 localPos, Material mat, int order)
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = Quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingOrder = order;
            return r;
        }

        public static void Track(UnityEngine.Object o) { if (o != null) made.Add(o); }

        static readonly Dictionary<PrimitiveType, Mesh> shapes = new Dictionary<PrimitiveType, Mesh>();

        /// <summary>A solid shape (Unity's cube, cylinder...) with no collider, so the player can never stand on or bump into it.</summary>
        public static Transform Solid(Transform parent, string name, PrimitiveType type, Material mat)
        {
            Mesh mesh;
            if (!shapes.TryGetValue(type, out mesh) || mesh == null)
            {
                var t = GameObject.CreatePrimitive(type);
                mesh = t.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(t);
                shapes[type] = mesh;
            }
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        // ------------------------------------------------------------------ text

        public static TMP_FontAsset GameFont
        {
            get
            {
                if (gameFont != null) return gameFont;
                try
                {
                    TMP_FontAsset d = TMP_Settings.defaultFontAsset;
                    if (d != null && d.HasCharacter('A')) gameFont = d;
                }
                catch (Exception) { }   // TMP_Settings can be missing this early
                if (gameFont == null)
                    foreach (TMP_FontAsset f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                        if (f != null && f != arabicFont && f.HasCharacter('A') && f.HasCharacter('(')) { gameFont = f; break; }
                if (gameFont == null) log.LogError("no TextMeshPro font found: the overlay has no text");
                else log.LogDebug("overlay font: " + gameFont.name);
                return gameFont;
            }
        }

        public static TMP_FontAsset ArabicFont
        {
            get
            {
                if (arabicFont != null || arabicTried) return arabicFont;
                arabicTried = true;
                try
                {
                    string dir = Path.Combine(Paths.CachePath, "ValyxJarvis");
                    string path = Path.Combine(dir, "NotoSansArabic-Regular.ttf");
                    if (!WriteResource("ValyxJarvis.NotoSansArabic-Regular.ttf", path)) throw new FileNotFoundException("the Arabic font is not embedded in this build");
                    WriteResource("ValyxJarvis.OFL.txt", Path.Combine(dir, "OFL.txt"));
                    arabicFont = TMP_FontAsset.CreateFontAsset(path, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
                    if (arabicFont == null) throw new InvalidOperationException("TextMeshPro could not make a font from " + path);
                    arabicFont.name = "Valyx Jarvis Arabic";
                    // Noto's line height is 2.1 times its size, the game's font 1.15 (kit 04): bring it close to the game's.
                    FaceInfo face = arabicFont.faceInfo;
                    face.lineHeight = face.pointSize * 1.45f;
                    face.ascentLine = face.pointSize * 1.0f;
                    face.descentLine = -face.pointSize * 0.4f;
                    arabicFont.faceInfo = face;
                    // The Arabic font has no Latin letters and no brackets: those come from the game's font.
                    if (GameFont != null) arabicFont.fallbackFontAssetTable = new List<TMP_FontAsset> { GameFont };
                    made.Add(arabicFont);
                }
                catch (Exception e) { log.LogError("the Arabic font failed, Arabic text will not show: " + e.Message); arabicFont = null; }
                return arabicFont;
            }
        }

        static bool WriteResource(string resource, string path)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (s == null) return false;
                if (!File.Exists(path) || new FileInfo(path).Length != s.Length)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (FileStream f = File.Create(path)) s.CopyTo(f);
                }
                return true;
            }
        }

        /// <summary>A single-line label; lineHeight and width are in metres. Null if the game has no font.</summary>
        public static TextMeshPro Text(Transform parent, string name, float lineHeight, float width, Color color, int order, TextAlignmentOptions align)
        {
            TMP_FontAsset f = GameFont;
            if (f == null) return null;
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * 0.01f;   // the text rect is then in centimetres
            var t = go.AddComponent<TextMeshPro>();
            t.font = f;
            t.fontSize = lineHeight * 100f / CmPerFontSize;
            t.alignment = align;
            t.color = color;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.richText = false;
            t.rectTransform.sizeDelta = new Vector2(width * 100f, lineHeight * 100f * 1.6f);
            t.sortingOrder = order;
            // Kerning pairs are written for logical order and pull shaped Arabic apart (kit 04); harmless for English.
            var features = new List<OTL_FeatureTag>(t.fontFeatures);
            features.Remove(OTL_FeatureTag.kern);
            t.fontFeatures = features;
            return t;
        }

        /// <summary>Sets logical text: Arabic is shaped and drawn with the Arabic font, anything else with the game's font.
        /// Only touches the component when the text really changed.</summary>
        public static void Set(TMP_Text t, string logical)   // world text and the desktop menu's screen text alike
        {
            if (t == null) return;
            logical = logical ?? "";
            bool arabic = ArabicShaper.ContainsArabic(logical);
            TMP_FontAsset want = arabic && ArabicFont != null ? ArabicFont : GameFont;
            string shown = arabic ? ArabicShaper.Shape(logical) : logical;
            if (t.font != want && want != null) t.font = want;
            if (t.text != shown) t.text = shown;
        }

        /// <summary>Like Set, for text that may need several lines. Shaped Arabic is stored right to left, so TextMeshPro's own wrapping
        /// puts the END of a sentence on the first line (seen in game 2026-10-26). Arabic is therefore split into lines here, in reading
        /// order, each measured shaped with the real font against the width, and each line shaped on its own. Other text wraps normally.</summary>
        public static void SetWrapped(TMP_Text t, string logical, float width)
        {
            if (t == null) return;
            logical = logical ?? "";
            if (!ArabicShaper.ContainsArabic(logical)) { t.textWrappingMode = TextWrappingModes.Normal; Set(t, logical); return; }
            var font = ArabicFont ?? GameFont;
            if (font != null && t.font != font) t.font = font;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            var lines = new List<string>(); string cur = "";
            foreach (var w in logical.Split(' '))
            {
                var cand = cur.Length == 0 ? w : cur + " " + w;
                if (cur.Length > 0 && t.GetPreferredValues(ArabicShaper.Shape(cand)).x > width) { lines.Add(cur); cur = w; }
                else cur = cand;
            }
            if (cur.Length > 0) lines.Add(cur);
            var parts = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++) parts[i] = ArabicShaper.Shape(lines[i]);
            var shown = string.Join("\n", parts);
            if (t.text != shown) t.text = shown;
        }

        /// <summary>Puts an object and all its children (TextMeshPro makes child meshes for fallback fonts) on one layer.</summary>
        public static void SetLayer(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject, layer);
        }

        public static void Cleanup()
        {
            foreach (UnityEngine.Object o in made)
            {
                if (o == null) continue;
                var fa = o as TMP_FontAsset;
                if (fa != null)
                {
                    if (fa.atlasTextures != null) foreach (Texture2D t in fa.atlasTextures) if (t != null) UnityEngine.Object.Destroy(t);
                    if (fa.material != null) UnityEngine.Object.Destroy(fa.material);
                }
                UnityEngine.Object.Destroy(o);
            }
            made.Clear();
            arabicFont = null;
            arabicTried = false;
            gameFont = null;
        }
    }

    /// <summary>
    /// The button pictures, drawn in code (no image files). White shapes on transparent, coloured by the material; each pixel is
    /// sampled 3x3 times so the edges are smooth. A shape test gets x and y with the height spanning -0.5..0.5 (y up).
    /// </summary>
    static class Shapes
    {
        static Texture2D Make(string name, int w, int h, Func<float, float, bool> inside)
        {
            const int N = 3;
            float aspect = (float)w / h;
            var px = new Color32[w * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    int hits = 0;
                    for (int sj = 0; sj < N; sj++)
                        for (int si = 0; si < N; si++)
                        {
                            float x = ((i + (si + 0.5f) / N) / w - 0.5f) * aspect;
                            float y = (j + (sj + 0.5f) / N) / h - 0.5f;
                            if (inside(x, y)) hits++;
                        }
                    px[j * w + i] = new Color32(255, 255, 255, (byte)(255 * hits / (N * N)));
                }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels32(px);
            t.Apply(false, true);
            Ui.Track(t);
            return t;
        }

        static bool RoundBox(float x, float y, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(x) - (hx - r), qy = Mathf.Abs(y) - (hy - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r <= 0f;
        }

        /// <summary>A rounded rectangle of w x h pixels; radius is a fraction of the height.</summary>
        public static Texture2D RoundRect(int w, int h, float radius)
        {
            float a = (float)w / h;
            return Make("VJ_RoundRect", w, h, (x, y) => RoundBox(x, y, a * 0.5f - 0.01f, 0.49f, radius));
        }

        public static Texture2D Disc(int size) { return Make("VJ_Disc", size, size, (x, y) => x * x + y * y <= 0.24f); }

        /// <summary>A small filled circle with a thin outer ring (the "live" dot).</summary>
        public static Texture2D Dot(int size) { return Make("VJ_Dot", size, size, (x, y) => x * x + y * y <= 0.2f); }

        /// <summary>A soft round glow: full in the middle, fading to nothing at the edge (the halo around Jarvis's dot).</summary>
        public static Texture2D Glow(int size)
        {
            var px = new Color32[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    float x = (i + 0.5f) / size - 0.5f, y = (j + 0.5f) / size - 0.5f;
                    float d = Mathf.Clamp01(Mathf.Sqrt(x * x + y * y) / 0.5f);
                    px[j * size + i] = new Color32(255, 255, 255, (byte)(255 * Mathf.Pow(1f - d, 2.2f)));
                }
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "VJ_Glow", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels32(px); t.Apply(false, true); Ui.Track(t);
            return t;
        }
    }
}
