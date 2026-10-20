// Prospector Drone — edited and maintained by Darkethereal.
using SDModKit.Game;
using UnityEngine;
using System.Collections.Generic;

namespace StellarProspectorDrone;

internal sealed class DroneOwnerIndicator : MonoBehaviour
{
    internal string OwnerName;
    internal bool Deployed;

    private readonly List<HighlightMaterial> _highlightMaterials = new();
    private GUIStyle _labelStyle;
    private bool _highlighted;

    internal void Attach(GameObject model)
    {
        if (model == null) return;
        _highlightMaterials.Clear();
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.materials)
            {
                if (material == null) continue;
                _highlightMaterials.Add(new HighlightMaterial(material));
            }
        }
    }

    private bool IsLocalOwner()
    {
        var local = PlayerAccess.LocalNetworkState?.PlayerName;
        return !string.IsNullOrWhiteSpace(local) && !string.IsNullOrWhiteSpace(OwnerName) &&
               string.Equals(local.Trim(), OwnerName.Trim(), System.StringComparison.OrdinalIgnoreCase);
    }

    private void OnGUI()
    {
        var camera = Camera.main;
        if (!Deployed || camera == null || !IsLocalOwner())
        {
            SetHighlighted(false);
            return;
        }

        var viewport = camera.WorldToViewportPoint(transform.position + Vector3.up * 0.25f);
        var behind = viewport.z < 0f;
        var point = new Vector2(viewport.x * Screen.width, (1f - viewport.y) * Screen.height);
        if (behind) point = new Vector2(Screen.width - point.x, Screen.height - point.y);
        point.x = Mathf.Clamp(point.x, 14f, Screen.width - 14f);
        point.y = Mathf.Clamp(point.y, 14f, Screen.height - 14f);

        var aim = Cursor.lockState == CursorLockMode.Locked
            ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
            : new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        var hovered = !behind && Vector2.Distance(point, aim) <= 34f;
        SetHighlighted(hovered);

        var previous = GUI.color;
        GUI.color = new Color(0.12f, 0.95f, 1f, behind ? 0.60f : 0.95f);
        GUI.DrawTexture(new Rect(point.x - 4f, point.y - 4f, 8f, 8f), Texture2D.whiteTexture);
        GUI.color = previous;

        if (!hovered) return;
        _labelStyle ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 15,
            normal = { textColor = Color.white }
        };
        var label = Possessive(OwnerName) + " Drone";
        GUI.Box(new Rect(point.x - 95f, point.y + 12f, 190f, 30f), label, _labelStyle);
    }

    private static string Possessive(string name)
    {
        var clean = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();
        return clean.EndsWith("s", System.StringComparison.OrdinalIgnoreCase) ? clean + "'" : clean + "'s";
    }

    private void SetHighlighted(bool value)
    {
        if (_highlighted == value) return;
        _highlighted = value;
        foreach (var entry in _highlightMaterials) entry.Set(value);
    }

    private sealed class HighlightMaterial
    {
        private readonly Material _material;
        private readonly bool _hasBaseColor;
        private readonly bool _hasColor;
        private readonly bool _hasEmission;
        private readonly Color _baseColor;
        private readonly Color _color;
        private readonly Color _emission;

        internal HighlightMaterial(Material material)
        {
            _material = material;
            _hasBaseColor = material.HasProperty("_BaseColor");
            _hasColor = material.HasProperty("_Color");
            _hasEmission = material.HasProperty("_EmissionColor");
            if (_hasBaseColor) _baseColor = material.GetColor("_BaseColor");
            if (_hasColor) _color = material.GetColor("_Color");
            if (_hasEmission) _emission = material.GetColor("_EmissionColor");
        }

        internal void Set(bool enabled)
        {
            if (_material == null) return;
            if (enabled)
            {
                var cyan = new Color(0.10f, 0.92f, 1f, 1f);
                if (_hasBaseColor) _material.SetColor("_BaseColor", Color.Lerp(_baseColor, cyan, 0.42f));
                if (_hasColor) _material.SetColor("_Color", Color.Lerp(_color, cyan, 0.42f));
                if (_hasEmission)
                {
                    _material.EnableKeyword("_EMISSION");
                    _material.SetColor("_EmissionColor", cyan * 1.8f);
                }
            }
            else
            {
                if (_hasBaseColor) _material.SetColor("_BaseColor", _baseColor);
                if (_hasColor) _material.SetColor("_Color", _color);
                if (_hasEmission) _material.SetColor("_EmissionColor", _emission);
            }
        }
    }
}
