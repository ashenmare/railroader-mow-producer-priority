using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Game.State;
using Model;
using Model.Definition.Data;
using Model.Ops;
using Model.Ops.Definition;

namespace MowProducerPriority
{
    // The game members reached by reflection or patched, resolved once at mod load. Main patches nothing unless every one
    // is found (the house rule: the game stays vanilla, with one log line). The lookups are per game build, so they are
    // kept across sessions. Each note below says what the member is in the game.
    internal static class Reflect
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static MethodInfo ContextAdd;       // IndustryContext.AddToStorage(Load, float, float)
        internal static MethodInfo ContextQuantity;  // IndustryContext.QuantityInStorage(Load)
        internal static FieldInfo ContextComponent;  // IndustryContext._industryComponent
        internal static FieldInfo DrawerCounts;      // Industry._drawerCounts
        internal static MethodInfo DoService;        // Industry.DoService
        internal static MethodInfo OrderCars;        // IndustryLoaderBase.OrderCars(IIndustryContext)
        internal static MethodInfo Predict;          // IndustryLoaderBase.PredictQuantityProducedInNextDay(float)
        internal static MethodInfo StoragePrefix;    // IndustryComponent.StoragePrefix (its getter)
        internal static MethodInfo SellerPanel;      // IndustrySeller.PanelFields(IndustryContext)

        // Null when every member is there, else the missing ones, comma separated.
        internal static string Resolve()
        {
            var missing = new List<string>();
            Type ctx = typeof(IndustryContext);
            // IndustryContext (a public readonly struct) implements IIndustryContext, and
            // AddToStorage(Load load, float quantity, float maxQuantity) is an ordinary method of its own (not an
            // explicit interface implementation), arguments in that order. The hooks bind them by position (__0, __1).
            // IIndustryContext is the public interface in Model.Ops that OrderCars and Service take.
            if (!ctx.IsValueType || !typeof(IIndustryContext).IsAssignableFrom(ctx)) missing.Add("IndustryContext");
            else
            {
                ContextAdd = Declared(ctx, "AddToStorage", typeof(void), typeof(Load), typeof(float), typeof(float));
                if (ContextAdd == null) missing.Add("IndustryContext.AddToStorage(Load, float, float)");
                // IndustryContext.QuantityInStorage(Load) is an ordinary method returning float that
                // reads the context's own component's key (for a loader: the shipping pile).
                ContextQuantity = Declared(ctx, "QuantityInStorage", typeof(float), typeof(Load));
                if (ContextQuantity == null) missing.Add("IndustryContext.QuantityInStorage(Load)");
                // IndustryContext._industryComponent (private) is the component the context was made for.
                ContextComponent = ctx.GetField("_industryComponent", Inst);
                if (ContextComponent == null || !typeof(IndustryComponent).IsAssignableFrom(ContextComponent.FieldType))
                    missing.Add("IndustryContext._industryComponent");
            }
            // Industry._drawerCounts (private) is a dictionary of drawer counts per storage key,
            // filled lazily by AvailableToDraw and never cleared by the game, so clearing it makes the next
            // AvailableToDraw count again for each key.
            DrawerCounts = typeof(Industry).GetField("_drawerCounts", Inst);
            if (DrawerCounts == null || !typeof(IDictionary).IsAssignableFrom(DrawerCounts.FieldType)) missing.Add("Industry._drawerCounts");
            // Industry.DoService (private) is the 15 game s service walk, scheduled only on
            // the host, and runs every component's Service after it starts. Picked over BeginServiceTick,
            // which is small enough for Mono to inline into DoService, where a patch would never run.
            DoService = Only(typeof(Industry), "DoService");
            if (DoService == null) missing.Add("Industry.DoService");
            // OrderCars(IIndustryContext) is declared on IndustryLoaderBase and
            // IndustryLoader doesn't override it, so a prefix on the base catches every paired loader.
            OrderCars = Declared(typeof(IndustryLoaderBase), "OrderCars", typeof(void), typeof(IIndustryContext));
            if (OrderCars == null || Declared(typeof(IndustryLoader), "OrderCars", null, typeof(IIndustryContext)) != null)
                missing.Add("IndustryLoaderBase.OrderCars(IIndustryContext)");
            // PredictQuantityProducedInNextDay(float multiplier) is a protected instance method of
            // IndustryLoaderBase returning float: productionRate x multiplier when
            // productionRate > 0.001, else the FIRST formula's output term for the load x multiplier, else 0 with a "Can't
            // predict" log line. Plants.Find calls it once per pile and skips a pile it gives 0.
            Predict = Declared(typeof(IndustryLoaderBase), "PredictQuantityProducedInNextDay", typeof(float), typeof(float));
            if (Predict == null) missing.Add("IndustryLoaderBase.PredictQuantityProducedInNextDay(float)");
            // IndustryComponent.StoragePrefix (internal) is a string property
            // worked out from sharedStorage each time it is read (not cached): null (the shared key) when true,
            // subIdentifier when false. Plants checks this at run time for each seller (SplitOne) and leaves a pile shared
            // if it doesn't hold.
            PropertyInfo sp = typeof(IndustryComponent).GetProperty("StoragePrefix", Inst);
            StoragePrefix = sp != null && sp.PropertyType == typeof(string) ? sp.GetGetMethod(true) : null;
            if (StoragePrefix == null) missing.Add("IndustryComponent.StoragePrefix");
            // IndustrySeller.PanelFields(IndustryContext) is the seller's override,
            // an iterator returning IEnumerable<IndustryComponent.PanelField>; the industry panel calls it virtually for
            // every unlocked component on every machine.
            SellerPanel = Declared(typeof(IndustrySeller), "PanelFields", typeof(IEnumerable<IndustryComponent.PanelField>), ctx);
            if (SellerPanel == null) missing.Add("IndustrySeller.PanelFields(IndustryContext)");
            return missing.Count == 0 ? null : string.Join(", ", missing.ToArray());
        }

        // The method of that name and parameter types declared on that type itself (any access); ret null = any return type.
        private static MethodInfo Declared(Type t, string name, Type ret, params Type[] args)
        {
            MethodInfo m = t.GetMethod(name, Inst | BindingFlags.DeclaredOnly, null, args, null);
            return m != null && (ret == null || m.ReturnType == ret) ? m : null;
        }

        // The one method of that name declared on that type, whatever its parameters; null if none or several.
        private static MethodInfo Only(Type t, string name)
        {
            MethodInfo found = null;
            foreach (MethodInfo m in t.GetMethods(Inst | BindingFlags.DeclaredOnly))
            {
                if (m.Name != name) continue;
                if (found != null) return null;
                found = m;
            }
            return found;
        }
    }

    // One pile the mod splits: an industry's loader and its MoW seller on the same load.
    internal sealed class Pair
    {
        internal Industry Industry;
        internal IndustryLoader Loader;
        internal IndustrySeller Seller;
        internal Load Load;
        internal string IndustryId, LoadId, SellerId, Name;
        internal string ShippingPrefix;   // the loader's StoragePrefix: the shared key, the shipping pile
        internal string ReservePrefix;    // the seller's StoragePrefix with sharedStorage false: the reserve
        internal bool Split;              // seller.sharedStorage set false by this mod (and only then set back)
        internal bool Draining;           // host, Reserve 0: kept split only until what is left in the reserve fits
        internal bool Broken;             // the self-check failed: left shared for the rest of this world
        // Log each decision: what moved since the last line, at most one line a minute per pile.
        internal double FromProduction, FromShipping;
        internal DateTime LoggedAt;
    }

    internal sealed class Same<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly Same<T> Instance = new Same<T>();
        public bool Equals(T a, T b) => ReferenceEquals(a, b);
        public int GetHashCode(T o) => RuntimeHelpers.GetHashCode(o);
    }

    // The reserves of one world, and the work the hooks (Hooks.cs) hand to them. Main thread only: the game's ops run
    // there (Industry service, ordering, the industry panel, the Preferences save); the order scope is ThreadStatic all
    // the same.
    //
    // 1. Split (WorldLoad, every machine): seller.sharedStorage = false, so the seller reads and draws only its own key
    //    (the reserve); then the industry's drawer counts are cleared, so the loader is the only drawer on the shipping
    //    key. On the host with Reserve above 0 the reserve is then kept (2 to 4). On a client the split is for its industry
    //    panel only (5): a client's game never services industries, so it moves no stock and orders nothing.
    // 2. Fill first (FillFirst, the prefix on IndustryContext.AddToStorage, host): what a producer (the formula making the
    //    load, or the loader itself at the quarry) adds goes to the reserve first, up to its cap; the rest goes on as
    //    vanilla.
    // 3. Top up (Tick, the prefix on Industry.DoService, host): before any component runs, the reserve takes what it lacks
    //    from the shipping pile, so MoW comes before the loader's draw, and a full shipping pile gets room again (the
    //    recipe keeps running) whenever MoW has bought from the reserve.
    // 4. Ordering (EnterOrder / OrderStock / LeaveOrder, around IndustryLoaderBase.OrderCars, host): vanilla's own
    //    OrderCars runs, but its read of the shipping stock comes back less the reserve's deficit (Logic.OrderStock), so its
    //    line is need = stock + perDay x days - room - deficit (Logic.OrderNeed). Everything else in it stays vanilla.
    // 5. The panel (Panel, the postfix on IndustrySeller.PanelFields, every machine): a split seller's panel shows its MoW
    //    reserve and the shipping pile, in place of vanilla's one line.
    // Merging back (Reserve 0 on the host, standing down, the mod unloaded with a world loaded): only what fits under the
    // loader's maxStorage moves to the shipping pile (Logic.MergeFits), so nothing is ever clamped away. With Reserve 0 a
    // pile whose reserve doesn't all fit stays split (Draining): MoW keeps buying from it, the plant works on as usual,
    // and once the whole rest fits it goes back in one move and the pile is shared again. Standing down and unloading can't wait (the hooks stop), so they
    // merge what fits, share the pile again at once, and leave the rest in the seller's key: it stays in the save, unused
    // until a save loads with the mod running.
    // A world unload writes nothing: the game is leaving the world (a return to menu or a mod-set change), and a return to
    // menu writes no save. Industry and its components are plain C# classes, not Unity
    // objects, so they are still there when OnWorldUnload runs; the mod only forgets them.
    internal static class Plants
    {
        private const int MaxFailures = 5;
        private static readonly TimeSpan LogEvery = TimeSpan.FromMinutes(1);

        private static readonly List<Pair> All = new List<Pair>();
        private static readonly Dictionary<Industry, Pair[]> ByIndustry = new Dictionary<Industry, Pair[]>(Same<Industry>.Instance);
        private static readonly Dictionary<IndustryComponent, Pair[]> ByProducer = new Dictionary<IndustryComponent, Pair[]>(Same<IndustryComponent>.Instance);
        private static readonly Dictionary<IndustryComponent, Pair> ByLoader = new Dictionary<IndustryComponent, Pair>(Same<IndustryComponent>.Instance);
        private static readonly Dictionary<IndustryComponent, Pair> BySeller = new Dictionary<IndustryComponent, Pair>(Same<IndustryComponent>.Instance);
        private static readonly HashSet<string> LoadIds = new HashSet<string>(StringComparer.Ordinal);

        private static bool _host;       // this game is the host (or single player): the only place stock is moved
        private static bool _anySplit;   // at least one seller is on its own pile right now
        private static int _failures;

        [ThreadStatic] private static Pair _orderPair;
        [ThreadStatic] private static float _orderDeficit, _orderReadAs, _orderStock;
        [ThreadStatic] private static int _orderReads;

        // Forget everything, write nothing (mod load, and after a world is gone).
        internal static void Reset()
        {
            All.Clear(); ByIndustry.Clear(); ByProducer.Clear(); ByLoader.Clear(); BySeller.Clear(); LoadIds.Clear();
            _host = false; _anySplit = false; _failures = 0;
            _orderPair = null; _orderReads = 0;
        }

        // ---- world load and unload, settings, standing down ----

        internal static void WorldLoad()
        {
            Reset();
            // OpsController.Shared and its AllIndustries are set by OnWorldLoad (Quick Turn's Turner reads them
            // there); null in the map editor.
            OpsController ops = OpsController.Shared;
            if (ops == null || ops.AllIndustries == null) return;
            // StateManager.IsHost is static, true in single player and on the host, and known by OnWorldLoad.
            _host = StateManager.IsHost;
            // a service due at the loaded time runs inside the restore, before any mod's OnWorldLoad:
            // that tick is vanilla, and the drawer counts it caches are why the split
            // clears them.
            // One industry's surprise costs only that industry.
            foreach (Industry industry in ops.AllIndustries)
            {
                try { Find(industry); }
                catch (Exception e)
                {
                    Main.WarnOnce("find|" + e.GetType().FullName, "MoW Producer Priority: skipped an industry it couldn't read (logged once): "
                                  + PathClean.CleanText(e.ToString(), 4000));
                }
            }
            if (All.Count == 0)
            {
                if (DebugLog.On) DebugLog.Event(Main.Id, "no-plants");
                return;
            }
            string names = Names();
            if (!_host)
            {
                // a client's game never services industries or orders cars, so on a
                // client the split only changes what its industry panel reads: the seller's own key, which the host
                // writes and the game replicates.
                int shown = SplitAll(false);
                if (DebugLog.On) DebugLog.Event(Main.Id, "client", "panelsApart", shown, "plants", All.Count, "names", names);
                return;
            }
            // 0.2.0: each pile follows its own type's size. A type at 0 is shared as in the base game (what its save-held
            // reserve can put back does); the rest are kept.
            MergeLeftovers(out double back, out double kept);
            int split = SplitAll(true);
            int off = 0;
            foreach (Pair p in All) if (!On(p)) off++;
            if (DebugLog.On) DebugLog.Event(Main.Id, "world", "kept", split, "plants", All.Count, "days", Main.Settings.Describe(), "names", names,
                                            "offAtZero", off, "backToShipping", (float)back, "apartUnits", (float)kept);
        }

        // Return to menu, loading another save, or a mod-set change: the world is going, and the game writes no save from
        // here, so nothing is written. The sellers go back to the shared pile (as the map makes them on the next load) and
        // the mod forgets this world.
        internal static void WorldUnload()
        {
            try
            {
                foreach (Pair p in All)
                {
                    if (!p.Split) continue;
                    p.Split = p.Draining = false;
                    if (p.Seller != null) p.Seller.sharedStorage = true;
                }
            }
            finally { Reset(); }
        }

        // The mod unloaded with a world still loaded (Main.OnModUnload): the hooks are already off and about to come out,
        // so the piles can't wait. Merge what fits, share every pile again, and say what stays in the save.
        internal static void Stop()
        {
            if (All.Count == 0) return;
            StopAll(out double back, out double kept);
            if (_host && (back > 0 || kept > 0))
                Main.Log?.Info("MoW Producer Priority is off: the piles are shared again; " + Logic.Units((float)back)
                               + " went back from the MoW reserves to the shipping piles" + Kept(kept));
            Reset();
        }

        // After a save in Preferences > Mods (host): Reserve 0 merges the reserves back (what doesn't fit drains); from 0
        // to more keeps them again; any other change only moves the cap (read live). A client's Reserve changes nothing.
        internal static void Apply()
        {
            if (All.Count == 0) return;
            if (!_host)
            {
                if (DebugLog.On) DebugLog.Event(Main.Id, "client-setting");
                return;
            }
            // Each pile follows its own type's size now: one at 0 merges back what fits (what doesn't drains), one raised from
            // 0 is kept again, any other change only moves the cap (read live).
            double back = 0, kept = 0;
            int n = 0, again = 0;
            var touched = new HashSet<Industry>(Same<Industry>.Instance);
            foreach (Pair p in All)
            {
                if (On(p))
                {
                    if (p.Split && p.Draining) { p.Draining = false; again++; }
                    continue;
                }
                if (!p.Split || p.Draining) continue;
                n++;
                float left = 0f;
                bool failed = false;
                try { back += MergeFit(p, out left); }
                catch (Exception e)
                {
                    failed = true;
                    Main.WarnOnce("merge|" + p.SellerId, "MoW Producer Priority: couldn't put " + p.Name + "'s MoW reserve back on its shipping pile, so it stays apart until it is sold or fits: "
                                  + PathClean.CleanText(e.ToString(), 4000));
                }
                if (failed || left > 0f) { p.Draining = true; kept += left; }
                else { Unsplit(p); touched.Add(p.Industry); }
            }
            ClearDrawers(touched);
            if (Switch.Live) again += SplitAll(true);
            _anySplit = AnySplit();
            if (n > 0)
                if (DebugLog.On) DebugLog.Event(Main.Id, "to-zero", "piles", n, "backToShipping", (float)back, "apartUnits", kept);
            if (again > 0)
                if (DebugLog.On) DebugLog.Event(Main.Id, "kept-again", "piles", again, "plants", All.Count, "days", Main.Settings.Describe());
            else if (n == 0)
                if (DebugLog.On) DebugLog.Event(Main.Id, "days", "days", Main.Settings.Describe());
        }

        internal static void Failed(string where, Exception e)
        {
            try
            {
                _failures++;
                Main.WarnOnce("fail|" + where + "|" + e.GetType().FullName,
                    "MoW Producer Priority: " + where + " failed (logged once): " + PathClean.CleanText(e.ToString(), 4000));
                if (_failures >= MaxFailures && Switch.Live)
                {
                    Switch.Live = false;
                    double back = 0, kept = 0;
                    try { StopAll(out back, out kept); } catch (Exception) { }
                    Main.Log?.Warning("MoW Producer Priority stands down for this session after " + _failures + " failures: the piles are shared again ("
                                      + Logic.Units((float)back) + " went back from the MoW reserves to the shipping piles" + Kept(kept)
                                      + "). Loading a save tries again.");
                }
            }
            catch (Exception) { Switch.Live = false; }
        }

        // What Reserve 0 leaves apart for now, for the log line.
        private static string Kept(double kept) => kept > 0
            ? "; " + Logic.Units((float)kept) + " didn't fit there and stays in the MoW reserves in the save, unused until a save loads with the mod running"
            : "";

        // ---- finding the pairs ----

        private static void Find(Industry industry)
        {
            // Industry.Components lists every component of the industry (Quick Turn and Picky Client walk it);
            // Industry.identifier is the map id; Industry.name is the name the log shows. Industry and IndustryComponent are
            // plain C# classes (not Unity objects).
            if (industry == null || industry.Components == null) return;
            var loaders = new List<IndustryLoader>();
            var sellers = new List<IndustrySeller>();
            var formulas = new List<FormulaicIndustryComponent>();
            foreach (IndustryComponent c in industry.Components)
            {
                if (c == null) continue;
                // exact type: no game class derives from IndustryLoader, so TeleportLoadingIndustry
                // and anything modded stay out.
                // the same for IndustrySeller.
                Type t = c.GetType();
                if (t == typeof(IndustryLoader)) loaders.Add((IndustryLoader)c);
                else if (t == typeof(IndustrySeller)) sellers.Add((IndustrySeller)c);
                else if (c is FormulaicIndustryComponent f) formulas.Add(f);
            }
            if (loaders.Count == 0 || sellers.Count == 0) return;
            var pairs = new List<Pair>();
            foreach (IndustrySeller seller in sellers)
            {
                // IndustrySeller.loadId (public string) names the load it sells.
                string id = seller.loadId;
                if (string.IsNullOrEmpty(id)) continue;
                IndustryLoader loader = null;
                int loadersOnIt = 0, sellersOnIt = 0;
                // , C21: IndustryLoader.load (Load) and Load.id (string).
                foreach (IndustryLoader l in loaders) if (l.load != null && l.load.id == id) { loader = l; loadersOnIt++; }
                foreach (IndustrySeller s in sellers) if (s.loadId == id) sellersOnIt++;
                if (loadersOnIt == 0) continue;
                string where = industry.name + " (" + id + ")";
                if (loadersOnIt > 1 || sellersOnIt > 1)
                {
                    if (DebugLog.On) Main.SkipOnce("skip|two|" + seller.Identifier, "several-loaders", where);
                    continue;
                }
                // sharedStorage is a public bool field, true by default and on these components in the stock
                // map. A pile the map has already split is not one this mod understands.
                if (!loader.sharedStorage || !seller.sharedStorage)
                {
                    if (DebugLog.On) Main.SkipOnce("skip|shape|" + seller.Identifier, "own-pile", where);
                    continue;
                }
                var producers = new List<IndustryComponent> { loader };
                bool makes = loader.productionRate > 0.001f;   // IndustryLoader.productionRate, per day at tier 1
                foreach (FormulaicIndustryComponent f in formulas)
                {
                    if (!f.sharedStorage || f.outputTerms == null) continue;
                    // FormulaicIndustryComponent.outputTerms, each with load and unitsPerDay (Quick Turn reads
                    // them); a formula outputs into its own key, the shared one when its sharedStorage is true.
                    foreach (var term in f.outputTerms)
                    {
                        object boxed = term;   // a class or a struct: either way an empty slot is skipped
                        if (boxed == null || term.load == null || term.load.id != id || !(term.unitsPerDay > 0f)) continue;
                        producers.Add(f);
                        makes = true;
                        break;
                    }
                }
                if (!makes)
                {
                    if (DebugLog.On) Main.SkipOnce("skip|none|" + seller.Identifier, "makes-nothing", where);
                    continue;
                }
                // The game's own prediction reads only the industry's FIRST formula: a pile it
                // can't predict would get a cap of 0 forever and the game's "Can't predict" line at every use. One call
                // here, and such a pile is left as it is.
                float perDay = PredictAt(loader, 1f);
                if (!(perDay > 0f) || float.IsInfinity(perDay))
                {
                    if (DebugLog.On) Main.SkipOnce("skip|predict|" + seller.Identifier, "cant-predict", where);
                    continue;
                }
                string reserve = PrefixWhenSplit(seller, out string shipping);
                if (reserve == null)
                {
                    Main.WarnOnce("skip|prefix|" + seller.Identifier, "MoW Producer Priority: " + where
                                  + "'s seller doesn't take a pile of its own on this game version, so it's left shared");
                    continue;
                }
                var p = new Pair
                {
                    Industry = industry, Loader = loader, Seller = seller, Load = loader.load,
                    IndustryId = industry.identifier, LoadId = id, SellerId = seller.Identifier, Name = where,
                    ShippingPrefix = Prefix(loader), ReservePrefix = reserve, LoggedAt = DateTime.UtcNow,
                };
                if (!string.Equals(p.ShippingPrefix, shipping, StringComparison.Ordinal))
                {
                    Main.WarnOnce("skip|keys|" + seller.Identifier, "MoW Producer Priority: " + where
                                  + "'s loader and seller don't share one pile on this game version, so it's left as it is");
                    continue;
                }
                pairs.Add(p);
                All.Add(p);
                ByLoader[loader] = p;
                BySeller[seller] = p;
                LoadIds.Add(id);
                foreach (IndustryComponent c in producers)
                {
                    ByProducer.TryGetValue(c, out Pair[] had);
                    ByProducer[c] = Append(had, p);
                }
            }
            if (pairs.Count > 0) ByIndustry[industry] = pairs.ToArray();
        }

        private static Pair[] Append(Pair[] had, Pair p)
        {
            if (had == null) return new[] { p };
            var a = new Pair[had.Length + 1];
            Array.Copy(had, a, had.Length);
            a[had.Length] = p;
            return a;
        }

        private static string Names()
        {
            var n = new string[All.Count];
            for (int i = 0; i < All.Count; i++) n[i] = All[i].Name;
            return string.Join(", ", n);
        }

        private static string Prefix(IndustryComponent c) => (string)Reflect.StoragePrefix.Invoke(c, null);

        private static float PredictAt(IndustryLoader loader, float multiplier) =>
            (float)Reflect.Predict.Invoke(loader, new object[] { multiplier });

        // The seller's prefix with sharedStorage false, read by setting it false for the read only; null if it doesn't
        // differ from the shared one (StoragePrefix cached, or not following sharedStorage: ). shipping is the
        // seller's own prefix while shared, which must be the loader's.
        private static string PrefixWhenSplit(IndustrySeller seller, out string shipping)
        {
            shipping = Prefix(seller);
            bool was = seller.sharedStorage;
            string own;
            try
            {
                seller.sharedStorage = false;
                own = Prefix(seller);
            }
            finally { seller.sharedStorage = was; }
            return string.IsNullOrEmpty(own) || string.Equals(own, shipping, StringComparison.Ordinal) ? null : own;
        }

        // ---- the split and the merge ----

        // Every pair not split yet onto its own pile (a broken one stays shared). How many were split.
        // what the pile scenery (BulkIndustryStorageSynchronizer) draws once the seller has its own key.
        // what IndustrySeller.CompanionLoader is used for, and that the split doesn't
        // change it.
        // byReserve (the host): a pile whose type is at 0 stays shared. A client splits every pile (display only).
        private static int SplitAll(bool byReserve)
        {
            int n = 0;
            var touched = new HashSet<Industry>(Same<Industry>.Instance);
            foreach (Pair p in All)
            {
                if (p.Split || p.Broken || (byReserve && !On(p))) continue;
                if (!SplitOne(p)) continue;
                touched.Add(p.Industry);
                n++;
            }
            ClearDrawers(touched);
            _anySplit = AnySplit();
            return n;
        }

        private static bool SplitOne(Pair p)
        {
            if (!Alive(p)) return false;
            bool moved = false;
            try
            {
                p.Seller.sharedStorage = false;
                moved = string.Equals(Prefix(p.Seller), p.ReservePrefix, StringComparison.Ordinal);
                if (!moved)
                    Main.WarnOnce("broken|" + p.SellerId, "MoW Producer Priority: " + p.Name + "'s seller didn't move to a pile of its own, so it's left shared");
            }
            catch (Exception e)
            {
                Main.WarnOnce("split|" + p.SellerId, "MoW Producer Priority: couldn't give " + p.Name + "'s seller a pile of its own, so it's left shared: "
                              + PathClean.CleanText(e.ToString(), 4000));
            }
            finally
            {
                // A seller is only ever off the shared pile while this mod knows it split it.
                if (!moved) { p.Seller.sharedStorage = true; p.Broken = true; }
            }
            if (!moved) return false;
            p.Split = true;
            p.Draining = false;
            return true;
        }

        private static void Unsplit(Pair p)
        {
            p.Split = p.Draining = false;
            p.Seller.sharedStorage = true;
        }

        // Standing down, or the mod unloaded with a world loaded: every split pile shared again at once. On the host what
        // fits goes back to the shipping pile first; what doesn't stays in the seller's key (in the save, unused until a
        // save loads with the mod running). A client only puts its sellers back.
        private static void StopAll(out double back, out double kept)
        {
            back = kept = 0;
            var touched = new HashSet<Industry>(Same<Industry>.Instance);
            foreach (Pair p in All)
            {
                if (!p.Split) continue;
                if (!Alive(p)) { p.Split = p.Draining = false; continue; }
                try
                {
                    if (_host)
                    {
                        back += MergeFit(p, out float left);
                        kept += left;
                    }
                }
                catch (Exception e)
                {
                    Main.WarnOnce("merge|" + p.SellerId, "MoW Producer Priority: couldn't put " + p.Name + "'s MoW reserve back on its shipping pile; it stays in the save, unused: "
                                  + PathClean.CleanText(e.ToString(), 4000));
                }
                finally { Unsplit(p); }
                touched.Add(p.Industry);
            }
            _anySplit = false;
            ClearDrawers(touched);
        }

        // A type at 0 at load (host): a save made with a reserve gets what fits back on the shipping pile; a pile whose
        // reserve doesn't all fit is split, draining (MoW keeps buying from it), until the rest fits.
        private static void MergeLeftovers(out double back, out double kept)
        {
            back = kept = 0;
            var touched = new HashSet<Industry>(Same<Industry>.Instance);
            foreach (Pair p in All)
            {
                if (!Alive(p) || p.Broken || On(p)) continue;
                float left;
                try { back += MergeFit(p, out left); }
                catch (Exception e)
                {
                    Main.WarnOnce("merge|" + p.SellerId, "MoW Producer Priority: couldn't put " + p.Name + "'s MoW reserve from the save back on its shipping pile; it stays in the save, unused: "
                                  + PathClean.CleanText(e.ToString(), 4000));
                    continue;
                }
                if (left <= 0f || !SplitOne(p)) continue;
                p.Draining = true;
                kept += left;
                touched.Add(p.Industry);
            }
            ClearDrawers(touched);
            _anySplit = AnySplit();
        }

        // What of the reserve fits under the loader's maxStorage goes onto the shipping pile, then off the reserve (in that
        // order: a failure between the two can add, never lose). How much moved; left is what is still in the reserve.
        private static float MergeFit(Pair p, out float left)
        {
            // Industry.Storage (IndustryStorageHelper) with public QuantityInStorage(Load,
            // string prefix), AddToStorage(Load, float quantity, float max, string prefix) and RemoveFromStorage(Load, float
            // quantity, string prefix), writing the replicated, saved storage dictionary.
            // AddToStorage clamps the whole pile to the max it is given, so only what fits may move.
            var st = p.Industry.Storage;
            float q = st.QuantityInStorage(p.Load, p.ReservePrefix);
            left = q > 0f ? q : 0f;
            float m = Logic.MergeFits(q, st.QuantityInStorage(p.Load, p.ShippingPrefix), p.Loader.maxStorage);
            if (m <= 0f) return 0f;
            // m is within the room already: float.MaxValue so a rounding clamp can't drop a crumb here.
            st.AddToStorage(p.Load, m, float.MaxValue, p.ShippingPrefix);
            st.RemoveFromStorage(p.Load, m, p.ReservePrefix);
            left = m >= q ? 0f : q - m;
            return m;
        }

        // clearing the cache is enough for the next AvailableToDraw to count each key's drawers again.
        private static void ClearDrawers(HashSet<Industry> industries)
        {
            foreach (Industry i in industries)
            {
                try { if (Reflect.DrawerCounts.GetValue(i) is IDictionary d) d.Clear(); }
                catch (Exception e) { Main.WarnOnce("drawers", "MoW Producer Priority: couldn't reset a plant's drawer counts (logged once): " + PathClean.CleanText(e.Message)); }
            }
        }

        private static bool AnySplit()
        {
            foreach (Pair p in All) if (p.Split) return true;
            return false;
        }

        // Industry and IndustryComponent are plain C# classes (, C27), so this is only a null check: they are
        // never destroyed under the mod while it holds them.
        private static bool Alive(Pair p) => p.Industry != null && p.Seller != null && p.Loader != null;

        // ---- the cap ----

        // Reserve days x what the plant makes in a day at its current tier, read live: a contract change, Quick Turn's
        // fields and a Preferences save all show at the next use. , C18.
        private static float Cap(Pair p)
        {
            float tier = p.Industry.GetContractMultiplier();   // 1 without contracts, 0 with no contract, else the tier's percent
            return Logic.ReserveCap(DaysOf(p), PredictAt(p.Loader, tier));
        }

        // The pair is keeping its reserve now: the host, split, not draining, the MoW seller unlocked by progression.
        // .
        private static bool Keeping(Pair p) => _host && p.Split && !p.Draining && On(p) && p.Seller.IsProgressionEnabled;

        // This pile's reserve size in days: its load type's setting (Logic.TypeOf), read live.
        private static float DaysOf(Pair p) => Main.Settings.DaysFor(p.LoadId);

        private static bool On(Pair p) => Logic.IsOn(DaysOf(p));

        // ---- the hooks' work ----

        // 3. At the start of an industry's service tick (the prefix on Industry.DoService, host): a kept reserve tops up
        // from the shipping pile; a draining one goes back whole once all of it fits, and its pile is shared again.
        internal static void Tick(Industry industry)
        {
            if (!_host || !_anySplit || industry == null || !ByIndustry.TryGetValue(industry, out Pair[] pairs)) return;
            foreach (Pair p in pairs)
            {
                if (!p.Split) continue;
                if (p.Draining) { Drain(p); continue; }
                if (!Keeping(p)) continue;
                float cap = Cap(p);
                if (cap <= 0f) continue;
                var st = industry.Storage;
                float reserve = st.QuantityInStorage(p.Load, p.ReservePrefix);
                float shipping = st.QuantityInStorage(p.Load, p.ShippingPrefix);
                float m = Logic.TopUp(shipping, reserve, cap);
                if (m <= 0f) continue;
                // Onto the reserve, then off the shipping pile (a failure between can add, never lose). m is already within
                // the room; float.MaxValue so a rounding clamp can't drop a crumb. , C26.
                st.AddToStorage(p.Load, m, float.MaxValue, p.ReservePrefix);
                st.RemoveFromStorage(p.Load, m, p.ShippingPrefix);
                p.FromShipping += m;
                Note(p, reserve + m, cap);
            }
        }

        // Draining (Reserve 0, a reserve that didn't all fit when it was turned off): nothing moves until the whole rest fits
        // under the loader's maxStorage (MoW buying it down, or the loader shipping room). Merging a part each tick would
        // fill every bit of room the loader ships before the plant could use it: Scott's recipe would see no room
        // and stop, the quarry's production add would be clamped away.
        private static void Drain(Pair p)
        {
            var st = p.Industry.Storage;
            float q = st.QuantityInStorage(p.Load, p.ReservePrefix);
            if (q > 0f && Logic.MergeFits(q, st.QuantityInStorage(p.Load, p.ShippingPrefix), p.Loader.maxStorage) < q) return;
            MergeFit(p, out float left);
            if (left > 0f) return;
            Unsplit(p);
            ClearDrawers(new HashSet<Industry>(Same<Industry>.Instance) { p.Industry });
            _anySplit = AnySplit();
            if (DebugLog.On) DebugLog.Event(Main.Id, "shared-again", "plant", p.Name);
        }

        // 2. Fill first (the prefix on IndustryContext.AddToStorage, host): quantity is what the context's component is
        // about to add to its own key; the part the reserve takes is added there instead and taken off quantity.
        internal static void FillFirst(IndustryContext context, Load load, ref float quantity)
        {
            if (!_host || !_anySplit || load == null || !(quantity > 0f) || !LoadIds.Contains(load.id)) return;
            // , C14: the producer's own context: the formula's output, or the quarry loader's production line.
            if (!(Reflect.ContextComponent.GetValue(context) is IndustryComponent c) || !ByProducer.TryGetValue(c, out Pair[] pairs)) return;
            Pair p = null;
            foreach (Pair q in pairs) if (q.LoadId == load.id) { p = q; break; }
            if (p == null || !Keeping(p)) return;
            float cap = Cap(p);
            var st = p.Industry.Storage;
            float reserve = st.QuantityInStorage(p.Load, p.ReservePrefix);
            float move = Logic.FillFirst(quantity, reserve, cap);
            if (move <= 0f) return;
            st.AddToStorage(p.Load, move, float.MaxValue, p.ReservePrefix);
            quantity -= move;
            p.FromProduction += move;
            Note(p, reserve + move, cap);
        }

        // 4. Ordering (host). The prefix on OrderCars works out the deficit for a paired loader called with the game's own
        // IndustryContext (the contract window's forecast calls OrderCars with its own CarCounterIndustryContext, which is
        // left alone); the postfix on IndustryContext.QuantityInStorage hands vanilla's stock read the shipping stock less
        // it, matching the context by its component (the struct is boxed afresh for every call, so never by reference);
        // the finalizer clears the scope.
        internal static void EnterOrder(IndustryLoaderBase loader, IIndustryContext context)
        {
            _orderPair = null; _orderReads = 0;
            if (!_host || !_anySplit || loader == null || !(context is IndustryContext) || !ByLoader.TryGetValue(loader, out Pair p) || !Keeping(p)) return;
            float deficit = Logic.Deficit(Cap(p), p.Industry.Storage.QuantityInStorage(p.Load, p.ReservePrefix));
            if (deficit <= 0f) return;
            _orderPair = p; _orderDeficit = deficit;
        }

        internal static void OrderStock(IndustryContext context, Load load, ref float stock)
        {
            Pair p = _orderPair;
            if (p == null || load == null || load.id != p.LoadId) return;
            if (!ReferenceEquals(Reflect.ContextComponent.GetValue(context), p.Loader)) return;
            _orderReads++;
            _orderStock = stock;
            stock = Logic.OrderStock(stock, _orderDeficit);
            _orderReadAs = stock;
        }

        internal static void LeaveOrder()
        {
            Pair p = _orderPair;
            int reads = _orderReads;
            _orderPair = null; _orderReads = 0;
            if (p == null || reads == 0) return;
            // vanilla reads the stock once per OrderCars. More than once means the game changed under us.
            if (reads > 1)
                Main.WarnOnce("reads|" + p.SellerId, "MoW Producer Priority: " + p.Name + "'s ordering read its pile " + reads
                              + " times in one go on this game version; each read was taken less the reserve's need (logged once)");
            if (DebugLog.On) DebugLog.Event(Main.Id, "order", "plant", p.Name, "readAs", _orderReadAs, "stock", _orderStock, "deficit", _orderDeficit);
        }

        // 5. The industry panel (the postfix on IndustrySeller.PanelFields, every machine). Vanilla's one line is the
        // seller's own key against the loader's maxStorage; a split seller shows its MoW reserve
        // (against the reserve's own cap where this game keeps it; elsewhere, as vanilla, against the shipping pile's
        // maxStorage: a client, which can't know the host's Reserve setting, tier 0, or draining) and the shipping pile
        // (against the loader's maxStorage), which the loader's own panel doesn't show while it orders empties
        //.
        // IndustryComponent.PanelField (a public struct with a public (label, text, tooltip) constructor) and its
        // public static InStorage(Load, float, float).
        internal static void Panel(IndustrySeller seller, ref IEnumerable<IndustryComponent.PanelField> fields)
        {
            if (seller == null || !_anySplit || !BySeller.TryGetValue(seller, out Pair p) || !p.Split || !Alive(p)) return;
            var st = p.Industry.Storage;
            float reserve = st.QuantityInStorage(p.Load, p.ReservePrefix);
            float shipping = st.QuantityInStorage(p.Load, p.ShippingPrefix);
            float max = p.Loader.maxStorage;
            float cap = Keeping(p) ? Cap(p) : 0f;
            fields = new List<IndustryComponent.PanelField>
            {
                new IndustryComponent.PanelField("MoW reserve", IndustryComponent.PanelField.InStorage(p.Load, reserve, cap > 0f ? cap : max).Text,
                    "This plant's pile for your MoW cars, the one they load from. While the host's Reserve is above 0, what the plant makes fills it first. Does not include loads in cars on the line."),
                new IndustryComponent.PanelField("Shipping pile", IndustryComponent.PanelField.InStorage(p.Load, shipping, max).Text,
                    "The pile the plant's outbound cars load from. Does not include loads in cars on the line."),
            };
        }

        // Debug log, each decision: at most one line a minute per pile, summing what moved since the last one.
        private static void Note(Pair p, float reserveNow, float cap)
        {
            if (!DebugLog.On) { p.FromProduction = p.FromShipping = 0; return; }
            DateTime now = DateTime.UtcNow;
            if (now - p.LoggedAt < LogEvery) return;
            if (DebugLog.On) DebugLog.Event(Main.Id, "reserve", "plant", p.Name, "fromProduction", (float)p.FromProduction, "fromShipping", (float)p.FromShipping,
                           "holds", reserveNow, "cap", cap);
            p.FromProduction = p.FromShipping = 0;
            p.LoggedAt = now;
        }

        // ---- the feed (Api.cs, release/FEEDS.md) ----

        // True when this game is keeping MoW reserves right now: running, the host (or single player), and at least one
        // pile kept (split, not draining, its seller unlocked).
        internal static bool FeedActive()
        {
            if (!Switch.Live || !_host) return false;
            foreach (Pair p in All) if (Keeping(p)) return true;
            return false;
        }

        // The feed's top-level reserveDays (0.2.0): the largest size among the piles found, else among all the settings, so a
        // reader that knows one number still gets one, and 0 only when everything is off.
        internal static float FeedReserveDays()
        {
            if (All.Count == 0) return Logic.Largest(Main.Settings.All());
            var d = new float[All.Count];
            for (int i = 0; i < d.Length; i++) d[i] = DaysOf(All[i]);
            return Logic.Largest(d);
        }

        // One plain dictionary per pile, in the order found. stock is the seller's own key (replicated, so right on every
        // machine); cap and active are this game's (a client keeps no reserve: cap 0, active false).
        internal static List<object> FeedPiles()
        {
            var piles = new List<object>(All.Count);
            foreach (Pair p in All)
            {
                if (!Alive(p)) continue;
                bool active = Switch.Live && Keeping(p);
                piles.Add(new Dictionary<string, object>
                {
                    { "industryId", p.IndustryId },
                    { "loadId", p.LoadId },
                    { "sellerId", p.SellerId },
                    { "stock", Logic.Finite(p.Industry.Storage.QuantityInStorage(p.Load, p.ReservePrefix)) },
                    { "cap", active ? Logic.Finite(Cap(p)) : 0f },
                    { "days", DaysOf(p) },
                    { "active", active },
                });
            }
            return piles;
        }
    }
}
