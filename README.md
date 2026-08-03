# Divine Overlay

Divine Overlay is the companion overlay package for Divine. It is based on
[Block Overlay](https://mods.vintagestory.at/show/mod/6178) by Xel and retains
the upstream `blocksoverlay` mod ID for compatibility.

This package changes the visible name and English UI labels only. The overlay
implementation is the official Block Overlay 4.11.6 binary.

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
