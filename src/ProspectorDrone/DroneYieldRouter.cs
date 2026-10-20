// Prospector Drone — edited and maintained by Darkethereal.
using System.Reflection;
using HarmonyLib;
using Items.Model;
using Players;
using SDModKit.Content;
using SDModKit.Game;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace StellarProspectorDrone;

internal static class DroneYieldRouter
{
    [System.ThreadStatic] private static Capture _active;
    [System.ThreadStatic] private static bool _suppressCapture;
    private static readonly List<Capture> Pending = new();
    private static bool _installed;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        var target = AccessTools.Method(typeof(TrackedPlayerServer), nameof(TrackedPlayerServer.AddItemToInventory),
            new[] { typeof(ItemDrop) });
        var prefix = new HarmonyMethod(typeof(DroneYieldRouter).GetMethod(nameof(BeforePlayerAward),
            BindingFlags.Static | BindingFlags.NonPublic));
        new HarmonyLib.Harmony("stellardrive.prospector-drone.yield-router").Patch(target, prefix: prefix);
    }

    internal static void Begin(DroneRuntime runtime, PartConfig config, uint expectedItem)
    {
        Pending.RemoveAll(capture => capture.ExpiresAt < Time.unscaledTime || capture.Runtime == runtime);
        _active = new Capture(runtime, config, expectedItem, Time.unscaledTime + 3f);
        Pending.Add(_active);
    }

    internal static void End() => _active = null;

    internal static void RunWithoutCapture(System.Action action)
    {
        var previous = _suppressCapture;
        _suppressCapture = true;
        try
        {
            action();
        }
        finally
        {
            _suppressCapture = previous;
        }
    }

    private static bool BeforePlayerAward(ItemDrop __0)
    {
        if (_suppressCapture) return true;
        Pending.RemoveAll(candidate => candidate.ExpiresAt < Time.unscaledTime);
        var capture = _active != null && _active.ExpectedItem == __0.ItemId
            ? _active
            : Pending.LastOrDefault(candidate => candidate.ExpectedItem == __0.ItemId);
        if (capture == null || __0.ItemId != capture.ExpectedItem || __0.Quantity <= 0) return true;

        var accepted = ProspectorStationBehaviour.AddCargo(capture.Config, __0.ItemId, __0.Quantity);
        if (accepted <= 0) return true;

        capture.Runtime.HarvestPending = false;
        capture.Runtime.State = ProspectorStationBehaviour.CargoRoomFor(capture.Config, __0.ItemId) <= 0
            ? DroneState.Returning
            : DroneState.Leveling;
        capture.Runtime.LevelTimer = 0f;
        capture.Config.Set("status", "Drone collected " + accepted + " " + ItemCatalog.NameOf(__0.ItemId));
        Pending.Remove(capture);
        return false;
    }

    private sealed class Capture
    {
        internal readonly DroneRuntime Runtime;
        internal readonly PartConfig Config;
        internal readonly uint ExpectedItem;
        internal readonly float ExpiresAt;

        internal Capture(DroneRuntime runtime, PartConfig config, uint expectedItem, float expiresAt)
        {
            Runtime = runtime;
            Config = config;
            ExpectedItem = expectedItem;
            ExpiresAt = expiresAt;
        }
    }
}
