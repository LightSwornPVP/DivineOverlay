# Divine Overlay

Divine Overlay is the companion overlay package for Divine. It is based on
[Block Overlay](https://mods.vintagestory.at/show/mod/6178) by Xel and retains
the upstream `blocksoverlay` mod ID for compatibility.

This package changes the visible name and English UI labels only. The overlay
implementation is the official Block Overlay 4.11.6 binary.

The companion Divine mod source is maintained in [`Divine/`](Divine/). Divine
provides combat indicators, resource waypoints, Divine Sight, storage tools,
and the `/divine` settings interface.

## Installation

Divine and Divine Overlay remain two separate mods because they use different
mod IDs, assets, and configuration files. Install them using either option:

- **One download:** download `Divine-and-DivineOverlay-0.2.24.zip`, extract it,
  and place both `Divine.zip` and `DivineOverlay.zip` in the game's `Mods`
  folder.
- **Separate downloads:** place `Divine-0.2.24.zip` and
  `DivineOverlay-4.11.6.zip` directly in the `Mods` folder.

Do not place the outer one-download bundle itself in `Mods`; extract its two
mod ZIPs first.

Default hotkeys:

- `]` toggles the overlay.
- `Ctrl+]` opens the target selector.

## Transparency

After starting the game once, open `VintagestoryData/ModConfig/blocksoverlay.json`
and set `OverlayOpacity` to a value from `0.0` to `1.0`:

```json
"OverlayOpacity": 0.5
```

`0.0` is fully transparent, `0.5` is half transparent, and `1.0` is fully
opaque (the default). Values outside that range are clamped automatically.
Restart the game after changing the value. The setting affects block outlines,
entity outlines, and their HUD labels.

You can also set opacity per target from the in-game color picker. Enter a
value from `0` to `100` in **Opacity (%)**, then click **Save**. The opacity is
stored together with that target's selected color.
