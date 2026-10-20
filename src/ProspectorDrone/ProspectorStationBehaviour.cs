// Prospector Drone — edited and maintained by Darkethereal.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using Items.Model;
using Planets.Objects;
using Players;
using SDModKit.Content;
using SDModKit.Game;
using SDModKit.Util;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Services;
using Ships.Services;
using UnityEngine;
using WorldObjects;
using WorldObjects.Services;

namespace StellarProspectorDrone;

internal sealed class ProspectorStationBehaviour : PartBehaviour
{
    private const float ScanInterval = 2f;
    private const float MineDistance = 7.5f;
    private const float DockHeight = 2.25f;
    private const float LaunchRise = 1.25f;
    private const float HarvestTimeout = 3f;
    private const float StuckTimeout = 20f;
    private const string UpgradeProfilePrefix = "stellar-prospector-drone.profile.";
    private const string UpgradeProfileVersionKey = UpgradeProfilePrefix + "version";
    private static readonly string[] UpgradeLevelKeys =
        { "flightLevel", "miningLevel", "cargoLevel", "rangeLevel" };

    private readonly Dictionary<string, DroneRuntime> _drones = new();
    private readonly Dictionary<int, string> _reservations = new();
    private WorldObjectsServerTracker _worldObjects;

    public override Vector2 PanelScale => new(2.07f, 1.817f);

    public override void OnWorldEnter()
    {
        _worldObjects = GameRefs.Service<WorldObjectsServerTracker>();
        if (_worldObjects != null)
            _worldObjects.OnObjectLoaded += OnWorldObjectLoaded;
    }

    public override void OnWorldExit()
    {
        if (_worldObjects != null)
            _worldObjects.OnObjectLoaded -= OnWorldObjectLoaded;
        _worldObjects = null;
        foreach (var runtime in _drones.Values)
            runtime.DestroyVisual();
        _drones.Clear();
        _reservations.Clear();
    }

    public override void OnPartAdded(ContentPart part)
    {
        Normalize(part.Config);
        RestoreOrCreateUpgradeProfile(part.Config);
        StationAccess.EnsureOwner(part.Config, null);
        EnsureOwnerName(part.Config, null);
        Runtime(part).State = DroneState.Docked;
        SetStatus(part.Config, part.Config.GetBool("enabled", false) ? "Searching" : "Paused");
    }

    public override void OnPartRemoved(uint shipId, ushort partId)
    {
        var key = Key(shipId, partId);
        if (!_drones.TryGetValue(key, out var runtime)) return;
        ReleaseTarget(runtime, key);
        runtime.DestroyVisual();
        _drones.Remove(key);
    }

    public override string ConfigTitle(ContentPart part) => "Prospector Drone";
    public override string Status(ContentPart part) => part.Config.GetString("status", "Paused");

    public override void DescribeConfig(ContentPart part, List<ConfigRow> rows)
    {
        Normalize(part.Config);
        var selected = part.Config.GetInt("resource", (int)Balance.Iron);
        var selectedIndex = Array.IndexOf(Balance.ResourceIds, (uint)selected);
        if (selectedIndex < 0) selectedIndex = 3;

        rows.Add(new ConfigRow
        {
            Tabs = Balance.ResourceNames,
            ActiveTab = selectedIndex,
            TabColors = new[]
            {
                new Color(0.45f, 0.75f, 0.28f),
                new Color(0.85f, 0.45f, 0.18f),
                new Color(0.55f, 0.85f, 0.95f),
                new Color(0.60f, 0.66f, 0.72f)
            },
            OnChoose = index => ContentActions.Send(part, "resource:" + Balance.ResourceIds[index])
        });

        rows.Add(new ConfigRow { Banner = part.Config.GetString("status", "Paused") });
        rows.Add(new ConfigRow
        {
            Label = "Drone inventory",
            Value = UsedCargoSlots(part.Config) + " / " + CargoSlots(part.Config) + " slots",
            ValueIcon = ItemCatalog.Icon((uint)selected)
        });
        if (CargoTotal(part.Config) > 0)
            rows.Add(new ConfigRow { Note = "Cargo: " + CargoSummary(part.Config) });
        rows.Add(new ConfigRow
        {
            Label = "Automation",
            Value = part.Config.GetBool("enabled", false) ? "ACTIVE" :
                part.Config.GetBool("droneDocked", true) ? "DOCKED" : "DOCKING",
            Highlight = part.Config.GetBool("enabled", false),
            Choices = new[] { part.Config.GetBool("enabled", false) ? "Dock" : "Start" },
            OnChoose = _ => ContentActions.Send(part, part.Config.GetBool("enabled", false) ? "recall" : "toggle")
        });

        rows.Add(new ConfigRow { Section = "UPGRADES" });
        AddUpgradeRow(part, rows, UpgradeTrack.Flight, "Flight", "flightLevel",
            level => Balance.FlightSpeed(level).ToString("0.0") + " m/s");
        AddUpgradeRow(part, rows, UpgradeTrack.Mining, "Mining", "miningLevel",
            level => (1f + level * 0.25f).ToString("0.00") + "x");
        AddUpgradeRow(part, rows, UpgradeTrack.Cargo, "Cargo", "cargoLevel",
            level => Balance.CargoSlots(level) + (Balance.CargoSlots(level) == 1 ? " slot" : " slots"));
        AddUpgradeRow(part, rows, UpgradeTrack.Range, "Range", "rangeLevel",
            level => Balance.Range(level).ToString("0") + " m");
    }

    public override void OnAction(ContentPart part, TrackedPlayerServer player, string action)
    {
        Normalize(part.Config);
        StationAccess.EnsureOwner(part.Config, player);
        EnsureOwnerName(part.Config, player);
        var steamId = StationAccess.SteamId(player);
        if (!StationAccess.CanOpen(part.Config, steamId))
        {
            SetStatus(part.Config, "Access denied — this station belongs to " + part.Config.GetString("ownerName", "another player"));
            return;
        }

        if (action.StartsWith("access:", StringComparison.Ordinal))
        {
            if (!StationAccess.IsOwner(part.Config, steamId))
            {
                SetStatus(part.Config, "Only the station owner can change access");
                return;
            }
            var fields = action.Split(':');
            if (fields.Length == 5 &&
                Enum.TryParse(fields[3], true, out StationPermission permission) &&
                bool.TryParse(fields[4], out var enabled))
            {
                var targetName = Uri.UnescapeDataString(fields[2]);
                StationAccess.Set(part.Config, fields[1], targetName, permission, enabled);
                SetStatus(part.Config, "Access updated for " + targetName);
            }
            return;
        }
        if (action == "toggle")
        {
            var enabled = !part.Config.GetBool("enabled", false);
            part.Config.Set("enabled", enabled);
            if (!enabled)
            {
                Recall(part);
            }
            else
            {
                var runtime = Runtime(part);
                runtime.State = DroneState.Launching;
                runtime.ScanTimer = 0f;
                SetStatus(part.Config, "Launching");
            }
            return;
        }

        if (action == "recall")
        {
            part.Config.Set("enabled", false);
            Recall(part);
            return;
        }

        if (action.StartsWith("resource:", StringComparison.Ordinal) &&
            uint.TryParse(action.Substring("resource:".Length), out var itemId) &&
            Balance.ResourceIds.Contains(itemId))
        {
            if (Selected(part.Config) == itemId) return;
            var conflicting = ConflictingStoredResource(part, itemId);
            if (conflicting != 0)
            {
                SetStatus(part.Config, "Cannot select " + ResourceName(itemId) + " — remove stored " +
                                            ResourceName(conflicting) + " first");
                return;
            }
            part.Config.Set("resource", (int)itemId);
            var runtime = Runtime(part);
            ReleaseTarget(runtime, Key(part));
            runtime.NoResourceNotified = false;
            runtime.State = DroneState.Returning;
            SetStatus(part.Config, "Returning — target changed to " + ResourceName(itemId));
            return;
        }

        if (action.StartsWith("take-slot:", StringComparison.Ordinal) &&
            int.TryParse(action.Substring("take-slot:".Length), out var droneSlot))
        {
            TakeDroneSlot(part, player, steamId, droneSlot);
            return;
        }

        if (action.StartsWith("upgrade:", StringComparison.Ordinal) &&
            Enum.TryParse(action.Substring("upgrade:".Length), true, out UpgradeTrack track))
        {
            if (!StationAccess.Has(part.Config, steamId, StationPermission.Upgrade))
            {
                SetStatus(part.Config, "You do not have permission to upgrade this station");
                return;
            }
            PurchaseUpgrade(part, player, track);
        }
    }

    public override void ServerTick(ContentPart part, float deltaTime)
    {
        Normalize(part.Config);
        RestoreOrCreateUpgradeProfile(part.Config);
        var runtime = Runtime(part);
        if (!TryStationPosition(part, out var station))
        {
            SetStatus(part.Config, "Waiting for station visuals");
            return;
        }

        var dock = station + Vector3.up * DockHeight;
        runtime.EnsureVisual(dock);
        EnsureOwnerName(part.Config, null);
        runtime.UpdateHome(dock, station, part.Config.GetString("ownerName", "Player"));
        StationVisuals.Apply(part.Part.Settings, ResolveVisualPart(part));

        if (runtime.HarvestPending && Time.unscaledTime > runtime.HarvestDeadline)
        {
            runtime.HarvestPending = false;
            ReleaseTarget(runtime, Key(part));
            runtime.State = CargoRoomFor(part.Config, Selected(part.Config)) <= 0
                ? DroneState.Returning
                : DroneState.Leveling;
            runtime.LevelTimer = 0f;
        }

        if (!part.Config.GetBool("enabled", false) && runtime.State != DroneState.Returning &&
            runtime.State != DroneState.Docking && runtime.State != DroneState.Unloading)
            runtime.State = Vector3.Distance(runtime.Position, dock) > 0.15f ? DroneState.Returning : DroneState.Paused;

        switch (runtime.State)
        {
            case DroneState.Docked:
            case DroneState.Paused:
                part.Config.Set("droneDocked", true);
                runtime.LevelVisual(dock, false);
                if (part.Config.GetBool("enabled", false) && StationRoomFor(part, Selected(part.Config)) > 0)
                    runtime.State = DroneState.Launching;
                else if (part.Config.GetBool("enabled", false))
                    SetStatus(part.Config, "Station storage is full for " + ResourceName(Selected(part.Config)) + " — clear a bin");
                else SetStatus(part.Config, "Paused");
                break;

            case DroneState.Launching:
                part.Config.Set("droneDocked", false);
                TickLaunch(part, runtime, deltaTime);
                break;

            case DroneState.Searching:
                part.Config.Set("droneDocked", false);
                if (StationRoomFor(part, Selected(part.Config)) <= 0)
                {
                    runtime.State = DroneState.Returning;
                    SetStatus(part.Config, "Returning — station storage is full for " + ResourceName(Selected(part.Config)));
                    break;
                }
                var searchPosition = EnsureMinimumFlightAltitude(runtime, runtime.Position);
                runtime.LevelVisual(searchPosition);
                SetStatus(part.Config, "Searching for " + ResourceName(Selected(part.Config)));
                runtime.ScanTimer -= deltaTime;
                if (runtime.ScanTimer <= 0f)
                {
                    runtime.ScanTimer = ScanInterval;
                    FindTarget(part, runtime, station);
                }
                break;

            case DroneState.Leveling:
                part.Config.Set("droneDocked", false);
                var levelPosition = EnsureMinimumFlightAltitude(runtime, runtime.Position);
                runtime.LevelVisual(levelPosition);
                runtime.LevelTimer += deltaTime;
                SetStatus(part.Config, "Leveling after mining");
                if (runtime.LevelTimer >= 0.45f)
                {
                    runtime.LevelTimer = 0f;
                    runtime.State = DroneState.Searching;
                    runtime.ScanTimer = 0f;
                }
                break;

            case DroneState.NoResource:
                part.Config.Set("enabled", false);
                runtime.LevelVisual(EnsureMinimumFlightAltitude(runtime, runtime.Position));
                runtime.State = DroneState.Returning;
                SetStatus(part.Config, "Returning — no " + ResourceName(Selected(part.Config)) + " found in range");
                break;

            case DroneState.Travelling:
                part.Config.Set("droneDocked", false);
                TickTravel(part, runtime, station, dock, deltaTime);
                break;

            case DroneState.Mining:
                part.Config.Set("droneDocked", false);
                TickMining(part, runtime, station, deltaTime);
                break;

            case DroneState.Returning:
                part.Config.Set("droneDocked", false);
                TickReturn(part, runtime, deltaTime);
                break;

            case DroneState.Docking:
                part.Config.Set("droneDocked", false);
                TickDock(part, runtime, deltaTime);
                break;

            case DroneState.Unloading:
                part.Config.Set("droneDocked", true);
                TickUnload(part, runtime);
                break;

            case DroneState.CargoBlocked:
                part.Config.Set("droneDocked", true);
                SetStatus(part.Config, "Cargo blocked — station inventory is full");
                runtime.MoveVisual(dock, dock + Vector3.forward, false, false);
                if (TryUnload(part))
                    runtime.State = part.Config.GetBool("enabled", false) ? DroneState.Launching : DroneState.Paused;
                break;
        }
    }

    private void FindTarget(ContentPart part, DroneRuntime runtime, Vector3 station)
    {
        var wanted = Selected(part.Config);
        var maxRange = Balance.Range(Level(part.Config, "rangeLevel"));
        var key = Key(part);
        MinablePlanetObject best = null;
        var bestDistance = float.MaxValue;

        foreach (var node in UnityEngine.Object.FindObjectsByType<MinablePlanetObject>(FindObjectsSortMode.None))
        {
            if (node == null || node.CurrentHealth <= 0f || node.dropRate.item == null || node.dropRate.item.id != wanted)
                continue;
            var id = node.GetInstanceID();
            if (_reservations.TryGetValue(id, out var owner) && owner != key)
                continue;
            var distance = Vector3.Distance(station, node.transform.position);
            if (distance <= maxRange && distance < bestDistance)
            {
                best = node;
                bestDistance = distance;
            }
        }

        if (best == null)
        {
            part.Config.Set("enabled", false);
            runtime.State = DroneState.Returning;
            if (!runtime.NoResourceNotified)
            {
                Toasts.Show("No " + ResourceName(wanted) + " found in range — drone returning to dock");
                runtime.NoResourceNotified = true;
            }
            SetStatus(part.Config, "Returning — no " + ResourceName(wanted) + " found in range");
            return;
        }

        runtime.NoResourceNotified = false;
        runtime.Target = best;
        runtime.TargetItem = wanted;
        runtime.LastProgressPosition = runtime.Position;
        runtime.StuckTimer = 0f;
        _reservations[best.GetInstanceID()] = key;
        runtime.State = DroneState.Travelling;
        SetStatus(part.Config, "Travelling to " + ResourceName(wanted) + " — " + bestDistance.ToString("0") + " m");
    }

    private void TickLaunch(ContentPart part, DroneRuntime runtime, float deltaTime)
    {
        if (StationRoomFor(part, Selected(part.Config)) <= 0)
        {
            runtime.State = Vector3.Distance(runtime.Position, runtime.HomePosition) <= 0.15f
                ? DroneState.Paused
                : DroneState.Returning;
            SetStatus(part.Config, "Station storage is full for " + ResourceName(Selected(part.Config)) + " — clear a bin");
            return;
        }
        var launchPoint = EnsureMinimumFlightAltitude(runtime, runtime.HomePosition + Vector3.up * LaunchRise);
        var distance = Vector3.Distance(runtime.Position, launchPoint);
        if (distance <= 0.08f)
        {
            runtime.MoveVisual(launchPoint, launchPoint + Vector3.forward, false);
            runtime.State = DroneState.Searching;
            runtime.ScanTimer = 0f;
            SetStatus(part.Config, "Searching for " + ResourceName(Selected(part.Config)));
            return;
        }
        var next = Vector3.MoveTowards(runtime.Position, launchPoint,
            Balance.FlightSpeed(Level(part.Config, "flightLevel")) * deltaTime);
        runtime.MoveVisual(next, launchPoint, false);
        SetStatus(part.Config, "Launching");
    }

    private void TickTravel(ContentPart part, DroneRuntime runtime, Vector3 station, Vector3 dock, float deltaTime)
    {
        if (StationRoomFor(part, Selected(part.Config)) <= 0)
        {
            ReleaseTarget(runtime, Key(part));
            runtime.State = DroneState.Returning;
            SetStatus(part.Config, "Returning — station storage is full for " + ResourceName(Selected(part.Config)));
            return;
        }
        if (!ValidTarget(part, runtime, station))
        {
            ReleaseTarget(runtime, Key(part));
            runtime.State = DroneState.Searching;
            return;
        }

        if (CargoRoomFor(part.Config, Selected(part.Config)) <= 0)
        {
            ReleaseTarget(runtime, Key(part));
            runtime.State = DroneState.Returning;
            return;
        }

        var target = TargetAimPoint(runtime.Target);
        var flightTarget = EnsureMinimumFlightAltitude(runtime, target + Vector3.up * 2.5f);
        var distance = Vector3.Distance(runtime.Position, target);
        if (distance <= MineDistance)
        {
            runtime.State = DroneState.Mining;
            runtime.StuckTimer = 0f;
            return;
        }

        var next = Vector3.MoveTowards(runtime.Position, flightTarget, Balance.FlightSpeed(Level(part.Config, "flightLevel")) * deltaTime);
        next = EnsureMinimumFlightAltitude(runtime, next);
        runtime.MoveVisual(next, flightTarget, false);
        TrackStuck(runtime, deltaTime, dock);
        SetStatus(part.Config, "Travelling to " + ResourceName(runtime.TargetItem) + " — " + distance.ToString("0") + " m");
    }

    private void TickMining(ContentPart part, DroneRuntime runtime, Vector3 station, float deltaTime)
    {
        if (StationRoomFor(part, Selected(part.Config)) <= 0)
        {
            ReleaseTarget(runtime, Key(part));
            runtime.State = DroneState.Returning;
            SetStatus(part.Config, "Returning — station storage is full for " + ResourceName(Selected(part.Config)));
            return;
        }
        if (!ValidTarget(part, runtime, station))
        {
            ReleaseTarget(runtime, Key(part));
            runtime.State = runtime.HarvestPending ? DroneState.Mining : DroneState.Leveling;
            if (!runtime.HarvestPending) runtime.LevelTimer = 0f;
            return;
        }

        var node = runtime.Target;
        var targetPos = TargetAimPoint(node);
        if (Vector3.Distance(runtime.Position, targetPos) > MineDistance + 1f)
        {
            runtime.State = DroneState.Travelling;
            return;
        }

        var remaining = CargoRoomFor(part.Config, runtime.TargetItem);
        if (remaining < Math.Max(1, node.dropRate.amountMax))
        {
            ReleaseTarget(runtime, Key(part));
            runtime.State = DroneState.Returning;
            return;
        }

        runtime.MoveVisual(EnsureMinimumFlightAltitude(runtime, runtime.Position), targetPos, true);
        SetStatus(part.Config, "Mining " + ResourceName(runtime.TargetItem));

        var damage = Balance.MiningDps(Level(part.Config, "miningLevel")) * deltaTime;
        var willBreak = node.CurrentHealth <= damage;
        if (willBreak)
        {
            runtime.HarvestPending = true;
            runtime.HarvestDeadline = Time.unscaledTime + HarvestTimeout;
            runtime.HarvestPosition = node.transform.position;
        }
        if (willBreak) DroneYieldRouter.Begin(runtime, part.Config, runtime.TargetItem);
        try
        {
            node.ReduceHealth(damage);
        }
        finally
        {
            if (willBreak) DroneYieldRouter.End();
        }

        if (willBreak)
        {
            ReleaseReservationOnly(runtime, Key(part));
            runtime.Target = null;
        }
    }

    private void TickReturn(ContentPart part, DroneRuntime runtime, float deltaTime)
    {
        ReleaseTarget(runtime, Key(part));
        var approach = EnsureMinimumFlightAltitude(runtime, runtime.HomePosition + Vector3.up * LaunchRise);
        var distance = Vector3.Distance(runtime.Position, approach);
        if (distance <= 0.12f)
        {
            runtime.LevelVisual(approach);
            runtime.State = DroneState.Docking;
            return;
        }
        var next = Vector3.MoveTowards(runtime.Position, approach, Balance.FlightSpeed(Level(part.Config, "flightLevel")) * deltaTime);
        next = EnsureMinimumFlightAltitude(runtime, next);
        runtime.MoveVisual(next, approach, false);
        SetStatus(part.Config, "Returning — " + distance.ToString("0") + " m");
        TrackStuck(runtime, deltaTime, approach);
    }

    private void TickDock(ContentPart part, DroneRuntime runtime, float deltaTime)
    {
        var dock = runtime.HomePosition;
        var distance = Vector3.Distance(runtime.Position, dock);
        if (distance <= 0.06f)
        {
            runtime.LevelVisual(dock, false);
            runtime.State = DroneState.Unloading;
            return;
        }
        var next = Vector3.MoveTowards(runtime.Position, dock,
            Mathf.Max(2f, Balance.FlightSpeed(Level(part.Config, "flightLevel")) * 0.45f) * deltaTime);
        runtime.MoveVisual(next, dock, false);
        SetStatus(part.Config, "Docking");
    }

    private void TickUnload(ContentPart part, DroneRuntime runtime)
    {
        SetStatus(part.Config, "Unloading");
        var emptied = TryUnload(part);
        if (!emptied)
        {
            runtime.State = DroneState.CargoBlocked;
            return;
        }
        runtime.State = part.Config.GetBool("enabled", false) ? DroneState.Launching : DroneState.Paused;
        runtime.ScanTimer = 0f;
    }

    private bool TryUnload(ContentPart part)
    {
        if (part.Part.State is not IStoragePart storage)
            return false;
        var changed = false;
        for (var droneSlot = 0; droneSlot < Balance.MaxCargoSlots; droneSlot++)
        {
            var itemId = SlotItem(part.Config, droneSlot);
            var remaining = SlotQuantity(part.Config, droneSlot);
            if (remaining <= 0) continue;
            for (byte slotId = 0; slotId < storage.SlotCount && remaining > 0; slotId++)
            {
                var slot = storage.GetSlot(slotId);
                if (slot.HasItem && slot.ItemId != itemId) continue;
                var current = slot.HasItem ? slot.Quantity : 0;
                var room = ItemMove.MaxStack(itemId) - current;
                if (room <= 0) continue;
                var moved = Math.Min(room, remaining);
                slot.HasItem = true;
                slot.ItemId = itemId;
                slot.Quantity = (byte)(current + moved);
                storage = storage.WithUpdatedSlot(slotId, slot);
                remaining -= moved;
                changed = true;
            }
            SetSlot(part.Config, droneSlot, remaining > 0 ? itemId : 0u, remaining);
        }
        if (changed)
            part.Ship.UpdatePartState(part.PartId, storage);
        return CargoTotal(part.Config) == 0;
    }

    private void OnWorldObjectLoaded(TrackedWorldObjectServer worldObject)
    {
        if (worldObject == null || worldObject.Content.Quantity == 0) return;
        foreach (var pair in _drones.ToArray())
        {
            var runtime = pair.Value;
            if (!runtime.HarvestPending || runtime.TargetItem != worldObject.Content.ItemId) continue;
            if (!PartData.TryGet(runtime.ShipId, runtime.PartId, out var config)) continue;

            var room = CargoRoomFor(config, runtime.TargetItem);
            if (room <= 0) continue;
            var collected = AddCargo(config, runtime.TargetItem, Math.Min(room, worldObject.Content.Quantity));
            if (collected <= 0) continue;
            runtime.HarvestPending = false;
            _worldObjects?.RemoveWorldObjectQuietly(worldObject.Id);
            runtime.State = CargoRoomFor(config, runtime.TargetItem) <= 0 ? DroneState.Returning : DroneState.Leveling;
            runtime.LevelTimer = 0f;
            SetStatus(config, "Collected " + collected + " " + ResourceName(runtime.TargetItem));
            break;
        }
    }

    private void PurchaseUpgrade(ContentPart part, TrackedPlayerServer player, UpgradeTrack track)
    {
        var key = LevelKey(track);
        var level = Level(part.Config, key);
        if (level >= Balance.MaxLevel)
        {
            SetStatus(part.Config, track + " is already at maximum level");
            return;
        }
        if (!part.Config.GetBool("droneDocked", true))
        {
            SetStatus(part.Config, "Recall and dock the drone before upgrading");
            return;
        }
        var cost = Balance.Costs[track][level];
        if (player == null || part.Part.State is not IStoragePart storage)
        {
            SetStatus(part.Config, "Missing materials for " + TrackName(track) + " level " + (level + 1));
            return;
        }
        var playerCost = new List<ItemDrop>();
        foreach (var item in cost)
        {
            var fromBench = Math.Min(ItemMove.Count(storage, item.ItemId), item.Quantity);
            var fromPlayer = item.Quantity - fromBench;
            if (fromPlayer > 0) playerCost.Add(new ItemDrop { ItemId = item.ItemId, Quantity = (byte)fromPlayer });
        }
        if (playerCost.Count > 0 && !player.HasItemsInInventory(playerCost))
        {
            SetStatus(part.Config, "Missing materials for " + TrackName(track) + " level " + (level + 1));
            return;
        }
        foreach (var item in cost)
            RemoveFromStorage(ref storage, item.ItemId, Math.Min(ItemMove.Count(storage, item.ItemId), item.Quantity));
        part.Ship.UpdatePartState(part.PartId, storage);
        if (playerCost.Count > 0) player.RemoveItemsFromInventory(playerCost);
        part.Config.Set(key, level + 1);
        SaveUpgradeProfile(part.Config);
        SetStatus(part.Config, TrackName(track) + " upgraded to level " + (level + 1));
    }

    private static void TakeDroneSlot(ContentPart part, TrackedPlayerServer player, string steamId, int droneSlot)
    {
        if (!StationAccess.Has(part.Config, steamId, StationPermission.TakeItems))
        {
            SetStatus(part.Config, "You do not have permission to take drone items");
            return;
        }
        if (player == null || droneSlot < 0 || droneSlot >= CargoSlots(part.Config)) return;

        var itemId = SlotItem(part.Config, droneSlot);
        var quantity = SlotQuantity(part.Config, droneSlot);
        if (itemId == 0 || quantity <= 0) return;

        var before = PlayerItemCount(player, itemId);
        DroneYieldRouter.RunWithoutCapture(() =>
            player.AddItemToInventory(new ItemDrop { ItemId = itemId, Quantity = (byte)Math.Min(byte.MaxValue, quantity) }));
        var moved = Math.Max(0, PlayerItemCount(player, itemId) - before);
        if (moved <= 0)
        {
            SetStatus(part.Config, "Player inventory is full");
            return;
        }

        var remaining = quantity - moved;
        SetSlot(part.Config, droneSlot, remaining > 0 ? itemId : 0u, Math.Max(0, remaining));
        SetStatus(part.Config, "Took " + moved + " " + ResourceName(itemId) +
                                    (remaining > 0 ? " — " + remaining + " remaining" : ""));
    }

    private static int PlayerItemCount(TrackedPlayerServer player, uint itemId)
    {
        if (player == null) return 0;
        var field = AccessTools.Field(typeof(TrackedPlayerServer), "_networkInventoryReader");
        var reader = field?.GetValue(player);
        if (reader == null) return 0;
        var method = AccessTools.Method(reader.GetType(), "GetItemQuantity", new[] { typeof(uint) });
        return method?.Invoke(reader, new object[] { itemId }) is int count ? count : 0;
    }

    private static void RemoveFromStorage(ref IStoragePart storage, uint itemId, int amount)
    {
        var remaining = Math.Max(0, amount);
        for (byte slotId = 0; slotId < storage.SlotCount && remaining > 0; slotId++)
        {
            var slot = storage.GetSlot(slotId);
            if (!slot.HasItem || slot.ItemId != itemId) continue;
            var removed = Math.Min(remaining, slot.Quantity);
            slot.Quantity = (byte)(slot.Quantity - removed);
            if (slot.Quantity == 0)
            {
                slot.HasItem = false;
                slot.ItemId = 0;
            }
            storage = storage.WithUpdatedSlot(slotId, slot);
            remaining -= removed;
        }
    }

    internal static Dictionary<uint, int> CombinedAvailable(ContentPart part)
    {
        var result = PlayerAccess.CurrentCounts();
        if (part.Part.State is IStoragePart storage)
        {
            foreach (var pair in ItemMove.Contents(storage))
            {
                result.TryGetValue(pair.Key, out var current);
                result[pair.Key] = current + pair.Value;
            }
        }
        return result;
    }

    internal static int StationRoomFor(ContentPart part, uint itemId)
    {
        if (part.Part?.State is not IStoragePart storage) return 0;
        var stack = Math.Max(1, (int)ItemMove.MaxStack(itemId));
        var room = 0;
        for (byte slotId = 0; slotId < storage.SlotCount; slotId++)
        {
            var slot = storage.GetSlot(slotId);
            if (!slot.HasItem || slot.Quantity <= 0) room += stack;
            else if (slot.ItemId == itemId) room += Math.Max(0, stack - slot.Quantity);
            else return 0;
        }
        for (var droneSlot = 0; droneSlot < CargoSlots(part.Config); droneSlot++)
            if (SlotQuantity(part.Config, droneSlot) > 0 && SlotItem(part.Config, droneSlot) != itemId)
                return 0;
        return room;
    }

    private static uint ConflictingStoredResource(ContentPart part, uint requestedItem)
    {
        for (var droneSlot = 0; droneSlot < CargoSlots(part.Config); droneSlot++)
        {
            var item = SlotItem(part.Config, droneSlot);
            if (SlotQuantity(part.Config, droneSlot) > 0 && item != 0 && item != requestedItem)
                return item;
        }
        if (part.Part?.State is not IStoragePart storage) return 0;
        for (byte slotId = 0; slotId < storage.SlotCount; slotId++)
        {
            var slot = storage.GetSlot(slotId);
            if (slot.HasItem && slot.Quantity > 0 && slot.ItemId != requestedItem)
                return slot.ItemId;
        }
        return 0;
    }

    private void AddUpgradeRow(ContentPart part, List<ConfigRow> rows, UpgradeTrack track, string label,
        string key, Func<int, string> format)
    {
        var level = Level(part.Config, key);
        var max = level >= Balance.MaxLevel;
        var cost = max ? string.Empty : ShortCostText(Balance.Costs[track][level]);
        rows.Add(new ConfigRow
        {
            Label = label + "  L" + level,
            Value = max ? format(level) : format(level) + " → " + format(level + 1) + " · " + cost,
            Highlight = !max,
            Choices = new[] { max ? "Max" : "Upgrade" },
            ChoiceEnabled = new[] { !max },
            OnChoose = _ => ContentActions.Send(part, "upgrade:" + track)
        });
    }

    private static string ShortCostText(IEnumerable<ItemDrop> cost) =>
        string.Join("/", cost.Select(item => item.Quantity + ResourceName(item.ItemId).Substring(0, 1)));

    private bool ValidTarget(ContentPart part, DroneRuntime runtime, Vector3 station)
    {
        if (runtime.Target == null || runtime.Target.CurrentHealth <= 0f || runtime.Target.dropRate.item == null)
            return false;
        if (runtime.Target.dropRate.item.id != Selected(part.Config))
            return false;
        return Vector3.Distance(station, runtime.Target.transform.position) <= Balance.Range(Level(part.Config, "rangeLevel"));
    }

    private void Recall(ContentPart part)
    {
        var runtime = Runtime(part);
        ReleaseTarget(runtime, Key(part));
        runtime.State = DroneState.Returning;
        SetStatus(part.Config, "Returning");
    }

    private void TrackStuck(DroneRuntime runtime, float deltaTime, Vector3 recovery)
    {
        if (Vector3.Distance(runtime.Position, runtime.LastProgressPosition) > 0.1f)
        {
            runtime.LastProgressPosition = runtime.Position;
            runtime.StuckTimer = 0f;
            return;
        }
        runtime.StuckTimer += deltaTime;
        if (runtime.StuckTimer < StuckTimeout) return;
        runtime.MoveVisual(recovery, recovery, false);
        runtime.State = DroneState.Returning;
        runtime.StuckTimer = 0f;
    }

    private static Vector3 EnsureMinimumFlightAltitude(DroneRuntime runtime, Vector3 intended)
    {
        // Match the layers used by the game's player grounding system. Walkable
        // planet terrain is on Default, while deck/floor surfaces are on Ship.
        // Keep the rendered model's lowest point at least one metre above either.
        const float minimumClearance = 1f;
        const float hoverBobMargin = 0.24f;
        var defaultLayer = LayerMask.NameToLayer("Default");
        var shipLayer = LayerMask.NameToLayer("Ship");
        if (defaultLayer < 0 && shipLayer < 0) return intended;
        var groundMask = (defaultLayer < 0 ? 0 : 1 << defaultLayer) |
                         (shipLayer < 0 ? 0 : 1 << shipLayer);
        var origin = new Vector3(intended.x, intended.y + 512f, intended.z);
        var hits = Physics.SphereCastAll(origin, 0.20f, Vector3.down, 4096f, groundMask, QueryTriggerInteraction.Collide)
            .Where(hit => hit.collider != null && hit.collider.GetComponentInParent<DroneHitboxMarker>() == null)
            .ToArray();
        if (hits == null || hits.Length == 0) return intended;
        var ground = hits.Max(hit => hit.point.y);
        intended.y = Mathf.Max(intended.y,
            ground + minimumClearance + runtime.BottomExtent + hoverBobMargin);
        return intended;
    }

    private static Vector3 TargetAimPoint(MinablePlanetObject target)
    {
        if (target == null) return Vector3.zero;

        // Object transforms are commonly placed at their base or below the
        // visible mesh. Aim at the combined collider centre first, then the
        // rendered centre, so both steering and the laser address the object.
        var colliders = target.GetComponentsInChildren<Collider>(true)
            .Where(collider => collider != null && collider.GetComponentInParent<DroneHitboxMarker>() == null)
            .ToArray();
        if (colliders.Length > 0)
        {
            var bounds = colliders[0].bounds;
            for (var i = 1; i < colliders.Length; i++) bounds.Encapsulate(colliders[i].bounds);
            return bounds.center;
        }

        var renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds.center;
        }

        return target.transform.position;
    }

    private void ReleaseTarget(DroneRuntime runtime, string owner)
    {
        ReleaseReservationOnly(runtime, owner);
        runtime.Target = null;
        runtime.SetLaser(false, runtime.Position);
    }

    private void ReleaseReservationOnly(DroneRuntime runtime, string owner)
    {
        if (runtime.Target == null) return;
        var id = runtime.Target.GetInstanceID();
        if (_reservations.TryGetValue(id, out var current) && current == owner)
            _reservations.Remove(id);
    }

    private DroneRuntime Runtime(ContentPart part)
    {
        var key = Key(part);
        if (_drones.TryGetValue(key, out var runtime)) return runtime;
        runtime = new DroneRuntime(part.ShipId, part.PartId);
        _drones[key] = runtime;
        return runtime;
    }

    private static bool TryStationPosition(ContentPart part, out Vector3 position)
    {
        position = Vector3.zero;
        var ships = GameRefs.Service<ShipsClientTracker>();
        return ships != null && ships.TryGetPartPosition(new PartContext(part.ShipId, part.PartId), out position);
    }

    private static GameObject ResolveVisualPart(ContentPart part)
    {
        var ships = GameRefs.Service<ShipsClientTracker>();
        if (ships != null && ships.TryGetVisualPart(new PartContext(part.ShipId, part.PartId), out var visual) && visual != null)
            return visual.GameObject;
        return null;
    }

    private static void EnsureOwnerName(PartConfig config, TrackedPlayerServer player)
    {
        if (!string.IsNullOrWhiteSpace(config.GetString("ownerName", string.Empty))) return;
        var name = player?.Name;
        if (string.IsNullOrWhiteSpace(name)) name = PlayerAccess.LocalNetworkState?.PlayerName;
        if (!string.IsNullOrWhiteSpace(name)) config.Set("ownerName", name.Trim());
    }

    private static void Normalize(PartConfig config)
    {
        if (!Balance.ResourceIds.Contains((uint)config.GetInt("resource", 0))) config.Set("resource", (int)Balance.Iron);
        foreach (var key in UpgradeLevelKeys)
            config.Set(key, Mathf.Clamp(config.GetInt(key, 0), 0, Balance.MaxLevel));
        for (var slot = 0; slot < Balance.MaxCargoSlots; slot++)
        {
            if (!config.Has(SlotItemKey(slot))) config.Set(SlotItemKey(slot), 0);
            if (!config.Has(SlotQuantityKey(slot))) config.Set(SlotQuantityKey(slot), 0);
        }
        if (!config.GetBool("slotInventoryMigrated", false))
        {
            foreach (var item in Balance.ResourceIds)
            {
                var legacy = Math.Max(0, config.GetInt(CargoKey(item), 0));
                if (legacy > 0) AddCargo(config, item, legacy, Balance.MaxCargoSlots);
                config.Set(CargoKey(item), 0);
            }
            config.Set("slotInventoryMigrated", true);
        }
        if (!config.Has("enabled")) config.Set("enabled", false);
        if (!config.Has("ownerName")) config.Set("ownerName", string.Empty);
        if (!config.Has("droneDocked")) config.Set("droneDocked", true);
    }

    private static void RestoreOrCreateUpgradeProfile(PartConfig config)
    {
        // WorldData is serialized in this world's sdmodkit-content.json, so the
        // profile survives removal of every table but never leaks into another
        // save. Existing table levels seed the profile during migration.
        if (WorldData.Get(UpgradeProfileVersionKey) != "1")
        {
            SaveUpgradeProfile(config);
            return;
        }

        foreach (var key in UpgradeLevelKeys)
        {
            var saved = WorldData.Get(UpgradeProfilePrefix + key);
            if (!int.TryParse(saved, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
            {
                level = Level(config, key);
                WorldData.Set(UpgradeProfilePrefix + key, level.ToString(CultureInfo.InvariantCulture));
            }
            config.Set(key, Mathf.Clamp(level, 0, Balance.MaxLevel));
        }
    }

    private static void SaveUpgradeProfile(PartConfig config)
    {
        foreach (var key in UpgradeLevelKeys)
            WorldData.Set(UpgradeProfilePrefix + key,
                Level(config, key).ToString(CultureInfo.InvariantCulture));
        WorldData.Set(UpgradeProfileVersionKey, "1");
    }

    internal static int Level(PartConfig config, string key) => Mathf.Clamp(config.GetInt(key, 0), 0, Balance.MaxLevel);
    internal static int CargoSlots(PartConfig config) => Balance.CargoSlots(Level(config, "cargoLevel"));
    internal static int UsedCargoSlots(PartConfig config) => Enumerable.Range(0, Balance.MaxCargoSlots)
        .Count(slot => SlotQuantity(config, slot) > 0);
    internal static int CargoRoomFor(PartConfig config, uint item)
    {
        var stack = Math.Max(1, (int)ItemMove.MaxStack(item));
        var room = 0;
        for (var slot = 0; slot < CargoSlots(config); slot++)
        {
            var quantity = SlotQuantity(config, slot);
            var slotItem = SlotItem(config, slot);
            if (quantity <= 0) room += stack;
            else if (slotItem == item) room += Math.Max(0, stack - quantity);
        }
        return room;
    }
    private static int CargoTotal(PartConfig config) => Enumerable.Range(0, Balance.MaxCargoSlots)
        .Sum(slot => SlotQuantity(config, slot));
    internal static uint Selected(PartConfig config) => (uint)config.GetInt("resource", (int)Balance.Iron);
    private static string CargoKey(uint item) => "cargo_" + item;
    private static string SlotItemKey(int slot) => "droneSlot" + slot + "Item";
    private static string SlotQuantityKey(int slot) => "droneSlot" + slot + "Quantity";
    internal static uint SlotItem(PartConfig config, int slot) => (uint)Math.Max(0, config.GetInt(SlotItemKey(slot), 0));
    internal static int SlotQuantity(PartConfig config, int slot) => Math.Max(0, config.GetInt(SlotQuantityKey(slot), 0));
    private static void SetSlot(PartConfig config, int slot, uint item, int quantity)
    {
        config.Set(SlotItemKey(slot), quantity > 0 ? (int)item : 0);
        config.Set(SlotQuantityKey(slot), Math.Max(0, quantity));
    }
    internal static int AddCargo(PartConfig config, uint item, int quantity, int slotLimit = -1)
    {
        var remaining = Math.Max(0, quantity);
        var stack = Math.Max(1, (int)ItemMove.MaxStack(item));
        var limit = slotLimit < 0 ? CargoSlots(config) : Mathf.Clamp(slotLimit, 0, Balance.MaxCargoSlots);
        for (var slot = 0; slot < limit && remaining > 0; slot++)
        {
            if (SlotItem(config, slot) != item || SlotQuantity(config, slot) <= 0) continue;
            var current = SlotQuantity(config, slot);
            var moved = Math.Min(remaining, Math.Max(0, stack - current));
            if (moved <= 0) continue;
            SetSlot(config, slot, item, current + moved);
            remaining -= moved;
        }
        for (var slot = 0; slot < limit && remaining > 0; slot++)
        {
            if (SlotQuantity(config, slot) > 0) continue;
            var moved = Math.Min(remaining, stack);
            SetSlot(config, slot, item, moved);
            remaining -= moved;
        }
        return quantity - remaining;
    }
    private static string Key(ContentPart part) => Key(part.ShipId, part.PartId);
    private static string Key(uint ship, ushort part) => ship + ":" + part;
    private static void SetStatus(PartConfig config, string status) => config.Set("status", status);

    private static string CargoSummary(PartConfig config)
    {
        var values = Enumerable.Range(0, Balance.MaxCargoSlots)
            .Where(slot => SlotQuantity(config, slot) > 0)
            .GroupBy(slot => SlotItem(config, slot))
            .Select(group => group.Sum(slot => SlotQuantity(config, slot)) + " " + ResourceName(group.Key));
        var text = string.Join(", ", values);
        return string.IsNullOrEmpty(text) ? "Empty" : text;
    }

    private static string ResourceName(uint itemId)
    {
        var index = Array.IndexOf(Balance.ResourceIds, itemId);
        return index >= 0 ? Balance.ResourceNames[index] : ItemCatalog.NameOf(itemId);
    }

    private static string LevelKey(UpgradeTrack track) => track switch
    {
        UpgradeTrack.Flight => "flightLevel",
        UpgradeTrack.Mining => "miningLevel",
        UpgradeTrack.Cargo => "cargoLevel",
        _ => "rangeLevel"
    };

    private static string TrackName(UpgradeTrack track) => track switch
    {
        UpgradeTrack.Flight => "Flight Speed",
        UpgradeTrack.Mining => "Mining Speed",
        UpgradeTrack.Cargo => "Drone Inventory",
        _ => "Operating Range"
    };
}

internal enum DroneState
{
    Docked,
    Launching,
    Searching,
    Travelling,
    Mining,
    Leveling,
    Returning,
    Docking,
    Unloading,
    Paused,
    CargoBlocked,
    NoResource
}
