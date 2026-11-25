using System;
using System.Globalization;

namespace MowProducerPriority
{
    // Every number the mod decides, with no game type: the reserve's cap, how much of a quantity just made goes to the
    // reserve first, how much the reserve takes from the shipping pile at the start of a service tick, how far short the
    // reserve is, and the stock the loader's order reads. It uses no game types, so it
    // compiles and tests on its own.
    //
    // Words used here and in Plants.cs:
    //   shipping pile  the industry's shared storage key for the load (the loader draws it for outbound cars; vanilla's
    //                  one pile);
    //   reserve        the seller's own key once its sharedStorage is false, filled first, drawn only by the seller (the
    //                  player's MoW cars);
    //   cap            how much the reserve holds: Reserve days x what the plant makes in a day at its current tier.
    // Quantities are in the load's units (pounds for ties, ballast and concrete) and floats, as the game keeps them.
    internal static class Logic
    {
        internal const float DefaultDays = 1f, MinDays = 0f, MaxDays = 3f;

        // Room or stock under this is treated as none, so a pile that is full but for a rounding crumb isn't topped up (and
        // its storage dictionary rewritten and sent to every player) every tick. One unit: a tie plant at T1 makes about 33
        // pounds a tick, so nothing real is held back by it.
        internal const float MinMove = 1f;

        // 0.2.0: one reserve size per load type. A load a plant both ships and sells to MoW cars has a setting of its own
        // (the stock map's ties, ballast and concrete; 1.0.0 adds rails and bridge parts, which a map mod's
        // plant may sell); any other load (another map's plant) uses the "other" one, which is the old single Reserve's key, so an
        // old settings.json still means something. TypeOf is the slot of a load id.
        internal const int Ties = 0, Ballast = 1, Concrete = 2, Rails = 3, Bridge = 4, Other = 5, TypeCount = 6;
        // the stock map's pairs are sylva-ties "rr/ties" and dillsboro-quarry "rr/ballast" and "rr/concrete" (Map.json: each
        // an IndustryLoader and an IndustrySeller on the load; no other industry has a seller). Rails and the two bridge-parts
        // loads are stock load ids (AssetPacksCore/loads/Definitions.json) that no stock plant sells; a data mod's plant does.
        // Each slot lists the exact load ids it covers (never a prefix match). Ties also covers the Concrete Ties data mod's
        // "ashenmare/concrete-tie", so ties of any kind share the Ties size. Slot Other has no list.
        internal static readonly string[][] TypeLoadIds =
        {
            new[] { "rr/ties", "ashenmare/concrete-tie" },
            new[] { "rr/ballast" },
            new[] { "rr/concrete" },
            new[] { "rr/rails" },
            new[] { "rr/bridge-parts-steel", "rr/bridge-parts-wood" },
        };
        // The settings.json key of each slot. The last is the key 0.1.x wrote.
        internal static readonly string[] TypeKeys = { "reserveDaysTies", "reserveDaysBallast", "reserveDaysConcrete", "reserveDaysRails", "reserveDaysBridge", "reserveDays" };
        internal static readonly string[] TypeNames = { "ties", "ballast", "concrete", "rails", "bridge parts", "other loads" };

        internal static int TypeOf(string loadId)
        {
            for (int i = 0; i < TypeLoadIds.Length; i++)
                foreach (string id in TypeLoadIds[i])
                    if (string.Equals(loadId, id, StringComparison.Ordinal)) return i;
            return Other;
        }

        // First read of an old settings.json: a type with no value of its own takes the old single Reserve's value (when the
        // file had one), else the default. `own` is the type's value when its key is in the file, `legacy` the old key's.
        internal static float Inherit(float? own, float? legacy) => own ?? legacy ?? DefaultDays;

        // The largest of the sizes: what the feed's top-level reserveDays says for readers that know one number. It is 0
        // only when every size is 0, so "off" still reads right.
        internal static float Largest(float[] days)
        {
            float best = 0f;
            if (days != null) foreach (float d in days) { float c = ClampDays(d); if (c > best) best = c; }
            return best;
        }

        // The Reserve setting as the mod uses it: 0 to 3 days, anything unreadable (NaN) the default.
        internal static float ClampDays(float days)
        {
            if (float.IsNaN(days)) return DefaultDays;
            if (days < MinDays) return MinDays;
            if (days > MaxDays) return MaxDays;
            return days;
        }

        // Reserve 0 is off: the pile is shared again, as in the base game.
        internal static bool IsOn(float days) => ClampDays(days) > 0f;

        // Reserve days x the plant's own prediction of a day's production at its current tier
        // (IndustryLoaderBase.PredictQuantityProducedInNextDay(GetContractMultiplier()), which is 0 at tier 0 and ignores
        // inputs, as the game's own ordering does). Never negative, never NaN.
        internal static float ReserveCap(float days, float perDay)
        {
            float d = ClampDays(days);
            if (float.IsNaN(perDay) || float.IsInfinity(perDay) || perDay <= 0f || d <= 0f) return 0f;
            double cap = (double)d * perDay;
            return cap >= float.MaxValue ? float.MaxValue : (float)cap;
        }

        // How far the reserve is below its cap (0 when full, over full, or nearly full).
        internal static float Room(float cap, float reserveStock)
        {
            if (float.IsNaN(cap) || float.IsNaN(reserveStock) || cap <= 0f) return 0f;
            double room = (double)cap - Math.Max(0f, reserveStock);
            return room < MinMove ? 0f : (float)room;
        }

        // Fill first: of a quantity a producer is about to add to the shipping pile, the part that goes to the reserve
        // instead. The rest (quantity minus this) goes on to the shipping pile as the game would have added it. Always
        // between 0 and quantity.
        internal static float FillFirst(float quantity, float reserveStock, float cap)
        {
            if (float.IsNaN(quantity) || quantity <= 0f) return 0f;
            float room = Room(cap, reserveStock);
            if (room <= 0f) return 0f;
            return quantity < room ? quantity : room;
        }

        // Top up (the plan's "rebalance"): at the start of each service tick, before any component runs, the reserve takes
        // what it lacks from the shipping pile. Always between 0 and the shipping stock.
        internal static float TopUp(float shippingStock, float reserveStock, float cap)
        {
            if (float.IsNaN(shippingStock) || shippingStock < MinMove) return 0f;
            float room = Room(cap, reserveStock);
            if (room <= 0f) return 0f;
            return shippingStock < room ? shippingStock : room;
        }

        // The part of the next day's production that will go to the reserve before any reaches the shipping pile.
        internal static float Deficit(float cap, float reserveStock) => Room(cap, reserveStock);

        // Merging back (Reserve 0, standing down, the mod unloaded): how much of the reserve fits on the shipping pile now,
        // under the pile's maxStorage. Never more than either. Only this much may move: the game's AddToStorage clamps the
        // whole pile to the max it is given, so stock merged over maxStorage would be cut back by the quarry loader's next
        // production add, and at Scott it would stop the recipe until
        // shipped down. What doesn't fit stays in the reserve.
        internal static float MergeFits(float reserveStock, float shippingStock, float maxStorage)
        {
            if (float.IsNaN(reserveStock) || reserveStock <= 0f || float.IsNaN(shippingStock) || float.IsNaN(maxStorage)) return 0f;
            double room = (double)maxStorage - Math.Max(0f, shippingStock);
            if (room <= 0d) return 0f;
            return reserveStock <= room ? reserveStock : (float)room;
        }

        // A float for the feed: always finite (NaN or infinity reads 0).
        internal static float Finite(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;

        // Vanilla's order line (IndustryLoaderBase.OrderCars), with the reserve's term:
        //   need = stock + perDay x days - room - deficit
        // stock is the shipping pile, room the capacity of empties already on order or waybilled here, days the game's
        // horizon (OrderDays). With deficit 0 it is vanilla's need exactly. An order is placed only when need > 0.
        internal static float OrderNeed(float stock, float perDay, float days, float room, float deficit) =>
            (float)((double)stock + (double)perDay * days - room - Math.Max(0f, deficit));

        // How the mod puts the deficit into vanilla's own line: the stock the loader's order reads is the shipping pile
        // less the deficit (it can go below zero; vanilla only compares the need with zero). So
        // OrderNeed(OrderStock(s, d), p, days, r, 0) == OrderNeed(s, p, days, r, d), and tests/LogicCheck.cs checks it.
        internal static float OrderStock(float stock, float deficit) => (float)((double)stock - Math.Max(0f, deficit));

        // Vanilla's horizon, max(min(avgDeliveryDays, 3), portion of a day to the next regular service), for the tests'
        // worked numbers only: the mod never computes it, the game's own OrderCars does.
        internal static float OrderDays(float avgDeliveryDays, float portionOfDay) =>
            Math.Max(Math.Min(avgDeliveryDays, 3f), portionOfDay);

        // A quantity for a log line: whole units with thousands separators, the same on every machine.
        internal static string Units(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return "?";
            return Math.Round(v).ToString("N0", CultureInfo.InvariantCulture);
        }

        // Days for a log line or the settings text: "1", "0.5", "2.25".
        internal static string Days(float d) => ClampDays(d).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
