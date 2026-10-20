# Test report — Prospector Drone 0.3.20

**Testing, editing, and release maintenance by Darkethereal.**

## 0.3.20 verification

- Release build succeeds against the installed StellarDrive and SDModKit assemblies.
- Hidden MelonLoader startup loads the DLL, registers one station and one research unlock, and prewarms the supplied Drone Mk VII model without a mod exception.
- The headless renderer still reports the game's expected null-graphics compute-shader errors; these are unrelated to Prospector Drone.
- The professional panel, in-world drone movement, and input interaction require a normal rendered save for final visual acceptance testing.

Date: 2026-10-15 (Pacific/Auckland)

## Completed

- Decompiled and inspected StellarDrive, SDModKit 1.3.3, and Upgrade Table integration points.
- Confirmed real resource IDs and the player's mining laser damage path.
- Confirmed normal node destruction creates server-tracked debris containing the yielded item and quantity.
- Built `StellarProspectorDrone.dll` in Release configuration.
- Build result: **0 warnings, 0 errors**.
- Confirmed the manager package contains `mod.json` and `Mods/StellarProspectorDrone.dll`.
- Confirmed content JSON parses as valid JSON.
- Confirmed the assembly can be inspected and contains the plugin, behavior, balance, and runtime types.
- Completed a hidden MelonLoader main-menu smoke test: SDModKit and Prospector Drone both loaded and registered successfully.
- The headless `-nographics` run produced expected game rendering/compute-shader errors unrelated to the mod; no Prospector Drone exception or load failure appeared.
- Integrated the supplied `mining_drone_mk7.glb` model, including both mesh primitives and six embedded PBR textures.
- Confirmed the final assembly contains the GLB resource and builds with the runtime GLB loader at zero warnings/errors.
- Startup preflight successfully decoded the embedded GLB into a Unity mesh and loaded its textures; MelonLoader reported `Mining Drone Mk VII model and textures loaded.`
- Added renderer-only cloning of the vanilla Research Centre/Table model while preserving the storage station's interaction components.
- Added player-name hologram lifecycle and live home-coordinate updates for every station runtime.
- Confirmed the native Research Table is part ID 22, asset `ResearchTable`, state `ResearchTableState`, and uses `PreciseOnFloor` placement.
- Added the visible `Prospector Drone` research node (ID 2110), requiring Beacons (research ID 14) and unlocking `ProspectorDroneStation`.
- Changed build-menu registration from a separate custom tab to the vanilla Parts tab, immediately after `ResearchTable`.
- Confirmed the final embedded content contains one custom station part and one research unlock.
- Updated the station construction recipe to exactly 30 Iron, 8 Glass, and 20 Copper.
- Replaced quantity-only cargo capacity with one to six persistent stack slots.
- Added launch, return approach, docking descent, unloading, and automatic relaunch states.
- Constrained drone facing to horizontal rotation and added a 4.5 cm airborne hover animation.
- Corrected the hologram billboard to stay upright and moved it above the station and docked drone.
- Simplified the panel and added a one-time no-resource popup alongside the persistent status banner.

## Requires interactive in-game verification

- Final visual placement alignment beside the Research Table entry.
- Opening the combined native storage/configuration panel.
- End-to-end mining for all four node types in an actual world.
- Visual tuning of the drone and laser.
- Save/load with a placed station and non-empty cargo.
- Station-full recovery.
- Host/client state behavior and late joining.

These items require entering a playable world and cannot be proven by compilation or a main-menu loader smoke test alone.
