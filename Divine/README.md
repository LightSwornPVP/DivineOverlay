# Divine

Divine is a client-side Vintage Story mod project with these features:

- A crosshair overlay that changes when your current crosshair target is an attackable entity.
- Adjustable visual target assist that keeps the indicator forgiving when your aim slips.
- Combat readability options for target name, target distance, hit-ready pulse, low-health color, hostile-only mode, combat-only mode, and optional sound cue.
- Target health tags with a health bar and current/max HP for the thing you are fighting.
- Source-based resource waypoints for natural ore blocks and native surface
  bits, plus pickup-confirmed resin and clay markers.
- Divine Sight brightness control.
- Integrated storage organization based on Chest Organizer by Kathanon.
- Bundled Divine Overlay support using the included Block Overlay release package.
- Optional right-click open-all behavior for storage containers.
- An in-game settings window opened with `/divine` or `Alt+C`.

## Build

Install the .NET 10 SDK, then build with:

```powershell
.\build.ps1 -VintageStoryInstall "C:\Program Files\Vintage Story"
```

If Vintage Story is installed somewhere else, change the path above.

The script creates `Divine.zip` and, when the overlay vendor zip is present, `DivineOverlay.zip`. Put both zips in your Vintage Story `Mods` folder.

If `vendor\blockoverlay-4.11.6.zip` is present, the build script creates a companion `DivineOverlay.zip` from Block Overlay, preserves Xel's authorship, and renames visible overlay labels to Divine Overlay.

## Notes

The waypoint feature uses the normal `/waypoint addati` command, so every mark
behaves like a player-created waypoint. Ore markers are created once from a
natural ore block break or native surface-bit collection. Crafting, smashing,
dropping, and re-picking processed ore do not create ore markers.

The integrated storage organization code is based on Chest Organizer by Kathanon under the BSD 2-Clause license. The license text is included in packaged builds as `ChestOrganizer-LICENSE.txt`.

Divine Overlay is a branded companion package based on Block Overlay by Xel. Its README preserves the upstream project link and attribution.
