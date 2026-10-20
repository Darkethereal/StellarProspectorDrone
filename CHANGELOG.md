# Prospector Drone changelog

**Edited and maintained by Darkethereal.**

## 0.3.22 — 2026-10-21

- Added one persistent Prospector upgrade profile per game save.
- Removing and rebuilding the table now restores Flight, Mining, Cargo, and Range upgrade levels.
- Existing table upgrades automatically seed the profile when an older save is first loaded.
- Multiple Prospector tables in the same save share and synchronize the saved upgrade profile.

## 0.3.21 — 2026-10-21

- Removed the nested -90° Z import correction that swapped the model's visible X and Y axes.
- Restored standard Unity orientation: X right/left, Y up/down, and Z forward/back.
- Forced the drone to zero pitch and roll while docked, paused, and completing its docking approach.
- Enforced a final per-frame airborne clearance invariant so the model's lowest visible point remains at least one metre above terrain, planets, ship decks, and constructed floors.

## 0.3.20 — 2026-10-15

- Restored an isolated drone-axis display for visual orientation checks.
- Standardized the runtime model mount to X-right, Y-up, and Z-forward.
- Kept the imported GLB correction on a nested visual-only transform.
- Kept hover bobbing aligned to world-space Y.
- Improved ground clearance, docking visibility, leveling, controls, storage rules, and UI sizing across the 0.3.x development series.
