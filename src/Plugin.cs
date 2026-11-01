// Valyx Jarvis: a free voice assistant for Gorilla Tag. Say "Jarvis, make it day" or type it; everything Jarvis changes is
// on your own screen only. The plugin draws the dot, the panel and the Desktop Mode menu, and runs the actions; a small helper
// program (embedded in this DLL) listens, speaks and talks to the AI provider.
using System;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace ValyxJarvis
{
    [BepInPlugin(Guid, "Valyx Jarvis", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dev.itsreallyhex.valyxjarvis", Version = "1.1.0";

        ConfigEntry<string> menuKey;
        ConfigEntry<bool> vrStickTalk;
        Link link;
        Overlay overlay;
        Menu menu;
        bool pttDown, failed;

        void Awake()
        {
            Ui.Init(Logger);
            Actions.Log = s => Logger.LogInfo(s);
            Travel.Log = s => Logger.LogInfo(s);
            Rooms.Log = s => Logger.LogInfo(s);
            menuKey = Config.Bind("Controls", "MenuKey", "F8", "The key that opens the Jarvis menu in Desktop Mode (a Unity Input System key name, e.g. F8, F7, J).");
            vrStickTalk = Config.Bind("Controls", "VrHoldLeftStick", true, "In VR, hold the left thumbstick pressed in to talk to Jarvis (no wake word needed).");
            link = new Link(s => Logger.LogInfo(s));
            overlay = gameObject.AddComponent<Overlay>(); overlay.Link = link;
            var hideOverlay = Config.Bind("Recording", "HideOverlay", false, "true: hide the dot and panel (for clean screen recordings). Jarvis still works and talks.");
            overlay.ShowAll = () => !hideOverlay.Value;
            menu = gameObject.AddComponent<Menu>(); menu.Link = link;
            menu.DismissedNotices = Config.Bind("Notices", "DismissedVoiceTerms", "", "Voice-terms pages you pressed Later on (the popup stays away for these).");
            Key k; if (Enum.TryParse(menuKey.Value, true, out k)) menu.OpenKey = k; else Logger.LogWarning("MenuKey '" + menuKey.Value + "' is not a key name; using F8");
            Logger.LogInfo("Valyx Jarvis " + Version + " loaded. Say \"Jarvis\", hold the left stick in VR, or press " + menu.OpenKey + " in Desktop Mode for the menu.");
        }

        void Start() { Beacon.Core.Load("Valyx Jarvis", updates: "itsreallyhex/ValyxJarvis"); }   // players hear once when a newer release is out

        void Update()
        {
            try
            {
                // what the helper asked for: a few per frame, on the game thread
                for (int i = 0; i < 8; i++)
                {
                    var a = link.NextAction(); if (a == null) break;
                    bool ok; string said;
                    bool ar = Json.S(link.State, "lang") == "ar";
                    if (a.Type == "room") { var act = a; Rooms.Start(a.Value, ar, (ok2, text) => Report(act, ok2, text)); continue; }   // a join reports when it is done
                    if (a.Type == "menu") said = a.Value == "close" ? menu.CloseFromVoice(ar, out ok) : menu.OpenFromVoice(ar, out ok);   // the menu belongs to the plugin, not Actions
                    else said = Actions.Run(a, ar, out ok);
                    Report(a, ok, said);
                }
                Rooms.Tick();
                // Push-to-talk: the talk key (default V, set in the menu) while the game has focus and the menu is closed,
                // or in VR the left thumbstick held in (Unity's own XR input; the game's poller has no stick click).
                bool down = !menu.IsOpen && Application.isFocused && KeyHeld(Json.S(Json.O(link.State, "cfg"), "ptt", "V"));
                if (!down && vrStickTalk.Value && GorillaTagger.hasInstance && GorillaTagger.Instance.activeXRDisplay != null)
                {
                    // On SteamVR the game reads every button through SteamVR actions; Unity's read returned nothing there (owner, 2026-10-27).
                    // The game's actions.json binds LeftJoystickClick to the stick press but has no generated shortcut for it.
                    if (stickAction == null && Time.unscaledTime >= nextStickLookup) { nextStickLookup = Time.unscaledTime + 5f; stickAction = StickAction(); }
                    bool stick;
                    if (stickAction != null) down = StickHeld();
                    else if (InputDevices.GetDeviceAtXRNode(XRNode.LeftHand).TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisClick, out stick)) down = stick;
                }
                if (down != pttDown) { pttDown = down; link.Send("ptt", down ? "down" : "up"); }
                if (Time.unscaledTime >= nextSnapshot) { nextSnapshot = Time.unscaledTime + 2f; link.GameSnapshot = Snapshot(); }
                Travel.Tick(this);
            }
            catch (Exception e)
            {
                if (!failed) { failed = true; Logger.LogError("update: " + e); }
            }
        }

        /// <summary>What really happened goes back, so Jarvis says the item he found, not what the AI guessed.</summary>
        void Report(GameAction a, bool ok, string said)
        {
            overlay.ShowResult(said);
            link.Send("result", new System.Collections.Generic.Dictionary<string, object> { { "id", a.Id ?? "" }, { "type", a.Type }, { "ok", ok }, { "text", said ?? "" } });
        }

        object stickAction; float nextStickLookup; bool stickLogged;

        // Separate methods so a game without SteamVR never loads its types (the call would fail at JIT time, not here).
        // Retried every 5 s: SteamVR.active is false until SteamVR has started; meanwhile (and on OpenXR) Unity's XR read is used.
        object StickAction()
        {
            try
            {
                if (!Valve.VR.SteamVR.active) return null;
                var a = Valve.VR.SteamVR_Input.GetBooleanAction("GorillaTag", "LeftJoystickClick");
                if (!stickLogged) { stickLogged = true; Logger.LogInfo("VR talk: SteamVR LeftJoystickClick " + (a != null ? "found" : "not found")); }
                return a;
            }
            catch (Exception e) { if (!stickLogged) { stickLogged = true; Logger.LogInfo("VR talk: no SteamVR (" + e.GetType().Name + "), using Unity XR"); } return null; }
        }

        bool StickHeld() { return ((Valve.VR.SteamVR_Action_Boolean)stickAction).GetState(Valve.VR.SteamVR_Input_Sources.LeftHand); }

        float nextSnapshot;
        /// <summary>The room as the player sees it, for questions like "how many players?". Counts and names only, never positions.</summary>
        static string Snapshot()
        {
            var d = new System.Collections.Generic.Dictionary<string, object> { { "t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() } };
            var ns = NetworkSystem.Instance;
            bool inRoom = ns != null && ns.InRoom;
            d["inRoom"] = inRoom;
d["maps"] = Travel.Catalog();   // every spawn point the game has (names only)
            var zm = ZoneManagement.instance;   // the active map (several can be active; the first is the one the player stands in, mostly)
            d["zone"] = zm != null && zm.activeZones.Count > 0 ? zm.activeZones[0].ToString() : "";
            if (inRoom)
            {
                d["room"] = ns.RoomName ?? "";
                d["private"] = ns.SessionIsPrivate;
                d["players"] = ns.RoomPlayerCount;
                var names = new System.Collections.Generic.List<object>();
                foreach (var p in ns.AllNetPlayers)
                {
                    if (p == null || names.Count >= 10) continue;
                    var n = string.IsNullOrEmpty(p.SanitizedNickName) ? p.NickName : p.SanitizedNickName;
                    names.Add((p.IsLocal ? "*" : "") + (n ?? ""));   // * marks the player
                }
                d["names"] = names;
                var gm = GorillaGameManager.instance;
                d["mode"] = gm != null ? gm.GameModeName() : (ns.GameModeString ?? "");
                // Lava counts: these managers override LocalIsTagged (verified 2026-10-26); the base returns false. Counts only (owner's rule).
                bool tagMode = gm is GorillaTagManager || gm is GorillaHuntManager || gm is GorillaPaintbrawlManager;
                d["tagMode"] = tagMode;
                if (tagMode)
                {
                    int lava = 0; bool you = false;
                    foreach (var p in ns.AllNetPlayers) if (p != null && gm.LocalIsTagged(p)) { lava++; if (p.IsLocal) you = true; }
                    d["lava"] = lava; d["youLava"] = you;
                }
            }
            return Json.Write(d);
        }

        string keyName; Key key = Key.None;

        /// <summary>Is the talk key held? A Unity Input System key name ("V", "F9", "CapsLock"), "Mouse4"/"Mouse5", or "None".</summary>
        bool KeyHeld(string name)
        {
            if (name == "Mouse4") return Mouse.current != null && Mouse.current.backButton.isPressed;
            if (name == "Mouse5") return Mouse.current != null && Mouse.current.forwardButton.isPressed;
            if (name != keyName) { keyName = name; if (!Enum.TryParse(name, true, out key)) key = Key.None; }
            return key != Key.None && Keyboard.current != null && Keyboard.current[key].isPressed;
        }

        void OnDestroy()
        {
            Travel.Release(this);
            if (link != null) link.Dispose();
            if (overlay != null) Destroy(overlay);
            if (menu != null) Destroy(menu);
            Beacon.Core.Unload();
            Ui.Cleanup();
        }
    }
}