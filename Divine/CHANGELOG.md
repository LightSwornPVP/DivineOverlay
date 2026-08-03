# Changelog

## 0.2.25

- Create ore waypoints from natural ore block breaks and native surface-bit
  collections instead of broad inventory increases.
- Ignore crafted, smashed, dropped, and repeatedly picked-up ore chunks and
  nuggets when creating waypoints.
- Require nearby world-pickup confirmation for resin and clay inventory gains.
- Fix right-click nearby-container auto-merge initialization and reach checks.
- Exclude GUI-less containers such as aged crates from automatic merging to
  prevent visual and inventory duplication bugs.
- Release Divine and the branded Divine Overlay companion as version 0.2.25.

## 0.2.24

- Replace the broad inventory count refresh with per-slot resource tracking.
- Avoid resource-waypoint work when ordinary broken blocks enter the inventory.

## 0.2.23

- Build against Vintage Story 1.22.6 and require Divine Overlay 4.11.6.
- Restore the player's original minimum-brightness setting when Divine Sight is disabled or unloaded.
- Fix stale resource counts after an inventory stack is completely removed.
- Expose the optional right-click storage open-all behavior in Divine Settings.
- Remove the unfinished smithing macro controls and prototype implementation.
- Remove the obsolete `clientsyn3x` configuration payload.
- Produce clean, installable release archives with documentation.
