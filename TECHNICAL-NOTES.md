# Technical notes

**Edited and maintained by Darkethereal — Prospector Drone 0.3.20.**

## Confirmed game integration points

- `Planets.Interface.Model.IMinableObject.ReduceHealth(float)` is the damage path called by `Tools.MiningLaser.MiningLaser.UpdateChecks`.
- `Planets.Objects.MinablePlanetObject.dropRate.item.id` is used for exact Wood, Copper, Glass, and Iron filtering.
- `MinablePlanetObject.DestroyObject()` calls the game's planet/world interaction service, which creates authoritative debris.
- `WorldObjects.Services.WorldObjectsServerTracker.OnObjectLoaded` exposes the actual debris `ItemDrop`; the mod captures that drop and calls `RemoveWorldObjectQuietly` to prevent duplication.
- `SDModKit.Content.ContentRegistry` registers the custom placeable and behavior.
- `SDModKit.Content.ContentResearch` registers the `Prospector Drone` node and unlocks the station through the normal game research provider.
- `SDModKit.Game.CustomParts` inserts the station immediately after `ResearchTable` in the vanilla Parts build menu.
- The part uses the vanilla `StorageChest` donor and `StorageState`, preserving native inventory synchronization and save behavior.
- `StationVisuals` replaces only the rendered station geometry with a renderer-only clone of the vanilla `ResearchTableState` donor. Storage colliders, interactions, state, synchronization, and menus remain intact.
- `SDModKit.Content.ContentActions` validates remote actions against the player currently interacting with the station.
- `SDModKit.Content.PartConfig` and `ContentSave` persist selection, upgrades, automation, status, and cargo.
- `Ships.Interface.Services.IShipsClientProvider.TryGetPartPosition` resolves the station position used for drone movement and range checks on the host.
- `TrackedPlayerServer.HasItemsInInventory` followed by `RemoveItemsFromInventory` performs authoritative, all-or-nothing upgrade payment.

## Resource IDs confirmed from game/mod assemblies

| Resource | Item ID | Source |
|---|---:|---|
| Iron | 101 | `ItemsConstants.IronItemId` |
| Glass | 102 | `ItemsConstants.GlassItemId` |
| Wood | 110 | `ItemsConstants.WoodItemId` |
| Copper | 112 | StellarDrive item catalog usage confirmed by Upgrade Table |

## Custom identifiers

- Content pack: `prospector-drone`
- Part asset: `ProspectorDroneStation`
- Part ID: `2110`
- Behavior key: `prospector-station`
- Research ID: `2110`
- Research prerequisite: Beacons (`14`)

Part ID 2110 was selected adjacent to the existing community Upgrade Table allocation (2100). If another installed content mod reports a collision, change it in `Content/prospector-content.json` and rebuild.

## Implementation boundaries

The authoritative behavior runs through SDModKit's server ship tick. `mining_drone_mk7.glb` is embedded in the DLL and loaded by `GlbModelLoader` into a Unity mesh with its two material slots, diffuse textures, normal maps, corrected handedness, and normalized scale. `DroneRuntime` retains a procedural model as a safe fallback if the supplied asset cannot be decoded on a future game version.

Each `DroneRuntime` also owns a billboarded `TextMesh` hologram and an explicit `HomePosition`. The home value is recalculated from `ShipsClientTracker.TryGetPartPosition` every tick, rather than being captured only when the station is placed. All docked and returning movement uses this live position.
