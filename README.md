# MoW Producer Priority

**Nexus Mods** https://www.nexusmods.com/railroader/mods/2042

This is the source of MoW Producer Priority 1.0.0. The ready-made zip is on the Nexus page.

Your maintenance of way cars come first at the plants that make their loads.

## What it does
Some plants both ship a load and sell it to your MoW cars: on the stock map, Scott Tie Plant (ties) and the Dillsboro
quarry, Rock Products (ballast and concrete). In the base game the outbound cars and your MoW cars share one pile.
When outbound empties are spotted the pile runs down to nothing and your MoW car gets about half of what the plant
makes: a car of ties at Scott can take about 3 game hours instead of about 10 minutes.
With MoW Producer Priority each of those plants keeps a reserve just for MoW, on top of its shipping pile:
- What the plant makes fills the reserve first. Only what doesn't fit goes on to the shipping pile.
- Your MoW car loads from the reserve at the plant's full selling rate while the reserve has stock, whatever the
  outbound cars are doing.
- As soon as MoW buys from it, the reserve refills from the shipping pile, ahead of the outbound cars.
- While the reserve is refilling, the plant orders fewer empties: it knows that part of what it makes next goes to MoW.
- Once the reserve is full, the plant ships as usual. It never stops working because MoW isn't buying.
- The plant's industry panel shows both piles: "MoW reserve" and "Shipping pile".
Each reserve holds one day of what the plant makes at its current contract tier, by default. Ties, ballast, concrete,
rails and bridge parts each have their own size (SETTINGS below).
It works out which plants by itself, so it works on other maps too: any industry with a loader and a MoW seller for
the same load.
Good to know:
- The first time you load a save with the mod, each reserve fills from its plant's shipping pile right away, up to a
  day of what the plant makes (at Scott, up to 1.5 million pounds of ties), so the shipping pile drops by that much,
  once. After that the reserve is extra room on top of the shipping pile. When nobody hauls the loads and both are
  full, the plant stops as it does in the base game.
- With no contract, the plant makes nothing, so the reserve doesn't fill. Your MoW cars can still buy what is in it.
- If a reserve ends up bigger than its size (a lower contract tier, or a smaller setting), what is over stays
  for MoW until you buy it.
- Works with Quick Turn: its bigger shipping piles stay as they are, and the reserve sits on top.
- It needs the plant's MoW seller to be open (unlocked by progression). Until then the plant ships everything it makes,
  as before.

## Install
1. Unzip so you have a MowProducerPriority folder in  Documents\Railroader\Mods\
   (Linux/Proton: the same folder inside the game's Proton prefix).
   Inside should be Info.json, MowProducerPriority.dll, 0Harmony.dll, README.txt, FEEDS.md and the license files.
2. Preferences > Features > Experimental > Allow DLL Mods, then restart the game.
3. Main menu > Mods > Manage Mods > tick MoW Producer Priority > Apply.
4. The first time you load a save made before you installed it, the game asks "This Save Uses Different Mods": choose
   "Keep My Current Mods" so MoW Producer Priority stays on for that load.

## Multiplayer
Everyone needs it, at the same version. The game treats this mod as required: a player without it, or with a different
version, gets "This server requires a mod you don't have" and can't join until they install the host's version. (Not
yet tried in a multiplayer game.)
The plants do their work in the host's game, so the host's reserve settings are the ones that count; a player's own
settings change nothing. Every player's industry panel shows the MoW reserve and the shipping pile with the host's
numbers. Only the host knows how big the reserve is, so on another player's panel the reserve's fill circle is drawn
against the shipping pile's size, and that panel keeps showing two piles even when the host has that load's reserve at 0 (the
host's MoW cars then buy from the shipping pile).

## Settings
Preferences (main menu or Esc menu) > Mods > MoW Producer Priority:
- "Ties reserve (days)", "Ballast reserve (days)", "Concrete reserve (days)", "Rails (days)", "Bridge parts (days)": how
  big each reserve is, in days of what the plant makes of that load at its current contract tier. 0 to 3, default 1 each. 0 turns that load's reserve off:
  what fits goes back on its shipping pile at once, and that pile is shared again, as in the base game. If a shipping
  pile is too full to take all of its reserve, the rest stays in the reserve for your MoW cars until they buy it down,
  or until the shipping pile has room for all of it; until then that plant keeps its two piles for that load. At 2 or
  3 days a full reserve can be bigger than the whole shipping pile, so then only your MoW cars empty it.
- Which load uses which size: "Ties" is rr/ties and the Concrete Ties mod's concrete tie; "Ballast" is rr/ballast;
  "Concrete" is rr/concrete; "Rails" is rr/rails; "Bridge parts" is rr/bridge-parts-steel and rr/bridge-parts-wood. The stock map has no plant that sells rails or bridge parts to MoW, so those two sizes matter
  only with a map mod that adds one.
- "Other loads (days)": the same, for any other load on another map's plant. 0 to 3, default 1. On the stock map
  nothing uses it. (It is the setting the older versions called "Reserve (days of production)".)
- "Debug log (for bug reports)", off by default: writes what the mod decides to a file in
  Documents/Railroader/Logs (ashenmare-debug-<date>-<time>.log). One switch for every ashenmare mod that has it. For a
  bug report, turn it on, repeat what went wrong, and send the newest file. Player.log gets no lines from the mod during play.
They take effect when you press Save, and are kept between sessions and updates (the zip holds no settings file).
The Mods tab has a search box (press Enter) that finds mods by name and settings by label. Mods with an older settings menu are matched by name only.
Updating from 0.1.x: the one size you had becomes the starting size of ties, ballast and concrete, so nothing changes
until you move a slider. The new sizes are kept from your first Save. Until then the sliders show your old size, and
"Reset to defaults" goes back to it (0 if you had it off) rather than to 1, until you restart the game after your first
Save; from then on it goes to 1.
Updating from 0.2.x: "Rails" and "Bridge parts" start at your "Other loads" size, which is what rails and bridge parts
used before, so nothing changes until you move a slider. A concrete tie from the Concrete Ties mod now uses the "Ties"
size instead of "Other loads", so if those two differ its reserve follows the Ties size (on if Ties is above 0).

## Removing it
Whatever the reserves held when you last saved stays in the save, unused: without the mod the game never looks at it,
and the plant's shared pile is the one it uses again. Install the mod again and the reserve is back.
To give some of it back first: set every reserve to 0 and save. What fits goes back at once; what doesn't stays for your
MoW cars until they buy it down (the debug log says how much stays apart). You don't have to wait for
it: removing the mod with some left is fine, it just stays in the save.
1. Untick it in Manage Mods (or delete its folder).
2. Load the save. Because the save used the mod, the game asks: "This Save Uses Different Mods" (choose "Keep My
   Current Mods") or, if you deleted the folder, "Mod Not Available" (choose "Load Without It").

## Seeing it work
Player.log: one line, "MoW Producer Priority loaded: ...", when the game starts, and a warning if something goes wrong.
With "Debug log (for bug reports)" on, the debug log file has a line when a save loads ("world", with how many reserves
are kept), when a plant orders fewer empties ("order"), about once a minute for each pile that moved ("reserve"), and
when you save the settings. This mod's own log lines never show your home folder or account name.

## Changes
1.0.0: New: "Rails (days)" and "Bridge parts (days)" reserve sizes, for map mods whose plants sell rails or bridge parts. Concrete ties from the Concrete Ties mod use the Ties size.
0.2.3: Quieter: no more lines in Player.log during play. The "Log decisions" setting is now "Debug log (for bug reports)", which writes to its own file.
0.2.2: the settings use the shared settings menu's Revision 10, with a search box in the Mods tab. Reserves, the feed and the saved sizes work as in 0.2.1.
0.2.1:
- Fixed: an empty settings.json no longer logs a warning.
- Changed: Reset to defaults after updating from 0.1.x is described as it works (see Updating from 0.1.x).
0.2.0:
- New: a reserve size for each of ties, ballast and concrete, instead of one for all.
- Changed: your old size carries over to each of them.
0.1.5:
- New: its data feed is listed for other mods (Mod Feeds).
0.1.4:
- Changed: the settings page uses the house's current settings template.
- Changed: shorter settings tooltips.
0.1.3: first release, for 0.6.2.

## Building

Needs the .NET SDK (8 or newer) and a Railroader install for the game's dlls. Point the build at the game's
Managed folder (`Railroader_Data/Managed`) with a property or an environment variable.

```
dotnet build MowProducerPriority.csproj -c Release -p:ManagedPath="<game folder>/Railroader_Data/Managed"
```

or set `RAILROADER_MANAGED` to that folder once and run `dotnet build MowProducerPriority.csproj -c Release`. The build stops with a
message if the folder is missing. NuGet brings in Harmony and the .NET Framework reference assemblies.

The output is `bin/Release/MowProducerPriority.dll` and `bin/Release/0Harmony.dll`. To try it, put both beside `Info.json`
in a folder named `MowProducerPriority` under `Documents/Railroader/Mods`, and turn on Allow DLL Mods in Preferences.

## License

MIT, see LICENSE.
The release zip includes Harmony (MIT), see THIRD-PARTY-NOTICES.txt.

Making a Railroader mod? Use our settings menu in yours: https://github.com/ashenmare/railroader-mod-settings
Made with AI assistance (Claude), every release reviewed and audited in game by a human.
