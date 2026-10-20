// Prospector Drone — edited and maintained by Darkethereal.
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SDModKit.Game;
using SDModKit.Content;
using SDModKit.Patching;
using SDModKit.Util;
using Ships.Interface.Services;
using Ships.Parts.Common.Model.Reactions;
using Ships.Interface.Settings;
using UnityEngine;

namespace StellarProspectorDrone;

internal static class StationVisuals
{
    private const ushort StationPartId = 2110;
    private static bool _installed;
    private static PartSettings _researchTable;
    private static Log _log;

    internal static void Install(Log log)
    {
        _log = log;
        if (_installed) return;
        var instancer = GameRefs.Service<IShipPartInstancer>();
        if (instancer == null)
        {
            log.Warn("[ProspectorDrone] Ship part instancer is not ready; Research Table visuals will retry next world.");
            return;
        }
        var harmony = ModPatches.For("stellardrive.prospectordrone.visuals");
        var visuals = AccessTools.Method(instancer.GetType(), "InstantiatePartVisuals");
        var preview = AccessTools.Method(instancer.GetType(), "InstantiatePartPreview");
        if (visuals != null)
            harmony.Patch(visuals, postfix: new HarmonyMethod(typeof(StationVisuals), nameof(AfterInstantiateVisuals)));
        if (preview != null)
            harmony.Patch(preview, postfix: new HarmonyMethod(typeof(StationVisuals), nameof(AfterInstantiatePreview)));
        _installed = visuals != null || preview != null;
        if (_installed) log.Info("[ProspectorDrone] Research Table station visual hook installed.");
    }

    public static void AfterInstantiateVisuals(PartSettings settings, GameObject __result) => Apply(settings, __result);
    public static void AfterInstantiatePreview(PartSettings partSettings, GameObject __result) => Apply(partSettings, __result);

    internal static void Apply(PartSettings settings, GameObject target)
    {
        if (settings == null || settings.id != StationPartId || target == null || target.GetComponent<ResearchStationMarker>() != null)
            return;
        try
        {
            _researchTable ??= Resources.FindObjectsOfTypeAll<PartSettings>()
                .FirstOrDefault(part => part != null && part.internalStateClass == "ResearchTableState" && part.part != null);
            if (_researchTable == null)
            {
                _log?.Warn("[ProspectorDrone] Vanilla Research Table donor was not found; keeping the station fallback visual.");
                return;
            }

            foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;

            var holder = new GameObject("ProspectorResearchTableVisual");
            holder.layer = target.layer;
            holder.transform.SetParent(target.transform, false);
            CloneVisualTree(_researchTable.part.transform, holder.transform, target.layer);
            var interactions = target.GetComponentInChildren<IShipPartInteractions>(true);
            var interactionBox = interactions == null ? null : PartFinish.InteractionBox(interactions);
            if (interactionBox != null)
                interactionBox.size *= 0.5f;
            target.AddComponent<ResearchStationMarker>();
        }
        catch (Exception error)
        {
            _log?.Error("[ProspectorDrone] Research Table visual clone failed", error);
        }
    }

    private static void CloneVisualTree(Transform source, Transform parent, int layer)
    {
        var clone = new GameObject(source.name);
        clone.layer = layer;
        clone.transform.SetParent(parent, false);
        clone.transform.localPosition = source.localPosition;
        clone.transform.localRotation = source.localRotation;
        clone.transform.localScale = source.localScale;

        var sourceFilter = source.GetComponent<MeshFilter>();
        var sourceRenderer = source.GetComponent<MeshRenderer>();
        if (sourceFilter != null && sourceFilter.sharedMesh != null && sourceRenderer != null)
        {
            clone.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
            var renderer = clone.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = sourceRenderer.sharedMaterials;
            renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            renderer.receiveShadows = sourceRenderer.receiveShadows;
        }
        else if (source.GetComponent<SkinnedMeshRenderer>() is { sharedMesh: not null } skinned)
        {
            clone.AddComponent<MeshFilter>().sharedMesh = skinned.sharedMesh;
            var renderer = clone.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = skinned.sharedMaterials;
            renderer.shadowCastingMode = skinned.shadowCastingMode;
            renderer.receiveShadows = skinned.receiveShadows;
        }

        foreach (Transform child in source)
            CloneVisualTree(child, clone.transform, layer);
    }

    private sealed class ResearchStationMarker : MonoBehaviour
    {
    }
}
