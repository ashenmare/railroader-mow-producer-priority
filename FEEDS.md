# MoW Producer Priority feed (MowProducerPriority.Api), MoW Producer Priority 1.0.0

MoW Producer Priority publishes a small read-only feed: `MowProducerPriority.Api` in `MowProducerPriority.dll`. Another mod can read it by reflection, with no reference and no load order, to show each plant's MoW reserve beside its shipping pile. This file gives the ashenmare feed convention first, then this feed's schema.

## Ashenmare feed convention, v1

A feed is a `public static class` named `Api` in the mod's own namespace with these three members:

| Member | Meaning |
| --- | --- |
| `public const int Version` | The feed's schema version. Within a version, fields are only ever added. Renaming, retyping or dropping one raises the version, so a consumer checks it and reads only the versions it knows. |
| `public const string Feed` | The feed's stable id, in kebab case. It never changes. |
| `public static object Read()` | The data. `"api"` (int, equal to `Version`) and `"feed"` (string, equal to `Feed`) are always at the top level. |

To find feeds generically, scan the loaded assemblies (`AppDomain.CurrentDomain.GetAssemblies()`) for public static classes named `Api` with a literal `int Version`, a literal `string Feed` and a public static parameterless `Read()` returning `object`. Read the constants with `FieldInfo.GetRawConstantValue()`, and bind `Read` once with `Delegate.CreateDelegate(typeof(Func<object>), method)`.

The rules:
- **Plain data only.** `Read()` returns BCL types only (`Dictionary<string, object>`, `List<object>`, `string`, `int`, `float`, `bool`, `null`), never one of the mod's own types, so any JSON serializer can write it as is. Floats are always finite.
- **camelCase keys.**
- **Main thread.** Call feeds on Unity's main thread (from a `MonoBehaviour`, or a game HTTP handler that runs there).
- **Never throws.** On a failure it returns `{ "api": <Version>, "feed": "<Feed>", "error": "<what went wrong>" }` and logs once in the mod's own log. Check for `"error"` first.
- **Read only.** Reading a feed changes nothing in the game.
- **Cheap to poll.** A feed works only when it's called (no timer, no Update loop, no Harmony patch of its own) and is light enough to read every couple of seconds.
- **Don't change the answers.** Copy what you want to keep or edit.

## MoW Producer Priority's members

```csharp
namespace MowProducerPriority
{
    public static class Api
    {
        public const int Version = 1;
        public const string Feed = "mow-producer-priority";
        public static object Read();
    }
}
```

`Read()` reads the mod's setting and, for each pile the mod found on this railroad, two numbers from the industry's own storage. It builds a new dictionary on every call.

The feed is also listed on the shared Mod Feeds list (a copy of `ModFeeds.cs`, Revision 2): `ModFeeds.Read("mow-producer-priority")` returns what `Read()` does, and `ModFeeds.List()` describes the top-level keys below. Finding it by scanning for `Api` works as before.

## Schema, version 1

| Key | Type | Meaning |
| --- | --- | --- |
| `api` | int | `1` |
| `feed` | string | `"mow-producer-priority"` |
| `active` | bool | This game is keeping MoW reserves now: the mod is running, this game is the host (or single player), and at least one pile is kept (a size above 0, the seller unlocked). `false` on a multiplayer client, with every size 0, between worlds, and on a game version the mod can't run on. |
| `reserveDays` | float | The largest of this game's reserve sizes, in days, 0 to 3: the largest `days` among the piles below (or among the settings when there are none yet). It is 0 only when every size is 0, so a reader that knows one number still reads "off" right. Each pile's own size is its `days`. Since 0.2.0 this is the largest per-type size (a compatible widening, still Version 1); a reader that wants one pile's size reads that pile's own `days`. Only the host's settings change anything. |
| `piles` | list | One entry per pile the mod found on this railroad (an industry with a loader and a MoW seller on the same load), in the order found; empty between worlds. On the stock map: Scott Tie Plant's ties and the Dillsboro quarry's ballast and concrete. |

Each entry of `piles`:

| Key | Type | Meaning |
| --- | --- | --- |
| `industryId` | string | The industry's `identifier` (`"sylva-ties"`). |
| `loadId` | string | The load's `id` (`"rr/ties"`). |
| `sellerId` | string | The MoW seller component's `Identifier` (`"sylva-ties.ties-seller"`). |
| `stock` | float | What the MoW reserve holds now, in the load's units (pounds for ties, ballast and concrete). It is the seller's own entry in the industry's storage, which the game saves and sends to every player, so it reads the same on a client. |
| `days` | float | This pile's reserve size on this game, in days, 0 to 3: the setting for its load (ties, ballast, concrete, rails or bridge parts; any other load uses "Other loads"). Added in 0.2.0. `0` means that load's reserve is off. |
| `cap` | float | How much the reserve holds when full on this game: `days` x what the plant makes in a day at its current contract tier (the game's own `PredictQuantityProducedInNextDay`). `0` where this game keeps no reserve: `days` 0, no contract, the seller locked by progression, or a client. |
| `active` | bool | This game is keeping this reserve now. |

The `stock` can be above the `cap` for a while (the tier dropped, or the size was lowered): what is over stays for MoW. With `days` 0 a reserve that didn't fit back on its shipping pile still has `stock` (and `active` `false`) until it is sold or fits.

The shipping pile is the industry's shared storage for the load, where it always was: the loader's own pile, drawn by its outbound cars, the one the loader's `maxStorage` describes. The reserve sits on top of it, not inside it, so a piles view that shows the loader's pile is still right and can add the reserve beside it.

On a failure the answer is instead `{ "api": 1, "feed": "mow-producer-priority", "error": "..." }`, where `error` is a string. The error text never carries a home folder or account name.

Example:

```json
{ "api": 1, "feed": "mow-producer-priority", "active": true, "reserveDays": 1.0,
  "piles": [ { "industryId": "sylva-ties", "loadId": "rr/ties", "sellerId": "sylva-ties.ties-seller",
               "stock": 900000.0, "cap": 1500000.0, "days": 1.0, "active": true } ] }
```
