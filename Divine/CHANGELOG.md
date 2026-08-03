# Changelog

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
