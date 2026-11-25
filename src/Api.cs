using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MowProducerPriority
{
    // MoW Producer Priority's public feed, in the ashenmare feed shape (release/FEEDS.md, shipped in the zip): other mods
    // (Railroad Books, Industry Inspector) read it by reflection, so neither depends on the other.
    //  - Version (const int): the schema version. Within a version fields are only added; a rename, retype or removal
    //    raises it.
    //  - Feed (const string): the stable feed id, "mow-producer-priority".
    //  - Read(): { "api": 1, "feed": "mow-producer-priority", "active": bool, "reserveDays": float,
    //    "piles": [ { "industryId", "loadId", "sellerId", "stock", "cap", "days", "active" } ] }. Plain BCL data only, a new
    //    dictionary on every call, floats always finite. Never throws: on a failure it answers
    //    { "api": 1, "feed": "mow-producer-priority", "error": "..." }, logged once. Read only.
    //
    // The house class-load rule: Read names only BCL types, PathClean (BCL only) and this class. Plants and Main name the
    // game's and the mod loader's types; on Mono, compiling a method that names them loads those types, and a failure
    // there throws at the CALLER of that method, before any try inside it runs. So Read reaches them only through
    // ReadFeed and LogOnce, NoInlining methods called inside Read's own try (tools/nogame calls Read with the game's
    // assemblies refused, and checks the NoInlining flags).
    public static class Api
    {
        public const int Version = 1;
        public const string Feed = "mow-producer-priority";

        // One log line per load; reset by Main.OnModLoad (statics survive a mod-set change).
        private static bool _logged;

        internal static void Reset() => _logged = false;

        public static object Read()
        {
            try { return ReadFeed(); }
            catch (Exception e)
            {
                string why;
                try { why = e.GetType().Name + ": " + PathClean.CleanText(e.Message); }
                catch (Exception) { why = "the feed failed"; }
                if (!_logged)
                {
                    _logged = true;
                    try { LogOnce(why); } catch (Exception) { }
                }
                try { return new Dictionary<string, object> { { "api", Version }, { "feed", Feed }, { "error", why } }; }
                catch (Exception) { return null; }
            }
        }

        // active: this game keeps MoW reserves now (the mod running, the host or single player, at least one pile kept).
        // reserveDays: the largest of this game's per-type reserve sizes (the host's count); each pile's own is days. piles: every pile the mod
        // found on this railroad (none between worlds), each from Plants.FeedPiles.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static object ReadFeed() => new Dictionary<string, object>
        {
            { "api", Version },
            { "feed", Feed },
            { "active", Plants.FeedActive() },
            { "reserveDays", Plants.FeedReserveDays() },
            { "piles", Plants.FeedPiles() },
        };

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void LogOnce(string why) => Main.Log?.Warning("MoW Producer Priority: the feed couldn't answer: " + why);
    }
}
