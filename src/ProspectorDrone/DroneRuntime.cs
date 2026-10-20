// Prospector Drone — edited and maintained by Darkethereal.
using Planets.Objects;
using UnityEngine;

namespace StellarProspectorDrone;

internal sealed class DroneRuntime
{
    internal readonly uint ShipId;
    internal readonly ushort PartId;
    internal DroneState State = DroneState.Docked;
    internal MinablePlanetObject Target;
    internal uint TargetItem;
    internal float ScanTimer;
    internal float StuckTimer;
    internal Vector3 LastProgressPosition;
    internal Vector3 LastStation;
    internal bool HarvestPending;
    internal float HarvestDeadline;
    internal float LevelTimer;
    internal Vector3 HarvestPosition;
    internal Vector3 HomePosition;
    internal bool NoResourceNotified;

    private GameObject _root;
    private Transform _modelMount;
    private LineRenderer _laser;
    private StationHoverLabel _stationLabel;
    private DroneOwnerIndicator _indicator;
    private DroneAxisGizmo _axisGizmo;
    private BoxCollider _hitbox;
    private float _clearanceExtent = 0.9f;

    private const float AirborneModelHeight = 0f;
    private const float HoverBobAmplitude = 0.22f;
    private const float MinimumSurfaceClearance = 1f;
    private const float ClearanceProbeHeight = 0.05f;
    private const float ClearanceProbeDistance = 4096f;

    internal Vector3 Position => _root == null ? LastStation : _root.transform.position;
    internal Vector3 Forward => _root == null ? Vector3.forward : _root.transform.forward;
    // A rotation-independent radius guarantees clearance even while the drone
    // pitches toward a target and its axis-aligned world bounds change shape.
    internal float BottomExtent => _clearanceExtent;

    internal DroneRuntime(uint shipId, ushort partId)
    {
        ShipId = shipId;
        PartId = partId;
    }

    internal void EnsureVisual(Vector3 position)
    {
        if (_root != null)
        {
            EnsureVisible();
            return;
        }
        _root = new GameObject("Prospector Drone " + ShipId + ":" + PartId);
        _root.transform.position = position;
        var mount = new GameObject("Drone Model Mount");
        mount.transform.SetParent(_root.transform, false);
        _modelMount = mount.transform;
        // Keep both the model and its mount on Unity's standard axes: X right,
        // Y up, Z forward. Rotating the imported asset -90 degrees around Z
        // swapped its visible X/Y axes and left the otherwise-level drone
        // pointing toward the ground.
        _modelMount.localRotation = Quaternion.identity;
        try
        {
            if (GlbModelLoader.Instantiate(_modelMount) == null)
            {
                AddFallbackVisual();
            }
        }
        catch (System.Exception error)
        {
            MelonLoader.MelonLogger.Warning("[ProspectorDrone] Mining Drone Mk VII model could not load; using fallback: " + error.Message);
            AddFallbackVisual();
        }

        AddVisualHitbox();

        _laser = _root.AddComponent<LineRenderer>();
        _laser.positionCount = 2;
        _laser.startWidth = 0.065f;
        _laser.endWidth = 0.025f;
        _laser.startColor = new Color(0.20f, 0.90f, 1f, 0.95f);
        _laser.endColor = new Color(1f, 0.52f, 0.12f, 0.95f);
        _laser.useWorldSpace = true;
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null) _laser.material = new Material(shader);
        _laser.enabled = false;
        _indicator = _root.AddComponent<DroneOwnerIndicator>();
        _indicator.Attach(_modelMount.gameObject);
        var gizmoObject = new GameObject("Drone Axis Display");
        gizmoObject.transform.SetParent(_modelMount, false);
        _axisGizmo = gizmoObject.AddComponent<DroneAxisGizmo>();
        _axisGizmo.Build(Vector3.zero);
    }

    internal void UpdateHome(Vector3 home, Vector3 station, string playerName)
    {
        HomePosition = home;
        LastStation = home;
        if (_stationLabel == null) _stationLabel = _root.AddComponent<StationHoverLabel>();
        _stationLabel.StationPosition = station;
        _stationLabel.OwnerName = playerName;
        if (_indicator != null) _indicator.OwnerName = playerName;
    }

    private void AddFallbackVisual()
    {
        AddPrimitive(PrimitiveType.Sphere, "Core", Vector3.zero, new Vector3(0.55f, 0.24f, 0.55f), new Color(0.10f, 0.16f, 0.20f));
        AddPrimitive(PrimitiveType.Cube, "Arm L", new Vector3(-0.55f, 0f, 0f), new Vector3(0.70f, 0.10f, 0.14f), new Color(0.12f, 0.48f, 0.62f));
        AddPrimitive(PrimitiveType.Cube, "Arm R", new Vector3(0.55f, 0f, 0f), new Vector3(0.70f, 0.10f, 0.14f), new Color(0.12f, 0.48f, 0.62f));
        AddPrimitive(PrimitiveType.Cylinder, "Thruster L", new Vector3(-0.72f, -0.12f, 0f), new Vector3(0.12f, 0.16f, 0.12f), new Color(0.18f, 0.70f, 0.88f));
        AddPrimitive(PrimitiveType.Cylinder, "Thruster R", new Vector3(0.72f, -0.12f, 0f), new Vector3(0.12f, 0.16f, 0.12f), new Color(0.18f, 0.70f, 0.88f));
        AddPrimitive(PrimitiveType.Cylinder, "Laser Emitter", new Vector3(0f, 0f, 0.48f), new Vector3(0.10f, 0.22f, 0.10f), new Color(0.80f, 0.28f, 0.12f), new Vector3(90f, 0f, 0f));
    }

    internal void MoveVisual(Vector3 next, Vector3 lookAt, bool laser, bool hover = true)
    {
        EnsureVisual(next);
        EnsureVisible();
        // Enforce clearance at the final visual boundary on every airborne
        // update. State-specific movement code cannot accidentally bypass it.
        if (hover) next = EnforceAirborneClearance(next);
        _root.transform.position = next;
        var direction = lookAt - next;
        if (direction.sqrMagnitude > 0.01f)
        {
            // Unity's LookRotation points local +Z at the destination. Keep the
            // complete 3D direction so the drone pitches toward targets above
            // or below it instead of being restricted to horizontal yaw.
            var desiredRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            var step = 360f * Mathf.Max(Time.deltaTime, 0.001f);
            _root.transform.rotation = Quaternion.RotateTowards(_root.transform.rotation, desiredRotation, step);
        }
        if (_modelMount != null)
        {
            var bobHeight = AirborneModelHeight + Mathf.Sin(Time.time * 1.9f + PartId * 0.13f) * HoverBobAmplitude;
            // Convert world-up into the rotating root's local space. This keeps
            // the hover motion on global Y even while the drone pitches or yaws.
            _modelMount.localPosition = hover
                ? _root.transform.InverseTransformVector(Vector3.up * bobHeight)
                : Vector3.zero;
        }
        if (_indicator != null) _indicator.Deployed = hover;
        SetLaser(laser, lookAt);
    }

    private Vector3 EnforceAirborneClearance(Vector3 intended)
    {
        // Query downward from the intended position on every solid layer so
        // terrain, planets, ship decks, constructed floors, and future surface
        // layers all uphold the same invariant. Starting at the drone rather
        // than high above it prevents ceilings/upper decks from being mistaken
        // for the floor when the drone is operating inside a ship.
        // The model's rotation-independent extent and the downward bob margin
        // keep its lowest visible point at least one full metre above a surface.
        var origin = new Vector3(intended.x, intended.y + ClearanceProbeHeight, intended.z);
        var hits = Physics.SphereCastAll(origin, 0.20f, Vector3.down,
            ClearanceProbeDistance, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        var highestSurface = float.NegativeInfinity;
        foreach (var hit in hits)
        {
            if (hit.collider == null || hit.collider.GetComponentInParent<DroneHitboxMarker>() != null)
                continue;
            if (hit.point.y > highestSurface) highestSurface = hit.point.y;
        }

        if (!float.IsNegativeInfinity(highestSurface))
        {
            var bobMargin = HoverBobAmplitude + 0.03f;
            var minimumRootY = highestSurface + BottomExtent + MinimumSurfaceClearance + bobMargin;
            intended.y = Mathf.Max(intended.y, minimumRootY);
        }
        return intended;
    }

    internal void LevelVisual(Vector3 next, bool hover = true)
    {
        var yaw = _root == null ? 0f : _root.transform.eulerAngles.y;
        var levelRotation = Quaternion.Euler(0f, yaw, 0f);
        MoveVisual(next, next + levelRotation * Vector3.forward, false, hover);
        // Reset pitch and roll immediately. Waiting for interpolation allowed a
        // new target to be acquired before the level attitude was ever reached.
        _root.transform.rotation = levelRotation;
    }

    private void EnsureVisible()
    {
        if (_root == null) return;
        if (!_root.activeSelf) _root.SetActive(true);
        foreach (var renderer in _root.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = true;
    }

    private void AddVisualHitbox()
    {
        var renderers = _modelMount.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length == 0) return;

        var initialized = false;
        var localBounds = new Bounds();
        foreach (var renderer in renderers)
        {
            var bounds = renderer.bounds;
            var min = bounds.min;
            var max = bounds.max;
            for (var x = 0; x < 2; x++)
            for (var y = 0; y < 2; y++)
            for (var z = 0; z < 2; z++)
            {
                var worldCorner = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                var localCorner = _root.transform.InverseTransformPoint(worldCorner);
                if (!initialized)
                {
                    localBounds = new Bounds(localCorner, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    localBounds.Encapsulate(localCorner);
                }
            }
        }

        if (!initialized) return;
        _hitbox = _root.AddComponent<BoxCollider>();
        _hitbox.center = localBounds.center;
        _hitbox.size = localBounds.size * 1.30f;
        _hitbox.isTrigger = true;
        _clearanceExtent = localBounds.center.magnitude + localBounds.extents.magnitude;
        _root.AddComponent<DroneHitboxMarker>();
    }

    internal void SetLaser(bool enabled, Vector3 end)
    {
        if (_laser == null) return;
        _laser.enabled = enabled;
        if (!enabled) return;
        var drillOrigin = _modelMount == null
            ? _root.transform.position + _root.transform.forward * 0.55f
            : _modelMount.position + _modelMount.forward * 1.05f;
        _laser.SetPosition(0, drillOrigin);
        _laser.SetPosition(1, end);
    }

    internal void DestroyVisual()
    {
        if (_root != null) Object.Destroy(_root);
        _root = null;
        _modelMount = null;
        _laser = null;
        _stationLabel = null;
        _indicator = null;
        _axisGizmo = null;
        _hitbox = null;
        _clearanceExtent = 0.9f;
    }

    private void AddPrimitive(PrimitiveType type, string name, Vector3 localPosition, Vector3 scale, Color color, Vector3? rotation = null)
    {
        var child = GameObject.CreatePrimitive(type);
        child.name = name;
        child.transform.SetParent(_modelMount, false);
        child.transform.localPosition = localPosition;
        child.transform.localScale = scale;
        if (rotation.HasValue) child.transform.localEulerAngles = rotation.Value;
        var collider = child.GetComponent<Collider>();
        if (collider != null) Object.Destroy(collider);
        var renderer = child.GetComponent<Renderer>();
        if (renderer != null)
        {
            var material = new Material(renderer.sharedMaterial);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.color = color;
            renderer.sharedMaterial = material;
        }
    }
}

internal sealed class DroneHitboxMarker : MonoBehaviour
{
}
