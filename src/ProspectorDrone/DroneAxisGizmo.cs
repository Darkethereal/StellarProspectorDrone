// Prospector Drone — edited and maintained by Darkethereal.
using UnityEngine;

namespace StellarProspectorDrone;

internal sealed class DroneAxisGizmo : MonoBehaviour
{
    private const int StepDegrees = 15;
    private const float Radius = 1.18f;
    private readonly System.Collections.Generic.List<Transform> _labels = new();
    private TextMesh _readout;

    internal bool Visible
    {
        get => gameObject.activeSelf;
        set
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                renderer.enabled = value;
        }
    }

    internal void Build(Vector3 localEuler)
    {
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        AddRing("X Rotation (YZ)", Axis.X, new Color(1f, 0.16f, 0.12f, 0.90f), shader);
        AddRing("Y Rotation (XZ)", Axis.Y, new Color(0.20f, 1f, 0.28f, 0.90f), shader);
        AddRing("Z Rotation (XY)", Axis.Z, new Color(0.15f, 0.50f, 1f, 0.95f), shader);
        AddAxis("X", Vector3.right, Color.red, shader);
        AddAxis("Y", Vector3.up, Color.green, shader);
        AddAxis("Z", Vector3.forward, Color.blue, shader);
        AddReadout(localEuler);
    }

    private void AddRing(string name, Axis axis, Color color, Shader shader)
    {
        var ring = new GameObject(name);
        ring.transform.SetParent(transform, false);
        var line = ring.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 360 / StepDegrees;
        line.startWidth = 0.018f;
        line.endWidth = 0.018f;
        line.startColor = color;
        line.endColor = color;
        if (shader != null) line.material = new Material(shader);
        for (var i = 0; i < line.positionCount; i++)
        {
            var radians = i * StepDegrees * Mathf.Deg2Rad;
            var a = Mathf.Cos(radians) * Radius;
            var b = Mathf.Sin(radians) * Radius;
            line.SetPosition(i, axis switch
            {
                Axis.X => new Vector3(0f, a, b),
                Axis.Y => new Vector3(a, 0f, b),
                _ => new Vector3(a, b, 0f)
            });
            AddTick(axis, a, b, color, i % 6 == 0 ? 0.10f : 0.055f);
        }
    }

    private void AddTick(Axis axis, float a, float b, Color color, float length)
    {
        var outward = axis switch
        {
            Axis.X => new Vector3(0f, a, b),
            Axis.Y => new Vector3(a, 0f, b),
            _ => new Vector3(a, b, 0f)
        };
        var tick = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tick.name = axis + " 15 degree tick";
        tick.transform.SetParent(transform, false);
        tick.transform.localPosition = outward.normalized * Radius;
        tick.transform.localScale = new Vector3(0.018f, 0.018f, length);
        tick.transform.localRotation = Quaternion.LookRotation(outward.normalized, AxisUp(axis));
        var collider = tick.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        var renderer = tick.GetComponent<Renderer>();
        if (renderer != null)
        {
            var material = new Material(renderer.sharedMaterial);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.color = color;
            renderer.sharedMaterial = material;
        }
    }

    private void AddAxis(string label, Vector3 direction, Color color, Shader shader)
    {
        var axis = new GameObject(label + " Axis");
        axis.transform.SetParent(transform, false);
        var line = axis.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = 2;
        line.SetPosition(0, Vector3.zero);
        line.SetPosition(1, direction * 1.55f);
        line.startWidth = 0.026f;
        line.endWidth = 0.010f;
        line.startColor = color;
        line.endColor = color;
        if (shader != null) line.material = new Material(shader);

        var textObject = new GameObject(label + " Label");
        textObject.transform.SetParent(transform, false);
        textObject.transform.localPosition = direction * 1.72f;
        var text = textObject.AddComponent<TextMesh>();
        text.text = label;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontSize = 64;
        text.characterSize = 0.026f;
        text.color = color;
        _labels.Add(textObject.transform);
    }

    private void AddReadout(Vector3 euler)
    {
        var readout = new GameObject("Rotation Readout");
        readout.transform.SetParent(transform, false);
        readout.transform.localPosition = Vector3.up * 1.72f;
        var text = readout.AddComponent<TextMesh>();
        _readout = text;
        text.text = RotationText(euler);
        text.anchor = TextAnchor.LowerCenter;
        text.alignment = TextAlignment.Center;
        text.fontSize = 48;
        text.characterSize = 0.018f;
        text.color = Color.white;
        _labels.Add(readout.transform);
    }

    private void LateUpdate()
    {
        var camera = Camera.main;
        if (camera == null) return;
        if (_readout != null) _readout.text = RotationText(transform.eulerAngles);
        foreach (var label in _labels)
            if (label != null) label.rotation = camera.transform.rotation;
    }

    private static Vector3 AxisUp(Axis axis) => axis == Axis.Y ? Vector3.forward : Vector3.up;

    private static string RotationText(Vector3 euler) =>
        $"WORLD ROTATION  X {euler.x:0}°   Y {euler.y:0}°   Z {euler.z:0}°\nRINGS/TICKS: 15°";

    private enum Axis { X, Y, Z }
}
