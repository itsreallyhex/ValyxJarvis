// Valyx Jarvis plugin: what Jarvis can change. Everything here is LOCAL: the time, the weather and the try-on are on this
// player's screen only. Game members verified in the decompiled source 2026-10-26 (gt-context): BetterDayNightManager.SetTimeOfDay /
// ClearTimeOfDay / SetFixedWeather / ClearFixedWeather (the ...Networked variants are never used), CosmeticsController.tryOnSet,
// ApplyCosmeticItemToSet(set, item, isLeftHand, applyToPlayerPrefs), UpdateWornCosmetics(sync: false) (sync true would tell others).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GorillaNetworking;
using UnityEngine;

namespace ValyxJarvis
{
    /// <summary>Pure: which time slot means "day", "night"... Read at run time. In game 2026-10-26 the 11 names were
    /// night, sunrise, sunrise, 10am, noon, 3pm, sunset, sunset, night, night, lightning: words and clock times mixed.</summary>
    public static class TimeSlots
    {
        static readonly Dictionary<string, int> Want = new Dictionary<string, int> { { "day", 12 }, { "night", 0 }, { "evening", 18 }, { "dawn", 6 } };

        static readonly Dictionary<string, int> Words = new Dictionary<string, int>
        {
            { "night", 0 }, { "midnight", 0 }, { "dawn", 6 }, { "sunrise", 6 }, { "morning", 8 }, { "noon", 12 }, { "day", 12 }, { "midday", 12 },
            { "afternoon", 15 }, { "sunset", 18 }, { "dusk", 18 }, { "evening", 19 },
        };

        public static int Hour(string name)
        {
            var s = (name ?? "").Trim().ToLowerInvariant();
            int w; if (Words.TryGetValue(s, out w)) return w;   // "lightning" and anything unknown stay -1
            int i = 0; while (i < s.Length && char.IsDigit(s[i])) i++;
            int h; if (i == 0 || !int.TryParse(s.Substring(0, i), out h) || h > 12) return -1;
            var rest = s.Substring(i).Trim();
            if (rest.StartsWith("am")) return h == 12 ? 0 : h;
            if (rest.StartsWith("pm")) return h == 12 ? 12 : h + 12;
            return -1;
        }

        /// <summary>The slot whose clock time is closest to the wanted hour; -1 when no name is a clock time.</summary>
        public static int Pick(IList<string> names, string want)
        {
            int target; if (names == null || !Want.TryGetValue(want, out target)) return -1;
            int best = -1, bestDist = 99;
            for (int i = 0; i < names.Count; i++)
            {
                int h = Hour(names[i]); if (h < 0) continue;
                int d = Math.Abs(h - target); d = Math.Min(d, 24 - d);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }
    }

    /// <summary>Pure: find the cosmetic a few spoken words mean ("cowboy hat", "قبعة") among the game's display names.</summary>
    public static class CosmeticMatch
    {
        static readonly Dictionary<string, string> Arabic = new Dictionary<string, string>
        {
            { "قبعة", "hat" }, { "قبعه", "hat" }, { "طاقية", "hat" }, { "كاب", "cap" }, { "نظارة", "glasses" }, { "نظاره", "glasses" }, { "نظارات", "glasses" },
            { "قميص", "shirt" }, { "تيشيرت", "shirt" }, { "هودي", "hoodie" }, { "تاج", "crown" }, { "شنطة", "backpack" }, { "حقيبة", "backpack" },
            { "وشاح", "scarf" }, { "جناح", "wings" }, { "اجنحة", "wings" }, { "أجنحة", "wings" }, { "قناع", "mask" }, { "بنطلون", "pants" }, { "سروال", "pants" },
            { "شعر", "hair" }, { "قرون", "horns" }, { "ساعة", "watch" }, { "قفازات", "gloves" }, { "سماعات", "headphones" }, { "زهرة", "flower" },
            { "احمر", "red" }, { "أحمر", "red" }, { "ازرق", "blue" }, { "أزرق", "blue" }, { "اسود", "black" }, { "أسود", "black" }, { "ابيض", "white" }, { "أبيض", "white" },
            { "حفلة", "party" }, { "حفله", "party" }, { "كاوبوي", "cowboy" }, { "قرصان", "pirate" }, { "ملك", "king" }, { "بطريق", "penguin" },
            { "اخضر", "green" }, { "أخضر", "green" }, { "ذهبي", "gold" }, { "وردي", "pink" }, { "بنفسجي", "purple" }, { "اصفر", "yellow" }, { "أصفر", "yellow" },
        };
        static readonly Dictionary<string, string[]> Same = new Dictionary<string, string[]>
        {
            { "hat", new[] { "cap", "beanie", "helmet", "hood" } }, { "cap", new[] { "hat" } }, { "glasses", new[] { "shades", "sunglasses", "goggles", "specs" } },
            { "shirt", new[] { "tee", "tshirt", "hoodie", "sweater", "jacket", "top" } }, { "backpack", new[] { "bag", "pack" } }, { "wings", new[] { "wing" } },
            { "pants", new[] { "shorts", "trousers", "jeans" } }, { "headphones", new[] { "headset", "earmuffs" } },
        };
        static readonly HashSet<string> Filler = new HashSet<string> { "a", "an", "the", "some", "my", "me", "please", "on", "cosmetic", "one", "that", "this", "ال" };

        public static List<string> Words(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in (s ?? "").ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            var list = new List<string>();
            foreach (var w0 in sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var w = w0.StartsWith("ال") && w0.Length > 3 ? w0.Substring(2) : w0;   // Arabic article
                string en; if (Arabic.TryGetValue(w, out en) || Arabic.TryGetValue(w0, out en)) w = en;
                if (Filler.Contains(w)) continue;
                if (w.Any(ch => ch >= 0x0600 && ch <= 0x06FF)) continue;   // an Arabic word we cannot translate would sink every match
                list.Add(w);
            }
            return list;
        }

        /// <summary>Higher is better; 0 means "not this one". Every query word must land somewhere.</summary>
        public static double Score(IList<string> query, string name)
        {
            if (query.Count == 0) return 0;
            var nw = Words(name); if (nw.Count == 0) return 0;
            double score = 0;
            foreach (var q in query)
            {
                double best = 0;
                string[] alts; Same.TryGetValue(q, out alts);
                foreach (var w in nw)
                {
                    if (w == q) best = Math.Max(best, 3);
                    else if ((alts != null && alts.Contains(w)) || SameAs(w, q)) best = Math.Max(best, 2.2);   // both ways: bag ~ backpack
                    else if (Prefix(q, w)) best = Math.Max(best, 2);
                    else if (q.Length >= 4 && w.Contains(q)) best = Math.Max(best, 1.2);
                }
                if (best == 0) return 0;
                score += best;
            }
            return score - 0.25 * Math.Max(0, nw.Count - query.Count);   // "cowboy hat" beats "golden cowboy hat with stars"
        }

        /// <summary>One word is the start of the other and nearly as long: "glass"~"glasses", but not "ham"~"hamster" (in game that suggested "Ham Hock").</summary>
        static bool Prefix(string a, string b)
        {
            string s = a.Length <= b.Length ? a : b, l = a.Length <= b.Length ? b : a;
            return s.Length >= 4 && l.StartsWith(s) && s.Length >= 0.6 * l.Length;
        }

        static bool SameAs(string w, string q) { string[] a; return Same.TryGetValue(w, out a) && a.Contains(q); }

        /// <summary>When nothing matches every word: the names that match the MOST words ("hamster costume" -> hamster items), best first.</summary>
        public static List<int> Suggest(string query, IList<string> names, int count)
        {
            var q = Words(query); var scored = new List<KeyValuePair<int, double>>();
            if (q.Count == 0) return new List<int>();
            for (int i = 0; i < names.Count; i++)
            {
                var nw = Words(names[i]); if (nw.Count == 0) continue;
                int hits = 0;
                foreach (var x in q)
                {
                    string[] alts; Same.TryGetValue(x, out alts);
                    if (nw.Any(w => w == x || SameAs(w, x) || (alts != null && alts.Contains(w)) || Prefix(x, w))) hits++;
                }
                if (hits > 0) scored.Add(new KeyValuePair<int, double>(i, hits - 0.05 * nw.Count));
            }
            var seen = new HashSet<string>(); var result = new List<int>();
            foreach (var kv in scored.OrderByDescending(k => k.Value))
                if (seen.Add(names[kv.Key].ToLowerInvariant()) && result.Count < count) result.Add(kv.Key);
            return result;
        }

        /// <summary>Index of the best name, or -1.</summary>
        public static int Best(string query, IList<string> names)
        {
            var q = Words(query); int best = -1; double bestScore = 0;
            for (int i = 0; i < names.Count; i++)
            {
                var s = Score(q, names[i]);
                if (s > bestScore + 1e-9 || (Math.Abs(s - bestScore) < 1e-9 && best >= 0 && s > 0 && names[i].Length < names[best].Length)) { bestScore = s; best = i; }
            }
            return best;
        }
    }

    /// <summary>Pure: which of the game's own spawn points a map key means. Names read in game 2026-10-26 (SpawnPoint objects).</summary>
    public static class Maps
    {
        public static readonly Dictionary<string, string> Spawn = new Dictionary<string, string>
        {
            { "forest", "Forest" }, { "treehouse", "Tree House" }, { "canyon", "Canyon" }, { "cave", "Cave (Upper Level)" }, { "city", "City" },
            { "beach", "Beach" }, { "clouds", "SkyJungle (Clouds)" }, { "mountain", "Mountains (Top)" }, { "basement", "Basement" }, { "metro", "Metro" },
            { "arcade", "Arcade" }, { "bayou", "Bayou" }, { "rotating", "Rotating" }, { "critters", "Critters" }, { "mall", "Mall" }, { "space", "SpaceMap" },
            { "hoverpark", "Hoverboard" }, { "monkeblocks", "MonkeBlocks" }, { "arena", "Arena" }, { "ghostreactor", "GhostReactor" },
        };
        public static readonly Dictionary<string, string> Display = new Dictionary<string, string>
        {
            { "forest", "the Forest" }, { "treehouse", "the Tree House" }, { "canyon", "Canyons" }, { "cave", "the Caves" }, { "city", "the City" },
            { "beach", "the Beach" }, { "clouds", "the Clouds" }, { "mountain", "the Mountains" }, { "basement", "the Basement" }, { "metro", "Metropolis" },
            { "arcade", "the Arcade" }, { "bayou", "the Bayou" }, { "rotating", "Rotating" }, { "critters", "Critters" }, { "mall", "the Mall" },
            { "space", "Space" }, { "hoverpark", "the Hoverpark" }, { "monkeblocks", "Monke Blocks" }, { "arena", "the Arena" }, { "ghostreactor", "Ghost Reactor" },
        };
    }

    /// <summary>"Take me to <map>": the way the game's own teleport stations move a player (TeleportStationManager.FPTPort, read 2026-10-26):
    /// movement and gravity held, TeleportTo(center: true), SetActiveZone, gravity zones cleared, released a moment later. In a public room the
    /// player leaves first (owner: immediately, never refuses), so nobody sees the jump; in a private room or alone it goes at once.</summary>
    static class Travel
    {
        static SpawnPoint target; static string targetKey;
        static float leaveDeadline, releaseAt;
        static bool held;
        public static Action<string> Log = s => { };

        static List<SpawnPoint> All() { return Resources.FindObjectsOfTypeAll<SpawnPoint>().Where(s => s != null && s.gameObject.scene.IsValid() && !string.IsNullOrEmpty(s.name)).ToList(); }

        /// <summary>Every spawn point the game has, by name (for game.json; the helper matches spoken words against it).</summary>
        static List<object> catalog; static float catalogAt = -999f;
        public static List<object> Catalog()
        {
            // a scan of every SpawnPoint is not free: once every 30 s is plenty (maps only change with game updates)
            if (catalog == null || Time.unscaledTime - catalogAt > 30f) { catalog = All().Select(s => s.name).Distinct().OrderBy(n => n).Cast<object>().ToList(); catalogAt = Time.unscaledTime; }
            return catalog;
        }

        /// <summary>A spawn name ("Ranked"), an old map key ("canyon") or loose words ("criters"); the game decides, nothing else moves the player.</summary>
        public static string Go(string value, bool ar, out bool ok)
        {
            ok = false;
            var all = All();
            string spawnName; if (value != null && Maps.Spawn.TryGetValue(value, out spawnName)) value = spawnName;
            target = all.FirstOrDefault(s => string.Equals(s.name, value, StringComparison.OrdinalIgnoreCase));
            if (target == null && !string.IsNullOrEmpty(value))
            {
                var names = all.Select(s => s.name).ToList(); int i = CosmeticMatch.Best(value, names);
                if (i >= 0) target = all[i];
            }
            if (target == null) { Log("travel: no spawn point for '" + value + "'"); return ar ? "ما أعرف هذا الماب." : "I don't know that map."; }
            var key = Maps.Spawn.FirstOrDefault(kv => kv.Value == target.name).Key;
            var name = key != null && Maps.Display.ContainsKey(key) ? Maps.Display[key] : Readable(target.name);
            // Already on that map: a move inside it can look like a teleport to anti-cheat, and leaving the lobby for it made no sense
            // (owner, 2026-10-27). Same zone as "where am I" reports.
            var zm = ZoneManagement.instance;
            if (zm != null && zm.activeZones.Count > 0 && zm.activeZones[0] == target.startZone)
            {
                Log("travel: already in " + target.startZone + ", not moving"); target = null;
                return ar ? "انت أصلاً في " + name + "." : "You're already in " + name + ".";
            }
            targetKey = key ?? target.name; ok = true;
            var ns = NetworkSystem.Instance;
            bool leaving = ns != null && ns.InRoom && !ns.SessionIsPrivate;
            if (leaving) { ns.ReturnToSinglePlayer(); leaveDeadline = Time.unscaledTime + 6f; Log("travel: leaving the public room first"); }
            else leaveDeadline = 0f;
            return ar ? (leaving ? "طلعتك من الروم، وأوديك " + name + "." : "رايحين " + name + ".") : (leaving ? "Leaving this lobby, taking you to " + name + "." : "Taking you to " + name + ".");
        }

        /// <summary>"SkyJungle (Clouds)" -> "Sky Jungle (Clouds)"; "MonkeBlocks" -> "Monke Blocks".</summary>
        public static string Readable(string n) { return System.Text.RegularExpressions.Regex.Replace(n ?? "", "([a-z])([A-Z])", "$1 $2"); }

        static SpawnPoint loading; static float loadDeadline, settleAt;

        public static void Tick(UnityEngine.Object owner)
        {
            var gp = GorillaLocomotion.GTPlayer.Instance;
            if (held && loading == null && Time.unscaledTime >= releaseAt) Release(owner);
            if (gp == null) return;
            if (loading != null) { WaitForMap(gp); return; }
            if (target == null) return;
            var ns = NetworkSystem.Instance;
            if (leaveDeadline > 0f && ns != null && ns.InRoom)
            {
                if (Time.unscaledTime < leaveDeadline) return;   // still leaving: never jump while others can see it
                Log("travel: the room did not let go in time; not moving"); target = null; return;
            }
            var sp = target; target = null;
            // The map loads first and the player waits, held still with no gravity: a teleport onto a map that was still loading
            // dropped the player through the world into the game's quit box and the game closed (Canyon from the forest, 2026-10-31).
            gp.disableMovement = true; gp.SetGravityOverride(owner, null); gp.SetPlayerVelocity(Vector3.zero); held = true;
            ZoneManagement.SetActiveZone(sp.startZone);
            loading = sp; loadDeadline = Time.unscaledTime + 20f; settleAt = 0f;
            Log("travel: loading " + sp.startZone);
        }

        static void WaitForMap(GorillaLocomotion.GTPlayer gp)
        {
            gp.SetPlayerVelocity(Vector3.zero);   // no gravity, but a running start would still carry the player off
            var zm = ZoneManagement.instance;
            // IsZoneLoaded is false for zones without their own scene, so: no load still running and the zone switched on
            bool ready = loading != null && zm != null && !zm.AnyActiveLoadOps() && zm.IsZoneActive(loading.startZone);
            if (!ready && Time.unscaledTime < loadDeadline && loading != null) { settleAt = 0f; return; }
            if (ready && settleAt == 0f) { settleAt = Time.unscaledTime + 0.3f; return; }   // a moment for the new map's colliders
            if (ready && Time.unscaledTime < settleAt) return;
            var sp = loading; loading = null; releaseAt = Time.unscaledTime + 1.0f;
            if (sp == null) { Log("travel: the spawn point went away while loading; not moving"); return; }
            if (!ready) Log("travel: the map did not finish loading in 20 s; moving anyway");
            var t = sp.transform;
            gp.TeleportTo(t.position, Quaternion.Euler(0f, t.eulerAngles.y, 0f), false, true);
            if (GorillaLocomotion.GTPlayerTransform.Instance != null) GorillaLocomotion.GTPlayerTransform.Instance.ClearAllGravityZones();
            Log("travel -> " + targetKey + " at " + t.position + " (zone " + sp.startZone + ", waited " + (20f - (loadDeadline - Time.unscaledTime)).ToString("0.0") + " s)");
        }

        public static void Release(UnityEngine.Object owner)
        {
            loading = null;
            if (!held) return; held = false;
            var gp = GorillaLocomotion.GTPlayer.Instance;
            if (gp != null) { gp.disableMovement = false; gp.UnsetGravityOverride(owner); }
        }
    }
    /// <summary>Lobbies by voice: leave, a new private room, a public lobby on this map, back to the room before. A join takes a few
    /// seconds, so the result goes back when the game is done (or gives up), not at once. Nothing here joins a code the player says
    /// or types: a private code is made here, and "rejoin" only returns to the room this game was in before (owner, 2026-11-01).
    /// Game members verified 2026-11-01 (gt-context): PhotonNetworkController.AttemptToJoinSpecificRoomAsync (creates the room if
    /// missing, private), GorillaComputer.primaryTriggersByZone + GorillaNetworkJoinTrigger.OnBoxTriggered (the game's own public join).</summary>
    static class Rooms
    {
        const string Alphabet = "ABCDEFGHIJKLMNPQRSTUVWXYZ123456789";   // the game's own room-code alphabet (NetworkSystem.GetRandomRoomName)
        public static Action<string> Log = s => { };
        static string current = "", last = "";
        static string kind, room, joinResult; static bool ar, joinSent, sawBusy; static float deadline, joinAt, idleSince;
        static GorillaNetworkJoinTrigger trigger;
        static Action<bool, string> done;

        /// <summary>Starts a lobby command; done(ok, text) runs once, now or from Tick.</summary>
        public static void Start(string what, bool arabic, Action<bool, string> reply)
        {
            var ns = NetworkSystem.Instance;
            if (done != null) { reply(false, arabic ? "لحظة، باقي أخلص اللي قبل." : "Hang on, I'm still on the last one."); return; }
            if (ns == null || PhotonNetworkController.Instance == null || GorillaComputer.instance == null) { reply(false, arabic ? "الشبكة مو جاهزة للحين." : "The network isn't ready yet."); return; }
            kind = what; ar = arabic; room = null; trigger = null; joinResult = null; joinSent = false; sawBusy = false; idleSince = 0f;
            switch (what)
            {
                case "leave":
                    if (!ns.InRoom) { reply(false, arabic ? "أنت مو في روم." : "You're not in a lobby."); return; }
                    break;
                case "private":
                    room = NewCode();
                    break;
                case "rejoin":
                    if (last.Length == 0) { reply(false, arabic ? "ما أذكر روم قبل هذا." : "I don't remember a room from before."); return; }
                    if (current == last) { reply(false, arabic ? "أنت أصلاً فيه." : "You're already in it."); return; }
                    room = last;
                    break;
                case "public":
                    trigger = TriggerHere();
                    if (trigger == null) { reply(false, arabic ? "هذا الماب ما فيه رومات عامة." : "This map has no public lobbies."); return; }
                    break;
                default: reply(false, ""); return;
            }
            // the game looks for a code in every region (~5 s each) before it creates the room: 25 s in game (2026-11-01)
            done = reply; deadline = Time.unscaledTime + (what == "leave" ? 10f : 40f);
            if (ns.InRoom) { Log("rooms: leaving " + ns.RoomName + " for " + what); ns.ReturnToSinglePlayer(); }
        }

        /// <summary>Every frame: remembers the room before this one, and moves a running command on.</summary>
        public static void Tick()
        {
            var ns = NetworkSystem.Instance; if (ns == null) return;
            var now = ns.InRoom ? ns.RoomName ?? "" : "";
            if (now != current) { if (current.Length > 0) last = current; current = now; }
            if (done == null) return;
            if (kind == "leave") { if (!ns.InRoom) Finish(true, ar ? "طلعتك من الروم." : "You left the lobby."); else Timeout(); return; }
            if (!joinSent)
            {
                if (ns.InRoom || ns.netState != NetSystemState.Idle) { Timeout(); return; }   // still leaving the old room
                joinSent = true; joinAt = Time.unscaledTime;
                if (trigger != null) { Log("rooms: public join through the " + trigger.networkZone + " trigger"); trigger.OnBoxTriggered(); }
                else
                {
                    Log("rooms: joining " + (kind == "private" ? "a new private room" : "the last room"));
                    PhotonNetworkController.Instance.AttemptToJoinSpecificRoomAsync(room, JoinType.Solo, r => joinResult = r.ToString())
                        .ContinueWith(t => joinResult = "error", System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                }
                return;
            }
            if (ns.InRoom && (room == null || ns.RoomName == room))
            {
                if (kind == "private") Finish(true, ar ? "سويت لك روم خاص، الكود " + room + "." : "Made you a private room. The code is " + string.Join(" ", room.ToCharArray()) + ".");
                else if (kind == "rejoin") Finish(true, ar ? "رجعتك للروم اللي قبل." : "Back in your last room.");
                else Finish(true, ar ? "دخلتك روم عام." : "You're in a public lobby.");
                return;
            }
            if (joinResult == "Failed_Full") { Finish(false, ar ? "الروم مليان." : "That room is full."); return; }
            if (joinResult != null && joinResult != "Success" && joinResult != "FallbackCreated" && joinResult != "AlreadyInRoom") { Finish(false, ar ? "اللعبة ما دخلتني الروم." : "The game didn't let me in (" + joinResult + ")."); return; }
            // The game gives up without telling anyone: back to Idle after trying. In game 2026-11-01 the server refused the Steam
            // login (CustomAuthenticationFailed) within seconds, and the player waited 40 s for "the game didn't answer".
            if (ns.netState != NetSystemState.Idle) { sawBusy = true; idleSince = 0f; }
            else if (sawBusy)
            {
                if (idleSince == 0f) idleSince = Time.unscaledTime;
                else if (Time.unscaledTime - idleSince > 2f) { Finish(false, ar ? "اللعبة ما قدرت تدخل روم، السيرفر رفض. جرب بعد شوي، وإذا تكرر سكر اللعبة وافتحها." : "The game couldn't get into a lobby; its servers said no. Try again in a bit, or restart the game if it keeps happening."); return; }
            }
            else if (Time.unscaledTime - joinAt > 6f) { Finish(false, ar ? "اللعبة ما بدت تدخل روم من هنا." : "The game wouldn't start a join from here."); return; }   // a blocked join never leaves Idle
            Timeout();
        }

        static void Timeout() { if (Time.unscaledTime > deadline) Finish(false, ar ? "اللعبة ما ردت، جرب بعد شوي." : "The game didn't answer. Try again in a moment."); }

        static void Finish(bool ok, string text)
        {
            var d = done; done = null; trigger = null;
            Log("rooms: " + kind + (ok ? " ok" : " failed") + (joinResult != null ? " (" + joinResult + ")" : "") + ": " + text);
            if (d != null) d(ok, text);
        }

        /// <summary>The public join box of the map the player is on, as the game registered it (not a subscribers-only one).</summary>
        static GorillaNetworkJoinTrigger TriggerHere()
        {
            var zm = ZoneManagement.instance;
            if (zm == null || zm.activeZones.Count == 0) return null;
            var zone = zm.activeZones[0];
            return GorillaComputer.instance.primaryTriggersByZone.Values.FirstOrDefault(t => t != null && t.zone == zone && !t.isSubsOnly);
        }

        static string NewCode()
        {
            for (int tries = 0; ; tries++)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < 6; i++) sb.Append(Alphabet[UnityEngine.Random.Range(0, Alphabet.Length)]);
                if (tries > 20 || GorillaComputer.instance.CheckAutoBanListForName(sb.ToString())) return sb.ToString();   // the game's own word filter
            }
        }
    }

    /// <summary>The game side: runs one action on the game thread and says what happened.</summary>
    static class Actions
    {
        static List<CosmeticsController.CosmeticItem> wardrobe;   // items a player may try on, built once
        static List<string> wardrobeNames;
        public static Action<string> Log = s => { };

        /// <summary>Runs one action. ok is false when nothing changed; the text says what happened (empty when there is nothing to add).</summary>
        public static string Run(GameAction a, bool arabic, out bool ok)
        {
            ok = false;
            try
            {
                string said;
                switch (a.Type)
                {
                    case "time": said = Time(a.Value, arabic, out ok); break;
                    case "weather": said = Weather(a.Value, arabic, out ok); break;
                    case "tryon": said = TryOn(a.Value, arabic, out ok); break;
                    case "tryon_clear": said = ClearTryOn(arabic, out ok); break;
                    case "travel": said = Travel.Go(a.Value, arabic, out ok); break;
                    default: said = ""; break;
                }
                return said;
            }
            catch (Exception e) { Log("action " + a.Type + " failed: " + e); ok = false; return arabic ? "ما قدرت أسويها." : "Couldn't do that one."; }
        }

        static bool loggedSlots;

        static string Time(string v, bool ar, out bool ok)
        {
            ok = false;
            var dn = BetterDayNightManager.instance;
            if (dn == null) return ar ? "الوقت مو جاهز للحين." : "Time isn't ready yet.";
            if (v == "normal") { dn.ClearTimeOfDay(true); ok = true; return ""; }
            var names = dn.dayNightLightmapNames;
            if (!loggedSlots) { loggedSlots = true; Log("time slots: " + string.Join(", ", names ?? new string[0])); }
            int idx = TimeSlots.Pick(names, v);
            if (idx < 0) idx = v == "day" ? 3 : -1;   // slot 3 is "10am" in the game's own code
            int slots = dn.timeOfDayRange != null ? Math.Min(names != null ? names.Length : 0, dn.timeOfDayRange.Length) : 0;   // 11 names, fewer real slots
            if (idx < 0 || idx >= slots) return ar ? "ما لقيت هذا الوقت." : "I couldn't find that time.";
            dn.SetTimeOfDay(idx, true);
            Log("time -> " + v + " = slot " + idx + " (" + names[idx] + ")");
            ok = true; return "";
        }

        static string Weather(string v, bool ar, out bool ok)
        {
            ok = false;
            var dn = BetterDayNightManager.instance;
            if (dn == null) return ar ? "الطقس مو جاهز للحين." : "Weather isn't ready yet.";
            if (v == "normal") dn.ClearFixedWeather(true);
            else dn.SetFixedWeather(v == "rain" ? BetterDayNightManager.WeatherType.Raining : BetterDayNightManager.WeatherType.None, true);
            Log("weather -> " + v);
            ok = true; return "";
        }

        static bool Wearable(CosmeticsController.CosmeticItem c)
        {
            if (c.isNullItem || c.isHoldable || !c.canTryOn) return false;   // VRRig.IsItemAllowed: an unowned item shows only when canTryOn
            switch (c.itemCategory)
            {
                case CosmeticsController.CosmeticCategory.Hat: case CosmeticsController.CosmeticCategory.Badge: case CosmeticsController.CosmeticCategory.Face:
                case CosmeticsController.CosmeticCategory.Chest: case CosmeticsController.CosmeticCategory.Fur: case CosmeticsController.CosmeticCategory.Shirt:
                case CosmeticsController.CosmeticCategory.Back: case CosmeticsController.CosmeticCategory.Arms: case CosmeticsController.CosmeticCategory.Pants:
                case CosmeticsController.CosmeticCategory.Set:
                // body slots added by the game update of late October 2026 (ears, nose, glasses, eyes, mouth, hair, beard, neck, tattoo)
                case CosmeticsController.CosmeticCategory.Ears: case CosmeticsController.CosmeticCategory.Nose: case CosmeticsController.CosmeticCategory.Glasses:
                case CosmeticsController.CosmeticCategory.Eyes: case CosmeticsController.CosmeticCategory.Mouth: case CosmeticsController.CosmeticCategory.Hair:
                case CosmeticsController.CosmeticCategory.FacialHair: case CosmeticsController.CosmeticCategory.Neck: case CosmeticsController.CosmeticCategory.Tattoo:
                    return true;
            }
            return false;   // held items, paws, tag effects and collectables need the game's own stands
        }

        public static string NameOf(CosmeticsController.CosmeticItem c) { return string.IsNullOrEmpty(c.overrideDisplayName) ? c.displayName : c.overrideDisplayName; }

        static bool BuildWardrobe()
        {
            var cc = CosmeticsController.instance;
            if (cc == null || !cc.allCosmeticsDict_isInitialized) return false;
            if (wardrobe != null) return true;
            wardrobe = cc.allCosmeticsDict.Values.Where(Wearable).Where(c => !string.IsNullOrEmpty(NameOf(c))).ToList();
            wardrobeNames = wardrobe.Select(NameOf).ToList();
            Log("wardrobe: " + wardrobe.Count + " wearable of " + cc.allCosmeticsDict.Count + " cosmetics");
            return true;
        }

        static string TryOn(string query, bool ar, out bool ok)
        {
            ok = false;
            if (!BuildWardrobe()) return ar ? "الملابس ما تحملت للحين." : "The cosmetics haven't loaded yet.";
            int i = CosmeticMatch.Best(query, wardrobeNames);
            if (i < 0)
            {
                // say what IS close, so the player can pick one (owner, 2026-10-26: "hamster costume" found nothing at all)
                var close = CosmeticMatch.Suggest(query, wardrobeNames, 3).Select(k => Pretty(wardrobeNames[k])).ToList();
                Log("try-on: nothing matches '" + query + "'; close: " + string.Join(", ", close));
                if (close.Count == 0) return ar ? "ما لقيت شي اسمه " + query + "." : "I couldn't find \"" + query + "\".";
                return ar ? "ما لقيت " + query + " بالضبط. القريب منه: " + string.Join("، ", close) + "."
                          : "I couldn't find \"" + query + "\". Close ones: " + string.Join(", ", close) + ".";
            }
            var cc = CosmeticsController.instance;
            var rig = VRRig.LocalRig;
            if (rig == null) return ar ? "شخصيتك مو جاهزة للحين." : "Your monke isn't ready yet.";
            var item = wardrobe[i];
            // The rig shows the try-on set only in the store's fitting room (VRRig.SetCosmeticsActive: inTryOnRoom ? tryOnSet : null),
            // so outside the store nothing showed (owner, 2026-10-27). The flag is this game's own; other players' games set theirs.
            if (!rig.inTryOnRoom) { rig.inTryOnRoom = true; openedTryOn = true; }
            cc.ApplyCosmeticItemToSet(cc.tryOnSet, item, false, false);
            cc.UpdateWornCosmetics(false);   // false: only this screen. True would send the try-on to everyone.
            bool shown = rig.mergedSet != null && rig.mergedSet.HasItem(item.itemName);
            Log("try-on: '" + query + "' -> " + NameOf(item) + " (" + item.itemName + ", " + item.itemCategory + ")" + (shown ? "" : " NOT shown"));
            if (!shown) return ar ? "حاولت ألبسك " + Pretty(NameOf(item)) + " بس اللعبة ما لبستك إياه." : "I tried " + Pretty(NameOf(item)) + ", but the game didn't put it on.";
            ok = true;
            return (ar ? "تجرب: " : "Trying on: ") + Pretty(NameOf(item));
        }

        static bool openedTryOn;   // Jarvis turned the try-on flag on (not the store), so he turns it off again

        /// <summary>"PARTY HAT" reads as shouting when spoken and shown: "Party Hat".</summary>
        public static string Pretty(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Any(char.IsLower)) return name;
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
        }

        static string ClearTryOn(bool ar, out bool ok)
        {
            ok = false;
            var cc = CosmeticsController.instance;
            if (cc == null) return "";
            // Only the try-on set: the player's own cosmetics are theirs. Say so instead of "everything's off" (owner, 2026-10-27).
            if (cc.tryOnSet.items == null || !cc.tryOnSet.items.Any(x => !x.isNullItem && !string.IsNullOrEmpty(x.itemName)))
                return ar ? "ما عليك شي جربته. أقدر أشيل بس اللي لبستك إياه، مو أغراضك." : "You're not trying anything on. I can only take off what I put on, not your own cosmetics.";
            cc.tryOnSet.ClearSet(cc.nullItem);
            var rig = VRRig.LocalRig;
            if (openedTryOn && rig != null) rig.inTryOnRoom = false;   // never when the store set it
            openedTryOn = false;
            cc.UpdateWornCosmetics(false);
            Log("try-on cleared");
            ok = true; return ar ? "شلت اللي كنت تجربه." : "Took off what you were trying on.";
        }
    }
}