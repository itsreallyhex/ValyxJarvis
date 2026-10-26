// Beacon 0.1.0, by itsreallyhex. One file: copy it into your mod's project, then call
//     Beacon.Core.Load("YourMod");
// once from your plugin's Start. Arabic text is fixed by default; pass arabicAutoFix: false to leave it alone.
// To run Beacon against your own server and content repo, change the two addresses in Endpoints (near the end of this file).
// Beacon counts how many copies run: Stats and StatsPing, near the end of this file, are all of what is sent.
//
// Copyright (C) 2026 itsreallyhex. This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option)
// any later version. It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; see
// <https://www.gnu.org/licenses/gpl-3.0.html>. A modified copy must keep this notice and say that it was changed.
// Your project needs references to these DLLs (they come with the game or BepInEx): BepInEx, 0Harmony, UnityEngine, UnityEngine.CoreModule, UnityEngine.UI, UnityEngine.UIModule, Unity.TextMeshPro, UnityEngine.TextCoreFontEngineModule, UnityEngine.TextCoreTextEngineModule, UnityEngine.TextRenderingModule, UnityEngine.AssetBundleModule, UnityEngine.UnityWebRequestModule, UnityEngine.AudioModule, UnityEngine.AnimationModule, UnityEngine.ParticleSystemModule, Photon3Unity3D, PhotonRealtime, PhotonUnityNetworking.

using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using BepInEx;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine.Networking;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore;
using UnityEngine.UI;
using UnityEngine;

namespace Beacon
{
    /// <summary>
    /// Where this copy of Beacon talks, with no game types: the server (the counter and the owner's messages) and the repo of lists
    /// and downloads. To run your own, change the two addresses and the two keys below to your own server, repo and keys. A copy whose
    /// addresses are not the official ones says so in the log and on screen, so it cannot pass as the official Beacon, and it never
    /// reaches the official server, content or signed messages.
    /// </summary>
    internal static class Endpoints
    {
        internal const string Server = "https://beacon.itsreallyhex.workers.dev/";
        internal const string ContentBase = "https://raw.githubusercontent.com/itsreallyhex/beacon-content/main/";
        internal const string CounterUrl = Server + "ping", MessagesUrl = Server + "messages";
        internal const string KeyMain = "3Y9gG1n3O9zgheqAXPnJuss5S6nU5xEM1y35NtbzoQjIBLoX2r0mrAAlCw7xTLM6GHqvrvqYkuX85thGsx3fWr0+XPNsbEvsYMXNkifUL+uE1L/ZAA2/fKyWzk34FKkMIrkjPmQyVKOcC4JEfUKpIv43pVbU4Ig6jqRtQx4lLcFoLAGrsvQGZhLQMDMyXPeS/SPtxW6XgYXBJ9Ga7RfjRXtgPqmqOCZos9/+zDD/rGfZZ7g01rHX3Oh+XMg5krZ5ktEjE5X0ovUONtZ00a35uejOjVsi2MhkXFF2r7ZQ7ed2GZe8PKUTm5UIBPveInSI3d7O5/VevoBhRERNtLT3Ew==";
        internal const string KeySpare = "sIXIOHvB2dxIfcz6E5tWS1l06BCC/RJCKlFBWeOCrWvJBVRA9RjNZSOp83y8poq/F76EGvpa4fudVw+TxQ/0bYUdXYRP5tN3TSZ97PaAzBqoGSp08+8Rz4rH4n/s8WjdPm1MxEBhz23SLGKcymI5+MtA00GrFf9JzCVgoso2ykOSbTqzDAGkCBddJBcW5zBopaN4bwclqSavcdvf6cnJUL3kh6SZJrkSUVBBslfttG0RLX3JVItllKav7u+Z5ePDl6jKfW3YMCTTLo0dUXeDQIS244D9XSdAeFPcipIQBSUnXqhoRrJbovVLFXNmu8njSOVkIYbXxmj2CTgjiFwuuQ==";
        const string OfficialServer = "https://beacon.itsreallyhex.workers.dev/";
        const string OfficialContent = "https://raw.githubusercontent.com/itsreallyhex/beacon-content/main/";

        /// <summary>True when this copy uses the official addresses.</summary>
        internal static bool Official { get { return Is(Server, ContentBase); } }

        internal static bool Is(string server, string content) { return server == OfficialServer && content == OfficialContent; }

        /// <summary>Why an address cannot be used, or null: it must be https with a plain host, no login, query or fragment.</summary>
        internal static string Problem(string url)
        {
            Uri u;
            if (string.IsNullOrEmpty(url) || url.Length > 200 || !Uri.TryCreate(url, UriKind.Absolute, out u)) return "'" + url + "' is not an address";
            if (u.Scheme != "https") return url + " is not https";
            if (u.UserInfo.Length > 0 || u.Query.Length > 0 || u.Fragment.Length > 0) return url + " has a login, query or fragment";
            if (!Util.Only(u.Host, 253, "a-z0-9.-") || u.Host.IndexOf('.') < 0) return url + " has no usable host name";
            return null;
        }

        /// <summary>The first problem with this copy's addresses, or null. Both must end in a slash.</summary>
        internal static string Problem() { return Check(Server, ContentBase); }

        internal static string Check(string server, string content)
        {
            return Problem(server) ?? Problem(content) ?? (!server.EndsWith("/") ? server + " must end with a slash"
                 : !content.EndsWith("/") ? content + " must end with a slash" : null);
        }

        /// <summary>The host names this copy talks to, for the log and the notice.</summary>
        internal static string Where(string server, string content)
        {
            Uri a, b;
            string x = Uri.TryCreate(server, UriKind.Absolute, out a) ? a.Host : "?", y = Uri.TryCreate(content, UriKind.Absolute, out b) ? b.Host + b.AbsolutePath : "?";
            return x + " and " + y;
        }

        internal static string Notice { get { return "Beacon (modified copy): talks to " + Where(Server, ContentBase); } }
    }

    /// <summary>
    /// Beacon's way in. Copy Beacon.cs into your mod and call <c>Beacon.Core.Load("YourMod");</c> once, from your plugin's Start.
    /// It joins the Beacon network (it sends only in private rooms), keeps the shared Beacon folder beside the game up to date,
    /// and fixes Arabic text in the game. Several mods may carry Beacon: only the newest copy runs, the others stand by.
    /// </summary>
    public static class Core
    {
        public const string Version = "0.1.0";

        // GorillaHurouf is the Arabic-only product. If it is installed too, Beacon leaves the Arabic layer to it.
        internal const string HuroufGUID = "dev.musaed.gorillahurouf";

        // Shared by every copy of Beacon in the game (each mod's copy is its own assembly, so only plain types go in here):
        // the copy that runs { version, mod name, its Unload } and the newest version that has called Load.
        const string RunningKey = "Beacon.Running", NewestKey = "Beacon.Newest";

        /// <summary>The folder beside the game's BepInEx folder that every mod carrying Beacon shares: fonts, content, assets.</summary>
        public static string Folder { get { return Path.Combine(Paths.GameRootPath, "Beacon"); } }

        /// <summary>The name this copy was loaded with.</summary>
        public static string ModName { get; private set; }

        /// <summary>True while this copy is the one that runs.</summary>
        public static bool Running { get; private set; }

        static GameObject host;
        static Action stop;
        static Action<string> notify;
        static bool loaded, arabicIdle, autoFixWanted;

        /// <summary>
        /// Starts Beacon for your mod. <paramref name="modName"/> shows in the log; <paramref name="arabicAutoFix"/> false leaves the game's
        /// text alone (Arabic.Fix and SetArabic still work). <paramref name="updates"/> is your mod's GitHub repo ("you/YourMod"): when it
        /// has a newer release than the version in your [BepInPlugin], your players are told once, with <paramref name="updateMessage"/>
        /// if you give one ({mod}, {new} and {current} are filled in) or "YourMod 1.3.0 is out (you have 1.2.0)".
        /// Safe to call more than once; only the first call counts.
        /// </summary>
        public static void Load(string modName, bool arabicAutoFix = true, string updates = null, string updateMessage = null)
        {
            if (loaded) return;
            loaded = true;
            ModName = string.IsNullOrEmpty(modName) ? "a mod" : modName.Length > 40 ? modName.Substring(0, 40) : modName;
            autoFixWanted = arabicAutoFix;
            string newest = Util.Shared<string>(NewestKey);
            if (newest == null || new System.Version(newest) < new System.Version(Version)) Util.Share(NewestKey, Version);
            Runner runner = Util.Host<Runner>("Beacon (" + ModName + ")");
            host = runner.gameObject;
            BepInPlugin own = OwnPlugin();
            Stats.Register(own?.GUID, ModName, own?.Version != null ? UpdateCheck.Short(own.Version) : null);
            if (updates == null) return;
            if (UpdateCheck.IsRepo(updates)) runner.StartCoroutine(Updates.Check(updates, updateMessage, ModName));
            else Log.Warning("'" + updates + "' is not a GitHub repo written as owner/name, so " + ModName + "'s updates are not checked");
        }

        // One frame after Load, when the other mods have called theirs: the newest copy runs; an older one already running steps down.
        static void Decide()
        {
            var mine = new System.Version(Version);
            var running = Util.Shared<object[]>(RunningKey);
            if (running != null)
            {
                if (new System.Version((string)running[0]) >= mine)
                {
                    Log.Info("Beacon " + Version + " in " + ModName + " stands by: " + running[1] + " runs Beacon " + running[0]);
                    return;
                }
                Log.Info("Beacon " + Version + " in " + ModName + " takes over from " + running[1] + " (Beacon " + running[0] + ")");
                ((Action)running[2])();
            }
            else if (new System.Version(Util.Shared<string>(NewestKey)) > mine)
            {
                Log.Info("Beacon " + Version + " in " + ModName + " stands by: a mod carries a newer Beacon");
                return;
            }
            stop = Unload;
            Util.Share(RunningKey, new object[] { Version, ModName, stop });
            Running = true;
            Log.Info("Beacon " + Version + " runs, from " + ModName);
            try { Directory.CreateDirectory(Folder); }
            catch (Exception e) when (Util.IsIO(e)) { Log.Warning("could not make " + Folder + ": " + e.Message); }

            // Two copies of the Arabic layer would both patch TextMeshPro and add two font assets.
            arabicIdle = Chainloader.PluginInfos.ContainsKey(HuroufGUID);
            if (arabicIdle) Log.Warning("GorillaHurouf is installed too: Beacon's Arabic layer stays idle so text is not fixed twice");
            else
            {
                host.AddComponent<NotificationPanel>();   // draws what Arabic.Notify queues; makes nothing until the first notification
                notify = text => Arabic.Notify(text, NotifyKind.Info, 8f);
                Util.Share(Updates.NotifyKey, notify);   // so the copies standing by can tell their mod's players about updates
            }
            string badAddress = Endpoints.Problem();
            if (badAddress != null) Log.Warning("Beacon's addresses cannot be used: " + badAddress);
            if (!Endpoints.Official)
            {
                Log.Warning("this is a modified Beacon: " + Endpoints.Notice);
                if (!arabicIdle) host.GetComponent<Runner>().StartCoroutine(TellModified());
            }
            Settings.Load();
            if (Settings.Language == null) host.AddComponent<Welcome>();   // the first time Beacon runs on this PC
            Network.Start();
            host.AddComponent<Badges>();   // a tag above the Beacon players in the room
            host.GetComponent<Runner>().StartCoroutine(Stats.Run());   // how many run Beacon, and through which mods
            host.GetComponent<Runner>().StartCoroutine(Board.Run());   // the owner's signed messages
        }

        // Once the player is in the game, a copy that talks to other addresses than the official ones says so.
        static IEnumerator TellModified()
        {
            yield return new WaitForSecondsRealtime(15f);
            Arabic.Notify(Endpoints.Notice, NotifyKind.Warning, 12f);
        }

        // The mod's own [BepInPlugin]: Beacon is compiled into the mod itself, so the mod's plugin class is in this assembly.
        internal static BepInPlugin OwnPlugin()
        {
            Type[] types;
            try { types = typeof(Core).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            foreach (Type t in types)
            {
                if (t == null) continue;
                BepInPlugin a;
                try { a = (BepInPlugin)Attribute.GetCustomAttribute(t, typeof(BepInPlugin)); }
                catch (Exception e) when (e is TypeLoadException || e is FormatException || e is ArgumentException) { continue; }
                if (a != null) return a;
            }
            return null;
        }

        /// <summary>Stops Beacon and removes everything it added to the game (for a mod that is unloaded, such as a ScriptEngine reload).</summary>
        public static void Unload()
        {
            if (Running)
            {
                if (!arabicIdle) Arabic.Cleanup();
                Network.Stop();
                ShowPlayer.Stop();
                AssetLoader.Cleanup();
                Sound.Cleanup();
                var running = Util.Shared<object[]>(RunningKey);
                if (running != null && ReferenceEquals(running[2], stop)) Util.Share(RunningKey, null);
                if (notify != null && ReferenceEquals(Util.Shared<object>(Updates.NotifyKey), notify)) Util.Share(Updates.NotifyKey, null);
            }
            if (host != null) UnityEngine.Object.Destroy(host);
            host = null;
            stop = null;
            notify = null;
            Running = loaded = arabicIdle = false;
        }

        sealed class Runner : MonoBehaviour
        {
            bool decided;

            void Update()
            {
                if (!decided) { decided = true; Decide(); return; }
                // Auto-fix waits for the font: without it, fixed Arabic would be drawn as boxes. It is turned on once, so a mod can turn it off.
                if (Running && autoFixWanted && !arabicIdle && Arabic.FontReady())
                {
                    autoFixWanted = false;
                    Arabic.EnableAutoFix();
                    Log.Debug("the Arabic font is ready, auto-fix is on");
                }
            }
        }
    }

    /// <summary>
    /// What other mods call. Use it from the main thread after the game has started.
    /// Quick start: <c>myTmpText.SetArabic("هلا");</c> or <c>myLegacyText.SetArabic("هلا");</c>
    /// </summary>
    public static class Arabic
    {
        // The font is not built into the mod: Content downloads it into the shared Beacon folder, and only this exact file is used.
        internal const string FontFile = "NotoSansArabic-Regular.ttf";
        internal const string FontSha256 = "bd86ca02f087d7f3c3788ba458fb6b73744c7639ed276b8d870dba6def6c40d0";
        internal static string FontPath { get { return Path.Combine(Path.Combine(Core.Folder, "Fonts"), FontFile); } }

        static TMP_FontAsset fontAsset;
        static bool fontTried;
        static readonly List<TMP_FontAsset> patched = new List<TMP_FontAsset>();

        /// <summary>Text a left-to-right renderer draws correctly: joined letters, display order, mixed English and digits kept right.
        /// Text without Arabic letters comes back unchanged, and fixed text is left alone if fixed again.</summary>
        public static string Fix(string text, ShapeOptions options = null) { return ArabicShaper.Shape(text, options ?? defaultOptions); }

        static ShapeOptions defaultOptions = new ShapeOptions();

        /// <summary>The shaping options used whenever a call is given none: <see cref="Fix"/>, <see cref="Wrap"/>, <see cref="TextExtensions.SetArabic(TMP_Text, string, ShapeOptions)"/>,
        /// auto-fix and notifications. Change a field (<c>Arabic.DefaultOptions.Digits = DigitStyle.ArabicIndic;</c>) or replace the object;
        /// null puts the built-in defaults back. Text already drawn is not shaped again until it changes.</summary>
        public static ShapeOptions DefaultOptions
        {
            get { return defaultOptions; }
            set { defaultOptions = value ?? new ShapeOptions(); }
        }

        // The default options with one direction, for the places that must force it (notifications are always right to left for Arabic).
        internal static ShapeOptions DefaultWith(TextDirection direction)
        {
            ShapeOptions d = defaultOptions;
            return new ShapeOptions { Direction = direction, Digits = d.Digits, KeepDiacritics = d.KeepDiacritics, RichText = d.RichText };
        }

        /// <summary>True if the text has any Arabic letters (fixed or not).</summary>
        public static bool IsArabic(string text) { return ArabicShaper.ContainsArabic(text); }

        /// <summary>The Arabic font as a TextMeshPro font asset, made on first use. Null until the font has been downloaded into the Beacon folder,
        /// or if it could not be made (the reason is in the log). It has Arabic only: no Latin letters and no brackets. Use <see cref="EnableFor"/>
        /// to put it behind a text's own font instead.</summary>
        public static TMP_FontAsset FontAsset
        {
            get
            {
                if (fontAsset != null || fontTried || !FontReady()) return fontAsset;
                fontTried = true;
                try
                {
                    string path = FontPath;
                    fontAsset = TMP_FontAsset.CreateFontAsset(path, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
                    if (fontAsset == null) { Log.Error("The Arabic font asset could not be made from " + path); return null; }
                    fontAsset.name = "Beacon Arabic";
                    // Noto's line height is 2.1 times its size, the game's fonts 1.15: a line with any Arabic in it would stand twice as far apart.
                    FaceInfo face = fontAsset.faceInfo;
                    face.lineHeight = face.pointSize * 1.45f;
                    face.ascentLine = face.pointSize * 1.0f;
                    face.descentLine = -face.pointSize * 0.4f;
                    fontAsset.faceInfo = face;
                    fontAsset.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    if (fontAsset.material != null) fontAsset.material.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    if (fontAsset.atlasTextures != null)
                        foreach (Texture2D t in fontAsset.atlasTextures) if (t != null) t.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    Log.Debug("Arabic font ready (" + fontAsset.atlasPopulationMode + ")");
                }
                catch (Exception e) { Log.Error("The Arabic font failed: " + e); fontAsset = null; }
                return fontAsset;
            }
        }

        static TMP_FontAsset gameFont;
        static bool gameFontTried;

        /// <summary>The game's own default text font (one with Latin letters and brackets), or null if none was found.</summary>
        internal static TMP_FontAsset GameFont
        {
            get
            {
                if (gameFont != null || gameFontTried) return gameFont;
                gameFontTried = true;
                try
                {
                    TMP_FontAsset d = TMP_Settings.defaultFontAsset;
                    if (d != null && d != fontAsset && d.HasCharacter('A') && d.HasCharacter('(')) gameFont = d;
                }
                catch (Exception) { }   // TMP_Settings can be missing this early
                if (gameFont == null)
                    foreach (TMP_FontAsset f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                        if (f != null && f != fontAsset && f.HasCharacter('A') && f.HasCharacter('(')) { gameFont = f; break; }
                Log.Debug("Game font for notifications: " + (gameFont != null ? gameFont.name : "none found, using the Arabic font only"));
                return gameFont;
            }
        }

        static string languageOverride;

        // The Arabic for a mod's English text from the shared translations, when this player reads Arabic; otherwise the text itself.
        internal static string Translate(string text) { return Translations.Count > 0 && Language == "ar" ? Translations.For(text) : text; }

        /// <summary>This player's language, as Beacon tells other players: "ar" or "en". By default the one the player picked when Beacon first ran,
        /// else "ar" when the game's system language is Arabic, otherwise "en". Set it to force one; set null to follow the player's choice again.</summary>
        public static string Language
        {
            get { return languageOverride ?? Settings.Language ?? (Application.systemLanguage == SystemLanguage.Arabic ? "ar" : "en"); }
            set { languageOverride = value; }
        }

        /// <summary>true: notifications are hidden from the game's spectator ("Shoulder") camera, so a stream or a recording of that view
        /// does not show them. Default false. Note that without a headset (Desktop Mode) the game window itself shows that camera.</summary>
        public static bool HideNotificationsFromSpectators { get; set; }

        /// <summary>Shows a notification in front of the player's view. The text is wrapped and fixed for Arabic; repeats are counted (×2).
        /// It queues, so it is safe to call before the game has finished loading.</summary>
        /// <param name="source">Optional name shown in front, such as your mod's name: "[Source] text".</param>
        public static void Notify(string text, NotifyKind kind = NotifyKind.Info, float seconds = -1f, string source = null)
        {
            Notify(text, NotificationSettings.ColorOf(kind), seconds, source);
        }

        // Beacon's own messages (the setup's confirmation, and the owner's messages) are gold; a mod's notifications use the kinds above.
        internal static readonly Color BeaconGold = new Color(1f, 0.8f, 0.2f);
        internal static void Say(string text, float seconds = -1f, bool announcement = false)
        {
            Notify(text, BeaconGold, seconds);
            Sound.Chime(announcement);
        }

        /// <summary>Same with your own colour. Seconds below 0 (the default) mean <see cref="NotificationSettings.DefaultSeconds"/>.</summary>
        public static void Notify(string text, Color color, float seconds = -1f, string source = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            NotificationPanel.Enqueue(text, source, color, Mathf.Max(0.5f, seconds < 0f ? NotificationSettings.DefaultSeconds : seconds));
        }

        /// <summary>Breaks the text into lines that fit <paramref name="maxWidth"/> (in the text's own units) and fixes them for Arabic.
        /// Use it instead of <see cref="Fix"/> when a long text must wrap: a fixed line is in display order, so TextMeshPro's own wrapping
        /// would cut the wrong end of it. Turn off the text's own wrapping and set the result as its text.</summary>
        public static string Wrap(TMP_Text text, string value, float maxWidth, ShapeOptions options = null)
        {
            if (text == null) return value;
            return ArabicShaper.Wrap(value, s => text.GetPreferredValues(s).x, maxWidth, options ?? defaultOptions);
        }

        // Used by auto-fix. Wrapping is on the logical text and only when the text wraps itself at a fixed size
        // (a shaped line is in display order, so TextMeshPro's own wrapping would cut it at the wrong end); 0 means no wrapping.
        internal static float WrapWidth(TMP_Text text)
        {
            TextWrappingModes mode = text.textWrappingMode;
            if ((mode != TextWrappingModes.Normal && mode != TextWrappingModes.PreserveWhitespace) || text.enableAutoSizing) return 0f;
            Vector4 margin = text.margin;
            return Mathf.Max(0f, text.rectTransform.rect.width - margin.x - margin.z);
        }

        // A tag in front draws a left-aligned text from the right. It needs rich text; the text's own alignment is not touched.
        internal static bool WantsRightAlign(TMP_Text text, bool enabled)
        {
            return enabled && text.richText && text.horizontalAlignment == HorizontalAlignmentOptions.Left;
        }

        internal static string Render(TMP_Text text, string logical, float wrapWidth, bool rightAlign)
        {
            string fixedText = wrapWidth > 0f ? Wrap(text, logical, wrapWidth) : Fix(logical);
            return rightAlign ? "<align=right>" + fixedText : fixedText;
        }

        /// <summary>Lets a TextMeshPro text draw Arabic: puts the Arabic font behind the text's own font (added once; the text keeps its look)
        /// and turns kerning off for that text, because the font's kerning pairs are written for logical order and pull joined letters apart.</summary>
        public static void EnableFor(TMP_Text text) { EnableFor(text, false); }

        // building: the text is being built right now (auto-fix). Assigning fontFeatures marks it dirty and asks for another rebuild inside the
        // rebuild, so the list is edited in place; the build that is running reads it afterwards.
        internal static void EnableFor(TMP_Text text, bool building)
        {
            if (text == null) return;
            if (text.fontFeatures.Contains(OTL_FeatureTag.kern))
            {
                if (building) text.fontFeatures.Remove(OTL_FeatureTag.kern);
                else
                {
                    var features = new List<OTL_FeatureTag>(text.fontFeatures);
                    features.Remove(OTL_FeatureTag.kern);
                    text.fontFeatures = features;
                }
            }
            TMP_FontAsset arabic = FontAsset;
            if (arabic == null || text.font == null || text.font == arabic) return;
            TMP_FontAsset own = text.font;
            if (own.fallbackFontAssetTable == null) own.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (own.fallbackFontAssetTable.Contains(arabic)) return;
            own.fallbackFontAssetTable.Add(arabic);
            patched.Add(own);
        }

        /// <summary>Turns on auto-fix: from now on every TextMeshPro and legacy <c>Text</c> in the game that is given Arabic is drawn fixed, and
        /// <c>.text</c> still returns what its owner set (nothing networked changes, only what is drawn). English text is left alone and costs one scan.
        /// Texts that wrap are wrapped on the Arabic text before it is fixed. Call it once from your mod; calling it again does nothing.
        /// Legacy <c>Text</c> is fixed per line (it is not wrapped), and a text that already went through <see cref="Fix"/> is left as it is.</summary>
        public static void EnableAutoFix() { AutoFix.Enable(); }

        /// <summary>Turns auto-fix off again. Texts already drawn stay as they are until they change.</summary>
        public static void DisableAutoFix() { AutoFix.Disable(); }

        /// <summary>True while auto-fix is on.</summary>
        public static bool AutoFixEnabled { get { return AutoFix.Enabled; } }

        /// <summary>Tells auto-fix to leave this text alone (or, with <c>false</c>, to fix it again). The text is shown as its owner set it, even if it has Arabic.
        /// A text that is already drawn changes the next time it is rebuilt. <see cref="TextExtensions.SetArabic(TMP_Text, string, ShapeOptions)"/> still works on it.</summary>
        public static void SkipAutoFix(TMP_Text text, bool skip = true) { AutoFix.Skip(text, skip); }

        /// <summary>Same for Unity's older <c>Text</c>.</summary>
        public static void SkipAutoFix(Text text, bool skip = true) { AutoFix.Skip(text, skip); }

        /// <summary>Default false. true: a TextMeshPro text that auto-fix fixes and that is left-aligned (the default) is drawn right-aligned, as Arabic is read.
        /// Texts aligned to the centre or the right are left as they are, and so are texts with rich text turned off. The text's own alignment setting is not changed.
        /// Legacy <c>Text</c> is not affected.</summary>
        public static bool AutoFixRightAlign
        {
            get { return AutoFix.RightAlign; }
            set { AutoFix.RightAlign = value; }
        }

        // Core.Unload: undo everything this library added to the game's fonts, so a reload starts clean.
        internal static void Cleanup()
        {
            AutoFix.Disable();   // before the font goes: a patched text must not look for a font asset that is destroyed
            foreach (TMP_FontAsset f in patched)
                if (f != null && f.fallbackFontAssetTable != null) f.fallbackFontAssetTable.Remove(fontAsset);
            patched.Clear();
            if (fontAsset != null)
            {
                if (fontAsset.atlasTextures != null) foreach (Texture2D t in fontAsset.atlasTextures) Destroy(t);
                Destroy(fontAsset.material);
                Destroy(fontAsset);
            }
            fontAsset = null;
            gameFont = null;
        }

        static void Destroy(UnityEngine.Object o) { if (o != null) UnityEngine.Object.Destroy(o); }

        static bool fontChecked;
        static DateTime fontSeen;

        /// <summary>True once the font file is in the Beacon folder with the expected SHA-256 (each version of the file is checked once).</summary>
        internal static bool FontReady()
        {
            if (fontChecked) return true;
            try
            {
                if (!File.Exists(FontPath)) return false;
                DateTime written = File.GetLastWriteTimeUtc(FontPath);
                if (written == fontSeen) return false;                     // this file was checked already and is not the font
                fontSeen = written;
                if (AssetManifest.Sha256Hex(File.ReadAllBytes(FontPath)) != FontSha256)
                {
                    Log.Warning(FontPath + " is not the expected font; it is not used");
                    return false;
                }
            }
            catch (Exception e) when (Util.IsIO(e)) { return false; }
            fontChecked = true;
            return true;
        }
    }

    /// <summary>One asset in the manifest: an id that players can ask for, and what to load for it.</summary>
    internal sealed class AssetEntry
    {
        public string Id;
        /// <summary>File name of the bundle inside the assets folder (never a path).</summary>
        public string Bundle;
        /// <summary>Lower-case SHA-256 of the whole bundle file; a file that does not match is never loaded.</summary>
        public string Sha256;
        /// <summary>Name of the prefab inside the bundle.</summary>
        public string Prefab;
        /// <summary>True when the prefab may carry box, sphere or capsule colliders.</summary>
        public bool Solid;
        /// <summary>Seconds the spawned object stays before it is destroyed.</summary>
        public float Lifetime;
        /// <summary>The longest side of the spawned object's bounds, in metres, that is still accepted.</summary>
        public float MaxSize;
        /// <summary>Most objects (transforms) the prefab may have. A big scene needs a bigger number here; see <see cref="AssetManifest.MaxObjectsLimit"/>.</summary>
        public int MaxObjects;
        /// <summary>Most particles, summed over the prefab's particle systems.</summary>
        public int MaxParticles;
        /// <summary>True lets the prefab carry components whose script is not in the bundle (they do nothing). Off unless the manifest says so.</summary>
        public bool AllowMissingScripts;
        /// <summary>True when the entry came from the hosted manifest: its bundle lives in the content folder and is downloaded when missing.</summary>
        public bool Hosted { get; set; }
    }

    /// <summary>What a Beacon message is for. The numbers are on the wire: never reuse 3 and 4 (older copies sent invites and phrases with them).</summary>
    internal enum MessageType : byte
    {
        Presence = 1,
        PresenceRequest = 2,
        Asset = 5,
        Show = 6,
        ShowStop = 7,
    }

    /// <summary>A message that passed <see cref="Wire.TryParse"/>. Only the fields of its type are filled.</summary>
    internal sealed class Message
    {
        public MessageType Type;
        public string Version;
        public string Language;
        public string AssetId;
        public float X, Y, Z, Yaw;
        public string ShowId;
        public int Start;
    }

    /// <summary>Ready-made colours for <c>Arabic.Notify</c>.</summary>
    public enum NotifyKind { Info, Success, Warning, Error }

    /// <summary>How notifications look and where they sit. Set these once from your mod (in <c>Awake</c>, say).
    /// Position, Gap, MaxEntries, the fades and the colours of new notifications change at once; Scale, Width, FontSize and Font apply to notifications made after the change.</summary>
    public static class NotificationSettings
    {
        /// <summary>Where the stack sits, in metres from the camera: x = right edge, y = top edge, z = distance. Default (0.55, 0.40, 1.4). Negative x moves it left of the view's centre.</summary>
        public static Vector3 Position { get; set; } = new Vector3(0.55f, 0.40f, 1.4f);
        /// <summary>Metres per text unit, so the size of the letters. Default 0.012.</summary>
        public static float Scale { get; set; } = 0.012f;
        /// <summary>How wide a line gets before it wraps, in text units. Default 80.</summary>
        public static float Width { get; set; } = 80f;
        /// <summary>Font size in text units. Default 36.</summary>
        public static float FontSize { get; set; } = 36f;
        /// <summary>Metres between two notifications. Default 0.012.</summary>
        public static float Gap { get; set; } = 0.012f;
        /// <summary>How many show at once; the oldest go first. Default 6.</summary>
        public static int MaxEntries { get; set; } = 6;
        /// <summary>Seconds to fade in and out. Defaults 0.15 and 0.6.</summary>
        public static float FadeIn { get; set; } = 0.15f;
        public static float FadeOut { get; set; } = 0.6f;
        /// <summary>How long a notification stays when <c>Notify</c> is called without seconds. Default 4.</summary>
        public static float DefaultSeconds { get; set; } = 4f;
        /// <summary>Your own font for notifications. Null (default) uses the game's font, with the Arabic font behind it.</summary>
        public static TMP_FontAsset Font { get; set; }
        /// <summary>The colours of the four <see cref="NotifyKind"/> values.</summary>
        public static Color InfoColor { get; set; } = Color.white;
        public static Color SuccessColor { get; set; } = new Color(0.45f, 0.90f, 0.50f);
        public static Color WarningColor { get; set; } = new Color(1.00f, 0.80f, 0.30f);
        public static Color ErrorColor { get; set; } = new Color(1.00f, 0.40f, 0.40f);

        internal static Color ColorOf(NotifyKind kind)
        {
            switch (kind)
            {
                case NotifyKind.Success: return SuccessColor;
                case NotifyKind.Warning: return WarningColor;
                case NotifyKind.Error: return ErrorColor;
                default: return InfoColor;
            }
        }
    }

    /// <summary>Another player in the room who is running Beacon too.</summary>
    public sealed class Peer
    {
        /// <summary>The player's number in the room.</summary>
        public int Actor { get; internal set; }
        /// <summary>The Beacon version they run, such as "0.0.1".</summary>
        public string Version { get; internal set; }
        /// <summary>Their language, such as "ar" or "en".</summary>
        public string Language { get; internal set; }
        /// <summary>Their name in the room, cleaned so it is safe to draw: no tags, no control characters, at most 16 characters.</summary>
        public string Nickname { get; internal set; }
        /// <summary>Their account id as the room shows it. Beacon uses it only on this machine, to remember who you muted; it is never sent anywhere.</summary>
        public string UserId { get; internal set; }
    }

    /// <summary>Which way a line runs. Auto follows the first strong letter, like a browser.</summary>
    public enum TextDirection { Auto, Rtl, Ltr }

    /// <summary>What to do with the digits 0-9 in text that contains Arabic.</summary>
    public enum DigitStyle { Keep, ArabicIndic, Western }

    public sealed class ShapeOptions
    {
        public TextDirection Direction = TextDirection.Auto;
        public DigitStyle Digits = DigitStyle.Keep;
        /// <summary>false removes the small vowel marks (fatha, kasra, shadda...).</summary>
        public bool KeepDiacritics = true;
        /// <summary>true keeps TextMeshPro tags such as &lt;color=red&gt; in place around the right letters.</summary>
        public bool RichText = true;
    }

    /// <summary>The <c>SetArabic</c> extension methods: <c>myTmpText.SetArabic("text")</c> and the same on Unity's older <c>Text</c>.</summary>
    public static class TextExtensions
    {
        /// <summary>Sets the text, fixed for Arabic, and makes sure the text's font can draw it.</summary>
        public static void SetArabic(this TMP_Text text, string value, ShapeOptions options = null)
        {
            if (text == null) return;
            if (Arabic.IsArabic(value)) Arabic.EnableFor(text);   // before the text is set, so the new text is built with the fallback
            text.text = Arabic.Fix(value, options);
        }

        /// <summary>Same for Unity's older <c>Text</c>: sets the fixed text. It does not change the font: a dynamic font such as the built-in one
        /// draws Arabic through the system's fallback fonts (seen in game); a font with no Arabic and no fallback would still show boxes.</summary>
        public static void SetArabic(this Text text, string value, ShapeOptions options = null)
        {
            if (text != null) text.text = Arabic.Fix(value, options);
        }
    }

    /// <summary>
    /// Loads the asset bundles listed in <c>Beacon\assets\manifest.json</c> (beside the BepInEx folder) and spawns their prefabs.
    /// A bundle is hashed and compared with the manifest before Unity reads it, a prefab is checked against the component allowlist
    /// before it is instantiated, and what is spawned is capped in size, lifetime and number. Nothing here touches the network:
    /// <see cref="Network"/> decides who may ask.
    /// </summary>
    internal static class AssetLoader
    {
        // An asset is not spawned this close to the camera, so a request cannot put an object over someone's eyes.
        const float MinDistance = 1.5f;
        const int MaxActive = 8;

        static readonly Dictionary<string, AssetEntry> byId = new Dictionary<string, AssetEntry>();
        static readonly Dictionary<string, AssetBundle> bundles = new Dictionary<string, AssetBundle>();
        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, string> refused = new Dictionary<string, string>();
        static readonly List<GameObject> active = new List<GameObject>();
        static bool loaded;

        // Bundles load in the background (no frozen frame); one with nothing spawned from it for UnloadAfter seconds is unloaded.
        const float UnloadAfter = 60f;
        static readonly HashSet<string> loading = new HashSet<string>();
        static readonly Dictionary<GameObject, string> spawnedFrom = new Dictionary<GameObject, string>();
        static readonly Dictionary<string, float> lastUsed = new Dictionary<string, float>();
        static readonly Dictionary<string, string> fileOf = new Dictionary<string, string>();   // asset id -> its bundle file
        static Worker worker;

        /// <summary>True while the asset's bundle is being read and loaded, or downloaded: a spawn asked for now is refused with "loading".</summary>
        internal static bool Loading(string id) { return id != null && (loading.Contains(id) || Content.Downloading(id)); }

        internal static string Folder { get { return Path.Combine(Core.Folder, "assets"); } }

        /// <summary>How many objects are alive now.</summary>
        internal static int ActiveCount { get { Prune(); return active.Count; } }

        /// <summary>The ids in the manifest for this platform.</summary>
        internal static List<string> Ids()
        {
            Load();
            return new List<string>(byId.Keys);
        }

        internal static bool Known(string id)
        {
            Load();
            return id != null && byId.ContainsKey(id);
        }

        /// <summary>"windows" or "android": manifest entries for other platforms are skipped.</summary>
        internal static string Platform { get { return Application.platform == RuntimePlatform.Android ? "android" : "windows"; } }

        /// <summary>Destroys what was spawned and unloads the bundles; the manifest is read again on the next request.</summary>
        internal static void Cleanup()
        {
            if (worker != null) UnityEngine.Object.Destroy(worker.gameObject);   // stops a load half way
            worker = null;
            loading.Clear();
            foreach (GameObject go in active) if (go != null) UnityEngine.Object.Destroy(go);
            active.Clear();
            spawnedFrom.Clear();
            prefabs.Clear();
            refused.Clear();
            foreach (AssetBundle b in bundles.Values) b?.Unload(true);
            bundles.Clear();
            lastUsed.Clear();
            fileOf.Clear();
            byId.Clear();
            loaded = false;
        }

        /// <summary>Spawns the asset and hands back the copy. Returns null on success, else why it was not spawned (and the copy is null).</summary>
        internal static string Spawn(string id, Vector3 position, float yaw, out GameObject spawned)
        {
            spawned = null;
            Load();
            if (id == null || !byId.TryGetValue(id, out AssetEntry entry)) return "no asset with that id in the manifest";

            GameObject prefab = Prefab(entry, out string why);
            if (prefab == null) return why;

            Camera camera = Camera.main;
            if (camera != null && (camera.transform.position - position).sqrMagnitude < MinDistance * MinDistance) return "too close to the camera";

            Prune();
            while (active.Count >= MaxActive)
            {
                if (active[0] != null) UnityEngine.Object.Destroy(active[0]);
                active.RemoveAt(0);
            }

            GameObject go = UnityEngine.Object.Instantiate(prefab, position, Quaternion.Euler(0f, yaw, 0f));
            go.name = "Beacon_Asset_" + entry.Id;
            float size = Size(go);
            if (size > entry.MaxSize)
            {
                UnityEngine.Object.Destroy(go);
                return Refuse(entry.Id, "its bounds are " + size.ToString("0.0") + " m long, more than the manifest's maxSize of " + entry.MaxSize + " m (largest part: " + Largest(go) + ")");
            }
            Limit(go);
            UnityEngine.Object.Destroy(go, entry.Lifetime);
            active.Add(go);
            spawnedFrom[go] = fileOf[entry.Id];
            spawned = go;
            return null;
        }

        // The local manifest first, then the hosted one; an id in both keeps the local entry.
        static void Load()
        {
            if (loaded) return;
            loaded = true;
            Read(Folder, false);
            Read(Content.Folder, true);
            Log.Debug("assets: " + byId.Count + " in the manifests");
        }

        static void Read(string folder, bool hosted)
        {
            string path = Path.Combine(folder, "manifest.json");
            string text = Util.ReadText(path);
            if (text == null) { Log.Debug("assets: no manifest at " + path); return; }
            var problems = new List<string>();
            List<AssetEntry> entries = AssetManifest.Parse(text, Platform, problems);
            foreach (string p in problems) Log.Warning("assets (" + path + "): " + p);
            if (entries == null) return;
            foreach (AssetEntry e in entries)
            {
                if (byId.ContainsKey(e.Id)) { Log.Debug("assets: '" + e.Id + "' is in the local manifest, so the hosted one is not used"); continue; }
                e.Hosted = hosted;
                byId[e.Id] = e;
            }
        }

        // The prefab for an entry, checked once: the file's hash, then Unity's load, then the allowlist. The first ask starts the
        // load in the background and answers "loading"; a refusal is remembered until Reload.
        static GameObject Prefab(AssetEntry entry, out string why)
        {
            why = null;
            if (prefabs.TryGetValue(entry.Id, out GameObject cached)) return cached;
            if (refused.TryGetValue(entry.Id, out why)) return null;
            if (Loading(entry.Id)) { why = "its bundle is loading"; return null; }

            string file = Path.Combine(entry.Hosted ? Content.Folder : Folder, entry.Bundle);
            if (!File.Exists(file))
            {
                // A hosted bundle that is not here yet is fetched; Content checks the hash before it saves.
                why = entry.Hosted ? Content.Download(entry) ?? "its bundle is downloading" : "the bundle file " + entry.Bundle + " is not in the assets folder";
                return null;
            }
            loading.Add(entry.Id);
            if (worker == null) worker = Util.Host<Worker>("Beacon_Assets");
            worker.StartCoroutine(LoadAsync(entry, file));
            why = "its bundle is loading";
            return null;
        }

        static IEnumerator LoadAsync(AssetEntry entry, string file)
        {
            float started = Time.realtimeSinceStartup;
            byte[] bytes = null;
            bool tooBig = false, matches = false;
            // Reading and hashing tens of megabytes would hold up a frame, so they run on a worker thread (no Unity calls there).
            Task read = Task.Run(() =>
            {
                if (new FileInfo(file).Length > AssetManifest.MaxBundleBytes) { tooBig = true; return; }
                bytes = File.ReadAllBytes(file);
                matches = AssetManifest.HashMatches(bytes, entry.Sha256);
            });
            while (!read.IsCompleted) yield return null;

            string why = null;
            if (read.IsFaulted) why = "could not read the bundle: " + read.Exception.GetBaseException().Message;
            else if (tooBig) why = Refuse(entry.Id, "the bundle is larger than " + AssetManifest.MaxBundleBytes + " bytes");
            else if (!matches && !entry.Hosted) why = Refuse(entry.Id, "the bundle's SHA-256 does not match the manifest");
            else if (!matches)
            {
                // An older hosted bundle: fetch the one the manifest names. A download that failed is not tried again until Reload.
                string failed = Content.Download(entry);
                if (failed != null) why = Refuse(entry.Id, failed);
            }
            if (why != null || !matches)
            {
                loading.Remove(entry.Id);
                if (why != null) Log.Warning("assets: '" + entry.Id + "' not loaded: " + why);
                yield break;
            }

            // The bytes that were hashed are the bytes Unity loads, so the file cannot change in between.
            if (!bundles.TryGetValue(file, out AssetBundle bundle))
            {
                AssetBundleCreateRequest create = AssetBundle.LoadFromMemoryAsync(bytes);
                yield return create;
                bundle = create.assetBundle;
                if (bundle == null) { Fail(entry, "Unity could not load the bundle"); yield break; }
                bundles[file] = bundle;
            }
            AssetBundleRequest asset = bundle.LoadAssetAsync<GameObject>(entry.Prefab);
            yield return asset;
            var prefab = asset.asset as GameObject;
            if (prefab == null) { Fail(entry, "the bundle has no prefab named " + entry.Prefab); yield break; }

            string problem = Validate(prefab, entry);
            if (problem != null) { Fail(entry, problem); yield break; }
            prefabs[entry.Id] = prefab;
            fileOf[entry.Id] = file;
            lastUsed[file] = Time.realtimeSinceStartup;
            loading.Remove(entry.Id);
            Log.Debug("assets: '" + entry.Id + "' checked and ready in the background (" + (Time.realtimeSinceStartup - started).ToString("0.00") +
                               " s to read, hash, load and check " + bytes.Length + " bytes)");
        }

        static void Fail(AssetEntry entry, string reason)
        {
            Refuse(entry.Id, reason);
            loading.Remove(entry.Id);
            Log.Warning("assets: '" + entry.Id + "' not loaded: " + reason);
        }

        // Unloads a bundle (and the prefabs from it) once nothing spawned from it has been alive for UnloadAfter seconds,
        // and nothing is loading: its next use loads and checks it again.
        static void UnloadIdle()
        {
            Prune();
            float now = Time.realtimeSinceStartup;
            foreach (string file in spawnedFrom.Values) lastUsed[file] = now;
            if (loading.Count > 0) return;
            foreach (string file in new List<string>(bundles.Keys))
            {
                if (lastUsed.TryGetValue(file, out float used) && now - used < UnloadAfter) continue;
                bundles[file]?.Unload(true);
                bundles.Remove(file);
                lastUsed.Remove(file);
                foreach (KeyValuePair<string, string> f in new List<KeyValuePair<string, string>>(fileOf))
                    if (f.Value == file) { prefabs.Remove(f.Key); fileOf.Remove(f.Key); }
                Log.Debug("assets: unloaded " + Path.GetFileName(file) + " (unused for " + UnloadAfter.ToString("0") + " s)");
            }
        }

        sealed class Worker : MonoBehaviour
        {
            float nextCheck;

            void Update()
            {
                if (Time.realtimeSinceStartup < nextCheck) return;
                nextCheck = Time.realtimeSinceStartup + 5f;
                try { UnloadIdle(); }
                catch (Exception e) { Log.Warning("assets: unloading failed: " + e.Message); }
            }
        }

        // Runs on the prefab asset, before anything is instantiated: an un-instantiated prefab runs no Awake or OnEnable.
        static string Validate(GameObject prefab, AssetEntry entry)
        {
            Component[] all = prefab.GetComponentsInChildren<Component>(true);
            var names = new List<string>(all.Length);
            foreach (Component c in all) names.Add(c?.GetType().FullName);
            string problem = AssetManifest.CheckComponents(names, entry.Solid, entry.AllowMissingScripts, entry.MaxObjects * 2);
            if (problem != null) return problem;

            int objects = prefab.GetComponentsInChildren<Transform>(true).Length;
            if (objects > entry.MaxObjects) return objects + " objects, more than the manifest's maxObjects of " + entry.MaxObjects;
            int particles = 0;
            foreach (ParticleSystem ps in prefab.GetComponentsInChildren<ParticleSystem>(true)) particles += ps.main.maxParticles;
            if (particles > entry.MaxParticles) return particles + " particles, more than the manifest's maxParticles of " + entry.MaxParticles;
            return null;
        }

        static string Refuse(string id, string reason)
        {
            refused[id] = reason;
            return reason;
        }

        // Audio and light are clamped on the spawned copy; the prefab itself stays as the author made it.
        static void Limit(GameObject go)
        {
            foreach (AudioSource a in go.GetComponentsInChildren<AudioSource>(true)) a.volume = Mathf.Min(a.volume, AssetManifest.MaxAudioVolume);
            foreach (Light l in go.GetComponentsInChildren<Light>(true))
            {
                l.intensity = Mathf.Min(l.intensity, AssetManifest.MaxLightIntensity);
                l.range = Mathf.Min(l.range, AssetManifest.MaxLightRange);
            }
        }

        // The longest side of the box around every renderer, in metres.
        static float Size(GameObject go)
        {
            Vector3 s = Util.RendererBounds(go).size;
            return Mathf.Max(s.x, Mathf.Max(s.y, s.z));
        }

        // The renderer with the longest side, for the log when something is too big.
        static string Largest(GameObject go)
        {
            string name = "none";
            float best = 0f;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Vector3 s = r.bounds.size;
                float side = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                if (side <= best) continue;
                best = side;
                name = r.GetType().Name + " '" + r.name + "' " + side.ToString("0.0") + " m";
            }
            return name;
        }

        static void Prune()
        {
            active.RemoveAll(go => go == null);
            foreach (GameObject go in new List<GameObject>(spawnedFrom.Keys)) if (go == null) spawnedFrom.Remove(go);
        }
    }

    /// <summary>
    /// A gold "BEACON ADMIN" tag above each Beacon admin in the room, only to show who the admins are. It is drawn on this player's
    /// screen only and nothing is sent: who runs Beacon comes from presence, who is an admin from the admin list.
    /// The game's player rigs are read by reflection (VRRigCache.GetActiveRigs, which only fills a list), so no game type is referenced.
    /// </summary>
    internal sealed class Badges : MonoBehaviour
    {
        static readonly Color AdminColor = new Color(1f, 0.8f, 0.2f);
        const float Above = 0.5f;         // metres above the head at the game's normal size
        const float TextScale = 0.03f;

        readonly Dictionary<Component, TextMeshPro> tags = new Dictionary<Component, TextMeshPro>();
        readonly Dictionary<Component, Transform> heads = new Dictionary<Component, Transform>();
        PropertyInfo cacheInstance, creatorProperty;
        MethodInfo getRigs;
        FieldInfo headField;
        IList rigs;
        bool ready, failed;
        float nextScan;

        void LateUpdate()
        {
            try
            {
                if (Time.realtimeSinceStartup >= nextScan)
                {
                    nextScan = Time.realtimeSinceStartup + 0.5f;
                    Scan();
                }
                Place();
            }
            catch (Exception e) { Log.WarnOnce(ref failed, "badges failed: " + e.GetType().Name + ": " + e.Message); }
        }

        bool Ready()
        {
            if (ready) return true;
            Type cacheType = Util.GameType("VRRigCache"), rigType = Util.GameType("VRRig");
            if (cacheType == null || rigType == null) return false;
            cacheInstance = cacheType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            getRigs = cacheType.GetMethod("GetActiveRigs", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            creatorProperty = rigType.GetProperty("Creator");
            headField = rigType.GetField("headMesh");
            if (cacheInstance == null || getRigs == null || creatorProperty == null || headField == null)
            {
                Log.WarnOnce(ref failed, "badges are off: the game's player rigs have changed");
                return false;
            }
            rigs = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rigType));
            ready = true;
            return true;
        }

        // Which rigs belong to Beacon admins.
        void Scan()
        {
            var wanted = new HashSet<Component>();
            object cache = Ready() ? cacheInstance.GetValue(null, null) : null;
            if (cache != null && Network.Enabled)
            {
                var beacon = new HashSet<int>();
                foreach (Peer p in Network.Peers) beacon.Add(p.Actor);
                getRigs.Invoke(cache, new object[] { rigs });
                foreach (object o in rigs)
                {
                    var rig = o as Component;
                    object player = rig == null ? null : creatorProperty.GetValue(rig, null);
                    if (player == null || (bool)Read(player, "IsLocal")) continue;
                    if (!beacon.Contains((int)Read(player, "ActorNumber")) || !Network.IsAdmin(Read(player, "UserId") as string)) continue;
                    wanted.Add(rig);
                    var head = headField.GetValue(rig) as GameObject;
                    if (head != null) heads[rig] = head.transform;
                }
            }
            foreach (Component rig in new List<Component>(tags.Keys))
                if (rig == null || !wanted.Contains(rig)) { Destroy(tags[rig].gameObject); tags.Remove(rig); heads.Remove(rig); }
            foreach (Component rig in wanted)
                if (!tags.ContainsKey(rig)) tags[rig] = MakeTag();
        }

        // Above each head, at the player's size, turned to face this player's camera.
        void Place()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            foreach (KeyValuePair<Component, TextMeshPro> pair in tags)
            {
                if (pair.Key == null || !heads.TryGetValue(pair.Key, out Transform head) || head == null) { pair.Value.gameObject.SetActive(false); continue; }
                float size = Mathf.Max(0.05f, pair.Key.transform.lossyScale.x);
                Vector3 at = head.position + Vector3.up * Above * size;
                Transform t = pair.Value.transform;
                t.position = at;
                t.rotation = Quaternion.LookRotation(at - cam.transform.position);
                t.localScale = Vector3.one * TextScale * size;
                pair.Value.gameObject.SetActive(true);
            }
        }

        TextMeshPro MakeTag()
        {
            var go = new GameObject("Beacon_Badge");
            go.transform.SetParent(transform, false);
            var t = go.AddComponent<TextMeshPro>();
            TMP_FontAsset font = Arabic.GameFont;
            if (font != null) t.font = font;
            t.fontSize = 36f;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.text = "BEACON ADMIN";
            t.color = AdminColor;
            return t;
        }

        static object Read(object o, string property)
        {
            return o.GetType().GetProperty(property).GetValue(o, null);
        }
    }

    /// <summary>The player's own choices, kept in settings.json in the shared Beacon folder so every mod carrying Beacon uses them: the language.</summary>
    internal static class Settings
    {
        internal static string FilePath { get { return Path.Combine(Core.Folder, "settings.json"); } }

        /// <summary>"ar" or "en" once the player has chosen; null before.</summary>
        internal static string Language { get; private set; }

        internal static void Load() { Language = Read(Util.ReadText(FilePath)); }

        /// <summary>The language in settings.json's text (<c>{"version": 1, "language": "ar"}</c>), or null if it holds none.</summary>
        internal static string Read(string json)
        {
            Dictionary<string, object> d;
            try { d = Json.Parse(json) as Dictionary<string, object>; }
            catch (FormatException) { return null; }
            string language = d != null && d.TryGetValue("version", out object v) && v is double n && n == 1 ? Json.Text(d, "language") : null;
            return language == "ar" || language == "en" ? language : null;
        }

        internal static void Choose(string language)
        {
            Language = language;
            string why = Util.WriteText(FilePath, "{\"version\": 1, \"language\": \"" + language + "\"}");
            Log.Debug(why == null ? "setup: the player chose " + language : "setup: " + why);
        }
    }

    /// <summary>
    /// The first time Beacon runs on a PC, a panel in front of a VR player thanks them for having Beacon and asks for their language:
    /// they point either controller at English or العربية and pull the trigger. Without a headset it is not shown (the system language is used).
    /// </summary>
    internal sealed class Welcome : MonoBehaviour
    {
        const float Distance = 1.1f, Below = 0.08f, FontWait = 60f, TextScale = 0.012f;
        static readonly Color Gold = Arabic.BeaconGold, Ink = new Color(0.055f, 0.06f, 0.075f), Idle = new Color(1f, 1f, 1f, 0.08f);

        sealed class Option { public string Language; public TextMeshPro Text; public Transform Box; public Material Fill; public Vector2 Size; public float Lit; }

        GameObject panel;
        Option[] options;
        TextMeshPro cursor;
        Vector2 card;
        readonly List<KeyValuePair<Material, Color>> fills = new List<KeyValuePair<Material, Color>>();
        readonly List<KeyValuePair<TextMeshPro, Color>> labels = new List<KeyValuePair<TextMeshPro, Color>>();
        readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();
        readonly bool[] held = { true, true };   // a trigger already held when the panel appears does not count
        float began, shown;
        bool following, leaving;
        Shader uber;
        Type taggerType, pollerType;

        void Start() { began = Time.realtimeSinceStartup; }

        void Update()
        {
            try
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                if (panel == null)
                {
                    if (!cam.stereoEnabled || !Spawned() || (!Arabic.FontReady() && Time.realtimeSinceStartup - began < FontWait)) return;
                    Build(cam);
                }
                float dt = Time.unscaledDeltaTime;
                Follow(cam, dt);
                if (!leaving) Point(dt);
                shown = Mathf.MoveTowards(shown, leaving ? 0f : 1f, dt / 0.3f);
                Paint();
                if (leaving && shown <= 0f) Destroy(this);
            }
            catch (Exception e) { Log.Warning("setup failed, the system language is used: " + e.Message); Destroy(this); }
        }

        void OnDestroy()
        {
            if (panel != null) Destroy(panel);
            foreach (UnityEngine.Object o in made) if (o != null) Destroy(o);
        }

        bool Spawned()
        {
            if (taggerType == null) taggerType = Util.GameType("GorillaTagger");
            FieldInfo f = taggerType?.GetField("hasInstance", BindingFlags.Public | BindingFlags.Static);
            return f != null && f.GetValue(null) is bool yes && yes;
        }

        void Build(Camera cam)
        {
            uber = Shader.Find("GorillaTag/UberShader");
            if (uber == null) Log.Warning("setup: the game's shader was not found, the panel is drawn without its card");
            panel = new GameObject("Beacon_Welcome");
            panel.transform.SetParent(transform, false);

            TextMeshPro head = Label("BEACON", 0.19f, 22f, Gold);
            head.fontStyle = FontStyles.Bold;
            head.characterSpacing = 40f;
            Label("Thanks for having Beacon!", 0.125f, 40f, Color.white);
            Label("شكراً إنك حمّلت Beacon!", 0.066f, 34f, new Color(1f, 1f, 1f, 0.85f));
            Label("Pick your language  ·  اختار لغتك", -0.025f, 26f, new Color(1f, 1f, 1f, 0.7f));
            Label("Point and pull the trigger  ·  أشّر واضغط التريقر", -0.195f, 20f, new Color(1f, 1f, 1f, 0.45f));
            options = new[] { Button("en", "English"), Button("ar", "العربية") };

            // The card is as wide as the widest line, the buttons share one width.
            float wide = 0f, button = 0f;
            foreach (var l in labels) { l.Key.ForceMeshUpdate(); wide = Mathf.Max(wide, l.Key.textBounds.size.x * TextScale); }
            foreach (Option o in options) { o.Text.ForceMeshUpdate(); button = Mathf.Max(button, o.Text.textBounds.size.x * TextScale + 0.08f); }
            card = new Vector2(Mathf.Max(wide, 2f * button + 0.03f) + 0.1f, 0.47f);
            for (int i = 0; i < options.Length; i++)
            {
                Option o = options[i];
                o.Size = new Vector2(button, 0.078f);
                float x = (i == 0 ? -1f : 1f) * (button / 2f + 0.015f);
                o.Text.transform.localPosition = new Vector3(x, -0.105f, 0f);
                o.Box = Shape("Button", RoundedRect(o.Size.x, o.Size.y, 0.024f), new Vector3(x, -0.105f, 0.002f), 2995, Idle, out o.Fill, false);
            }
            Shape("Card", RoundedRect(card.x, card.y, 0.035f), new Vector3(0f, 0f, 0.004f), 2990, new Color(Ink.r, Ink.g, Ink.b, 0.92f), out _, true);
            Shape("Edge", RoundedRect(card.x + 0.008f, card.y + 0.008f, 0.039f), new Vector3(0f, 0f, 0.005f), 2989, new Color(Gold.r, Gold.g, Gold.b, 0.55f), out _, true);
            Shape("Line", RoundedRect(card.x - 0.12f, 0.002f, 0.001f), new Vector3(0f, 0.022f, 0.003f), 2991, new Color(1f, 1f, 1f, 0.12f), out _, true);

            cursor = Text("●", 0f, 26f);
            cursor.color = Gold;
            cursor.gameObject.SetActive(false);
            Place(cam);
            Paint();
            Log.Debug("setup: showing the welcome panel");
        }

        TextMeshPro Text(string text, float y, float size)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(panel.transform, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.transform.localScale = Vector3.one * TextScale;
            var t = go.AddComponent<TextMeshPro>();
            if (Arabic.GameFont != null) t.font = Arabic.GameFont;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.SetArabic(text);
            return t;
        }

        TextMeshPro Label(string text, float y, float size, Color color)
        {
            TextMeshPro t = Text(text, y, size);
            labels.Add(new KeyValuePair<TextMeshPro, Color>(t, color));
            return t;
        }

        Option Button(string language, string text) { return new Option { Language = language, Text = Text(text, -0.105f, 38f) }; }

        // A flat shape on the game's shader, drawn before the text (a lower queue) so the text always shows on top of it.
        Transform Shape(string name, Mesh mesh, Vector3 at, int queue, Color color, out Material fill, bool fades)
        {
            fill = null;
            made.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(panel.transform, false);
            go.transform.localPosition = at;
            if (uber == null) return go.transform;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            fill = ShowPlayer.SeeThrough(new Material(uber) { name = "Beacon_" + name });
            fill.renderQueue = queue;
            if (fill.HasProperty("_Cull")) fill.SetFloat("_Cull", 0f);
            if (queue <= 2990) fill.SetFloat("_ZWrite", 1f);   // the card hides what is behind it; text and buttons in front still draw
            made.Add(fill);
            go.AddComponent<MeshRenderer>().sharedMaterial = fill;
            if (fades) fills.Add(new KeyValuePair<Material, Color>(fill, color));
            return go.transform;
        }

        static Mesh RoundedRect(float w, float h, float r)
        {
            const int steps = 6;
            r = Mathf.Min(r, w / 2f, h / 2f);
            float x = w / 2f - r, y = h / 2f - r;
            var corners = new[] { new Vector2(x, y), new Vector2(-x, y), new Vector2(-x, -y), new Vector2(x, -y) };
            var points = new List<Vector3> { Vector3.zero };
            for (int c = 0; c < 4; c++)
                for (int s = 0; s <= steps; s++)
                {
                    float a = (c * 90f + s * 90f / steps) * Mathf.Deg2Rad;
                    points.Add(new Vector3(corners[c].x + Mathf.Cos(a) * r, corners[c].y + Mathf.Sin(a) * r, 0f));
                }
            int n = points.Count - 1;
            var tris = new int[n * 3];
            for (int i = 1; i <= n; i++) { tris[(i - 1) * 3] = 0; tris[(i - 1) * 3 + 1] = i % n + 1; tris[(i - 1) * 3 + 2] = i; }
            var normals = new Vector3[points.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.back;
            var mesh = new Mesh { name = "Beacon_RoundedRect" };
            mesh.SetVertices(points);
            mesh.normals = normals;
            mesh.uv = new Vector2[points.Count];
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        static Vector3 Ahead(Camera cam, Transform fallback)
        {
            Vector3 a = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            return a.sqrMagnitude > 0.001f ? a.normalized : fallback.forward;
        }

        void Place(Camera cam)
        {
            Vector3 ahead = Ahead(cam, panel.transform);
            panel.transform.position = cam.transform.position + ahead * Distance + Vector3.down * Below;
            panel.transform.rotation = Quaternion.LookRotation(ahead);
        }

        // Stays put while the player looks around a little; once they turn away or move off, it glides back in front of them.
        void Follow(Camera cam, float dt)
        {
            Vector3 head = cam.transform.position, ahead = Ahead(cam, panel.transform);
            Vector3 want = head + ahead * Distance + Vector3.down * Below;
            Vector3 to = panel.transform.position - head;
            if (Vector3.Angle(Vector3.ProjectOnPlane(to, Vector3.up), ahead) > 28f || Mathf.Abs(to.magnitude - Distance) > 0.35f || Mathf.Abs(to.y + Below) > 0.3f) following = true;
            if (following)
            {
                panel.transform.position = Vector3.Lerp(panel.transform.position, want, 1f - Mathf.Exp(-4f * dt));
                if ((panel.transform.position - want).sqrMagnitude < 0.0004f) following = false;
            }
            Vector3 face = Vector3.ProjectOnPlane(panel.transform.position - head, Vector3.up);
            if (face.sqrMagnitude > 0.0001f)
                panel.transform.rotation = Quaternion.Slerp(panel.transform.rotation, Quaternion.LookRotation(face), 1f - Mathf.Exp(-8f * dt));
        }

        // Each hand points along its forward; the button the ray crosses lights up, and a fresh trigger pull on that hand picks it.
        void Point(float dt)
        {
            object tagger = taggerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
            if (pollerType == null) pollerType = Util.GameType("ControllerInputPoller");
            object poller = pollerType?.GetField("instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (tagger == null || poller == null) return;
            Option lit = null;
            cursor.gameObject.SetActive(false);
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "left" : "right";
                var hand = taggerType.GetField(side + "HandTransform")?.GetValue(tagger) as Transform;
                bool pulled = pollerType.GetField(side + "ControllerIndexFloat")?.GetValue(poller) is float f && f > 0.7f;
                bool fresh = pulled && !held[i];
                held[i] = pulled;
                if (hand == null || !Hit(hand.position, hand.forward, out Vector3 at)) continue;
                Vector3 p = panel.transform.InverseTransformPoint(at);
                if (Mathf.Abs(p.x) > card.x / 2f + 0.05f || Mathf.Abs(p.y) > card.y / 2f + 0.05f) continue;
                cursor.gameObject.SetActive(true);
                cursor.transform.position = at - panel.transform.forward * 0.006f;
                foreach (Option o in options)
                {
                    Vector3 q = o.Box.localPosition;
                    if (Mathf.Abs(p.x - q.x) > o.Size.x / 2f + 0.01f || Mathf.Abs(p.y - q.y) > o.Size.y / 2f + 0.01f) continue;
                    lit = o;
                    if (fresh) { Pick(o); return; }
                }
            }
            foreach (Option o in options) o.Lit = Mathf.MoveTowards(o.Lit, o == lit ? 1f : 0f, dt / 0.12f);
        }

        // Where a ray meets the panel's plane, within reach.
        bool Hit(Vector3 origin, Vector3 dir, out Vector3 at)
        {
            Vector3 normal = panel.transform.forward;
            float along = Vector3.Dot(dir, normal);
            float t = Mathf.Abs(along) < 0.001f ? -1f : Vector3.Dot(panel.transform.position - origin, normal) / along;
            at = origin + dir * t;
            return t > 0f && t < 5f;
        }

        void Paint()
        {
            float a = shown * shown * (3f - 2f * shown);
            panel.transform.localScale = Vector3.one * (0.92f + 0.08f * a);
            foreach (var f in fills) f.Key.SetColor("_BaseColor", new Color(f.Value.r, f.Value.g, f.Value.b, f.Value.a * a));
            foreach (var l in labels) l.Key.color = new Color(l.Value.r, l.Value.g, l.Value.b, l.Value.a * a);
            foreach (Option o in options)
            {
                Color box = Color.Lerp(Idle, Gold, o.Lit), ink = Color.Lerp(Color.white, Ink, o.Lit);
                if (o.Fill != null) o.Fill.SetColor("_BaseColor", new Color(box.r, box.g, box.b, box.a * a));
                o.Text.color = new Color(ink.r, ink.g, ink.b, a);
                float grow = 1f + 0.05f * o.Lit;
                o.Box.localScale = Vector3.one * grow;
                o.Text.transform.localScale = Vector3.one * TextScale * grow;
            }
            cursor.alpha = a;
        }

        void Pick(Option chosen)
        {
            Settings.Choose(chosen.Language);
            foreach (Option o in options) o.Lit = o == chosen ? 1f : 0f;
            cursor.gameObject.SetActive(false);
            leaving = true;
            Arabic.Say(chosen.Language == "ar" ? "تم، Beacon بالعربي" : "Done, Beacon is in English", 4f);
        }
    }

    /// <summary>
    /// The hosted content, from the public content repo over HTTPS. At start, the shared Beacon folder is brought up to date from
    /// files.json (fonts and the like; a changed file is swapped in at the next start). While the network is on, the lists (manifest,
    /// shows, admins) are fetched at start and every ten minutes into <c>Beacon\content</c>, and a hosted bundle is downloaded the first
    /// time it is needed. A list is saved only if it reads as one, any file only if its SHA-256 matches. Only addresses built here from
    /// checked ids and paths are fetched: a message never carries one.
    /// </summary>
    internal sealed class Content : MonoBehaviour
    {
        const string Base = Endpoints.ContentBase;
        const float RefreshSeconds = 600f;
        static readonly string[] Lists = { "manifest.json", "shows.json", "admins.json", "translations.json" };

        static Content instance;
        static readonly Dictionary<string, string> failed = new Dictionary<string, string>();
        static readonly HashSet<string> downloading = new HashSet<string>();
        static bool fetchingLists, reloadDue;
        float nextFetch;

        internal static string Folder { get { return Path.Combine(Core.Folder, "content"); } }

        /// <summary>True while this asset's bundle is downloading.</summary>
        internal static bool Downloading(string id) { return downloading.Contains(id); }

        /// <summary>True while the lists or a bundle are on their way, or a new manifest waits to be read.</summary>
        internal static bool Busy { get { return fetchingLists || reloadDue || downloading.Count > 0; } }

        internal static void Begin()
        {
            if (instance != null) return;
            ApplyPending();
            instance = Util.Host<Content>("Beacon_Content");
            instance.StartCoroutine(instance.FetchFolder());
            // The admin list from the last run counts until the fresh one arrives.
            string admins = Util.ReadText(Path.Combine(Folder, "admins.json"));
            if (admins != null) ApplyAdmins(admins);
            string lines = Util.ReadText(Path.Combine(Folder, "translations.json"));
            if (lines != null) Translations.Set(Translations.Parse(lines, new List<string>()));
        }

        internal static void End()
        {
            if (instance != null) Destroy(instance.gameObject);
            instance = null;
            downloading.Clear();
            failed.Clear();
            fetchingLists = false;
            reloadDue = false;
        }

        /// <summary>
        /// Starts downloading the entry's bundle if it is not already on its way. Returns null, or why an earlier download failed
        /// (remembered until the lists are fetched again).
        /// </summary>
        internal static string Download(AssetEntry entry)
        {
            if (failed.TryGetValue(entry.Id, out string why)) return why;
            if (instance == null) return "hosted bundles are only downloaded while the network is on";
            if (downloading.Add(entry.Id)) instance.StartCoroutine(instance.FetchBundle(entry));
            return null;
        }

        void Update()
        {
            // A new manifest is read between shows: reading it unloads the bundles, which would end a show that is playing.
            if (reloadDue && !ShowPlayer.Playing)
            {
                reloadDue = false;
                failed.Clear();
                AssetLoader.Cleanup();
            }
            if (!fetchingLists && Time.realtimeSinceStartup >= nextFetch)
            {
                nextFetch = Time.realtimeSinceStartup + RefreshSeconds;
                StartCoroutine(FetchLists());
            }
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        IEnumerator FetchLists()
        {
            fetchingLists = true;
            failed.Clear();
            foreach (string name in Lists)
            {
                var d = new Download(Base + "data/" + name, AssetManifest.MaxManifestChars, 30);
                yield return d;
                if (d.Error != null) Log.Warning("content: could not fetch " + name + ": " + d.Error);
                else Take(name, Util.Utf8(d.Data));
            }
            fetchingLists = false;
        }

        // Saves a fetched list if it reads as one and differs from the saved copy.
        static void Take(string name, string text)
        {
            var problems = new List<string>();
            bool readable;
            Dictionary<string, string> lines = null;
            if (name == "manifest.json") readable = AssetManifest.Parse(text, AssetLoader.Platform, problems) != null;
            else if (name == "shows.json") readable = ShowScript.Parse(text, problems) != null;
            else if (name == "translations.json") readable = (lines = Translations.Parse(text, problems)) != null;
            else readable = AssetManifest.ParseAdmins(text, out bool _, problems) != null;
            foreach (string p in problems) Log.Warning("content " + name + ": " + p);
            if (!readable) return;
            if (name == "admins.json") ApplyAdmins(text);
            if (lines != null) Translations.Set(lines);

            string path = Path.Combine(Folder, name);
            if (Util.ReadText(path) == text) return;
            string why = Util.WriteText(path, text);
            if (why != null) { Log.Warning("content: " + why); return; }
            Log.Debug("content: " + name + " updated");
            if (name == "manifest.json") reloadDue = true;
        }

        static void ApplyAdmins(string text)
        {
            List<string> ids = AssetManifest.ParseAdmins(text, out bool enabled, new List<string>());
            if (ids != null) Network.SetHostedAdmins(ids, enabled);
        }

        IEnumerator FetchBundle(AssetEntry entry)
        {
            float began = Time.realtimeSinceStartup;
            string url = Base + "assets/" + entry.Id + "/" + entry.Bundle;
            Log.Debug("content: downloading " + url);
            var d = new Download(url, AssetManifest.MaxBundleBytes, 300);
            yield return d;
            byte[] data = d.Data;
            string why = d.Error != null ? "the download failed: " + d.Error
                : !AssetManifest.HashMatches(data, entry.Sha256) ? "the downloaded bundle's SHA-256 does not match the hosted manifest"
                : Util.Write(Path.Combine(Folder, entry.Bundle), data);
            downloading.Remove(entry.Id);
            if (why != null)
            {
                failed[entry.Id] = why;
                Log.Warning("content: '" + entry.Id + "' not downloaded: " + why);
                yield break;
            }
            Log.Debug("content: '" + entry.Id + "' downloaded, " + data.Length + " bytes in " + (Time.realtimeSinceStartup - began).ToString("0.0") + " s");
        }

        // A listed file that changed while the game ran waits beside the old one as "<name>.new" (the old one may be in use):
        // it takes the old one's place here, at the next start, before anything reads the folder.
        static void ApplyPending()
        {
            string[] waiting;
            try { waiting = Directory.Exists(Core.Folder) ? Directory.GetFiles(Core.Folder, "*.new", SearchOption.AllDirectories) : new string[0]; }
            catch (Exception e) when (Util.IsIO(e)) { Log.Warning("content: could not look for updates: " + e.Message); return; }
            foreach (string next in waiting)
            {
                string path = next.Substring(0, next.Length - ".new".Length);
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(next, path);
                    Log.Debug("content: updated " + path);
                }
                catch (Exception e) when (Util.IsIO(e)) { Log.Warning("content: could not update " + path + ": " + e.Message); }
            }
        }

        // files.json lists what the shared folder should hold (fonts and the like), each hosted at files/<path>. Read once per start.
        IEnumerator FetchFolder()
        {
            var list = new Download(Base + "data/files.json", AssetManifest.MaxManifestChars, 30);
            yield return list;
            if (list.Error != null) { Log.Warning("content: could not fetch files.json: " + list.Error); yield break; }
            var problems = new List<string>();
            List<AssetManifest.FolderFile> files = AssetManifest.ParseFiles(Util.Utf8(list.Data), problems);
            foreach (string p in problems) Log.Warning("content files.json: " + p);
            if (files == null) yield break;

            foreach (AssetManifest.FolderFile f in files)
            {
                string path = Path.Combine(Core.Folder, f.Path.Replace('/', Path.DirectorySeparatorChar));
                if (Holds(path, f) || Holds(path + ".new", f)) continue;
                var d = new Download(Base + "files/" + f.Path, f.Size, 120);
                yield return d;
                string why = d.Error != null ? "the download failed: " + d.Error
                    : d.Data.Length != f.Size || !AssetManifest.HashMatches(d.Data, f.Sha256) ? "its size or SHA-256 does not match files.json"
                    : Place(path, d.Data);
                if (why != null) Log.Warning("content: " + f.Path + " not updated: " + why);
            }
        }

        // True if the file is there with the listed size and SHA-256.
        static bool Holds(string path, AssetManifest.FolderFile f)
        {
            try { return File.Exists(path) && new FileInfo(path).Length == f.Size && AssetManifest.HashMatches(File.ReadAllBytes(path), f.Sha256); }
            catch (Exception e) when (Util.IsIO(e)) { return false; }
        }

        // A file that is not there yet goes straight in; one that is there may be in use, so the new copy waits beside it as ".new".
        static string Place(string path, byte[] data)
        {
            bool exists = File.Exists(path);
            string why = Util.Write(exists ? path + ".new" : path, data);
            if (why == null) Log.Debug("content: " + (exists ? "an update to " + path + " is ready for the next start" : path + " downloaded"));
            return why;
        }
    }

    /// <summary>Beacon's log lines, through BepInEx's logger so they reach <c>LogOutput.log</c>.</summary>
    internal static class Log
    {
        static readonly ManualLogSource log = BepInEx.Logging.Logger.CreateLogSource("Beacon");

        public static void Debug(string message) { log.LogDebug(message); }
        public static void Info(string message) { log.LogInfo(message); }
        public static void Warning(string message) { log.LogWarning(message); }
        public static void Error(string message) { log.LogError(message); }

        /// <summary>Logs the warning the first time only: a failure that repeats every frame would flood the log.</summary>
        public static void WarnOnce(ref bool logged, string message)
        {
            if (logged) return;
            logged = true;
            log.LogWarning(message);
        }
    }

    /// <summary>
    /// An HTTPS GET a coroutine waits for with <c>yield return</c>; it gives up past <c>max</c> bytes. Afterwards <see cref="Data"/>
    /// holds the bytes, or <see cref="Error"/> says why there are none. Headers come in pairs: name, value.
    /// </summary>
    internal sealed class Download : CustomYieldInstruction
    {
        UnityWebRequest request;
        readonly long max;
        public byte[] Data;
        public string Error;
        public long Code;

        public Download(string url, long max, int timeout, params string[] headers) : this(UnityWebRequest.Get(url), max, timeout, headers) { }

        /// <summary>An HTTPS POST of JSON text, waited for the same way.</summary>
        public static Download PostJson(string url, string json, long max, int timeout)
        {
            var r = new UnityWebRequest(url, "POST") { uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)), downloadHandler = new DownloadHandlerBuffer() };
            return new Download(r, max, timeout, new[] { "Content-Type", "application/json" });
        }

        Download(UnityWebRequest r, long max, int timeout, string[] headers)
        {
            this.max = max;
            request = r;
            request.timeout = timeout;
            for (int i = 0; i + 1 < headers.Length; i += 2) request.SetRequestHeader(headers[i], headers[i + 1]);
            request.SendWebRequest();
        }

        public override bool keepWaiting
        {
            get
            {
                if (request == null) return false;
                if (!request.isDone && request.downloadedBytes <= (ulong)max) return true;
                Code = request.responseCode;
                if (!request.isDone || request.downloadHandler.data.Length > max) { request.Abort(); Error = "it is larger than " + max + " bytes"; }
                else if (request.result != UnityWebRequest.Result.Success) Error = request.error;
                else Data = request.downloadHandler.data;
                request.Dispose();
                request = null;
                return false;
            }
        }
    }

    /// <summary>Small helpers the rest of Beacon shares: files, downloads, text checks, game types, sizes.</summary>
    internal static class Util
    {
        /// <summary>The errors a file read or write can throw on a player's PC; anything else is a bug and is not caught.</summary>
        internal static bool IsIO(Exception e) { return e is IOException || e is UnauthorizedAccessException; }

        /// <summary>The file's text, or null if it is not there or cannot be read (that is logged).</summary>
        internal static string ReadText(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception e) when (IsIO(e)) { Log.Warning("could not read " + path + ": " + e.Message); return null; }
        }

        internal static string WriteText(string path, string text) { return Write(path, Encoding.UTF8.GetBytes(text)); }

        /// <summary>Writes the file, making its folder, beside the target first so a half-written file is never read. Null, or why it failed.</summary>
        internal static string Write(string path, byte[] data)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string part = path + ".part";
                File.WriteAllBytes(part, data);
                if (File.Exists(path)) File.Delete(path);
                File.Move(part, path);
                return null;
            }
            catch (Exception e) when (IsIO(e)) { return "could not save " + path + ": " + e.Message; }
        }

        /// <summary>The bytes as text, without a byte-order mark.</summary>
        internal static string Utf8(byte[] data) { return Encoding.UTF8.GetString(data).TrimStart('﻿'); }

        /// <summary>
        /// True if <paramref name="s"/> has 1 to <paramref name="max"/> characters, all in <paramref name="set"/>: single characters
        /// and ranges such as "a-z0-9_-" (a '-' meant as itself goes last).
        /// </summary>
        internal static bool Only(string s, int max, string set)
        {
            if (string.IsNullOrEmpty(s) || s.Length > max) return false;
            foreach (char c in s) if (!InSet(c, set)) return false;
            return true;
        }

        static bool InSet(char c, string set)
        {
            for (int i = 0; i < set.Length; i++)
            {
                if (i + 2 < set.Length && set[i + 1] == '-') { if (c >= set[i] && c <= set[i + 2]) return true; i += 2; }
                else if (c == set[i]) return true;
            }
            return false;
        }

        /// <summary>The text without control characters, trimmed, cut to <paramref name="max"/> characters. Never null.</summary>
        internal static string Clean(string s, int max)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) if (!char.IsControl(c)) sb.Append(c);
            string r = sb.ToString().Trim();
            return r.Length > max ? r.Substring(0, max) : r;
        }

        /// <summary>Data every copy of Beacon in the game sees. Each mod's copy is its own assembly, so only plain types go in here.</summary>
        internal static T Shared<T>(string key) where T : class { return AppDomain.CurrentDomain.GetData(key) as T; }

        internal static void Share(string key, object value) { AppDomain.CurrentDomain.SetData(key, value); }

        /// <summary>A new object that lives across scene loads, carrying one <typeparamref name="T"/>.</summary>
        internal static T Host<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go.AddComponent<T>();
        }

        /// <summary>A type from the game by its full name, or null: Beacon reads the game by reflection so a game update cannot break the build.</summary>
        internal static Type GameType(string fullName)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>The box around every renderer under <paramref name="go"/>; a zero box at its position when it has none.</summary>
        internal static Bounds RendererBounds(GameObject go)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }

    /// <summary>
    /// The link between copies of Beacon in the same room. A mod turns it on with <see cref="Start"/>; until then Beacon sends and listens to nothing.
    /// Messages travel as Photon room events and are checked by <see cref="Wire"/> before anything is done with them.
    /// </summary>
    public static class Network
    {
        // Assets: an admin asks every copy to spawn a checked object from the manifest. Here is who may ask; AssetLoader does the loading.
        static readonly HashSet<string> admins = new HashSet<string>(StringComparer.Ordinal);
        static readonly RateLimiter incomingAssets = new RateLimiter(3, 0.5, () => Time.realtimeSinceStartup);

        // How many asset requests were spawned or refused on this copy (the refusal reason is logged).
        internal static int AssetsSpawned, AssetsRefused;

        /// <summary>A mod can set this to false to ignore every asset request and refuse to send one.</summary>
        public static bool AssetsEnabled = true;

        /// <summary>The ids of the assets in this copy's manifest.</summary>
        public static IList<string> AssetIds { get { return AssetLoader.Ids(); } }

        /// <summary>Lets the player with this Photon user ID ask this copy to spawn assets.</summary>
        public static void AddAdmin(string userId) { if (!string.IsNullOrEmpty(userId)) admins.Add(userId); }

        public static void RemoveAdmin(string userId) { if (userId != null) admins.Remove(userId); }

        public static bool IsAdmin(string userId) { return userId != null && (admins.Contains(userId) || hostedAdmins.Contains(userId)); }

        // The admins named in the hosted admins.json, kept apart from the ones a mod adds so a new list replaces only its own.
        static readonly HashSet<string> hostedAdmins = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>True while the hosted admins.json says "enabled": false: every show and asset message is then refused and none is sent.</summary>
        internal static bool RemoteOff { get; private set; }

        internal static void SetHostedAdmins(List<string> ids, bool enabled)
        {
            hostedAdmins.Clear();
            foreach (string id in ids) hostedAdmins.Add(id);
            if (!enabled && !RemoteOff)
            {
                Log.Warning("Beacon's hosted list switched shows and assets off");
                ForgetMyShow();
                ShowPlayer.Stop();
            }
            else if (enabled && RemoteOff) Log.Debug("Beacon's hosted list switched shows and assets on again");
            RemoteOff = !enabled;
            Log.Debug(hostedAdmins.Count + " admins in the hosted list");
        }

        /// <summary>Reads the manifest again and forgets every checked bundle, for example after the files were replaced.</summary>
        public static void ReloadAssets() { AssetLoader.Cleanup(); }

        /// <summary>
        /// Spawns the manifest asset <paramref name="id"/> at <paramref name="position"/> here and asks every other copy to do the same.
        /// Other copies only act if this player is on their admin list. Returns false (and logs why) if nothing was sent.
        /// </summary>
        public static bool SpawnAsset(string id, Vector3 position, float yaw)
        {
            if (!Enabled || !AssetsEnabled || RemoteOff || !PhotonNetwork.InRoom) return false;
            object[] frame = Wire.Asset(id, position.x, position.y, position.z, yaw);
            if (frame == null) { Log.Warning("cannot send that asset request: the id or the position is not valid"); return false; }
            if (!AssetLoader.Known(id)) { Log.Warning("cannot send asset '" + id + "': it is not in the manifest"); return false; }
            string why = AssetLoader.Spawn(id, position, yaw, out _);
            if (why != null) { Log.Warning("did not spawn asset '" + id + "': " + why); return false; }
            Raise(frame, -1);
            return true;
        }

        static void HandleAsset(int sender, Message m)
        {
            if (!AssetsEnabled || !FromAdmin(sender, "an asset request", incomingAssets, ref AssetsRefused)) return;
            string why = AssetLoader.Spawn(m.AssetId, new Vector3(m.X, m.Y, m.Z), m.Yaw, out _);
            if (why == null)
            {
                AssetsSpawned++;
                Log.Debug("spawned asset '" + m.AssetId + "' for player " + sender);
                return;
            }
            AssetsRefused++;
            Log.Warning("refused asset '" + m.AssetId + "' from player " + sender + ": " + why);
        }

        // Do not change EventCode: the game and other mods already use many codes (the game raises 1 to 3, 8, 51, 100 to 103, 176, 177, 180, 186 and 199).
        internal const byte EventCode = 142;

        static readonly Dictionary<int, Peer> peers = new Dictionary<int, Peer>();
        static readonly RateLimiter incoming = new RateLimiter(4, 0.5, () => Time.realtimeSinceStartup);
        static MonoBehaviour pump;
        static Action<EventData> listener;
        static bool inRoom;
        static bool announced;
        static bool backendChecked;
        static float nextBackendCheck;
        static bool failureLogged;

        // How many messages were acted on, dropped by the checks, or held back by the rate limit.
        internal static int Handled, Dropped, RateLimited;

        /// <summary>True after <see cref="Start"/> until <see cref="Stop"/>.</summary>
        public static bool Enabled { get; private set; }

        /// <summary>Raised when a player in the room turns out to be running Beacon.</summary>
        public static event Action<Peer> PeerFound;

        /// <summary>The players in the current room who run Beacon, not counting you.</summary>
        public static IList<Peer> Peers { get { return new List<Peer>(peers.Values); } }

        /// <summary>Turns the link on. Safe to call more than once.</summary>
        public static void Start()
        {
            if (Enabled) return;
            Enabled = true;
            Log.Debug("network is on: this mod talks to other copies of Beacon in the room");
            pump = Util.Host<NetworkPump>("Beacon_Network");
            Content.Begin();
        }

        /// <summary>Turns the link off and forgets everyone.</summary>
        public static void Stop()
        {
            if (!Enabled) return;
            Enabled = false;
            Unsubscribe();
            if (pump != null) UnityEngine.Object.Destroy(pump.gameObject);
            pump = null;
            peers.Clear();
            inRoom = announced = backendChecked = false;
            waitingShow = null;
            ForgetMyShow();
            Content.End();
        }

        // Runs every frame while the link is on.
        internal static void Tick()
        {
            if (!backendChecked && !CheckBackend()) return;

            if (listener == null)
            {
                listener = OnEvent;
                PhotonNetwork.NetworkingClient.EventReceived += listener;
            }

            bool now = PhotonNetwork.InRoom;
            if (now && !inRoom)
            {
                inRoom = true;
                announced = false;
                peers.Clear();
            }
            else if (!now && inRoom)
            {
                inRoom = false;
                peers.Clear();
                waitingShow = null;
                ForgetMyShow();
            }
            // The game may say the room is private a moment after joining it, so the announcement waits for that.
            if (inRoom && !announced && RoomIsPrivate())
            {
                announced = true;
                Announce();
            }
            if (inRoom) DropPlayersWhoLeft();
            TickShows();
        }

        // The link rides on Photon PUN's room events, so it stays off if the game runs on another backend.
        static bool CheckBackend()
        {
            if (Time.realtimeSinceStartup < nextBackendCheck) return false;
            nextBackendCheck = Time.realtimeSinceStartup + 1f;

            object system = NetworkSystem();
            PropertyInfo p = system?.GetType().GetProperty("CurrentPhotonBackend");
            string backend = p?.GetValue(system, null) as string;
            if (backend == null) return false;
            backendChecked = true;
            if (backend == "PUN") return true;

            Log.Warning("network stays off: the game uses " + backend + " and Beacon needs PUN");
            Stop();
            return false;
        }

        static void Announce()
        {
            Raise(Wire.Presence(Core.Version, Arabic.Language), -1);
            Raise(Wire.PresenceRequest(), -1);
        }

        // Sends a frame to everyone (target below 0) or to one player. A null frame means the builder refused it.
        // Nothing is sent in a public room.
        static void Raise(object[] frame, int target)
        {
            if (frame == null || !PhotonNetwork.InRoom || !RoomIsPrivate()) return;
            var options = new RaiseEventOptions();
            if (target >= 0) options.TargetActors = new[] { target };
            else options.Receivers = ReceiverGroup.Others;
            PhotonNetwork.RaiseEvent(EventCode, frame, options, SendOptions.SendReliable);
        }

        static FieldInfo systemInstance;

        // The game's NetworkSystem.Instance (a static field), read by reflection; null until the game has made it.
        static object NetworkSystem()
        {
            if (systemInstance == null)
            {
                Type t = Util.GameType("NetworkSystem");
                systemInstance = t?.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            }
            return systemInstance?.GetValue(null);
        }

        /// <summary>
        /// True when the game says the current room is private (its NetworkSystem.SessionIsPrivate, read by reflection).
        /// Anything unclear counts as public, and Beacon sends nothing in a public room.
        /// </summary>
        public static bool RoomIsPrivate()
        {
            if (!PhotonNetwork.InRoom) return false;
            object system = NetworkSystem();
            if (system == null) return false;
            Type t = system.GetType();
            PropertyInfo p = t.GetProperty("SessionIsPrivate");
            FieldInfo f = p == null ? t.GetField("SessionIsPrivate") : null;
            object value = p != null ? p.GetValue(system, null) : f?.GetValue(system);
            return value is bool on && on;
        }

        static void DropPlayersWhoLeft()
        {
            if (peers.Count == 0) return;
            var present = new HashSet<int>();
            foreach (Player p in PhotonNetwork.PlayerListOthers) present.Add(p.ActorNumber);
            foreach (int actor in new List<int>(peers.Keys))
                if (!present.Contains(actor)) { peers.Remove(actor); incoming.Forget(actor); }
        }

        static void OnEvent(EventData e)
        {
            if (e.Code != EventCode || !Enabled) return;
            try
            {
                int sender = e.Sender;
                if (sender <= 0 || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom.GetPlayer(sender) == null) return;
                if (sender == PhotonNetwork.LocalPlayer.ActorNumber) return;

                if (!Wire.TryParse(e.CustomData, out Message m, out string reject))
                {
                    Dropped++;
                    Log.Debug("dropped a message from player " + sender + ": " + reject);
                    return;
                }
                if (!incoming.Allow(sender)) { RateLimited++; return; }
                Handled++;

                switch (m.Type)
                {
                    case MessageType.PresenceRequest:
                        Raise(Wire.Presence(Core.Version, Arabic.Language), sender);
                        // A player asks this when joining; sent after the presence answer, so the show arrives from a known peer.
                        SendMyShowTo(sender);
                        break;
                    case MessageType.Presence:
                        bool isNew = !peers.TryGetValue(sender, out Peer peer);
                        if (isNew) { peer = new Peer { Actor = sender }; peers[sender] = peer; }
                        peer.Version = m.Version;
                        peer.Language = m.Language;
                        Player who = PhotonNetwork.CurrentRoom.GetPlayer(sender);
                        peer.Nickname = Wire.CleanName(who?.NickName);
                        peer.UserId = who?.UserId;
                        if (isNew && PeerFound != null) PeerFound(peer);
                        break;
                    case MessageType.Asset:
                        HandleAsset(sender, m);
                        break;
                    case MessageType.Show:
                        HandleShow(sender, m);
                        break;
                    case MessageType.ShowStop:
                        HandleShowStop(sender, m);
                        break;
                }
            }
            catch (Exception ex) { Log.WarnOnce(ref failureLogged, "could not handle a message: " + ex.GetType().Name + ": " + ex.Message); }
        }

        static void Unsubscribe()
        {
            if (listener == null) return;
            try { PhotonNetwork.NetworkingClient.EventReceived -= listener; }
            catch (Exception) { }
            listener = null;
        }

        // Calls Tick every frame; a failure is logged once and the next frame carries on.
        sealed class NetworkPump : MonoBehaviour
        {
            void Update()
            {
                try { Tick(); }
                catch (Exception ex) { Log.WarnOnce(ref failureLogged, "network failed in Update: " + ex.GetType().Name + ": " + ex.Message); }
            }
        }

        // Shows: an admin starts a show from shows.json and every copy in the room plays it in step, timed on Photon's server clock.
        // Here is who may start or stop one and when it began; ShowPlayer does the playing.
        // Only admins are heard here, so this is looser than the other limits: 4 at once, then one every 2 s.
        static readonly RateLimiter incomingShows = new RateLimiter(4, 0.5, () => Time.realtimeSinceStartup);

        // The show this copy started, sent again to each player who joins while it runs.
        static string myShow;
        static Vector3 myShowAt;
        static float myShowYaw;
        static int myShowStart;

        // The start of the show playing here because of a message, so a repeat of the same message does not restart it.
        static int playingStart;

        // How many show messages were refused on this copy (the reason is logged).
        internal static int ShowsRefused;

        /// <summary>A mod can set this to false to ignore every show message and refuse to send one.</summary>
        public static bool ShowsEnabled = true;

        /// <summary>
        /// Plays the show <paramref name="id"/> from shows.json at <paramref name="position"/> here and asks every other copy in the room
        /// to play it in step. Other copies only act if this player is on their admin list and they have the show's files.
        /// Only in a private room. Returns false (and logs why) if it did not start.
        /// </summary>
        public static bool PlayShow(string id, Vector3 position, float yaw)
        {
            if (!Enabled || !ShowsEnabled || RemoteOff || !PhotonNetwork.InRoom) return false;
            if (!RoomIsPrivate()) { Log.Warning("does not send a show in a public room"); return false; }
            int start = PhotonNetwork.ServerTimestamp;
            object[] frame = Wire.Show(id, position.x, position.y, position.z, yaw, start);
            if (frame == null) { Log.Warning("cannot send that show: the id or the position is not valid"); return false; }
            string why = ShowPlayer.Play(id, position, yaw, () => Wire.Elapsed(PhotonNetwork.ServerTimestamp, start));
            if (why != null) { Log.Warning("did not play show '" + id + "': " + why); return false; }
            myShow = id;
            myShowAt = position;
            myShowYaw = yaw;
            myShowStart = start;
            Raise(frame, -1);
            return true;
        }

        /// <summary>Stops the show playing here and, if this copy started it, on every other copy in the room.</summary>
        public static void StopShow()
        {
            string id = myShow;
            ForgetMyShow();
            ShowPlayer.Stop();
            if (id != null && Enabled && PhotonNetwork.InRoom) Raise(Wire.ShowStop(id), -1);
        }

        static void ForgetMyShow() { myShow = null; }

        // A player who has just joined gets the show this copy started, if it is still playing.
        static void SendMyShowTo(int actor)
        {
            if (myShow == null) return;
            if (ShowPlayer.CurrentId != myShow) { ForgetMyShow(); return; }
            Raise(Wire.Show(myShow, myShowAt.x, myShowAt.y, myShowAt.z, myShowYaw, myShowStart), actor);
        }

        static bool MayRunShows(int sender, string what) { return ShowsEnabled && FromAdmin(sender, what, incomingShows, ref ShowsRefused); }

        // Whether an admin message from this player may be acted on: it announced Beacon, it is on the admin list, and it is within
        // its rate limit. Logs why not.
        static bool FromAdmin(int sender, string what, RateLimiter limiter, ref int refused)
        {
            Player who = PhotonNetwork.CurrentRoom.GetPlayer(sender);
            if (RemoteOff || who == null) return false;
            string why = !peers.ContainsKey(sender) ? "it has not announced Beacon" : !IsAdmin(who.UserId) ? "not on the admin list" : null;
            if (why != null) { refused++; Log.Debug("ignored " + what + " from player " + sender + ": " + why); return false; }
            if (limiter.Allow(sender)) return true;
            RateLimited++;
            Log.Debug("held back " + what + " from player " + sender + ": too many at once");
            return false;
        }

        // A show whose lists or bundle were still downloading when its message came; it starts once they are in, as far in as the clock says.
        static Message waitingShow;
        static int waitingSender;

        static void HandleShow(int sender, Message m)
        {
            if (!MayRunShows(sender, "a show")) return;
            if (ShowPlayer.CurrentId == m.ShowId && playingStart == m.Start) return;
            StartShow(sender, m);
        }

        static void StartShow(int sender, Message m)
        {
            waitingShow = null;
            int start = m.Start;
            string why = ShowPlayer.Play(m.ShowId, new Vector3(m.X, m.Y, m.Z), m.Yaw, () => Wire.Elapsed(PhotonNetwork.ServerTimestamp, start));
            if (why == null)
            {
                playingStart = start;
                Log.Debug("is playing show '" + m.ShowId + "' for player " + sender);
                return;
            }
            if (Content.Busy)
            {
                waitingShow = m;
                waitingSender = sender;
                Log.Debug("will play show '" + m.ShowId + "' from player " + sender + " once its files are in (" + why + ")");
                return;
            }
            ShowsRefused++;
            Log.Warning("did not play show '" + m.ShowId + "' from player " + sender + ": " + why);
        }

        // Runs every frame: starts a waiting show once nothing is downloading, if its sender is still here.
        static void TickShows()
        {
            if (waitingShow == null || Content.Busy) return;
            if (RemoteOff || !ShowsEnabled || !inRoom || PhotonNetwork.CurrentRoom.GetPlayer(waitingSender) == null) { waitingShow = null; return; }
            StartShow(waitingSender, waitingShow);
        }

        static void HandleShowStop(int sender, Message m)
        {
            if (!MayRunShows(sender, "a show stop")) return;
            if (waitingShow != null && waitingShow.ShowId == m.ShowId) waitingShow = null;
            if (ShowPlayer.CurrentId != m.ShowId) return;
            ShowPlayer.Stop();
            Log.Debug("stopped show '" + m.ShowId + "' for player " + sender);
        }
    }

    // Draws the notifications in front of the player's view. Arabic.Notify only queues a request; this component (one, on the plugin) shows it.
    class NotificationPanel : MonoBehaviour
    {
        sealed class Request { public string Text, Source; public Color Color; public float Seconds; }

        sealed class Entry
        {
            public TextMeshPro Tmp;
            public string Text, Source;
            public Color Color;
            public int Count = 1;
            public float Born, Expires, Height;
        }

        // the settings, kept safe to use (a zero width or fade would break the layout)
        static int MaxEntries { get { return Mathf.Max(1, NotificationSettings.MaxEntries); } }
        static float FadeIn { get { return Mathf.Max(0.01f, NotificationSettings.FadeIn); } }
        static float FadeOut { get { return Mathf.Max(0.01f, NotificationSettings.FadeOut); } }
        static float Width { get { return Mathf.Max(1f, NotificationSettings.Width); } }
        static float Scale { get { return Mathf.Max(0.0001f, NotificationSettings.Scale); } }
        static float Gap { get { return NotificationSettings.Gap; } }
        static Vector3 Offset { get { return NotificationSettings.Position; } }
        // Layer 19 "FirstPersonOnly": the main camera draws it and the spectator ("Shoulder") camera does not (read from their culling masks).
        const int SpectatorHiddenLayer = 19;

        static readonly List<Request> pending = new List<Request>();

        readonly List<Entry> entries = new List<Entry>();
        GameObject root;
        int layer;
        static bool failed;

        internal static void Enqueue(string text, string source, Color color, float seconds)
        {
            if (pending.Count >= 50) pending.RemoveAt(0);   // nothing draws before the camera exists: keep the latest
            pending.Add(new Request { Text = text, Source = source, Color = color, Seconds = seconds });
        }

        // Unity's own exception log does not reach LogOutput.log here, so a failure is logged once through ours and the next frame carries on.
        void Update() { try { Tick(); } catch (Exception e) { Log.WarnOnce(ref failed, "Notifications failed in Update: " + e); } }
        void LateUpdate() { try { Follow(); } catch (Exception e) { Log.WarnOnce(ref failed, "Notifications failed in LateUpdate: " + e); } }

        void Tick()
        {
            float now = Time.unscaledTime;
            if (pending.Count > 0 && Camera.main != null)
            {
                if (root == null)
                {
                    root = new GameObject("Beacon_Notifications");
                    DontDestroyOnLoad(root);   // the game loads and unloads scenes while it starts: a root left in one of them is destroyed with it
                }
                foreach (Request r in pending) Add(r, now);
                pending.Clear();
            }
            entries.RemoveAll(e => e.Tmp == null);   // texts something else destroyed: drop them, or every frame throws on them
            for (int i = entries.Count - 1; i >= 0; i--)
                if (now >= entries[i].Expires) { Destroy(entries[i].Tmp.gameObject); entries.RemoveAt(i); }
            int wanted = Arabic.HideNotificationsFromSpectators ? SpectatorHiddenLayer : 0;
            if (wanted != layer) { layer = wanted; foreach (Entry e in entries) SetLayer(e.Tmp); }
            Layout(now);
        }

        // The text's own object and the child meshes TextMeshPro makes for fallback fonts.
        void SetLayer(TMP_Text text)
        {
            foreach (Transform t in text.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        void Follow()
        {
            Camera main = Camera.main;
            if (root == null || main == null) return;
            root.transform.position = main.transform.TransformPoint(Offset);
            root.transform.rotation = main.transform.rotation;
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root);
            entries.Clear();
        }

        void Add(Request r, float now)
        {
            Entry e = entries.Find(x => x.Text == r.Text && x.Source == r.Source && x.Color == r.Color);
            if (e != null)   // the same notification again: count it instead of stacking a copy
            {
                e.Count++;
                e.Expires = now + r.Seconds;
                Fill(e);
                return;
            }

            var go = new GameObject("Notification");
            go.transform.SetParent(root.transform, false);
            go.transform.localScale = Vector3.one * Scale;
            var tmp = go.AddComponent<TextMeshPro>();
            TMP_FontAsset font = NotificationSettings.Font ?? Arabic.GameFont ?? Arabic.FontAsset;
            if (font != null) tmp.font = font;
            tmp.fontSize = NotificationSettings.FontSize;
            tmp.alignment = TextAlignmentOptions.TopRight;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;   // lines are wrapped before shaping, not by TextMeshPro
            tmp.color = r.Color;
            tmp.rectTransform.pivot = new Vector2(1f, 1f);
            tmp.rectTransform.sizeDelta = new Vector2(Width, 10f);

            e = new Entry { Tmp = tmp, Text = r.Text, Source = r.Source, Color = r.Color, Born = now, Expires = now + r.Seconds };
            Fill(e);
            entries.Insert(0, e);   // newest on top
            while (entries.Count > MaxEntries)
            {
                Destroy(entries[entries.Count - 1].Tmp.gameObject);
                entries.RemoveAt(entries.Count - 1);
            }
        }

        void Fill(Entry e)
        {
            string text = (string.IsNullOrEmpty(e.Source) ? "" : "[" + e.Source + "] ") + e.Text + (e.Count > 1 ? " ×" + e.Count : "");
            bool arabic = Arabic.IsArabic(e.Text);
            if (arabic) Arabic.EnableFor(e.Tmp);
            // a "[Source]" in front must not turn an Arabic line left to right, so Arabic text is always laid out right to left
            string wrapped = Arabic.Wrap(e.Tmp, text, Width, Arabic.DefaultWith(arabic ? TextDirection.Rtl : TextDirection.Auto));
            e.Tmp.text = wrapped;
            e.Tmp.ForceMeshUpdate();
            e.Height = e.Tmp.GetRenderedValues(false).y * e.Tmp.transform.localScale.x;
            SetLayer(e.Tmp);   // after the mesh exists, so the fallback child meshes get the layer too
        }

        void Layout(float now)
        {
            float y = 0f;
            foreach (Entry e in entries)
            {
                e.Tmp.transform.localPosition = new Vector3(0f, -y, 0f);
                float a = Mathf.Clamp01((now - e.Born) / FadeIn) * Mathf.Clamp01((e.Expires - now) / FadeOut);
                if (Mathf.Abs(e.Tmp.alpha - a) > 0.003f) e.Tmp.alpha = a;
                y += e.Height + Gap;
            }
        }
    }

    /// <summary>
    /// Plays a show from <c>Beacon\assets\shows.json</c>: its asset with its music, shown and faded by the show's cues on the
    /// music's clock, drawn with the game's own GorillaTag/UberShader. It plays on this copy only; Network.PlayShow is what tells
    /// the other copies.
    /// </summary>
    internal sealed class ShowPlayer : MonoBehaviour
    {
        const string Uber = "GorillaTag/UberShader";
        static ShowPlayer current;

        ShowScript show;
        Copy ground;
        AudioSource music;
        float started;

        // The shared clock (seconds into the show) when the show came with one, and how far the Animators have been moved
        // (Unity moves them by Time.deltaTime, which it caps, so a frozen frame leaves them behind the music).
        Func<float> clock;
        float danced;
        float nextCheck;

        sealed class Copy
        {
            public GameObject Root;
            public Renderer[] Renderers;
            public Material[][] Solid;
            public Material[][] Faded;
            public float[] Shown;           // per renderer, -1 until first set
            public int[] Part;              // per renderer: the show part it belongs to, or -1
        }

        internal static bool Playing { get { return current != null; } }

        /// <summary>The id of the show playing now, or null.</summary>
        internal static string CurrentId { get { return current?.origin; } }

        // The show a chain began with: the id the room knows, whichever show of the chain is playing.
        string origin;

        /// <summary>
        /// Starts the show at <paramref name="at"/>, facing <paramref name="yaw"/>. <paramref name="elapsed"/>, if given, says how many
        /// seconds into the show it already is; it is read after the bundle has loaded (loading takes a moment), and the music and the
        /// dance jump there. Returns null, or why it did not start.
        /// </summary>
        internal static string Play(string showId, Vector3 at, float yaw, Func<float> elapsed = null, string origin = null)
        {
            Stop();
            ShowScript s = Find(showId, out string why);
            if (s == null) return why;
            // Already past its end and another show follows: join that one instead, without loading this one's bundle.
            if (s.Then != null && elapsed != null && elapsed() >= s.End + s.Gap)
            {
                float end = s.End + s.Gap;
                return Play(s.Then, at, yaw, () => elapsed() - end, origin ?? s.Id);
            }

            ShowPlayer p = Util.Host<ShowPlayer>("Beacon_Show_" + s.Id);
            p.origin = origin ?? s.Id;
            p.show = s;
            p.at = at;
            p.yaw = yaw;
            p.clock = elapsed;
            why = p.Spawn();
            if (why != null && !AssetLoader.Loading(s.Asset)) { Destroy(p.gameObject); return why; }
            // A bundle still loading or downloading: the show waits here (Update) and starts as far in as the clock says.
            current = p;
            if (why != null) Log.Debug("show " + s.Id + ": waits for its bundle (" + why + ")");
            return null;
        }

        // Where and how the show was asked for, kept while its bundle loads.
        Vector3 at;
        float yaw;

        // Spawns the asset and starts the show. Null once it started, else why not.
        string Spawn()
        {
            string why = AssetLoader.Spawn(show.Asset, at, yaw, out GameObject g);
            if (why != null) return why;
            Transform first = show.Parts.Count > 0 ? g.transform.Find(show.Parts[0].Name) : null;   // several dancers: the first one sets the size
            float height = Util.RendererBounds(first != null ? first.gameObject : g).size.y;
            float scale = show.Scale > 0f ? show.Scale : show.Height / (height > 0.1f ? height : 1f);
            g.transform.localScale = Vector3.one * scale;

            float from = clock == null ? 0f : Mathf.Max(0f, clock());
            if (from > show.End)
            {
                Destroy(g);
                return "it ended " + (from - show.End).ToString("0") + " s ago";
            }
            Begin(show, g, from);
            Log.Debug("show " + show.Id + ": started at " + at.ToString("0.0") + ", size " + scale.ToString("0.0") + "x, " +
                               show.Cues.Count + " cues, " + show.Parts.Count + " parts, ends at " + show.End.ToString("0.0") + " s" +
                               (from > 0f ? "; joined " + from.ToString("0.0") + " s in" : ""));
            return null;
        }

        /// <summary>Ends the show and removes it, with the materials it made.</summary>
        internal static void Stop()
        {
            if (current == null) return;
            ShowPlayer p = current;
            current = null;
            if (p.ground != null)
            {
                if (p.ground.Root != null) Destroy(p.ground.Root);
                var made = new HashSet<Material>();
                foreach (Material[] set in p.ground.Solid) foreach (Material m in set) if (m != null && m.name.EndsWith("_gt")) made.Add(m);
                foreach (Material[] set in p.ground.Faded) foreach (Material m in set) if (m != null && m.name.EndsWith("_gt")) made.Add(m);
                foreach (Material m in made) Destroy(m);
            }
            if (p != null) Destroy(p.gameObject);
        }

        // The local shows.json first, then the hosted one.
        static ShowScript Find(string id, out string why)
        {
            why = "no show '" + id + "' in shows.json";
            foreach (string folder in new[] { AssetLoader.Folder, Content.Folder })
            {
                string path = Path.Combine(folder, "shows.json");
                string text = Util.ReadText(path);
                if (text == null) continue;
                var problems = new List<string>();
                List<ShowScript> shows = ShowScript.Parse(text, problems);
                foreach (string p in problems) Log.Warning(path + ": " + p);
                if (shows != null)
                    foreach (ShowScript s in shows)
                        if (s.Id == id) return s;
            }
            return null;
        }

        void Begin(ShowScript s, GameObject g, float from)
        {
            show = s;
            started = Time.time - from;
            music = g.GetComponentInChildren<AudioSource>(true);
            Shader uber = Shader.Find(Uber);
            if (uber == null) Log.Warning("show " + s.Id + ": " + Uber + " not found; the bundle's own materials are used and fades become cuts");
            ground = Prepare(g, uber, s);
            foreach (ShowScript.Part p in s.Parts)
                if (g.transform.Find(p.Name) == null) Log.Warning("show " + s.Id + ": its asset has no part '" + p.Name + "'; those cues do nothing");
            if (from > 0.05f) Seek(from);
            Update();
        }

        // Jumps the music and every Animator to `t` seconds. The Animators are stepped forward rather than set, so the
        // transitions between songs happen on the way exactly as they would have.
        void Seek(float t)
        {
            float began = Time.realtimeSinceStartup;
            if (music != null && music.clip != null)
            {
                if (!music.isPlaying) music.Play();
                music.time = Mathf.Min(t, music.clip.length - 0.05f);
            }
            int steps = StepDance(t);
            danced = t;
            Log.Debug("show " + show.Id + ": jumped to " + t.ToString("0.0") + " s in " + steps + " steps, " +
                               ((Time.realtimeSinceStartup - began) * 1000f).ToString("0") + " ms");
        }

        // Moves every Animator forward by `seconds`, in small steps so transitions on the way still happen. Returns the step count.
        int StepDance(float seconds)
        {
            int steps = 0;
            if (ground == null || ground.Root == null) return 0;
            foreach (Animator a in ground.Root.GetComponentsInChildren<Animator>(true))
                for (float done = 0f; done < seconds; done += SeekStep, steps++) a.Update(Mathf.Min(SeekStep, seconds - done));
            return steps;
        }

        const float DriftAllowed = 0.25f;          // the music
        const float DanceBehindAllowed = 0.3f;
        const float DanceAheadAllowed = 1f;

        // Keeps the music and the dance on the show's clock: the shared clock when the show came with one, else the music itself.
        // Unity moves Animators by Time.deltaTime, which it caps per frame, so on a slow frame rate the dance falls behind real time
        // a little every frame: it is stepped forward by what it is missing every frame, which shows as smooth motion. The music is
        // checked once a second. A dance ahead (the clock wobbled back) is held still until the clock catches up. Jumps bigger
        // than DanceBehindAllowed, and holds past DanceAheadAllowed, are logged.
        void KeepInStep()
        {
            float want;
            if (clock != null) want = clock();
            else if (music != null && music.isPlaying) want = music.time;
            else return;
            if (want < 0f || want > show.End) return;

            float musicOff = 0f;
            if (clock != null && Time.realtimeSinceStartup >= nextCheck && music != null && music.clip != null && want < music.clip.length - 0.1f)
            {
                nextCheck = Time.realtimeSinceStartup + 1f;
                musicOff = music.isPlaying ? music.time - want : -want;
                if (Mathf.Abs(musicOff) > DriftAllowed)
                {
                    if (!music.isPlaying) music.Play();
                    music.time = want;
                }
                else musicOff = 0f;
            }

            // Ahead (the clock wobbled back): hold the dance still until the clock catches up. Do not rewind it with
            // Animator.Rebind: parts of the dancer (his head, his eyes) stopped being drawn after it.
            float danceOff = danced - want;
            SetDanceHeld(danceOff > 0.02f);                // before stepping: a held Animator does not move (its speed is 0)
            if (danceOff < 0f) { StepDance(-danceOff); danced = want; }
            if (danceOff > -DanceBehindAllowed && danceOff <= DanceAheadAllowed) danceOff = 0f;

            if (musicOff != 0f || danceOff != 0f)
                Log.Debug("show " + show.Id + ": back in step at " + want.ToString("0.0") + " s (the music was " +
                                   musicOff.ToString("+0.00;-0.00;0") + " s off, the dance " + danceOff.ToString("+0.00;-0.00;0") + " s)");
        }

        const float SeekStep = 0.25f;

        bool held;

        void SetDanceHeld(bool hold)
        {
            if (hold == held) return;
            held = hold;
            if (ground != null && ground.Root != null)
                foreach (Animator a in ground.Root.GetComponentsInChildren<Animator>(true)) a.speed = hold ? 0f : 1f;
        }

        // A skinned part's own bounds can stand still while the dancer moves away from them, and the camera then culls a part that is
        // plainly in view. Each skinned part gets a fixed box this big instead (in metres at the dancer's size), so none is culled while he is near.
        const float DrawBox = 400f;

        static void KeepDrawn(GameObject root)
        {
            foreach (SkinnedMeshRenderer r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Transform space = r.rootBone ?? r.transform;
                float scale = Mathf.Max(0.0001f, space.lossyScale.x);
                Vector3 centre = space.InverseTransformPoint(r.bounds.center);   // on the part, not the bone: a dance can start far from its root
                r.updateWhenOffscreen = false;
                r.localBounds = new Bounds(centre, Vector3.one * (DrawBox / scale));
            }
        }

        static Copy Prepare(GameObject root, Shader uber, ShowScript show)
        {
            KeepDrawn(root);
            var c = new Copy { Root = root, Renderers = root.GetComponentsInChildren<Renderer>(true) };
            c.Solid = new Material[c.Renderers.Length][];
            c.Faded = new Material[c.Renderers.Length][];
            c.Shown = new float[c.Renderers.Length];
            c.Part = new int[c.Renderers.Length];
            for (int i = 0; i < c.Renderers.Length; i++)
            {
                c.Shown[i] = -1f;
                Transform top = c.Renderers[i].transform;                 // a part is a direct child of the asset, found by name
                while (top.parent != null && top.parent != root.transform) top = top.parent;
                c.Part[i] = show.Parts.FindIndex(p => p.Name == top.name);
                c.Renderers[i].allowOcclusionWhenDynamic = false;
                Material[] original = c.Renderers[i].sharedMaterials;
                c.Solid[i] = new Material[original.Length];
                c.Faded[i] = new Material[original.Length];
                for (int j = 0; j < original.Length; j++)
                {
                    Material o = original[j];
                    bool keep = uber == null || o == null;
                    c.Solid[i][j] = keep || o.name.Contains("Glass") ? o : Base(o, uber);
                    c.Faded[i][j] = keep ? o : SeeThrough(Base(o, uber));
                }
            }
            return c;
        }

        // The game's shader with the bundle's base texture (what the game's own textured materials switch on).
        static Material Base(Material from, Shader uber)
        {
            var m = new Material(uber) { name = from.name + "_gt" };
            Texture tex = from.HasProperty("_BaseMap") ? from.GetTexture("_BaseMap") : from.mainTexture;
            if (tex != null)
            {
                m.SetTexture("_BaseMap", tex);
                m.EnableKeyword("_USE_TEXTURE");
            }
            m.SetColor("_BaseColor", Color.white);
            m.EnableKeyword("_UV_SOURCE__UV0");
            return m;
        }

        // Alpha blending the way the game's own see-through UberShader materials are set up (read from them in the game).
        internal static Material SeeThrough(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_SrcBlend", 5f);         // SrcAlpha
            m.SetFloat("_DstBlend", 10f);        // OneMinusSrcAlpha
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
            return m;
        }

        void Update()
        {
            if (ground == null)
            {
                if (AssetLoader.Loading(show.Asset)) return;
                string why = Spawn();
                if (why != null) { Log.Warning("show " + show.Id + " did not start: " + why); Stop(); }
                return;
            }
            if (ground.Root == null) { Stop(); return; }
            if (!held) danced += Time.deltaTime;
            KeepInStep();
            float t = music != null && music.isPlaying ? music.time : Time.time - started;
            Apply(ground, show.At(t), show, t);
            if (show.Then != null && (clock != null ? clock() : t) >= show.End + show.Gap) { Handover(); return; }
            if (t > show.End + 1f)
            {
                Log.Debug("show " + show.Id + ": ended at " + t.ToString("0.0") + " s");
                Stop();
            }
        }

        // Replaces this show with the one it hands over to, in the same place, starting on this show's clock where this one ends.
        void Handover()
        {
            string next = show.Then, id = show.Id;
            float end = show.End + show.Gap, begun = started;
            Func<float> now = clock ?? (() => Time.time - begun);
            string why = Play(next, at, yaw, () => now() - end, origin);
            if (why == null) Log.Debug("show " + id + " ended, show " + next + " follows");
            else Log.Warning("show " + id + " ended but show " + next + " did not start: " + why);
        }

        // Hidden at 0; the solid materials when fully shown; otherwise the see-through ones at that alpha.
        // A renderer inside a show part also takes that part's own visibility.
        static void Apply(Copy c, float copyAlpha, ShowScript show, float t)
        {
            if (c == null || c.Root == null) return;
            for (int i = 0; i < c.Renderers.Length; i++)
            {
                Renderer r = c.Renderers[i];
                if (r == null) continue;
                float alpha = c.Part[i] < 0 ? copyAlpha : copyAlpha * show.PartAt(c.Part[i], t);
                if (Mathf.Abs(alpha - c.Shown[i]) < 0.002f) continue;
                c.Shown[i] = alpha;
                bool visible = alpha > 0.002f;
                bool solid = alpha > 0.998f;
                r.enabled = visible;
                if (!visible) continue;
                r.sharedMaterials = solid ? c.Solid[i] : c.Faded[i];
                if (solid) continue;
                foreach (Material m in c.Faded[i])
                    if (m != null && m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(1f, 1f, 1f, alpha));
            }
        }

        void OnDestroy()
        {
            if (current == this) current = null;
        }
    }

    /// <summary>
    /// Counts how many people run Beacon and through which mods. Every copy adds its mod to a list all copies share; the copy that
    /// runs sends that list with a random id kept in the Beacon folder, soon after the game starts and every 10 minutes while it runs.
    /// </summary>
    internal static class Stats
    {
        // Shared by every copy of Beacon: an object[] of string[] { plugin ID, mod name, mod version }.
        const string ModsKey = "Beacon.Mods";

        internal static void Register(string guid, string name, string version)
        {
            var mods = Util.Shared<object[]>(ModsKey) ?? new object[0];
            var grown = new object[mods.Length + 1];
            Array.Copy(mods, grown, mods.Length);
            grown[mods.Length] = new[] { guid, name, version };
            Util.Share(ModsKey, grown);
        }

        internal static IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(StatsPing.FirstAfter);
            string id = InstallId();
            if (id == null) yield break;
            bool told = false;
            while (true)
            {
                var d = Download.PostJson(StatsPing.Url, StatsPing.Body(id, Core.Version, Mods()), 4096, 20);
                yield return d;
                if (!told) Log.Debug(d.Error == null ? "counted as running (no name, account or room is sent)" : "could not reach the counter: " + d.Error);
                told = true;
                yield return new WaitForSecondsRealtime(StatsPing.Every);
            }
        }

        static IEnumerable<string[]> Mods()
        {
            var mods = Util.Shared<object[]>(ModsKey);
            if (mods == null) yield break;
            foreach (object m in mods) if (m is string[] mod) yield return mod;
        }

        // A random id made once per game folder and kept in <game>\Beacon\id.txt.
        static string InstallId()
        {
            string path = Path.Combine(Core.Folder, "id.txt");
            string old = (Util.ReadText(path) ?? "").Trim();
            if (StatsPing.IsId(old)) return old;
            string id = StatsPing.NewId();
            string why = Util.WriteText(path, id);
            if (why == null) return id;
            Log.Warning("this game is not counted: " + why);
            return null;
        }
    }

    /// <summary>The chime played with Beacon's own gold messages, made from <see cref="Chime"/>'s samples; at most one a second.</summary>
    internal static class Sound
    {
        static AudioSource source;
        static readonly AudioClip[] clips = new AudioClip[2];
        static float last = -10f;

        internal static void Chime(bool announcement)
        {
            if (Time.realtimeSinceStartup - last < 1f) return;
            last = Time.realtimeSinceStartup;
            if (source == null)
            {
                source = Util.Host<AudioSource>("Beacon_Chime");
                source.playOnAwake = false;
                source.spatialBlend = 0f;   // flat in the ears, not from a place in the world
                source.volume = global::Beacon.Chime.Volume;
            }
            int i = announcement ? 1 : 0;
            if (clips[i] == null)
            {
                float[] samples = global::Beacon.Chime.Samples(announcement);
                clips[i] = AudioClip.Create(announcement ? "Beacon_Announcement" : "Beacon_Notification", samples.Length, 1, global::Beacon.Chime.Rate, false);
                // By reflection: the game's AudioClip.SetData also has a ReadOnlySpan overload, which a .NET 4.8 mod cannot compile against.
                typeof(AudioClip).GetMethod("SetData", new[] { typeof(float[]), typeof(int) }).Invoke(clips[i], new object[] { samples, 0 });
            }
            source.PlayOneShot(clips[i]);
        }

        internal static void Cleanup()
        {
            if (source != null) UnityEngine.Object.Destroy(source.gameObject);
            source = null;
            for (int i = 0; i < clips.Length; i++) { if (clips[i] != null) UnityEngine.Object.Destroy(clips[i]); clips[i] = null; }
        }
    }

    /// <summary>
    /// Shows the owner's messages: every 2 minutes the copy that runs asks the server for its list, checks each message's signature
    /// against the owner's keys, and shows a new one once, in gold, in the player's language. Shown ids are kept in the Beacon folder,
    /// so an announcement is not shown again at the next start.
    /// </summary>
    internal static class Board
    {
        static readonly List<string> seenOrder = new List<string>();
        static readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal), refused = new HashSet<string>(StringComparer.Ordinal);

        static string SeenPath { get { return Path.Combine(Core.Folder, "messages-seen.txt"); } }

        internal static IEnumerator Run()
        {
            foreach (string line in (Util.ReadText(SeenPath) ?? "").Split('\n'))
            {
                string id = line.Trim();
                if (id.Length > 0 && seen.Add(id)) seenOrder.Add(id);
            }
            yield return new WaitForSecondsRealtime(OwnerMessage.FirstAfter);
            bool told = false;
            while (true)
            {
                var d = new Download(Endpoints.MessagesUrl, OwnerMessage.MaxReply, 20);
                yield return d;
                string why = d.Error;
                double now = 0;
                List<string[]> items = null;
                if (why == null) items = OwnerMessage.ReadList(Util.Utf8(d.Data), out now, out why);
                if (why != null) { if (!told) Log.Debug("could not read the owner's messages: " + why); told = true; }
                else Show(items, now);
                yield return new WaitForSecondsRealtime(OwnerMessage.Every);
            }
        }

        static void Show(List<string[]> items, double now)
        {
            if (Network.RemoteOff) return;
            bool added = false;
            for (int i = items.Count - 1; i >= 0; i--)   // the oldest first
            {
                string body = items[i][0], sig = items[i][1];
                OwnerMessage m = OwnerMessage.Read(body, now, out string why);
                if (m == null) { if (refused.Add(body)) Log.Warning("a message from the server was refused: " + why); continue; }
                if (seen.Contains(m.Id)) continue;
                if (!OwnerMessage.Signed(body, sig, items[i][2] == "1" ? Endpoints.KeySpare : Endpoints.KeyMain))
                {
                    if (refused.Add(m.Id)) Log.Warning("a message with a wrong signature was refused (" + m.Id + ")");
                    continue;
                }
                seen.Add(m.Id);
                seenOrder.Add(m.Id);
                added = true;
                Arabic.Say(m.For(Arabic.Language), m.Kind == "announce" ? 12f : 8f, m.Kind == "announce");
                Log.Debug("showed the owner's " + m.Kind + " " + m.Id);
            }
            if (!added) return;
            if (seenOrder.Count > OwnerMessage.MaxSeen) seenOrder.RemoveRange(0, seenOrder.Count - OwnerMessage.MaxSeen);
            string failed = Util.WriteText(SeenPath, string.Join("\n", seenOrder.ToArray()));
            if (failed != null) Log.Warning(failed);
        }
    }

    /// <summary>
    /// Tells a mod's players when the mod has a newer release on GitHub, once per new version. Every copy of Beacon checks its own
    /// mod, the copies standing by too; the notification is drawn by the copy that runs. Nothing is downloaded but the release's
    /// details: the player installs the update themselves.
    /// </summary>
    internal static class Updates
    {
        // Shared by every copy of Beacon: the running copy's way to show a notification (an Action<string>).
        internal const string NotifyKey = "Beacon.Notify";

        internal static IEnumerator Check(string repo, string template, string mod)
        {
            BepInPlugin own = Core.OwnPlugin();
            Version current = own != null && own.Version != null ? UpdateCheck.Normal(own.Version) : null;
            if (current == null) { Log.Warning(mod + " has no version in its [BepInPlugin], so its updates are not checked"); yield break; }
            // Lets the game start and the running copy choose itself before anything is shown.
            yield return new WaitForSecondsRealtime(5f);

            var d = new Download("https://api.github.com/repos/" + repo + "/releases/latest", UpdateCheck.MaxReplyBytes, 30,
                                 "Accept", "application/vnd.github+json", "User-Agent", "Beacon/" + Core.Version);
            yield return d;
            string why = d.Code == 404 ? "the repo has no release yet, or it is private" : d.Error;
            string tag = why == null ? UpdateCheck.ReadTag(Util.Utf8(d.Data), out why) : null;
            Version latest = tag != null ? UpdateCheck.ParseTag(tag) : null;
            if (why == null && latest == null) why = "its latest release tag is not a version number";
            if (why != null) { Log.Debug("could not check " + mod + " for updates (" + repo + "): " + why); yield break; }
            if (latest <= current) { Log.Debug(mod + " " + UpdateCheck.Short(current) + " is up to date"); yield break; }

            string seen = Path.Combine(Path.Combine(Core.Folder, "updates"), UpdateCheck.SeenFile(repo));
            if ((Util.ReadText(seen) ?? "").Trim() == UpdateCheck.Short(latest)) yield break;
            string message = UpdateCheck.Message(template, mod, latest, current);
            Log.Debug(message);
            var notify = Util.Shared<Action<string>>(NotifyKey);
            if (notify == null) { Log.Debug("no copy of Beacon shows notifications here, so this is shown at the next start"); yield break; }
            notify(message);
            string failed = Util.WriteText(seen, UpdateCheck.Short(latest));
            if (failed != null) Log.Warning(failed);
        }
    }

    // Opt-in (Arabic.EnableAutoFix): fixes Arabic in any TextMeshPro or legacy Text as it is drawn, with the text its owner set left as it was
    // (.text still returns the original, so a networked name or anything sent anywhere is never touched).
    // TextMeshPro: every way of setting a text (text, SetText(string), measuring with GetPreferredValues(string)) ends in the private
    // TMP_Text.PopulateTextBackingArray(string, int, int); a prefix there swaps in the shaped string. The legacy Text half follows below.
    // Not covered: SetText with a StringBuilder or char[], and a text with Arabic that is already fixed (it is left alone, as Fix does).
    static class AutoFix
    {
        sealed class State { public string Source, Result; public float Width; public bool Right; public TMP_FontAsset Font; }

        static Harmony harmony;
        static bool busy, failed;
        static ConditionalWeakTable<TMP_Text, State> states = new ConditionalWeakTable<TMP_Text, State>();

        // Texts a mod asked auto-fix to leave alone (Arabic.SkipAutoFix). A text is a key, so a destroyed text drops out by itself.
        static readonly ConditionalWeakTable<UnityEngine.Object, object> skipped = new ConditionalWeakTable<UnityEngine.Object, object>();
        static readonly object Marker = new object();

        /// <summary>Arabic.AutoFixRightAlign: a left-aligned text that gets Arabic is drawn right-aligned (TextMeshPro only).</summary>
        internal static bool RightAlign;

        internal static void Skip(UnityEngine.Object text, bool skip)
        {
            if (text == null) return;
            skipped.Remove(text);
            if (skip) skipped.Add(text, Marker);
        }

        static bool IsSkipped(UnityEngine.Object text)
        {
            return skipped.TryGetValue(text, out _);
        }

        internal static bool Enabled { get { return harmony != null; } }

        internal static void Enable()
        {
            if (harmony != null) return;
            MethodInfo populate = AccessTools.Method(typeof(TMP_Text), "PopulateTextBackingArray", new[] { typeof(string), typeof(int), typeof(int) });
            if (populate == null) { Log.Error("Auto-fix stays off: TMP_Text.PopulateTextBackingArray(string, int, int) was not found (a different TextMeshPro version?)"); return; }
            var h = new Harmony("dev.itsreallyhex.beacon.autofix");
            try
            {
                h.Patch(populate, prefix: new HarmonyMethod(typeof(AutoFix), nameof(PopulatePrefix)));
                PatchLegacyText(h);
            }
            catch (Exception e)
            {
                h.UnpatchSelf();
                Log.Error("Auto-fix stays off: " + e.Message);
                return;
            }
            harmony = h;
            failed = false;
            Log.Debug("Auto-fix is on");
        }

        internal static void Disable()
        {
            if (harmony == null) return;
            harmony.UnpatchSelf();   // ScriptEngine does not undo patches on a reload, so Core.Unload comes here
            harmony = null;
            states = new ConditionalWeakTable<TMP_Text, State>();
            Log.Debug("Auto-fix is off");
        }

        // Runs for every TextMeshPro text each time its mesh is built, so the common case (no Arabic) is one scan of the string and nothing else.
        static void PopulatePrefix(TMP_Text __instance, ref string sourceText, ref int start, ref int length)
        {
            // busy: our own measuring (GetPreferredValues) comes back through here. Only whole strings are texts; the other overloads are left alone.
            if (busy || sourceText == null || start != 0 || length != sourceText.Length) return;
            string text = Arabic.Translate(sourceText);
            if (!ArabicShaper.NeedsShaping(text) || IsSkipped(__instance)) return;
            busy = true;
            try
            {
                State st = states.GetOrCreateValue(__instance);
                TMP_FontAsset font = __instance.font;
                if (st.Font != font)   // first Arabic for this text, or its font was changed: give it the Arabic font and turn kerning off
                {
                    Arabic.EnableFor(__instance, true);
                    st.Font = font;
                    st.Source = null;
                }

                // Wrapping and the <align=right> tag: see Arabic.WrapWidth and Arabic.WantsRightAlign. The tag is drawn, not part of .text, and the text's
                // own alignment is not touched (changing it while the text is built would queue a rebuild inside the rebuild).
                float width = Arabic.WrapWidth(__instance);
                bool right = Arabic.WantsRightAlign(__instance, RightAlign);

                if (st.Source != sourceText || st.Width != width || st.Right != right)   // the same text drawn again (alpha, a colour change) costs nothing
                {
                    st.Result = Arabic.Render(__instance, text, width, right);
                    st.Source = sourceText;
                    st.Width = width;
                    st.Right = right;
                }
                sourceText = st.Result;
                length = sourceText.Length;
            }
            catch (Exception e)   // a failed fix must leave the game's text as it was, and say so once
            {
                if (!failed) { failed = true; Log.Error("Auto-fix skipped a text: " + e); }
            }
            finally { busy = false; }
        }

        // The legacy Text half of auto-fix. Text reads its private m_Text when it builds its mesh and when it reports its preferred size, so
        // for those three calls m_Text holds the shaped string and is put back afterwards: .text keeps returning what the owner set.
        // No wrapping here: a long legacy Text needs Arabic.Fix per line from its owner (the game uses it for short names).
        static FieldInfo textField;

        static void PatchLegacyText(Harmony h)
        {
            textField = AccessTools.Field(typeof(Text), "m_Text");
            if (textField == null) throw new MissingFieldException("UnityEngine.UI.Text.m_Text was not found");
            var prefix = new HarmonyMethod(typeof(AutoFix), nameof(TextPrefix));
            var finalizer = new HarmonyMethod(typeof(AutoFix), nameof(TextFinalizer));
            MethodBase[] targets =
            {
                AccessTools.Method(typeof(Text), "OnPopulateMesh", new[] { typeof(VertexHelper) }),
                AccessTools.PropertyGetter(typeof(Text), "preferredWidth"),
                AccessTools.PropertyGetter(typeof(Text), "preferredHeight"),
            };
            foreach (MethodBase m in targets)
            {
                if (m == null) throw new MissingMethodException("a UnityEngine.UI.Text member was not found");
                h.Patch(m, prefix: prefix, finalizer: finalizer);
            }
        }

        static void TextPrefix(Text __instance, out string __state)
        {
            __state = null;
            var original = (string)textField.GetValue(__instance);
            string text = original == null ? null : Arabic.Translate(original);
            if (!ArabicShaper.NeedsShaping(text) || IsSkipped(__instance)) return;
            __state = original;
            textField.SetValue(__instance, ArabicShaper.Shape(text));
        }

        // A finalizer, not a postfix, so an exception inside the mesh build cannot leave the shaped string behind.
        static Exception TextFinalizer(Exception __exception, Text __instance, string __state)
        {
            if (__state != null) textField.SetValue(__instance, __state);
            return __exception;
        }
    }

    /// <summary>
    /// Turns logical Arabic text into text a left-to-right renderer draws correctly: letters get their
    /// joined forms (Unicode Arabic Presentation Forms-B), lam-alef becomes one glyph, and each line is
    /// put in display order. No Unity or game code.
    /// Text with no Arabic letters comes back untouched, and so does text that was already shaped.
    /// </summary>
    public static class ArabicShaper
    {
        enum Cls : byte { L, R, AL, EN, AN, ES, ET, CS, WS, ON }

        sealed class Tag { public string Open, Close, Name; }

        sealed class Unit
        {
            public string Text, Marks = "";
            public Cls Cls;
            public int Join;               // 0 none, 1 joins previous only, 2 joins both, 3 joiner (ZWJ)
            public bool Atomic, Hidden;
            public char B, Iso, Fin, Ini, Med;
            public int Level;
            public Tag[] Tags;
        }

        // base letter, isolated, final, initial, medial (0 = the form does not exist)
        static readonly int[] LetterData =
        {
            0x0621, 0xFE80, 0,      0,      0,
            0x0622, 0xFE81, 0xFE82, 0,      0,
            0x0623, 0xFE83, 0xFE84, 0,      0,
            0x0624, 0xFE85, 0xFE86, 0,      0,
            0x0625, 0xFE87, 0xFE88, 0,      0,
            0x0626, 0xFE89, 0xFE8A, 0xFE8B, 0xFE8C,
            0x0627, 0xFE8D, 0xFE8E, 0,      0,
            0x0628, 0xFE8F, 0xFE90, 0xFE91, 0xFE92,
            0x0629, 0xFE93, 0xFE94, 0,      0,
            0x062A, 0xFE95, 0xFE96, 0xFE97, 0xFE98,
            0x062B, 0xFE99, 0xFE9A, 0xFE9B, 0xFE9C,
            0x062C, 0xFE9D, 0xFE9E, 0xFE9F, 0xFEA0,
            0x062D, 0xFEA1, 0xFEA2, 0xFEA3, 0xFEA4,
            0x062E, 0xFEA5, 0xFEA6, 0xFEA7, 0xFEA8,
            0x062F, 0xFEA9, 0xFEAA, 0,      0,
            0x0630, 0xFEAB, 0xFEAC, 0,      0,
            0x0631, 0xFEAD, 0xFEAE, 0,      0,
            0x0632, 0xFEAF, 0xFEB0, 0,      0,
            0x0633, 0xFEB1, 0xFEB2, 0xFEB3, 0xFEB4,
            0x0634, 0xFEB5, 0xFEB6, 0xFEB7, 0xFEB8,
            0x0635, 0xFEB9, 0xFEBA, 0xFEBB, 0xFEBC,
            0x0636, 0xFEBD, 0xFEBE, 0xFEBF, 0xFEC0,
            0x0637, 0xFEC1, 0xFEC2, 0xFEC3, 0xFEC4,
            0x0638, 0xFEC5, 0xFEC6, 0xFEC7, 0xFEC8,
            0x0639, 0xFEC9, 0xFECA, 0xFECB, 0xFECC,
            0x063A, 0xFECD, 0xFECE, 0xFECF, 0xFED0,
            0x0640, 0x0640, 0x0640, 0x0640, 0x0640,   // tatweel
            0x0641, 0xFED1, 0xFED2, 0xFED3, 0xFED4,
            0x0642, 0xFED5, 0xFED6, 0xFED7, 0xFED8,
            0x0643, 0xFED9, 0xFEDA, 0xFEDB, 0xFEDC,
            0x0644, 0xFEDD, 0xFEDE, 0xFEDF, 0xFEE0,
            0x0645, 0xFEE1, 0xFEE2, 0xFEE3, 0xFEE4,
            0x0646, 0xFEE5, 0xFEE6, 0xFEE7, 0xFEE8,
            0x0647, 0xFEE9, 0xFEEA, 0xFEEB, 0xFEEC,
            0x0648, 0xFEED, 0xFEEE, 0,      0,
            0x0649, 0xFEEF, 0xFEF0, 0,      0,
            0x064A, 0xFEF1, 0xFEF2, 0xFEF3, 0xFEF4,
        };

        // lam + alef ligatures: alef variant, isolated, final
        static readonly int[] LigatureData =
        {
            0x0622, 0xFEF5, 0xFEF6,
            0x0623, 0xFEF7, 0xFEF8,
            0x0625, 0xFEF9, 0xFEFA,
            0x0627, 0xFEFB, 0xFEFC,
        };

        static readonly Dictionary<char, char[]> Forms = new Dictionary<char, char[]>();
        static readonly Dictionary<char, char[]> Ligatures = new Dictionary<char, char[]>();
        static readonly Dictionary<char, char> Mirrors = new Dictionary<char, char>
        {
            { '(', ')' }, { ')', '(' }, { '[', ']' }, { ']', '[' }, { '{', '}' }, { '}', '{' },
            { '<', '>' }, { '>', '<' }, { '«', '»' }, { '»', '«' },
            { '‹', '›' }, { '›', '‹' },
        };

        // TextMeshPro tags that have a closing tag (read from TMP's own MarkupTag list: each has a SLASH_ entry).
        static readonly HashSet<string> PairedTags = new HashSet<string>
        {
            "a", "action", "align", "allcaps", "b", "cspace", "color", "font", "font-weight", "frac", "gradient", "indent", "i",
            "liga", "line-height", "line-indent", "link", "lowercase", "margin", "mark", "material", "mspace", "nobr", "noparse",
            "rotate", "scale", "size", "smallcaps", "strikethrough", "s", "style", "sub", "sup", "table", "td", "th", "tr",
            "underline", "u", "uppercase", "voffset", "width",
        };
        // Tags with no closing tag. They stay where they are. <alpha> is handled apart: it stays on until changed,
        // so it is treated as wrapping, and "closed" by setting it back to <alpha=#FF>.
        static readonly HashSet<string> AtomicTags = new HashSet<string>
        {
            "sprite", "space", "pos", "nbsp", "zwj", "zwsp", "shy", "cr", "page", "margin-left", "margin-right",
        };

        static ArabicShaper()
        {
            for (int i = 0; i < LetterData.Length; i += 5)
                Forms[(char)LetterData[i]] = new[] { (char)LetterData[i + 1], (char)LetterData[i + 2], (char)LetterData[i + 3], (char)LetterData[i + 4] };
            for (int i = 0; i < LigatureData.Length; i += 3)
                Ligatures[(char)LigatureData[i]] = new[] { (char)LigatureData[i + 1], (char)LigatureData[i + 2] };
        }

        /// <summary>True if the text has any Arabic-script letters, shaped or not.</summary>
        public static bool ContainsArabic(string text)
        {
            if (text == null) return false;
            for (int i = 0; i < text.Length; i++)
                if (IsArabicLetter(text[i]) || IsPresentationForm(text[i])) return true;
            return false;
        }

        /// <summary>True if the text has Arabic letters that still need shaping (already shaped text is false).</summary>
        public static bool NeedsShaping(string text)
        {
            if (text == null) return false;
            // Only letters the table shapes count. The tatweel (as in the Hijri "هـ") and letters without forms are still in the text after shaping,
            // so counting them made fixed text look unfixed, and shaping it a second time flipped the line back.
            for (int i = 0; i < text.Length; i++)
                if (text[i] != 'ـ' && Forms.ContainsKey(text[i])) return true;
            return false;
        }

        /// <summary>Shapes and reorders text for a left-to-right renderer. Safe to call twice on the same text.</summary>
        public static string Shape(string text, ShapeOptions options = null)
        {
            if (!NeedsShaping(text)) return text;
            ShapeOptions o = options ?? new ShapeOptions();
            if (o.RichText) text = text.Replace("<br>", "\n").Replace("<BR>", "\n");

            var sb = new StringBuilder(text.Length + 16);
            var stack = new List<Tag>();   // tags still open, carried from line to line
            int start = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                if (i < text.Length && text[i] != '\n') continue;
                int len = i - start;
                bool cr = len > 0 && text[start + len - 1] == '\r';
                if (cr) len--;
                ShapeLine(text.Substring(start, len), o, stack, sb, i == text.Length);
                if (cr) sb.Append('\r');
                if (i < text.Length) sb.Append('\n');
                start = i + 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Breaks text into lines no wider than <paramref name="maxWidth"/> and shapes them. A candidate line is measured after shaping,
        /// so joined letters and mixed English count the way they will be drawn; tags carry over from line to line.
        /// A word wider than the limit stays whole on its own line. <paramref name="measure"/> returns the width of an already shaped line.
        /// </summary>
        public static string Wrap(string text, Func<string, float> measure, float maxWidth, ShapeOptions options = null)
        {
            if (string.IsNullOrEmpty(text)) return text;
            ShapeOptions o = options ?? new ShapeOptions();
            if (o.RichText) text = text.Replace("<br>", "\n").Replace("<BR>", "\n");

            var lines = new List<string>();
            foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = "";
                foreach (string word in paragraph.Split(' '))
                {
                    string candidate = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && measure(Shape(candidate, o)) > maxWidth) { lines.Add(line); line = word; }
                    else line = candidate;
                }
                lines.Add(line);
            }
            return Shape(string.Join("\n", lines.ToArray()), o);
        }

        static void ShapeLine(string line, ShapeOptions o, List<Tag> stack, StringBuilder sb, bool last)
        {
            var startState = new List<Tag>(stack);
            if (!NeedsShaping(line))
            {
                if (o.RichText && line.IndexOf('<') >= 0) Parse(line, o, stack);   // keep the open-tag stack right
                sb.Append(line);
                return;
            }

            List<Unit> units = Parse(line, o, stack);
            MergeLigatures(units);
            Join(units);

            int baseLevel = o.Direction == TextDirection.Rtl ? 1 : 0;
            if (o.Direction == TextDirection.Auto)
                foreach (Unit u in units)
                {
                    if (u.Cls == Cls.L) break;
                    if (u.Cls == Cls.R || u.Cls == Cls.AL) { baseLevel = 1; break; }
                }

            Resolve(units, baseLevel);
            Reorder(units);
            foreach (Unit u in units)
            {
                if ((u.Level & 1) == 1 && !u.Atomic && u.Text.Length == 1 && Mirrors.TryGetValue(u.Text[0], out char m)) u.Text = m.ToString();
            }
            Emit(sb, units, startState, stack.ToArray(), last);
        }

        // ---- reading the line into units (one unit = a letter plus its marks, or one tag) ----

        static List<Unit> Parse(string s, ShapeOptions o, List<Tag> stack)
        {
            var units = new List<Unit>(s.Length);
            Tag[] snap = null;
            for (int i = 0; i < s.Length;)
            {
                char c = s[i];
                if (c == '<' && o.RichText)
                {
                    int end; bool closing; string name;
                    if (TryTag(s, i, out end, out closing, out name))
                    {
                        string raw = s.Substring(i, end - i + 1);
                        if (!closing && AtomicTags.Contains(name))
                        {
                            units.Add(new Unit { Text = raw, Cls = Cls.ON, Atomic = true, Tags = snap ?? (snap = stack.ToArray()) });
                            i = end + 1;
                            continue;
                        }
                        bool alpha = name == "alpha";
                        if (PairedTags.Contains(name) || (alpha && !closing))   // </alpha> is not a TMP tag: left as plain text
                        {
                            if (!closing) stack.Add(new Tag { Open = raw, Close = alpha ? "<alpha=#FF>" : "</" + name + ">", Name = name });
                            else
                            {
                                int k = stack.FindLastIndex(t => t.Name == name);
                                if (k >= 0) stack.RemoveAt(k);   // a closer with nothing to close is dropped
                            }
                            snap = null;
                            i = end + 1;
                            continue;
                        }
                    }
                }

                if (o.Digits == DigitStyle.ArabicIndic && c >= '0' && c <= '9') c = (char)(0x0660 + (c - '0'));
                else if (o.Digits == DigitStyle.Western)
                {
                    if (c >= '٠' && c <= '٩') c = (char)('0' + (c - 0x0660));
                    else if (c >= '۰' && c <= '۹') c = (char)('0' + (c - 0x06F0));
                }

                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    units.Add(new Unit { Text = s.Substring(i, 2), Cls = Cls.ON, Tags = snap ?? (snap = stack.ToArray()) });
                    i += 2;
                    continue;
                }
                if (IsMark(c))
                {
                    if (!o.KeepDiacritics && IsArabicMark(c)) { i++; continue; }
                    Unit last = units.Count > 0 ? units[units.Count - 1] : null;
                    if (last != null && !last.Atomic && !last.Hidden) { last.Marks += c; i++; continue; }
                }
                if (c == '‌' || c == '‍')
                {
                    units.Add(new Unit { Text = "", Hidden = true, Cls = Cls.ON, Join = c == '‍' ? 3 : 0, Tags = snap ?? (snap = stack.ToArray()) });
                    i++;
                    continue;
                }
                if (c == '‎' || c == '‏')
                {
                    units.Add(new Unit { Text = "", Hidden = true, Cls = c == '‎' ? Cls.L : Cls.R, Tags = snap ?? (snap = stack.ToArray()) });
                    i++;
                    continue;
                }
                if ((c >= '‪' && c <= '‮') || (c >= '⁦' && c <= '⁩')) { i++; continue; }   // embedding controls: not supported, dropped

                var u = new Unit { Text = c.ToString(), B = c, Cls = Classify(c), Tags = snap ?? (snap = stack.ToArray()) };
                if (Forms.TryGetValue(c, out char[] f))
                {
                    u.Iso = f[0]; u.Fin = f[1]; u.Ini = f[2]; u.Med = f[3];
                    u.Join = f[1] == 0 ? 0 : (f[2] == 0 ? 1 : 2);
                }
                units.Add(u);
                i++;
            }
            return units;
        }

        // s[i] is '<'. A tag is a known TextMeshPro name (or <#rrggbb>) up to the next '>'.
        static bool TryTag(string s, int i, out int end, out bool closing, out string name)
        {
            end = i; closing = false; name = null;
            int j = i + 1;
            if (j < s.Length && s[j] == '/') { closing = true; j++; }
            int nameStart = j;
            while (j < s.Length && s[j] != '>' && s[j] != '<' && j - i < 128) j++;
            if (j >= s.Length || s[j] != '>' || j == nameStart) return false;
            char first = s[nameStart];
            if (!((first >= 'a' && first <= 'z') || (first >= 'A' && first <= 'Z') || first == '#')) return false;
            if (first == '#') name = "color";
            else
            {
                int k = nameStart;
                while (k < j && s[k] != '=' && s[k] != ' ') k++;
                name = s.Substring(nameStart, k - nameStart).ToLowerInvariant();
            }
            end = j;
            return PairedTags.Contains(name) || AtomicTags.Contains(name) || name == "alpha";
        }

        // ---- shaping ----

        static void MergeLigatures(List<Unit> u)
        {
            for (int i = 0; i + 1 < u.Count; i++)
            {
                Unit a = u[i], b = u[i + 1];
                char[] lig;
                if (a.B != 'ل' || a.Atomic || b.Atomic || a.Hidden || b.Hidden || !ReferenceEquals(a.Tags, b.Tags)) continue;
                if (!Ligatures.TryGetValue(b.B, out lig)) continue;
                a.Marks += b.Marks;
                a.Iso = lig[0]; a.Fin = lig[1]; a.Ini = '\0'; a.Med = '\0';
                a.Join = 1;
                a.B = '\0';
                u.RemoveAt(i + 1);
            }
        }

        static void Join(List<Unit> u)
        {
            for (int i = 0; i < u.Count; i++)
            {
                Unit c = u[i];
                if (c.Iso == '\0') continue;
                bool prev = c.Join >= 1 && i > 0 && (u[i - 1].Join == 2 || u[i - 1].Join == 3);
                bool next = c.Join == 2 && i + 1 < u.Count && u[i + 1].Join >= 1;
                c.Text = (prev ? (next ? c.Med : c.Fin) : (next ? c.Ini : c.Iso)).ToString();
                c.Cls = Cls.AL;
            }
        }

        // ---- display order (a trimmed version of the Unicode bidirectional algorithm) ----

        static void Resolve(List<Unit> u, int baseLevel)
        {
            int n = u.Count;
            var t = new Cls[n];
            for (int i = 0; i < n; i++) t[i] = u[i].Cls;
            Cls sos = baseLevel == 0 ? Cls.L : Cls.R;

            Cls last = sos;                                              // W2: a digit after an Arabic letter is an Arabic digit
            for (int i = 0; i < n; i++)
            {
                if (t[i] == Cls.L || t[i] == Cls.R || t[i] == Cls.AL) last = t[i];
                else if (t[i] == Cls.EN && last == Cls.AL) t[i] = Cls.AN;
            }
            for (int i = 0; i < n; i++) if (t[i] == Cls.AL) t[i] = Cls.R;   // W3

            for (int i = 1; i < n - 1; i++)                              // W4: 1,000 and 12.5 and 10:30 stay one number
            {
                if (t[i] == Cls.ES && t[i - 1] == Cls.EN && t[i + 1] == Cls.EN) t[i] = Cls.EN;
                else if (t[i] == Cls.CS && (t[i - 1] == Cls.EN || t[i - 1] == Cls.AN) && t[i + 1] == t[i - 1]) t[i] = t[i - 1];
            }
            for (int i = 0; i < n; i++)                                  // W5: 50% stays together
            {
                if (t[i] != Cls.ET) continue;
                int j = i;
                while (j < n && t[j] == Cls.ET) j++;
                if ((i > 0 && t[i - 1] == Cls.EN) || (j < n && t[j] == Cls.EN))
                    for (int k = i; k < j; k++) t[k] = Cls.EN;
                i = j - 1;
            }
            for (int i = 0; i < n; i++)                                  // W6
                if (t[i] == Cls.ES || t[i] == Cls.ET || t[i] == Cls.CS) t[i] = Cls.ON;
            last = sos;                                                  // W7: a digit after Latin text is Latin
            for (int i = 0; i < n; i++)
            {
                if (t[i] == Cls.L || t[i] == Cls.R) last = t[i];
                else if (t[i] == Cls.EN && last == Cls.L) t[i] = Cls.L;
            }

            Cls embed = baseLevel == 0 ? Cls.L : Cls.R;                  // N1/N2: spaces and punctuation take the side they sit in
            for (int i = 0; i < n; i++)
            {
                if (t[i] != Cls.WS && t[i] != Cls.ON) continue;
                int j = i;
                while (j < n && (t[j] == Cls.WS || t[j] == Cls.ON)) j++;
                Cls before = i == 0 ? sos : Side(t[i - 1]);
                Cls after = j == n ? sos : Side(t[j]);
                Cls r = before == after ? before : embed;
                for (int k = i; k < j; k++) t[k] = r;
                i = j - 1;
            }

            for (int i = 0; i < n; i++)                                  // I1/I2: levels
            {
                bool rtl = baseLevel == 1;
                if (t[i] == Cls.L) u[i].Level = rtl ? 2 : 0;
                else if (t[i] == Cls.R) u[i].Level = 1;
                else u[i].Level = 2;
            }
            for (int i = n - 1; i >= 0 && (u[i].Cls == Cls.WS || u[i].Hidden); i--) u[i].Level = baseLevel;   // L1: trailing spaces
        }

        static Cls Side(Cls c) { return c == Cls.L ? Cls.L : Cls.R; }   // digits count as right-to-left next to a space

        static void Reorder(List<Unit> u)
        {
            int max = 0, minOdd = int.MaxValue;
            foreach (Unit x in u)
            {
                if (x.Level > max) max = x.Level;
                if ((x.Level & 1) == 1 && x.Level < minOdd) minOdd = x.Level;
            }
            for (int lvl = max; lvl >= minOdd; lvl--)
                for (int i = 0; i < u.Count;)
                {
                    if (u[i].Level < lvl) { i++; continue; }
                    int j = i;
                    while (j < u.Count && u[j].Level >= lvl) j++;
                    u.Reverse(i, j - i);
                    i = j;
                }
        }

        // ---- writing the result, with tags moved to follow their letters ----

        // At the end of a line the tags still open in the text must be open in the output too, because the next line carries on
        // from them. After the last line nothing follows, so tags are only closed there, never opened again.
        static void Emit(StringBuilder sb, List<Unit> units, List<Tag> open, Tag[] endState, bool last)
        {
            foreach (Unit x in units)
            {
                if (x.Hidden) continue;
                Transition(sb, open, x.Tags, true);
                sb.Append(x.Text).Append(x.Marks);
            }
            Transition(sb, open, endState, !last);
        }

        static void Transition(StringBuilder sb, List<Tag> open, Tag[] target, bool openNew)
        {
            int common = 0;
            while (common < open.Count && common < target.Length && ReferenceEquals(open[common], target[common])) common++;
            for (int k = open.Count - 1; k >= common; k--) sb.Append(open[k].Close);
            open.RemoveRange(common, open.Count - common);
            if (!openNew) return;
            for (int k = common; k < target.Length; k++) { sb.Append(target[k].Open); open.Add(target[k]); }
        }

        // ---- character classes ----

        static bool IsArabicLetter(char c)
        {
            return (c >= 'ؠ' && c <= 'ي') || (c >= 'ٮ' && c <= 'ە') || c == 'ۮ' || c == 'ۯ'
                || (c >= 'ۺ' && c <= 'ۿ') || (c >= 'ݐ' && c <= 'ݿ') || (c >= 'ࢠ' && c <= 'ࣉ');
        }

        static bool IsPresentationForm(char c) { return (c >= 'ﭐ' && c <= '﷿') || (c >= 'ﹰ' && c <= '﻿'); }

        static bool IsArabicMark(char c)
        {
            return (c >= 'ً' && c <= 'ٟ') || c == 'ٰ' || (c >= 'ۖ' && c <= 'ۜ')
                || (c >= '۟' && c <= 'ۤ') || c == 'ۧ' || c == 'ۨ' || (c >= '۪' && c <= 'ۭ');
        }

        static bool IsMark(char c) { return IsArabicMark(c) || (c >= '̀' && c <= 'ͯ'); }

        static Cls Classify(char c)
        {
            if (c >= '0' && c <= '9') return Cls.EN;
            if (c == '+' || c == '-') return Cls.ES;
            if (c == '#' || c == '$' || c == '%' || (c >= '¢' && c <= '¥') || c == '°' || c == '‰' || c == '٪') return Cls.ET;
            if (c == ',' || c == '.' || c == '/' || c == ':' || c == ' ' || c == '،' || c == '٫' || c == '٬') return Cls.CS;
            if (c == ' ' || c == '\t' || c == '\r') return Cls.WS;
            if (c >= '٠' && c <= '٩') return Cls.AN;
            if (c >= '۰' && c <= '۹') return Cls.EN;
            if (IsArabicLetter(c) || IsPresentationForm(c) || c == '؛' || c == '؟' || c == '٭') return Cls.AL;
            if (c >= '֐' && c <= '׿') return Cls.R;
            if (char.IsLetter(c)) return Cls.L;
            if (char.IsDigit(c)) return Cls.EN;
            return Cls.ON;
        }
    }

    /// <summary>
    /// The rules for admin-spawned assets, with no Unity types: reading the manifest,
    /// checking a bundle's hash, and deciding from a prefab's component type names whether it may be spawned.
    /// </summary>
    internal static class AssetManifest
    {
        public const int MaxManifestChars = 65536;
        public const int MaxEntries = 64;
        public const long MaxBundleBytes = 32 * 1024 * 1024;
        // A manifest entry may ask for more objects or particles than the defaults, up to the limits; nothing goes past a limit.
        public const int DefaultMaxObjects = 64;
        public const int MaxObjectsLimit = 5000;
        public const int DefaultMaxParticles = 500;
        public const int MaxParticlesLimit = 50000;
        public const int MaxIdLength = 32;
        public const float MaxLifetime = 600f;
        public const float MaxSizeLimit = 1000f;
        public const float DefaultLifetime = 20f;
        public const float DefaultMaxSize = 5f;
        public const float MaxAudioVolume = 0.5f;
        public const float MaxLightIntensity = 3f;
        public const float MaxLightRange = 20f;

        // The only component types a prefab may carry. A script from a bundle is never here: it loads as a missing script or as a type
        // that is not listed, and either way the prefab is refused. Colliders are separate because they need "solid".
        static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "UnityEngine.Transform", "UnityEngine.RectTransform",
            "UnityEngine.MeshFilter", "UnityEngine.MeshRenderer", "UnityEngine.SkinnedMeshRenderer", "UnityEngine.SpriteRenderer",
            "UnityEngine.ParticleSystem", "UnityEngine.ParticleSystemRenderer", "UnityEngine.TrailRenderer", "UnityEngine.LineRenderer",
            "UnityEngine.Light", "UnityEngine.AudioSource", "UnityEngine.Animator",
            // The render pipeline's settings holder that every Light in a URP project carries (the game is URP). It has no behaviour of its own.
            "UnityEngine.Rendering.Universal.UniversalAdditionalLightData",
            "UnityEngine.Canvas", "UnityEngine.CanvasRenderer", "TMPro.TextMeshPro", "TMPro.TextMeshProUGUI",
        };

        static readonly HashSet<string> SolidOnly = new HashSet<string>(StringComparer.Ordinal)
        {
            "UnityEngine.BoxCollider", "UnityEngine.SphereCollider", "UnityEngine.CapsuleCollider",
        };

        /// <summary>
        /// Null if a prefab with these component type names may be spawned, else the reason it may not.
        /// A null name stands for a component whose script could not be loaded ("missing script"): it does nothing, and is
        /// refused unless <paramref name="allowMissingScripts"/> is set. At most <paramref name="maxComponents"/> components.
        /// </summary>
        public static string CheckComponents(IList<string> typeNames, bool solid, bool allowMissingScripts, int maxComponents)
        {
            if (typeNames == null) return "no components";
            if (typeNames.Count > maxComponents) return typeNames.Count + " components, more than " + maxComponents;
            foreach (string name in typeNames)
            {
                if (name == null)
                {
                    if (allowMissingScripts) continue;
                    return "a missing script";
                }
                if (Allowed.Contains(name)) continue;
                if (SolidOnly.Contains(name))
                {
                    if (solid) continue;
                    return name + " needs the asset to be marked solid";
                }
                return name + " is not allowed";
            }
            return null;
        }

        /// <summary>True for 1 to 32 characters of a-z, 0-9, '_' and '-'.</summary>
        public static bool IsId(string s) { return Util.Only(s, MaxIdLength, "a-z0-9_-"); }

        // A bundle file name or a prefab name: letters, digits, '_' and '-' (and one ".bundle" ending for a file), so it can never be a path.
        static bool IsName(string s, bool file)
        {
            if (s == null || s.Length > 64) return false;
            if (file)
            {
                if (!s.EndsWith(".bundle", StringComparison.Ordinal)) return false;
                s = s.Substring(0, s.Length - ".bundle".Length);
            }
            return Util.Only(s, 64, "a-zA-Z0-9_-");
        }

        static bool IsHash(string s) { return s != null && s.Length == 64 && Util.Only(s, 64, "0-9a-fA-F"); }

        public static string Sha256Hex(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                var sb = new StringBuilder(64);
                foreach (byte b in sha.ComputeHash(bytes)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>True when <paramref name="bytes"/> hash to <paramref name="expectedHex"/> (any letter case).</summary>
        public static bool HashMatches(byte[] bytes, string expectedHex)
        {
            return bytes != null && IsHash(expectedHex) && string.Equals(Sha256Hex(bytes), expectedHex, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads manifest text for one platform ("windows" or "android"). Returns the entries that are well formed for that platform;
        /// each entry that is skipped adds a line to <paramref name="problems"/>. Returns null if the text is not a manifest at all.
        /// </summary>
        public static List<AssetEntry> Parse(string json, string platform, List<string> problems)
        {
            List<object> items = Json.VersionedList(json, "the manifest", "assets", MaxManifestChars, problems, out Dictionary<string, object> _);
            if (items == null) return null;

            var result = new List<AssetEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                if (i >= MaxEntries) { problems.Add("more than " + MaxEntries + " assets: the rest are ignored"); break; }
                var d = items[i] as Dictionary<string, object>;
                string why = d == null ? "is not an object" : null;
                var e = new AssetEntry();
                string entryPlatform = null;
                if (why == null)
                {
                    e.Id = Json.Text(d, "id");
                    e.Bundle = Json.Text(d, "bundle");
                    e.Sha256 = Json.Text(d, "sha256");
                    e.Prefab = Json.Text(d, "prefab");
                    entryPlatform = Json.Text(d, "platform");
                    if (!IsId(e.Id)) why = "has a bad id";
                    else if (!IsName(e.Bundle, true)) why = "has a bad bundle file name";
                    else if (!IsHash(e.Sha256)) why = "has no valid sha256";
                    else if (!IsName(e.Prefab, false)) why = "has a bad prefab name";
                    else if (entryPlatform != "windows" && entryPlatform != "android") why = "has a platform that is not windows or android";
                    else if (!seen.Add(e.Id)) why = "repeats an id";
                    else if (!Json.Bool(d, "solid", out e.Solid)) why = "has a solid that is not true or false";
                    else if (!Json.Bool(d, "allowMissingScripts", out e.AllowMissingScripts)) why = "has an allowMissingScripts that is not true or false";
                    else if (!Json.Whole(d, "maxObjects", DefaultMaxObjects, 1, MaxObjectsLimit, out e.MaxObjects)) why = "has a maxObjects that is not a whole number from 1 to " + MaxObjectsLimit;
                    else if (!Json.Whole(d, "maxParticles", DefaultMaxParticles, 0, MaxParticlesLimit, out e.MaxParticles)) why = "has a maxParticles that is not a whole number from 0 to " + MaxParticlesLimit;
                    else if (!Json.Number(d, "lifetime", DefaultLifetime, 1f, MaxLifetime, out e.Lifetime)) why = "has a lifetime outside 1 to " + MaxLifetime;
                    else if (!Json.Number(d, "maxSize", DefaultMaxSize, 0.1f, MaxSizeLimit, out e.MaxSize)) why = "has a maxSize outside 0.1 to " + MaxSizeLimit;
                    else e.Sha256 = e.Sha256.ToLowerInvariant();
                }
                if (why != null) { problems.Add("asset " + (i + 1) + (e.Id != null && IsId(e.Id) ? " (" + e.Id + ")" : "") + " " + why); continue; }
                if (entryPlatform == platform) result.Add(e);
            }
            return result;
        }

        public const int MaxAdmins = 500;

        /// <summary>
        /// Reads admins.json: <c>{"version": 1, "enabled": true, "admins": ["user id", ...]}</c>. <paramref name="enabled"/> is false only
        /// when the file says so. A bad id is skipped with a line in <paramref name="problems"/>. Returns null if the text is not an admin list.
        /// </summary>
        public static List<string> ParseAdmins(string json, out bool enabled, List<string> problems)
        {
            enabled = true;
            List<object> items = Json.VersionedList(json, "the admin list", "admins", MaxManifestChars, problems, out Dictionary<string, object> top);
            if (items == null) return null;
            if (!Json.Bool(top, "enabled", out bool on)) { problems.Add("the admin list's enabled is not true or false"); return null; }
            enabled = !top.ContainsKey("enabled") || on;

            var result = new List<string>();
            for (int i = 0; i < items.Count; i++)
            {
                if (i >= MaxAdmins) { problems.Add("more than " + MaxAdmins + " admins: the rest are ignored"); break; }
                var id = items[i] as string;
                if (!IsUserId(id)) { problems.Add("admin " + (i + 1) + " is not a valid user id"); continue; }
                if (!result.Contains(id)) result.Add(id);
            }
            return result;
        }

        public const int MaxFolderFiles = 64;
        public const long MaxFolderFileBytes = 8 * 1024 * 1024;
        static readonly string[] FolderExtensions = { ".ttf", ".otf", ".txt", ".json", ".png" };
        static readonly string[] DeviceNames = { "con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
                                                 "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9" };

        /// <summary>One file the shared Beacon folder should hold: its path inside the folder ('/' between parts), size and SHA-256.</summary>
        public struct FolderFile
        {
            public string Path;
            public long Size;
            public string Sha256;
        }

        /// <summary>
        /// Reads files.json: <c>{"version": 1, "files": [{"path": "Fonts/x.ttf", "size": 123, "sha256": "..."}]}</c>, the files the shared
        /// Beacon folder should hold. A bad entry is skipped with a line in <paramref name="problems"/>. Returns null if the text is not a file list.
        /// </summary>
        public static List<FolderFile> ParseFiles(string json, List<string> problems)
        {
            List<object> items = Json.VersionedList(json, "the file list", "files", MaxManifestChars, problems, out Dictionary<string, object> _);
            if (items == null) return null;

            var result = new List<FolderFile>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < items.Count; i++)
            {
                if (i >= MaxFolderFiles) { problems.Add("more than " + MaxFolderFiles + " files: the rest are ignored"); break; }
                var d = items[i] as Dictionary<string, object>;
                var f = new FolderFile { Path = d != null ? Json.Text(d, "path") : null, Sha256 = d != null ? Json.Text(d, "sha256") : null };
                double size = d != null && d.TryGetValue("size", out object s) && s is double n ? n : -1;
                string why = d == null ? "is not an object"
                    : !IsFolderPath(f.Path) ? "has a path that is not allowed"
                    : size < 1 || size > MaxFolderFileBytes || size != Math.Floor(size) ? "has a size outside 1 to " + MaxFolderFileBytes + " bytes"
                    : !IsHash(f.Sha256) ? "has a bad sha256"
                    : !seen.Add(f.Path) ? "repeats a path" : null;
                if (why != null) { problems.Add("file " + (i + 1) + " " + why); continue; }
                f.Size = (long)size;
                f.Sha256 = f.Sha256.ToLowerInvariant();
                result.Add(f);
            }
            return result;
        }

        /// <summary>
        /// A path the file list may write inside the Beacon folder: 1 to 3 parts joined by '/', each of letters, digits, '_', '-' and '.'
        /// and never starting with '.' (so never ".."), no Windows device name, not inside "assets" (the admin's own files), and ending in
        /// a data extension (a font, text, JSON or a PNG): never code.
        /// </summary>
        public static bool IsFolderPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 128) return false;
            string[] parts = path.Split('/');
            if (parts.Length > 3 || string.Equals(parts[0], "assets", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (string part in parts)
            {
                if (!Util.Only(part, 64, "a-zA-Z0-9_.-") || part[0] == '.') return false;
                if (Array.IndexOf(DeviceNames, part.Split('.')[0].ToLowerInvariant()) >= 0) return false;
            }
            string last = parts[parts.Length - 1];
            int dot = last.LastIndexOf('.');
            return dot > 0 && Array.IndexOf(FolderExtensions, last.Substring(dot).ToLowerInvariant()) >= 0;
        }

        // A Photon user id as the game hands it out: 1 to 64 letters, digits or '-'.
        static bool IsUserId(string s) { return Util.Only(s, 64, "a-zA-Z0-9-"); }

    }

    /// <summary>
    /// The shared Arabic translations: <c>{"version": 1, "lines": [{"en": "Settings", "ar": "الإعدادات"}, ...]}</c>. A text a mod draws that is
    /// exactly one of the English lines is drawn as its Arabic for a player whose language is Arabic. No Unity types.
    /// </summary>
    internal static class Translations
    {
        public const int MaxChars = 256 * 1024, MaxLines = 3000, MaxEnglish = 200, MaxArabic = 400;

        static Dictionary<string, string> current = new Dictionary<string, string>();

        public static int Count { get { return current.Count; } }

        public static void Set(Dictionary<string, string> lines) { current = lines ?? new Dictionary<string, string>(); }

        /// <summary>The Arabic for an English text that is exactly one of the lines; otherwise the text itself.</summary>
        public static string For(string text) { return text.Length <= MaxEnglish && current.TryGetValue(text, out string ar) ? ar : text; }

        /// <summary>English line to Arabic line. A bad line is skipped with a line in <paramref name="problems"/>; null if the text is not a translation list.</summary>
        public static Dictionary<string, string> Parse(string json, List<string> problems)
        {
            List<object> items = Json.VersionedList(json, "the translations", "lines", MaxChars, problems, out Dictionary<string, object> _);
            if (items == null) return null;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                if (i >= MaxLines) { problems.Add("more than " + MaxLines + " lines: the rest are ignored"); break; }
                var d = items[i] as Dictionary<string, object>;
                string en = d != null ? Json.Text(d, "en") : null, ar = d != null ? Json.Text(d, "ar") : null;
                string why = d == null ? "is not an object"
                    : !Plain(en, MaxEnglish) ? "has an English side that is empty, too long, or has tags or control characters"
                    : Util.Only(en, 12, "A-Z0-9") ? "has an English side that looks like a player name (capitals and digits only), which is never translated"
                    : !Plain(ar, MaxArabic) || !ArabicShaper.ContainsArabic(ar) ? "has an Arabic side that is empty, too long, has tags, or has no Arabic"
                    : result.ContainsKey(en) ? "repeats an English line" : null;
                if (why != null) { problems.Add("line " + (i + 1) + " " + why); continue; }
                result[en] = ar;
            }
            return result;
        }

        // Some text, within the length, with no rich-text tags and no control characters.
        static bool Plain(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length > max || s.IndexOf('<') >= 0 || s.IndexOf('>') >= 0) return false;
            foreach (char c in s) if (char.IsControl(c)) return false;
            return true;
        }
    }

    // Just enough JSON for small text files such as translations: objects become Dictionary<string, object>, strings stay strings,
    // arrays become List<object>, numbers become double, true/false become bool, null stays null. Mono has no System.Web.Extensions.
    internal static class Json
    {
        const int MaxDepth = 64;

        public static object Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) throw new FormatException("the text is empty");
            int i = 0;
            object v = Value(s, ref i, 0);
            Skip(s, ref i);
            if (i != s.Length) throw Err(s, i, "unexpected text after the end");
            return v;
        }

        /// <summary>
        /// The list under <paramref name="key"/> in a <c>{"version": 1, "key": [...]}</c> file, and the file's top object. Null, with the
        /// reason in <paramref name="problems"/>, if the text is not such a file; <paramref name="what"/> names it there ("the manifest").
        /// </summary>
        public static List<object> VersionedList(string json, string what, string key, int maxChars, List<string> problems, out Dictionary<string, object> top)
        {
            top = null;
            if (json == null || json.Length > maxChars) { problems.Add(what + " is missing or longer than " + maxChars + " characters"); return null; }
            try { top = Parse(json) as Dictionary<string, object>; }
            catch (FormatException e) { problems.Add(what + " is not valid JSON: " + e.Message); return null; }
            if (top == null || !top.TryGetValue("version", out object version) || !(version is double ver) || ver != 1)
            {
                problems.Add(what + " must be an object with \"version\": 1");
                return null;
            }
            var items = top.TryGetValue(key, out object list) ? list as List<object> : null;
            if (items == null) problems.Add(what + " has no \"" + key + "\" list");
            return items;
        }

        public static string Text(Dictionary<string, object> d, string key) { return d.TryGetValue(key, out object v) ? v as string : null; }

        // The readers below keep the fallback when the key is missing, and return false when it is there but not the right kind of value.
        public static bool Bool(Dictionary<string, object> d, string key, out bool value)
        {
            value = false;
            if (!d.TryGetValue(key, out object v)) return true;
            if (!(v is bool b)) return false;
            value = b;
            return true;
        }

        public static bool Whole(Dictionary<string, object> d, string key, int fallback, int min, int max, out int value)
        {
            value = fallback;
            if (!d.TryGetValue(key, out object v)) return true;
            if (!(v is double n)) return false;
            if (double.IsNaN(n) || n != Math.Floor(n) || n < min || n > max) return false;
            value = (int)n;
            return true;
        }

        public static bool Number(Dictionary<string, object> d, string key, float fallback, float min, float max, out float value)
        {
            value = fallback;
            if (!d.TryGetValue(key, out object v)) return true;
            if (!(v is double n)) return false;
            if (double.IsNaN(n) || n < min || n > max) return false;
            value = (float)n;
            return true;
        }

        static object Value(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw Err(s, i, "nested too deeply");
            Skip(s, ref i);
            if (i >= s.Length) throw Err(s, i, "the text ends too early");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++;
                Skip(s, ref i);
                if (Peek(s, i) == '}') { i++; return d; }
                while (true)
                {
                    Skip(s, ref i);
                    if (Peek(s, i) != '"') throw Err(s, i, "expected a quoted key");
                    string key = Str(s, ref i);
                    Skip(s, ref i);
                    if (Peek(s, i) != ':') throw Err(s, i, "expected ':'");
                    i++;
                    d[key] = Value(s, ref i, depth + 1);
                    Skip(s, ref i);
                    char n = Peek(s, i);
                    i++;
                    if (n == '}') return d;
                    if (n != ',') throw Err(s, i - 1, "expected ',' or '}'");
                }
            }
            if (c == '[')
            {
                var a = new List<object>();
                i++;
                Skip(s, ref i);
                if (Peek(s, i) == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(s, ref i, depth + 1));
                    Skip(s, ref i);
                    char n = Peek(s, i);
                    i++;
                    if (n == ']') return a;
                    if (n != ',') throw Err(s, i - 1, "expected ',' or ']'");
                }
            }
            if (c == '"') return Str(s, ref i);

            int start = i;
            while (i < s.Length && ",}] \t\r\n".IndexOf(s[i]) < 0) i++;
            string token = s.Substring(start, i - start);
            if (token == "true") return true;
            if (token == "false") return false;
            if (token == "null") return null;
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                throw Err(s, start, "unexpected '" + token + "'");
            return number;
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;   // the opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                            throw Err(s, i - 2, "bad \\u escape");
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: throw Err(s, i - 2, "bad escape '\\" + e + "'");
                }
            }
            throw Err(s, s.Length, "a string is not closed");
        }

        static char Peek(string s, int i) { return i < s.Length ? s[i] : '\0'; }

        static void Skip(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n' || s[i] == '\uFEFF')) i++;
        }

        static FormatException Err(string s, int at, string what)
        {
            int line = 1;
            for (int k = 0; k < at && k < s.Length; k++) if (s[k] == '\n') line++;
            return new FormatException(what + " (line " + line + ")");
        }
    }

    /// <summary>
    /// Lets each sender through at most <c>capacity</c> messages in a burst and <c>perSecond</c> after that (a token bucket per sender).
    /// The clock is passed in, in seconds, so a test can drive it.
    /// </summary>
    internal sealed class RateLimiter
    {
        // The table is cleared if this many senders are tracked, so a stream of new sender ids cannot grow it without limit.
        const int MaxTracked = 256;

        sealed class Bucket { public double Tokens; public double Last; }

        readonly double capacity;
        readonly double perSecond;
        readonly Func<double> clock;
        readonly Dictionary<int, Bucket> buckets = new Dictionary<int, Bucket>();

        public RateLimiter(double capacity, double perSecond, Func<double> clock)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException("capacity");
            if (perSecond <= 0) throw new ArgumentOutOfRangeException("perSecond");
            this.capacity = capacity;
            this.perSecond = perSecond;
            this.clock = clock;
        }

        /// <summary>True if this sender may send one more message now; it uses up one token if so.</summary>
        public bool Allow(int sender)
        {
            double now = clock();
            if (!buckets.TryGetValue(sender, out Bucket b))
            {
                if (buckets.Count >= MaxTracked) buckets.Clear();
                b = new Bucket { Tokens = capacity, Last = now };
                buckets[sender] = b;
            }
            double elapsed = now - b.Last;
            if (elapsed < 0) elapsed = 0;
            b.Tokens = Math.Min(capacity, b.Tokens + elapsed * perSecond);
            b.Last = now;
            if (b.Tokens < 1) return false;
            b.Tokens -= 1;
            return true;
        }

        /// <summary>Drops what is remembered about a sender, for when that player leaves.</summary>
        public void Forget(int sender) { buckets.Remove(sender); }
    }

    /// <summary>
    /// One show from <c>Beacon\assets\shows.json</c>: an asset played with its music, shown and hidden by cues on the music's clock,
    /// each change faded over its cue's fade (the show's <see cref="Fade"/> by default). No Unity types.
    /// </summary>
    internal sealed class ShowScript
    {
        public const int MaxShows = 32;
        public const int MaxCues = 200;
        public const int MaxChars = 64 * 1024;

        public string Id;
        public string Asset;
        public float Height = 37f;          // the asset's height in metres
        public float Scale;                 // a fixed size factor instead of Height; 0 when not given
        public float Fade = 0.5f;
        public string Then;                 // the show that starts where this one ends, or null
        public float Gap;                   // seconds of quiet between the end and the show that follows
        public readonly List<Cue> Cues = new List<Cue>();

        public const int MaxParts = 8;
        public readonly List<Part> Parts = new List<Part>();

        public struct Cue
        {
            public float At;
            public bool Shown;              // the show's cues say "ground", a part's say "show"
            public float Fade;              // this change's fade, in seconds
        }

        /// <summary>A named child of the asset (one dancer of several) with its own show and hide cues on the same clock.</summary>
        public sealed class Part
        {
            public string Name;
            public readonly List<Cue> Cues = new List<Cue>();
        }

        /// <summary>The second after which nothing changes any more.</summary>
        public float End
        {
            get
            {
                float end = 0f;
                foreach (Cue c in Cues) end = Math.Max(end, c.At + c.Fade);
                foreach (Part p in Parts) foreach (Cue c in p.Cues) end = Math.Max(end, c.At + c.Fade);
                return end;
            }
        }

        /// <summary>How visible the show is at second <paramref name="t"/>, 0 to 1.</summary>
        public float At(float t)
        {
            return Channel(Cues, t);
        }

        /// <summary>How visible part <paramref name="i"/> is at second <paramref name="t"/>, 0 to 1, before the show's own fade.</summary>
        public float PartAt(int i, float t)
        {
            return Channel(Parts[i].Cues, t);
        }

        // The last cue at or before t fades from the value before it to its own.
        static float Channel(List<Cue> cues, float t)
        {
            float from = 0f, to = 0f, since = -1f, fade = 0f;
            foreach (Cue c in cues)
            {
                if (c.At > t) break;
                from = since < 0f ? 0f : Current(from, to, since, c.At, fade);
                to = c.Shown ? 1f : 0f;
                since = c.At;
                fade = c.Fade;
            }
            return since < 0f ? 0f : Current(from, to, since, t, fade);
        }

        static float Current(float from, float to, float since, float t, float fade)
        {
            if (fade <= 0f) return to;
            float k = (t - since) / fade;
            return k >= 1f ? to : from + (to - from) * Math.Max(0f, k);
        }

        /// <summary>
        /// Reads shows.json. Returns the well-formed shows; each skipped show adds a line to <paramref name="problems"/>.
        /// Returns null if the text is not a shows file at all.
        /// </summary>
        public static List<ShowScript> Parse(string json, List<string> problems)
        {
            List<object> items = Json.VersionedList(json, "the shows file", "shows", MaxChars, problems, out Dictionary<string, object> _);
            if (items == null) return null;

            var result = new List<ShowScript>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                if (i >= MaxShows) { problems.Add("more than " + MaxShows + " shows: the rest are ignored"); break; }
                var d = items[i] as Dictionary<string, object>;
                var s = new ShowScript();
                string why = d == null ? "is not an object" : Read(d, s);
                if (why == null && !seen.Add(s.Id)) why = "repeats an id";
                if (why != null) { problems.Add("show " + (i + 1) + (s.Id != null && AssetManifest.IsId(s.Id) ? " (" + s.Id + ")" : "") + " " + why); continue; }
                result.Add(s);
            }
            return result;
        }

        static string Read(Dictionary<string, object> d, ShowScript s)
        {
            s.Id = Json.Text(d, "id");
            s.Asset = Json.Text(d, "asset");
            if (!AssetManifest.IsId(s.Id)) return "has a bad id";
            if (!AssetManifest.IsId(s.Asset)) return "has a bad asset id";
            if (!Json.Number(d, "height", 37f, 0.1f, 200f, out s.Height)) return "has a height outside 0.1 to 200";
            if (!Json.Number(d, "scale", 0f, 0.1f, 200f, out s.Scale)) return "has a scale outside 0.1 to 200";
            if (!Json.Number(d, "fade", 0.5f, 0f, 10f, out s.Fade)) return "has a fade outside 0 to 10";
            s.Then = Json.Text(d, "then");
            if (s.Then != null && (!AssetManifest.IsId(s.Then) || s.Then == s.Id)) return "has a bad \"then\" id";
            if (!Json.Number(d, "gap", 0f, 0f, 10f, out s.Gap)) return "has a gap outside 0 to 10";
            string why = ReadCues(d, s.Fade, false, s.Cues);
            if (why != null) return why;
            if (!d.TryGetValue("parts", out object v)) return null;
            var parts = v as List<object>;
            if (parts == null || parts.Count == 0 || parts.Count > MaxParts) return "has parts that are not a list of 1 to " + MaxParts;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (object o in parts)
            {
                var pd = o as Dictionary<string, object>;
                var p = new Part { Name = pd != null ? Json.Text(pd, "name") : null };
                if (!AssetManifest.IsId(p.Name) || !names.Add(p.Name)) return "has a part with a bad or repeated name";
                why = pd == null ? "is not an object" : ReadCues(pd, s.Fade, true, p.Cues);
                if (why != null) return "part " + p.Name + " " + why;
                s.Parts.Add(p);
            }
            return null;
        }

        // A part's cues say "show", the show's own say "ground": true or false. Each may carry its own "fade".
        static string ReadCues(Dictionary<string, object> d, float fade, bool part, List<Cue> into)
        {
            object v;
            string key = part ? "show" : "ground";
            var cues = d.TryGetValue("cues", out v) ? v as List<object> : null;
            if (cues == null || cues.Count == 0) return "has no cues";
            if (cues.Count > MaxCues) return "has more than " + MaxCues + " cues";
            float last = 0f;
            foreach (object o in cues)
            {
                var c = o as Dictionary<string, object>;
                if (c == null) return "has a cue that is not an object";
                var cue = new Cue();
                if (!c.ContainsKey("at") || !Json.Number(c, "at", 0f, 0f, 3600f, out cue.At)) return "has a cue without an \"at\" from 0 to 3600";
                if (cue.At < last) return "has cues out of time order";
                if (!Json.Number(c, "fade", fade, 0f, 10f, out cue.Fade)) return "has a cue with a fade outside 0 to 10";
                if (!c.TryGetValue(key, out v)) return "has a cue without \"" + key + "\"";
                if (!(v is bool)) return "has a cue whose " + key + " is not true or false";
                cue.Shown = (bool)v;
                last = cue.At;
                into.Add(cue);
            }
            return null;
        }
    }

    /// <summary>
    /// The usage ping, with no game types: a random id made on this PC (never the player's account), Beacon's version and the mods
    /// that carry Beacon here (plugin ID, name, version). Nothing about the player, the room or anyone else is in it.
    /// </summary>
    internal static class StatsPing
    {
        internal const string Url = Endpoints.CounterUrl;
        internal const float FirstAfter = 10f, Every = 600f;
        internal const int MaxMods = 16, MaxGuid = 64, MaxName = 40, MaxVersion = 16;

        /// <summary>A new install id: 32 lower-case hex digits from a random GUID.</summary>
        internal static string NewId() { return Guid.NewGuid().ToString("N"); }

        /// <summary>True for exactly 32 lower-case hex digits.</summary>
        internal static bool IsId(string id) { return id != null && id.Length == 32 && Util.Only(id, 32, "0-9a-f"); }

        /// <summary>
        /// The ping's JSON. Each mod is { guid, name, version }; a mod with no usable plugin ID is left out, a repeated ID counts once,
        /// at most <see cref="MaxMods"/> go in. Null when the id is not one <see cref="IsId"/> accepts.
        /// </summary>
        internal static string Body(string id, string beaconVersion, IEnumerable<string[]> mods)
        {
            if (!IsId(id)) return null;
            var sb = new StringBuilder(256);
            sb.Append("{\"v\":1,\"id\":\"").Append(id).Append("\",\"beacon\":\"").Append(VersionText(beaconVersion)).Append("\",\"mods\":[");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int count = 0;
            if (mods != null)
                foreach (string[] m in mods)
                {
                    if (count >= MaxMods) break;
                    if (m == null || m.Length < 1 || !IsGuid(m[0]) || !seen.Add(m[0])) continue;
                    if (count > 0) sb.Append(',');
                    sb.Append("{\"guid\":\"").Append(m[0])
                      .Append("\",\"name\":\"").Append(Escape(Util.Clean(m.Length > 1 ? m[1] : null, MaxName)))
                      .Append("\",\"version\":\"").Append(VersionText(m.Length > 2 ? m[2] : null)).Append("\"}");
                    count++;
                }
            return sb.Append("]}").ToString();
        }

        /// <summary>A BepInEx plugin ID as Beacon sends it: 1 to 64 of letters, digits, '.', '_' and '-'.</summary>
        internal static bool IsGuid(string guid) { return Util.Only(guid, MaxGuid, "a-zA-Z0-9._-"); }

        // Digits and dots only, capped; anything else becomes "".
        static string VersionText(string v) { return Util.Only(v, MaxVersion, "0-9.") ? v : ""; }

        static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20 || c > 0x7e) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Beacon's chime, with no game types: a warm tone (a note and three overtones) per note, a soft attack, a decay that rings out,
    /// and a fade to silence at the end so it never stops mid-ring. A notification is two notes, an announcement four rising ones.
    /// </summary>
    internal static class Chime
    {
        internal const int Rate = 44100;
        internal const float Volume = 0.3f, Peak = 0.7f;
        const double Decay = 4.5, Attack = 0.006, FadeOut = 0.35;
        static readonly double[][] Partials = { new[] { 1.0, 1.0 }, new[] { 2.0, 0.5 }, new[] { 3.0, 0.2 }, new[] { 4.0, 0.1 } };

        /// <summary>The chime's samples, mono at <see cref="Rate"/>, its loudest sample at <see cref="Peak"/>.</summary>
        internal static float[] Samples(bool announcement)
        {
            int[] notes = announcement ? new[] { 84, 88, 91, 96 } : new[] { 88, 95 };   // C6 E6 G6 C7, or E6 B6
            double gap = announcement ? 0.12 : 0.11, tail = announcement ? 1.8 : 1.6;
            int length = (int)Math.Ceiling((gap * (notes.Length - 1) + tail) * Rate);
            var s = new double[length];
            for (int k = 0; k < notes.Length; k++)
            {
                double hz = 440.0 * Math.Pow(2.0, (notes[k] - 69) / 12.0);
                int start = (int)Math.Floor(k * gap * Rate);
                for (int i = 0; start + i < length; i++)
                {
                    double t = (double)i / Rate, env = Math.Min(1.0, t / Attack) * Math.Exp(-t * Decay), v = 0;
                    foreach (double[] p in Partials) v += p[1] * Math.Sin(2 * Math.PI * hz * p[0] * t);
                    s[start + i] += v * env;
                }
            }
            int fade = (int)(FadeOut * Rate);
            for (int i = 0; i < fade; i++) s[length - 1 - i] *= 0.5 - 0.5 * Math.Cos(Math.PI * i / fade);
            double peak = 0;
            foreach (double x in s) peak = Math.Max(peak, Math.Abs(x));
            var result = new float[length];
            for (int i = 0; i < length; i++) result[i] = (float)(s[i] / peak * Peak);
            return result;
        }
    }

    /// <summary>
    /// A message the owner sends from the server's panel, with no game types: a notification for whoever is playing, or an announcement
    /// also shown to anyone who starts the game before it ends. Only a message signed with one of the owner's keys is shown, so neither
    /// the server nor anyone between can make Beacon show something he did not send.
    /// </summary>
    internal sealed class OwnerMessage
    {
        internal const int MaxText = 300, MaxBody = 2000, MaxSig = 512, MaxListed = 20, MaxReply = 64 * 1024, MaxSeen = 300;
        internal const float FirstAfter = 20f, Every = 120f;
        const double Day = 86400000.0;

        public string Id, Kind, En, Ar;
        public double Until;

        /// <summary>The line to show this player: the one in their language, else the other.</summary>
        internal string For(string language) { return language == "ar" ? Ar ?? En : En ?? Ar; }

        /// <summary>True when <paramref name="sig"/> (base64) is an RSA PKCS#1 v1.5 SHA-256 signature of the body's UTF-8 bytes by the key
        /// with this modulus (base64, exponent 65537).</summary>
        internal static bool Signed(string body, string sig, string modulus)
        {
            if (body == null || sig == null || modulus == null || sig.Length > MaxSig) return false;
            byte[] s, n;
            try { s = Convert.FromBase64String(sig); n = Convert.FromBase64String(modulus); }
            catch (FormatException) { return false; }
            using (RSA rsa = RSA.Create())
            {
                try
                {
                    rsa.ImportParameters(new RSAParameters { Modulus = n, Exponent = new byte[] { 1, 0, 1 } });
                    return rsa.VerifyData(Encoding.UTF8.GetBytes(body), s, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
                catch (CryptographicException) { return false; }
            }
        }

        /// <summary>The message in a body the server listed, checked against the server's clock (ms since 1970), or null with why.</summary>
        internal static OwnerMessage Read(string body, double now, out string why)
        {
            why = null;
            Dictionary<string, object> d = null;
            if (body != null && body.Length <= MaxBody)
                try { d = Json.Parse(body) as Dictionary<string, object>; } catch (FormatException) { }
            if (d == null) { why = "it is not a message"; return null; }
            var m = new OwnerMessage { Id = Json.Text(d, "id"), Kind = Json.Text(d, "kind"), En = Json.Text(d, "en"), Ar = Json.Text(d, "ar") };
            double at = Num(d, "at"), version = Num(d, "v");
            m.Until = Num(d, "until");
            double life = m.Kind == "notify" ? Day : m.Kind == "announce" ? 30 * Day : 0;
            if (version != 1) why = "its version is not 1";
            else if (m.Id == null || m.Id.Length < 16 || !Util.Only(m.Id, 32, "0-9a-f")) why = "its id is not 16 to 32 hex digits";
            else if (life == 0) why = "its kind is not notify or announce";
            else if (double.IsNaN(at) || double.IsNaN(m.Until) || m.Until <= now || m.Until - at > life) why = "it has ended or lasts too long";
            else if (m.En == null && m.Ar == null) why = "it has no text";
            else if ((d.ContainsKey("en") && !Line(m.En)) || (d.ContainsKey("ar") && !Line(m.Ar))) why = "its text is empty, too long or has tags or control characters";
            return why == null ? m : null;
        }

        static bool Line(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > MaxText) return false;
            foreach (char c in s) if (c < 0x20 || (c >= 0x7f && c <= 0x9f) || c == '<' || c == '>') return false;
            return true;
        }

        static double Num(Dictionary<string, object> d, string key) { return d.TryGetValue(key, out object v) && v is double x ? x : double.NaN; }

        /// <summary>The server's reply: its clock and up to <see cref="MaxListed"/> listed messages as { body, sig, "0" or "1" for the key },
        /// newest first. Null with why when the reply does not read.</summary>
        internal static List<string[]> ReadList(string json, out double now, out string why)
        {
            now = double.NaN;
            why = null;
            Dictionary<string, object> d = null;
            if (json != null && json.Length <= MaxReply)
                try { d = Json.Parse(json) as Dictionary<string, object>; } catch (FormatException) { }
            List<object> items = d != null && d.TryGetValue("messages", out object m) ? m as List<object> : null;
            if (d != null) now = Num(d, "now");
            if (items == null || double.IsNaN(now)) { why = "the reply is not a message list"; return null; }
            var list = new List<string[]>();
            foreach (object o in items)
            {
                if (list.Count >= MaxListed) break;
                var it = o as Dictionary<string, object>;
                if (it == null) continue;
                double key = Num(it, "key");
                string body = Json.Text(it, "body"), sig = Json.Text(it, "sig");
                if (body == null || sig == null || (key != 0 && key != 1)) continue;
                list.Add(new[] { body, sig, key == 1 ? "1" : "0" });
            }
            return list;
        }
    }

    /// <summary>
    /// The rules for telling a mod's players about a new release, with no game types: the GitHub repo a mod names, the release
    /// GitHub sends back, the version compare and the message. Everything from GitHub is treated as hostile text.
    /// </summary>
    internal static class UpdateCheck
    {
        internal const string DefaultMessage = "{mod} {new} is out (you have {current})";
        internal const int MaxReplyBytes = 512 * 1024;
        const int MaxMessage = 200, MaxTag = 40;

        /// <summary>True for "owner/name" as GitHub allows them: letters, digits, '-', '_' and '.' (the owner without '.'), not "." or "..".</summary>
        internal static bool IsRepo(string repo)
        {
            if (string.IsNullOrEmpty(repo)) return false;
            string[] parts = repo.Split('/');
            if (parts.Length != 2) return false;
            return Util.Only(parts[0], 39, "a-zA-Z0-9-") && Util.Only(parts[1], 100, "a-zA-Z0-9._-") && parts[1] != "." && parts[1] != "..";
        }

        /// <summary>
        /// A release tag as a version: "v1.3.0", "1.3", "1.3.0-beta" (the numbers before any suffix). Null for anything else.
        /// Missing parts count as 0, so 1.2 equals 1.2.0.
        /// </summary>
        internal static Version ParseTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || tag.Length > MaxTag) return null;
            int i = tag[0] == 'v' || tag[0] == 'V' ? 1 : 0;
            var parts = new List<int>();
            while (parts.Count < 4)
            {
                int start = i, n = 0;
                while (i < tag.Length && tag[i] >= '0' && tag[i] <= '9' && i - start < 9) n = n * 10 + (tag[i++] - '0');
                if (i == start) return null;
                parts.Add(n);
                if (i >= tag.Length || tag[i] != '.') break;
                i++;
            }
            if (i < tag.Length && tag[i] != '-' && tag[i] != '+') return null;
            return Normal(new Version(parts[0], parts.Count > 1 ? parts[1] : 0, parts.Count > 2 ? parts[2] : 0, parts.Count > 3 ? parts[3] : 0));
        }

        /// <summary>The version with every missing part as 0 (System.Version takes 1.2 for less than 1.2.0).</summary>
        internal static Version Normal(Version v)
        {
            return new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        /// <summary>The release's tag from GitHub's reply to releases/latest, or null with the reason.</summary>
        internal static string ReadTag(string json, out string why)
        {
            why = null;
            object root;
            try { root = Json.Parse(json); }
            catch (FormatException e) { why = "the reply is not JSON: " + e.Message; return null; }
            var d = root as Dictionary<string, object>;
            if (d == null || !d.TryGetValue("tag_name", out object tag) || !(tag is string)) { why = "the reply has no tag_name"; return null; }
            return (string)tag;
        }

        /// <summary>
        /// What the players read. <paramref name="template"/> is the mod's own text (null or empty for the default), where {mod},
        /// {new} and {current} are filled in; the versions are drawn as numbers, never as the tag GitHub sent.
        /// </summary>
        internal static string Message(string template, string mod, Version newer, Version current)
        {
            string t = string.IsNullOrEmpty(template) ? DefaultMessage : template;
            return Util.Clean(t.Replace("{mod}", mod ?? "").Replace("{new}", Short(newer)).Replace("{current}", Short(current)), MaxMessage);
        }

        /// <summary>"1.3.0" rather than "1.3.0.0"; at least three parts.</summary>
        internal static string Short(Version v)
        {
            v = Normal(v);
            return v.Revision > 0 ? v.ToString(4) : v.ToString(3);
        }

        /// <summary>The file name that remembers the last version announced for this repo (only call it with a repo IsRepo accepts).</summary>
        internal static string SeenFile(string repo) { return repo.Replace('/', '@') + ".txt"; }
    }

    /// <summary>
    /// Builds and checks the frames Beacon sends between players. A frame is an <c>object[]</c> that Photon can carry:
    /// <c>{ "BCN", protocol byte, type byte, fields... }</c>. Anything received is untrusted, so <see cref="TryParse"/> checks
    /// the shape, every type, every length and every character before a message exists.
    /// </summary>
    internal static class Wire
    {
        // Do not change Magic or Protocol on their own: copies that do not know a protocol number drop its frames.
        public const string Magic = "BCN";
        public const byte Protocol = 1;

        public const int MaxVersionLength = 16;
        public const int MaxLanguageLength = 8;
        public const int MaxNameLength = 16;

        /// <summary>A world position sent in a frame may be at most this far from the origin on each axis, in metres.</summary>
        public const float MaxCoordinate = 1000f;

        public static object[] Presence(string version, string language) => Checked(Frame(MessageType.Presence, version, language));

        public static object[] PresenceRequest() => Checked(Frame(MessageType.PresenceRequest));

        /// <summary>Asks every copy to spawn the manifest asset <paramref name="id"/> at a world position, turned by <paramref name="yaw"/> degrees.</summary>
        public static object[] Asset(string id, float x, float y, float z, float yaw) => Checked(Frame(MessageType.Asset, id, x, y, z, yaw));

        /// <summary>
        /// Asks every copy to play the show <paramref name="id"/> from shows.json at a world position, turned by <paramref name="yaw"/>
        /// degrees. <paramref name="start"/> is when it began on the room's shared clock (Photon's server timestamp, in milliseconds).
        /// </summary>
        public static object[] Show(string id, float x, float y, float z, float yaw, int start) => Checked(Frame(MessageType.Show, id, x, y, z, yaw, start));

        public static object[] ShowStop(string id) => Checked(Frame(MessageType.ShowStop, id));

        /// <summary>
        /// Seconds from <paramref name="start"/> to <paramref name="now"/> on Photon's server timestamp, which counts milliseconds
        /// in an int that wraps around; the difference is taken in wrapping arithmetic, so a start just before the wrap still works.
        /// </summary>
        public static float Elapsed(int now, int start) => unchecked(now - start) / 1000f;

        static object[] Frame(MessageType type, params object[] fields)
        {
            var frame = new object[3 + fields.Length];
            frame[0] = Magic;
            frame[1] = Protocol;
            frame[2] = (byte)type;
            fields.CopyTo(frame, 3);
            return frame;
        }

        // A builder returns null instead of a frame the receiving side would reject.
        static object[] Checked(object[] frame) => TryParse(frame, out Message _, out string _) ? frame : null;

        /// <summary>Reads a received frame. Returns false and a short reason (for the log) if anything about it is wrong.</summary>
        public static bool TryParse(object data, out Message message, out string reject)
        {
            reject = Read(data as object[], out Message m);
            message = reject == null ? m : null;
            return reject == null;
        }

        // Null, or why the frame is refused.
        static string Read(object[] frame, out Message m)
        {
            m = null;
            if (frame == null) return "not an array";
            if (frame.Length < 3 || frame.Length > 9) return "bad length";
            if (!(frame[0] is string) || (string)frame[0] != Magic) return "bad magic";
            if (!ToInt(frame[1], out int protocol)) return "bad protocol field";
            if (protocol != Protocol) return "protocol " + protocol + " is not " + Protocol;
            if (!ToInt(frame[2], out int type) || type < 0 || type > 255 || !Enum.IsDefined(typeof(MessageType), (byte)type)) return "unknown type";

            m = new Message { Type = (MessageType)type };
            switch (m.Type)
            {
                case MessageType.Presence:
                    if (frame.Length != 5) return "bad length for presence";
                    m.Version = frame[3] as string;
                    m.Language = frame[4] as string;
                    if (!Util.Only(m.Version, MaxVersionLength, "0-9.") || m.Version.IndexOfAny("0123456789".ToCharArray()) < 0) return "bad version";
                    if (!Util.Only(m.Language, MaxLanguageLength, "a-zA-Z-")) return "bad language";
                    m.Language = m.Language.ToLowerInvariant();
                    return null;
                case MessageType.PresenceRequest:
                    return frame.Length == 3 ? null : "bad length for presence request";
                case MessageType.Asset:
                    if (frame.Length != 8) return "bad length for asset";
                    m.AssetId = frame[3] as string;
                    return AssetManifest.IsId(m.AssetId) ? ReadPlace(frame, m, "asset") : "bad asset id";
                case MessageType.Show:
                    if (frame.Length != 9) return "bad length for show";
                    m.ShowId = frame[3] as string;
                    if (!AssetManifest.IsId(m.ShowId)) return "bad show id";
                    string place = ReadPlace(frame, m, "show");
                    if (place != null) return place;
                    if (!(frame[8] is int)) return "bad show start";
                    m.Start = (int)frame[8];
                    return null;
                default:
                    if (frame.Length != 4) return "bad length for show stop";
                    m.ShowId = frame[3] as string;
                    return AssetManifest.IsId(m.ShowId) ? null : "bad show id";
            }
        }

        // The position and yaw at frame[4] to frame[7], shared by assets and shows.
        static string ReadPlace(object[] frame, Message m, string what)
        {
            if (!ToFloat(frame[4], MaxCoordinate, out m.X) || !ToFloat(frame[5], MaxCoordinate, out m.Y) || !ToFloat(frame[6], MaxCoordinate, out m.Z))
                return "bad " + what + " position";
            return ToFloat(frame[7], 360f, out m.Yaw) ? null : "bad " + what + " yaw";
        }

        // Photon may hand a small number back as a byte, short or int; anything else (a float, a string, an array) is not a number here.
        static bool ToInt(object o, out int value)
        {
            bool small = o is byte || o is sbyte || o is short || o is ushort || o is int;
            value = small ? Convert.ToInt32(o) : 0;
            return small;
        }

        // A float or double that is a real number (not NaN or infinity) within -limit..limit. Photon sends floats as floats.
        static bool ToFloat(object o, float limit, out float value)
        {
            value = 0;
            double d;
            if (o is float single) d = single;
            else if (o is double wide) d = wide;
            else return false;
            if (double.IsNaN(d) || double.IsInfinity(d) || d < -limit || d > limit) return false;
            value = (float)d;
            return true;
        }

        /// <summary>
        /// A player's name made safe to draw. Another player's client sets its own name, so it can hold TextMeshPro tags or control
        /// characters: both are dropped, spaces are squeezed and the name is cut to <see cref="MaxNameLength"/> characters. Never null.
        /// </summary>
        public static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            var sb = new StringBuilder();
            bool space = false;
            foreach (char c in name)
            {
                if (c == '<' || c == '>' || c == '&' || char.IsControl(c)) continue;
                if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c);
                if (sb.Length >= MaxNameLength) break;
            }
            return sb.Length == 0 ? "?" : sb.ToString();
        }
    }
}
