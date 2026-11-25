using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Model;
using Model.Definition.Data;
using Model.Ops;
using Model.Ops.Definition;

namespace MowProducerPriority
{
    // The five patches, applied by hand on the members Reflect resolved (no attributes, so nothing here is swept in by
    // anyone's PatchAll). All or nothing: Main unpatches everything if one of them throws.
    //
    // IndustryContext is a readonly struct, so its two hooks take it typed and by value
    // (IndustryContext __instance), never as object: Harmony 2.2.2 refuses to build an object-typed instance argument on a
    // struct method (invalid IL), and on newer Harmony an object __instance is a fresh box on every call, so it can never
    // be matched with the box OrderCars was handed. Other arguments bind by position (__0, __1), so a renamed parameter
    // can't stop a patch binding. Every patch is tested on the game's own assemblies under
    // mono, with Harmony 2.2.2 and with 2.3.6: each one applies and works.
    //
    // Every patch body: off (Switch.Live false) means return at once; anything thrown is caught and counted (Plants.Failed,
    // which stands the mod down after 5), so the game's own code always runs as it would have.
    internal static class Hooks
    {
        internal static void Patch(Harmony harmony)
        {
            harmony.Patch(Reflect.DoService, prefix: M(nameof(ServicePrefix)));
            harmony.Patch(Reflect.ContextAdd, prefix: M(nameof(AddPrefix)));
            harmony.Patch(Reflect.ContextQuantity, postfix: M(nameof(QuantityPostfix)));
            harmony.Patch(Reflect.OrderCars, prefix: M(nameof(OrderPrefix)), finalizer: M(nameof(OrderFinalizer)));
            harmony.Patch(Reflect.SellerPanel, postfix: M(nameof(PanelPostfix)));
        }

        private static HarmonyMethod M(string name) =>
            new HarmonyMethod(typeof(Hooks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));

        // Industry.DoService, before any component's Service (host only): the top-up, and merging back what now fits.
        private static void ServicePrefix(Industry __instance)
        {
            if (!Switch.Live) return;
            try { Plants.Tick(__instance); }
            catch (Exception e) { Plants.Failed("the service tick", e); }
        }

        // IndustryContext.AddToStorage(Load load, float quantity, float max): fill first (host only).
        private static void AddPrefix(IndustryContext __instance, Load __0, ref float __1)
        {
            if (!Switch.Live) return;
            try { Plants.FillFirst(__instance, __0, ref __1); }
            catch (Exception e) { Plants.Failed("fill first", e); }
        }

        // IndustryContext.QuantityInStorage(Load load): inside a paired loader's OrderCars, the stock its order reads, less
        // the reserve's need. Outside that scope it returns at once.
        private static void QuantityPostfix(IndustryContext __instance, Load __0, ref float __result)
        {
            if (!Switch.Live) return;
            try { Plants.OrderStock(__instance, __0, ref __result); }
            catch (Exception e) { Plants.Failed("the order's stock read", e); }
        }

        // IndustryLoaderBase.OrderCars(IIndustryContext ctx): opens the order scope for a paired loader (host only).
        private static void OrderPrefix(IndustryLoaderBase __instance, IIndustryContext __0)
        {
            if (!Switch.Live) return;
            try { Plants.EnterOrder(__instance, __0); }
            catch (Exception e) { Plants.Failed("the order", e); }
        }

        // Always closes the scope, whatever OrderCars did (a throw in it is the game's, passed on unchanged).
        private static void OrderFinalizer()
        {
            try { Plants.LeaveOrder(); }
            catch (Exception e) { Plants.Failed("the order's end", e); }
        }

        // IndustrySeller.PanelFields(IndustryContext): on a split seller (host and client), the industry panel shows the MoW
        // reserve and the shipping pile instead of the one vanilla line.
        private static void PanelPostfix(IndustrySeller __instance, ref IEnumerable<IndustryComponent.PanelField> __result)
        {
            if (!Switch.Live) return;
            try { Plants.Panel(__instance, ref __result); }
            catch (Exception e) { Plants.Failed("the industry panel", e); }
        }
    }
}
