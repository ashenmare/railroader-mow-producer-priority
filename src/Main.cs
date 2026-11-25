using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Railroader.Modding;

namespace MowProducerPriority
{
    internal sealed class Settings
    {
        // Days of the plant's production kept for MoW on top of its shipping pile, 0 to 3 (Logic.ClampDays), one size per
        // load type (0.2.0). 0 is off for that type: its pile is shared as in the base game. Read live: a save in
        // Preferences > Mods takes effect at once. The host's counts; a client's changes nothing.
        // reserveDays is the key 0.1.x wrote: it is now the size for loads without a row of their own (another map's
        // plants), and the value an old file's missing per-type keys start from (Migrate).
        public float reserveDaysTies = Logic.DefaultDays;
        public float reserveDaysBallast = Logic.DefaultDays;
        public float reserveDaysConcrete = Logic.DefaultDays;
        public float reserveDaysRails = Logic.DefaultDays;
        public float reserveDaysBridge = Logic.DefaultDays;
        public float reserveDays = Logic.DefaultDays;
        // The shared debug log's switch (DebugLog.cs): one switch for every ashenmare mod, so this is only this mod's copy of it.
        // The reserves' moves and each reserve-aware order go to that log, never to Player.log.
        public bool debugLog = false;

        internal float DaysAt(int slot)
        {
            switch (slot)
            {
                case Logic.Ties: return reserveDaysTies;
                case Logic.Ballast: return reserveDaysBallast;
                case Logic.Concrete: return reserveDaysConcrete;
                case Logic.Rails: return reserveDaysRails;
                case Logic.Bridge: return reserveDaysBridge;
                case Logic.Other: return reserveDays;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        internal float DaysFor(string loadId) => Logic.ClampDays(DaysAt(Logic.TypeOf(loadId)));

        internal float[] All() => new[] { reserveDaysTies, reserveDaysBallast, reserveDaysConcrete, reserveDaysRails, reserveDaysBridge, reserveDays };

        // "ties 1, ballast 1, concrete 1, rails 1, bridge parts 1, other loads 1" for a log line.
        internal string Describe()
        {
            var parts = new string[Logic.TypeCount];
            for (int i = 0; i < parts.Length; i++) parts[i] = Logic.TypeNames[i] + " " + Logic.Days(DaysAt(i));
            return string.Join(", ", parts);
        }

        // An old file (0.1.x) has only reserveDays. A per-type key the file doesn't have takes that value (else the
        // default), so nobody's size changes on update. Nothing is written here: the new keys reach the file when the
        // player saves (the section writes every row). `file` is the parsed settings.json; `absent` gets the keys it lacked.
        internal void Migrate(JObject file, List<string> absent)
        {
            float? legacy = Read(file, "reserveDays");
            for (int i = 0; i < Logic.Other; i++)
            {
                string key = Logic.TypeKeys[i];
                if (Read(file, key) != null) continue;
                absent?.Add(key);
                float v = Logic.Inherit(null, legacy);
                switch (i)
                {
                    case Logic.Ties: reserveDaysTies = v; break;
                    case Logic.Ballast: reserveDaysBallast = v; break;
                    case Logic.Concrete: reserveDaysConcrete = v; break;
                    case Logic.Rails: reserveDaysRails = v; break;
                    case Logic.Bridge: reserveDaysBridge = v; break;
                    default: throw new ArgumentOutOfRangeException(nameof(i));
                }
            }
        }

        private static float? Read(JObject file, string key)
        {
            if (file == null || !file.TryGetValue(key, out JToken t) || t.Type == JTokenType.Null) return null;
            return t.Type == JTokenType.Integer || t.Type == JTokenType.Float || t.Type == JTokenType.String ? t.ToObject<float>() : (float?)null;
        }
    }

    // The off state, game-free so the lifecycle can set it directly: every hook returns at once while it is false. True
    // after every patch is in; false from the first line of OnModUnload, and when the mod stands down (Plants.Failed).
    internal static class Switch
    {
        internal static volatile bool Live;
    }

    // Railroader 0.6 entry point. A plant's MoW seller gets its own pile, filled first (Plants.cs). Every player needs the
    // mod (Info.json scope "unscoped": the game makes the save and every peer carry it, at the same version): industry
    // service and ordering run only on the host's game, so only the host moves stock, orders and uses its Reserve setting;
    // a client's copy splits its sellers too, so its industry panels show the same two piles as the host's.
    //
    // Mod load resolves every game member the mod reaches by reflection (Reflect.Resolve) and patches nothing unless all of
    // them are there; then it applies the five patches (Hooks.Patch), all or nothing. Either way a game update that breaks
    // it leaves one warning and a vanilla game.
    // The class-load rule: Mono loads every class a method names when it compiles that method,
    // before the method's own try runs, so the lifecycle methods name no game-typed class of ours (Plants, Reflect, Hooks)
    // and reach them only through the NoInlining trampolines below, each called inside a try. Switch and Logic are
    // game-free.
    // Railroader.Modding's IModEntryPoint, IWorldLifecycle, IModContext (ModDirectory, Log) and IModLogger, as
    // Picky Client uses them (OnModLoad at boot, OnWorldLoad after the restore, unloads in reverse order).
    public sealed class Main : IModEntryPoint, IWorldLifecycle
    {
        private const string HarmonyId = "ashenmare.mow-producer-priority";
        internal static IModLogger Log;
        internal static Settings Settings = new Settings();
        // The per-type keys the settings.json read at load didn't have, and the value each started from (0.2.0 migration):
        // the settings rows show that value, not a bare default, until the player saves.
        private static readonly Dictionary<string, float> Inherited = new Dictionary<string, float>();
        private static readonly HashSet<string> Logged = new HashSet<string>();
        private Harmony _harmony;
        private static bool _ready;   // every member resolved and every patch in: the world callbacks may run

        internal const string Id = "mow-producer-priority";
        // A once-a-session note on a plant left as it is: to the debug log only (Player.log stays quiet during play).
        internal static void SkipOnce(string key, string why, string where) { if (Logged.Add(key)) DebugLog.Event(Id, "skip", "why", why, "where", where); }
        internal static void WarnOnce(string key, string text) { if (Logged.Add(key)) Log?.Warning(text); }

        public void OnModLoad(IModContext context)
        {
            // Assemblies never unload and statics survive a disable/enable: reset everything here.
            Log = context.Log;
            string settingsPath = Path.Combine(context.ModDirectory, "settings.json");
            Inherited.Clear();
            Settings = LoadSettings(settingsPath);
            try { StartDebug(settingsPath); } catch (Exception) { }
            Logged.Clear();
            _ready = false;
            Switch.Live = false;
            Api.Reset();
            _harmony = new Harmony(HarmonyId);
            // A second OnModLoad with no unload between must not patch twice: every patch of ours comes off first, so each
            // hook runs once (twice would run every hook twice, and the order's deficit would come off twice).
            try { _harmony.UnpatchAll(HarmonyId); } catch (Exception) { }
            string missing;
            try
            {
                ResetPlants();
                missing = ResolveGame();
            }
            catch (Exception e)
            {
                Log.Warning("MoW Producer Priority is incompatible with this game version; nothing is patched and every pile stays shared: "
                            + PathClean.CleanText(e.ToString(), 4000));
                return;
            }
            if (missing != null)
            {
                Log.Warning("MoW Producer Priority is incompatible with this game version (not found: " + missing
                            + "); nothing is patched and every pile stays shared");
                return;
            }
            try { PatchGame(_harmony); }
            catch (Exception e)
            {
                try { _harmony.UnpatchAll(HarmonyId); } catch (Exception) { }
                Log.Warning("MoW Producer Priority is incompatible with this game version (a hook wouldn't go on); nothing is patched and every pile stays shared: "
                            + PathClean.CleanText(e.ToString(), 4000));
                return;
            }
            _ready = true;
            Switch.Live = true;
            // 0.1.5: the feed goes on the shared Mod Feeds list (ModFeeds.cs) so any mod can find it; a refusal (another mod
            // holds the id) is one log line. ModFeeds and Api name no game type, so this is safe here.
            try { if (!PublishFeed()) Log.Warning("MoW Producer Priority: another mod already publishes the feed id \"" + Api.Feed + "\", so this one isn't listed"); }
            catch (Exception) { }
            Log.Info("MoW Producer Priority loaded: a plant's MoW seller gets its own pile, filled first (Reserve days: " + Settings.Describe()
                     + (Logic.IsOn(Logic.Largest(Settings.All())) ? "" : ", so off")
                     + "; every player needs it, and the host's game keeps the reserves)");
            try { SettingsUi.Start(_harmony, settingsPath); }
            catch (Exception e)
            {
                StopSettings();
                Log.Warning("MoW Producer Priority: the settings aren't in Preferences > Mods on this game version (the mod works without them): "
                            + PathClean.CleanText(e.Message));
            }
        }

        // The feed's entry on the shared list: Api.Read, with a line per top-level key (release/FEEDS.md has the full schema).
        internal static bool PublishFeed()
        {
            Version v = typeof(Main).Assembly.GetName().Version;
            string version = v == null ? "" : v.Major + "." + v.Minor + "." + v.Build;
            var fields = new Dictionary<string, string>
            {
                { "active", "this game keeps MoW reserves now: the mod is running, this game is the host or single player, and at least one pile is kept" },
                { "reserveDays", "the largest of this game's reserve sizes, in days of production (0 to 3), kept for readers that know one number: 0 only when every size is 0; each pile's own size is its days" },
                { "piles", "one entry per MoW reserve pile found: industryId, loadId, sellerId, stock and cap (in the load's units), days, active" },
            };
            return ModFeeds.Publish(Api.Feed, Api.Read, "MoW Producer Priority", "MoW Producer Priority", version, Api.Version, fields);
        }

        // The off state first, so every hook is inert from here; then, where a world is still loaded, what fits of each
        // reserve back onto its shipping pile and every pile shared again (the rest stays in the seller's key, so in the
        // save, unused until a save loads with the mod running); then the patches off, in their own try. In the game a
        // mod-set change runs OnWorldUnload first, so this usually finds no world.
        public void OnModUnload()
        {
            Switch.Live = false;
            try { ModFeeds.Withdraw(Api.Feed); } catch (Exception) { }
            StopSettings();
            if (_ready)
            {
                try { StopPlants(); } catch (Exception) { }
            }
            try { DebugLog.Forget(Id); } catch (Exception) { }
            try { _harmony?.UnpatchAll(HarmonyId); }
            catch (Exception e) { Log?.Warning("MoW Producer Priority: couldn't take its hooks off: " + PathClean.CleanText(e.Message)); }
            _harmony = null;
            _ready = false;
        }

        // After the save is restored and the scheduler started: find this railroad's piles and split them (every machine;
        // only the host keeps the reserves).
        public void OnWorldLoad()
        {
            if (!_ready) return;
            Logged.Clear();
            Switch.Live = true;   // a stand-down lasts one session: a new world tries again
            try { LoadPlants(); }
            catch (Exception e)
            {
                Log?.Warning("MoW Producer Priority: couldn't set up this railroad's MoW reserves, so every pile stays shared: "
                             + PathClean.CleanText(e.ToString(), 4000));
                try { StopPlants(); } catch (Exception) { }
            }
        }

        // The world is going (a return to menu or another save; neither writes a save from here): nothing is written, the
        // mod forgets the piles.
        public void OnWorldUnload()
        {
            if (!_ready) return;
            try { ForgetPlants(); } catch (Exception) { }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ResetPlants() => Plants.Reset();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ResolveGame() => Reflect.Resolve();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PatchGame(Harmony harmony) => Hooks.Patch(harmony);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void LoadPlants() => Plants.WorldLoad();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void StopPlants() => Plants.Stop();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ForgetPlants() => Plants.WorldUnload();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyPlants() => Plants.Apply();

        private static void StopSettings()
        {
            try { SettingsUi.Stop(); } catch (Exception) { }
        }

        private static string _settingsPath;

        // The shared debug log (DebugLog.cs). The switch itself is shared; the row in this mod's settings is a copy of it, so at load
        // the copy is made to match, and whenever any mod changes the switch every loaded mod's copy follows.
        private static void StartDebug(string settingsPath)
        {
            _settingsPath = settingsPath;
            Version v = typeof(Main).Assembly.GetName().Version;
            DebugLog.Init(Id, v == null ? "" : v.Major + "." + v.Minor + "." + v.Build);
            if (Settings.debugLog != DebugLog.On) { Settings.debugLog = DebugLog.On; MirrorDebug(DebugLog.On); }
            DebugLog.OnChanged(Id, on =>
            {
                Settings.debugLog = on;
                MirrorDebug(on);
                if (DebugLog.On && on) DebugLog.Event(Id, "settings", "days", Settings.Describe(), "allOff", !Logic.IsOn(Logic.Largest(Settings.All())));
            });
        }

        // Writes the switch into this mod's settings.json when the file's own value differs, keeping every other key. A file
        // that can't be read is left alone.
        private static void MirrorDebug(bool on)
        {
            try
            {
                if (_settingsPath == null || !File.Exists(_settingsPath)) return;
                string text = File.ReadAllText(_settingsPath);
                if (string.IsNullOrWhiteSpace(text)) return;   // blank reads as the defaults, silently (LoadSettings)
                JObject o;
                try { o = JObject.Parse(text); }
                catch (JsonException) { return; }   // LoadSettings already said the file can't be read
                if (o.TryGetValue("debugLog", StringComparison.OrdinalIgnoreCase, out JToken t) && t.Type == JTokenType.Boolean && (bool)t == on) return;
                o["debugLog"] = on;
                string tmp = _settingsPath + ".tmp";
                File.WriteAllText(tmp, o.ToString(Formatting.Indented));
                File.Replace(tmp, _settingsPath, null);   // the file exists (checked above): one replace, never a moment with no settings.json
            }
            catch (Exception e) { Log?.Warning("MoW Producer Priority: couldn't update the debug log row in settings.json: " + PathClean.CleanText(e.Message)); }
        }

        // After a save in Preferences > Mods: the same read as at load, from the file just written, then applied to the
        // world on the host (Reserve 0 merges back what fits and shares each pile again once the rest fits too; from 0 the
        // reserves are kept again). A throw here is the section's to report: it says the settings take effect after a
        // restart.
        internal static void Applied(JObject saved)
        {
            Settings = saved.ToObject<Settings>() ?? new Settings();
            Settings.Migrate(saved, null);
            // The row is this mod's copy of the shared switch: when the saved file has it and it differs from the switch, the
            // player just changed it here; a file without the key says nothing about it.
            try
            {
                if (!saved.TryGetValue("debugLog", StringComparison.OrdinalIgnoreCase, out _)) Settings.debugLog = DebugLog.On;
                else if (Settings.debugLog != DebugLog.On) DebugLog.Set(Settings.debugLog);
            }
            catch (Exception) { }
            if (DebugLog.On) DebugLog.Event(Id, "settings", "days", Settings.Describe(), "allOff", !Logic.IsOn(Logic.Largest(Settings.All())));
            if (_ready) ApplyPlants();
        }

        // What the settings row of this key starts from where the file lacks it: the old single Reserve's value on the first
        // run after an update (the player's size carried over), else the default.
        internal static float RowDefault(string key) => Inherited.TryGetValue(key, out float v) ? Logic.ClampDays(v) : Logic.DefaultDays;

        // Mono puts the file's whole path, and so the player's account name, into an IO exception's message ("Access to
        // the path ... is denied."), and Player.log goes with bug reports: both logs below take it through PathClean.
        private static Settings LoadSettings(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path);
                    // An empty or blank file reads as the defaults, silently (as in 0.1.x).
                    if (string.IsNullOrWhiteSpace(text)) return new Settings();
                    Settings read = JsonConvert.DeserializeObject<Settings>(text) ?? new Settings();
                    // 0.2.0: a file without the per-type keys (0.1.x wrote only reserveDays) carries its size over to them.
                    if (JToken.Parse(text) is JObject file)
                    {
                        var absent = new List<string>();
                        read.Migrate(file, absent);
                        foreach (string key in absent) Inherited[key] = read.DaysAt(Array.IndexOf(Logic.TypeKeys, key));
                    }
                    return read;
                }
            }
            catch (Exception e) { Inherited.Clear(); Log?.Warning("MoW Producer Priority: couldn't read settings.json, using the defaults (Reserve 1 day): " + PathClean.CleanText(e.Message)); return new Settings(); }
            // No file yet: write one with the defaults, so players can find it. Never in the zip, so an update can't
            // overwrite someone's choice; an existing file (even an unreadable one) is never touched.
            Settings d = new Settings();
            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(d, Formatting.Indented));
                File.Move(tmp, path);
                Log?.Info("MoW Producer Priority: wrote settings.json with the defaults (Reserve 1 day)");
            }
            catch (Exception e)
            {
                Log?.Warning("MoW Producer Priority: couldn't write a default settings.json (the defaults apply anyway): " + PathClean.CleanText(e.Message));
                try { File.Delete(path + ".tmp"); } catch (Exception) { }
            }
            return d;
        }
    }

    // Everything typed by SettingsSection.cs (and so by the game's UI) lives here, off Main, behind NoInlining methods
    // called inside a try, so a game update that breaks the section can't take the reserves with it (the house pattern).
    internal static class SettingsUi
    {
        private static object _section;   // a SettingsSection

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Start(Harmony harmony, string settingsPath)
        {
            Stop();
            var fields = new[]
            {
                ReserveRow(Logic.Ties, "Ties reserve (days)", "Days of production kept for MoW ties. 0 is off."),
                ReserveRow(Logic.Ballast, "Ballast reserve (days)", "Days of production kept for MoW ballast. 0 is off."),
                ReserveRow(Logic.Concrete, "Concrete reserve (days)", "Days of production kept for MoW concrete. 0 is off."),
                ReserveRow(Logic.Rails, "Rails (days)", "Days of production kept for MoW rails. 0 is off."),
                ReserveRow(Logic.Bridge, "Bridge parts (days)", "Days of production kept for MoW bridge parts. 0 is off."),
                ReserveRow(Logic.Other, "Other loads (days)", "Plants on other maps. 0 is off."),
                SettingField.Bool("debugLog", "Debug log (for bug reports)", false, true,
                    "Writes Documents/Railroader/Logs; send the file with a bug report."),
            };
            _section = new SettingsSection("MoW Producer Priority", settingsPath, fields, Main.Applied, m => Main.Log?.Warning("MoW Producer Priority: " + m),
                "Every player needs the mod; the host's settings count.");
            PreferencesModsTab.Add(harmony, b => (_section as SettingsSection)?.Build(b, true), m => Main.Log?.Warning("MoW Producer Priority: " + m));
        }

        // One slider per load type, 0 to 3 days in half days. A row whose key an old file lacks starts from the old single
        // Reserve's value (Main.RowDefault), so the row shows what the mod uses.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static SettingField ReserveRow(int slot, string label, string help) =>
            SettingField.FloatSlider(Logic.TypeKeys[slot], label, slot == Logic.Other ? Logic.DefaultDays : Main.RowDefault(Logic.TypeKeys[slot]),
                Logic.MinDays, Logic.MaxDays, 0.5, 1, true, help, "Off", "3 days");

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Stop()
        {
            PreferencesModsTab.Remove();
            _section = null;
        }
    }
}
