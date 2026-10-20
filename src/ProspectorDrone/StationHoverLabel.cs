// Prospector Drone — edited and maintained by Darkethereal.
using UnityEngine;

namespace StellarProspectorDrone;

internal sealed class StationHoverLabel : MonoBehaviour
{
    internal Vector3 StationPosition;
    internal string OwnerName;

    private void OnGUI()
    {
        var camera = Camera.main;
        if (camera == null) return;
        var point = camera.WorldToScreenPoint(StationPosition + Vector3.up * 1.2f);
        if (point.z <= 0f) return;
        var aim = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        var screen = new Vector2(point.x, Screen.height - point.y);
        if (Vector2.Distance(screen, aim) > 72f) return;
        var name = string.IsNullOrWhiteSpace(OwnerName) ? "Player" : OwnerName.Trim();
        var label = name.EndsWith("s", System.StringComparison.OrdinalIgnoreCase)
            ? name + "' Prospector Drone Station"
            : name + "'s Prospector Drone Station";
        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18,
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = new Color(0.25f, 0.92f, 1f, 1f);
        GUI.Label(new Rect(aim.x - 230f, aim.y + 42f, 460f, 34f), label, style);
    }
}
