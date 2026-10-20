// Prospector Drone — edited and maintained by Darkethereal.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Items.Model;
using SDModKit.Content;
using SDModKit.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StellarProspectorDrone;

internal static class ProspectorPanel
{
    private static bool _installed;

    internal static void Install(object _)
    {
        if (_installed) return;
        _installed = true;
        var harmony = new HarmonyLib.Harmony("stellardrive.prospector-drone.panel");
        var prefix = new HarmonyMethod(typeof(ProspectorPanel).GetMethod(nameof(BeforeOpen), BindingFlags.Static | BindingFlags.NonPublic));
        var postfix = new HarmonyMethod(typeof(ProspectorPanel).GetMethod(nameof(AfterOpen), BindingFlags.Static | BindingFlags.NonPublic));
        harmony.Patch(AccessTools.Method(typeof(ContentConfigMenu), nameof(ContentConfigMenu.TryAppend)), prefix: prefix, postfix: postfix);
        harmony.Patch(AccessTools.Method(typeof(ContentConfigMenu), nameof(ContentConfigMenu.TryAppendClient)), prefix: prefix, postfix: postfix);
    }

    private static bool BeforeOpen(ContentPart part, ref bool __result)
    {
        if (part.Def?.Id != 2110) return true;
        StationAccess.EnsureOwner(part.Config, null);
        if (StationAccess.CanOpen(part.Config, StationAccess.LocalSteamId)) return true;
        __result = false;
        return false;
    }

    private static void AfterOpen(ContentPart part, bool __result)
    {
        if (!__result || part.Def?.Id != 2110) return;
        var side = AccessTools.Field(typeof(ContentConfigMenu), "_side")?.GetValue(null) as GameObject;
        var layout = AccessTools.Field(typeof(ContentConfigMenu), "_layout")?.GetValue(null) as MenuLayout;
        var template = AccessTools.Field(typeof(ContentConfigMenu), "_template")?.GetValue(null) as Component;
        if (side == null || layout?.Grid == null || template == null) return;
        var view = side.GetComponent<ProspectorPanelView>() ?? side.AddComponent<ProspectorPanelView>();
        view.Initialize(part, layout.Grid, FindInventoryFrame(layout), template);
    }

    private static Transform FindInventoryFrame(MenuLayout layout)
    {
        var stop = layout.Menu == null ? null : layout.Menu.transform;
        var current = layout.Panel;
        while (current != null && current != stop)
        {
            if (current.GetComponent<Image>() != null &&
                current.GetComponentsInChildren<Component>(true).Any(MenuLayout.IsText))
                return current;
            current = current.parent;
        }
        return layout.Panel;
    }
}

internal sealed class ProspectorPanelView : MonoBehaviour
{
    private const float UiScale = 1.15f;
    private const float InventoryBaseDrop = 270f;
    private const float InventoryGapPixels = 16f;
    private static readonly Color Panel = new(0.035f, 0.10f, 0.15f, 0.96f);
    private static readonly Color Card = new(0.09f, 0.17f, 0.22f, 0.98f);
    private static readonly Color Accent = new(0.10f, 0.78f, 0.92f, 1f);
    private static readonly Color Gold = new(1f, 0.72f, 0.18f, 1f);
    private static readonly Color Green = new(0.35f, 0.78f, 0.25f, 1f);
    private static readonly Color Muted = new(0.35f, 0.40f, 0.43f, 1f);
    private static readonly Dictionary<uint, Texture2D> Icons = new();

    private ContentPart _part;
    private Transform _grid;
    private RectTransform _inventoryWindow;
    private Vector2 _inventoryOriginalPosition;
    private bool _inventoryMoved;
    private Component _template;
    private GameObject _customRoot;
    private string _fingerprint;
    private float _nextRefresh;
    private bool _showAccess;

    internal void Initialize(ContentPart part, Transform grid, Transform inventoryWindow, Component template)
    {
        _part = part;
        _grid = grid;
        _inventoryWindow = inventoryWindow as RectTransform;
        _template = template;
        _fingerprint = null;
        _nextRefresh = 0f;
        _showAccess = false;
        MoveInventoryDown();
    }

    private void MoveInventoryDown()
    {
        if (_inventoryMoved || _inventoryWindow == null) return;
        _inventoryOriginalPosition = _inventoryWindow.anchoredPosition;
        _inventoryWindow.anchoredPosition = _inventoryOriginalPosition + new Vector2(0f, -InventoryBaseDrop);
        _inventoryMoved = true;
    }

    private void OnDestroy()
    {
        if (_inventoryMoved && _inventoryWindow != null)
            _inventoryWindow.anchoredPosition = _inventoryOriginalPosition;
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 0.20f;
        if (_grid == null || _template == null) return;
        var fingerprint = Fingerprint();
        if (_customRoot != null && _customRoot.transform.parent == _grid && fingerprint == _fingerprint)
        {
            ResolveInventoryOverlap();
            return;
        }
        Build(fingerprint);
        ResolveInventoryOverlap();
    }

    private void ResolveInventoryOverlap()
    {
        if (_inventoryWindow == null || _customRoot == null || !_inventoryMoved) return;
        var panelRect = _customRoot.transform as RectTransform;
        if (panelRect == null) return;

        Canvas.ForceUpdateCanvases();
        var panelCorners = new Vector3[4];
        var inventoryCorners = new Vector3[4];
        panelRect.GetWorldCorners(panelCorners);
        _inventoryWindow.GetWorldCorners(inventoryCorners);

        var canvas = _inventoryWindow.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var panelBottom = RectTransformUtility.WorldToScreenPoint(camera, panelCorners[0]).y;
        var inventoryTop = RectTransformUtility.WorldToScreenPoint(camera, inventoryCorners[1]).y;
        var overlap = inventoryTop - panelBottom + InventoryGapPixels;
        if (overlap <= 0f) return;

        var scale = canvas == null ? 1f : Mathf.Max(0.01f, canvas.scaleFactor);
        _inventoryWindow.anchoredPosition += new Vector2(0f, -overlap / scale);
    }

    private void Build(string fingerprint)
    {
        foreach (Transform child in _grid.Cast<Transform>().ToArray())
            DestroyImmediate(child.gameObject);
        foreach (var layout in _grid.GetComponents<LayoutGroup>()) DestroyImmediate(layout);

        var vertical = _grid.gameObject.AddComponent<VerticalLayoutGroup>();
        vertical.padding = new RectOffset(ScaleInt(10), ScaleInt(10), ScaleInt(8), ScaleInt(8));
        vertical.spacing = Scale(7f);
        vertical.childAlignment = TextAnchor.UpperLeft;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;

        _customRoot = Box("Prospector Professional Panel", _grid, Panel, -1f);
        var rootLayout = _customRoot.AddComponent<VerticalLayoutGroup>();
        rootLayout.padding = new RectOffset(ScaleInt(10), ScaleInt(10), ScaleInt(8), ScaleInt(8));
        rootLayout.spacing = Scale(8f);
        rootLayout.childControlHeight = true;
        rootLayout.childControlWidth = true;
        rootLayout.childForceExpandHeight = false;
        rootLayout.childForceExpandWidth = true;

        var localId = StationAccess.LocalSteamId;
        var owner = StationAccess.IsOwner(_part.Config, localId);
        var canOpen = StationAccess.CanOpen(_part.Config, localId);
        var canTake = StationAccess.Has(_part.Config, localId, StationPermission.TakeItems);
        SetInventoryAccess(canTake);

        AddText(_customRoot.transform, "PROSPECTOR DRONE", 24, TextAlignmentOptions.Center, Color.white, 34f);
        AddText(_customRoot.transform, _part.Config.GetString("status", "Paused"), 15,
            TextAlignmentOptions.Center, Gold, 25f);

        if (!canOpen)
        {
            AddText(_customRoot.transform, "PRIVATE STATION", 25, TextAlignmentOptions.Center,
                new Color(1f, 0.35f, 0.28f), 60f);
            AddText(_customRoot.transform,
                "This Prospector Drone Station belongs to " + _part.Config.GetString("ownerName", "another player") + ".\nAsk the owner to grant access.",
                18, TextAlignmentOptions.Center, Color.white, 90f);
            _fingerprint = fingerprint;
            return;
        }

        var navigation = Row(_customRoot.transform, 48f, 8f);
        AddButton(navigation, "DRONE", _showAccess ? Muted : Accent, () => { _showAccess = false; _fingerprint = null; }, true, 170f, 44f, 16f);
        if (owner)
            AddButton(navigation, "ACCESS", _showAccess ? Accent : Muted, () => { _showAccess = true; _fingerprint = null; }, true, 170f, 44f, 16f);

        if (_showAccess && owner)
        {
            BuildAccess();
            _fingerprint = fingerprint;
            return;
        }

        var top = Row(_customRoot.transform, 252f, 12f);
        BuildControls(Box("Controls", top, Card, -1f).transform);
        BuildInventory(Box("Drone Inventory", top, Card, 350f).transform);

        AddText(_customRoot.transform, "UPGRADES", 16, TextAlignmentOptions.Left, new Color(1f, 1f, 1f, 0.7f), 23f);
        BuildUpgrade(UpgradeTrack.Flight, "FLIGHT SPEED", "flightLevel", level => Balance.FlightSpeed(level).ToString("0.0") + " m/s");
        BuildUpgrade(UpgradeTrack.Mining, "MINING SPEED", "miningLevel", level => (1f + level * 0.25f).ToString("0.00") + "x");
        BuildUpgrade(UpgradeTrack.Cargo, "DRONE STORAGE", "cargoLevel", level => Balance.CargoSlots(level) + " slots");
        BuildUpgrade(UpgradeTrack.Range, "OPERATING RANGE", "rangeLevel", level => Balance.Range(level).ToString("0") + " m");

        _fingerprint = fingerprint;
    }

    private void BuildAccess()
    {
        AddText(_customRoot.transform, "STATION ACCESS", 20, TextAlignmentOptions.Left, Color.white, 34f);
        AddText(_customRoot.transform,
            "Permissions are saved to each player's Steam ID. Upgrade allows station upgrades; Take Items allows inventory collection.",
            15, TextAlignmentOptions.Left, new Color(1f, 1f, 1f, 0.68f), 52f);
        var ownerId = StationAccess.LocalSteamId;
        var players = StationAccess.ConnectedPlayers().Where(player => player.SteamId != ownerId).ToArray();
        if (players.Length == 0)
        {
            AddText(_customRoot.transform, "No other players are currently in the game.", 17,
                TextAlignmentOptions.Center, Gold, 70f);
            return;
        }

        foreach (var player in players)
        {
            var permission = StationAccess.Permission(_part.Config, player.SteamId);
            var row = Box("Access " + player.SteamId, _customRoot.transform, Card, -1f);
            var horizontal = row.AddComponent<HorizontalLayoutGroup>();
            horizontal.padding = new RectOffset(ScaleInt(10), ScaleInt(10), ScaleInt(7), ScaleInt(7));
            horizontal.spacing = Scale(8f);
            horizontal.childAlignment = TextAnchor.MiddleLeft;
            horizontal.childControlWidth = true;
            horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = true;
            row.GetComponent<LayoutElement>().preferredHeight = Scale(68f);
            AddText(row.transform, player.Name + "\nSteam ID: " + player.SteamId, 16,
                TextAlignmentOptions.MidlineLeft, Color.white, 54f, 390f);
            AddAccessToggle(row.transform, player, StationPermission.Upgrade, "UPGRADE", (permission & StationPermission.Upgrade) != 0);
            AddAccessToggle(row.transform, player, StationPermission.TakeItems, "TAKE ITEMS", (permission & StationPermission.TakeItems) != 0);
        }
    }

    private void AddAccessToggle(Transform parent, StationPlayer player, StationPermission permission, string label, bool enabled)
    {
        var action = "access:" + player.SteamId + ":" + Uri.EscapeDataString(player.Name) + ":" + permission + ":" + (!enabled);
        AddButton(parent, label + (enabled ? "  ✓" : "  —"), enabled ? Green : Muted,
            () => ContentActions.Send(_part, action), true, 175f, 48f, 15f);
    }

    private void SetInventoryAccess(bool allowed)
    {
        if (_inventoryWindow == null) return;
        var group = _inventoryWindow.GetComponent<CanvasGroup>() ?? _inventoryWindow.gameObject.AddComponent<CanvasGroup>();
        group.interactable = allowed;
        group.blocksRaycasts = allowed;
        group.alpha = allowed ? 1f : 0.55f;
    }

    private void BuildControls(Transform parent)
    {
        ConfigureVertical(parent, 7f, 9);
        AddText(parent, "TARGET RESOURCE", 18, TextAlignmentOptions.Left, Color.white, 28f);
        var targets = Row(parent, 58f, 6f);
        var selected = ProspectorStationBehaviour.Selected(_part.Config);
        for (var i = 0; i < Balance.ResourceIds.Length; i++)
        {
            var item = Balance.ResourceIds[i];
            AddButton(targets, Balance.ResourceNames[i], item == selected ? Green : Muted,
                () => ContentActions.Send(_part, "resource:" + item), true, 112f, 52f, 16f);
        }
        var automation = Row(parent, 56f, 8f);
        var enabled = _part.Config.GetBool("enabled", false);
        var docked = _part.Config.GetBool("droneDocked", true);
        var storageBlocked = ProspectorStationBehaviour.StationRoomFor(_part, selected) <= 0;
        var automationLabel = enabled && storageBlocked ? "WAITING: STORAGE FULL" : enabled ? "DRONE ACTIVE" : docked ? "DRONE DOCKED" : "DOCKING";
        AddText(automation, automationLabel, 17, TextAlignmentOptions.MidlineLeft,
            enabled && !storageBlocked ? Green : Gold, 48f, 255f);
        AddButton(automation, enabled ? "DOCK" : storageBlocked ? "STORAGE FULL" : "START",
            enabled ? new Color(0.72f, 0.34f, 0.20f) : storageBlocked ? Muted : Accent,
            () => ContentActions.Send(_part, enabled ? "recall" : "toggle"), enabled || !storageBlocked, 150f, 48f, 16f);
        AddText(parent, "Cargo unloads into this station first, then automation resumes.", 15,
            TextAlignmentOptions.Left, new Color(1f, 1f, 1f, 0.62f), 35f);
    }

    private void BuildInventory(Transform parent)
    {
        ConfigureVertical(parent, 6f, 9);
        var unlocked = ProspectorStationBehaviour.CargoSlots(_part.Config);
        AddText(parent, "DRONE INVENTORY  " + ProspectorStationBehaviour.UsedCargoSlots(_part.Config) + " / " + unlocked + " SLOTS",
            18, TextAlignmentOptions.Left, Color.white, 28f);
        var slotGridObject = new GameObject("Drone Chest Slot Grid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
        slotGridObject.layer = parent.gameObject.layer;
        slotGridObject.transform.SetParent(parent, false);
        var slotGrid = slotGridObject.GetComponent<GridLayoutGroup>();
        slotGrid.cellSize = new Vector2(Scale(92f), Scale(92f));
        slotGrid.spacing = new Vector2(Scale(8f), Scale(8f));
        slotGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        slotGrid.constraintCount = 3;
        slotGrid.startAxis = GridLayoutGroup.Axis.Horizontal;
        slotGrid.childAlignment = TextAnchor.UpperCenter;
        slotGridObject.GetComponent<LayoutElement>().preferredHeight = Scale(192f);
        for (var slot = 0; slot < Balance.MaxCargoSlots; slot++)
        {
            var open = slot < unlocked;
            var quantity = ProspectorStationBehaviour.SlotQuantity(_part.Config, slot);
            var item = ProspectorStationBehaviour.SlotItem(_part.Config, slot);
            var cell = Box("Drone Slot " + (slot + 1), slotGridObject.transform,
                open ? new Color(0.12f, 0.27f, 0.35f) : new Color(0.07f, 0.09f, 0.11f), 92f);
            var cellElement = cell.GetComponent<LayoutElement>();
            cellElement.preferredWidth = Scale(92f);
            cellElement.preferredHeight = Scale(92f);
            cellElement.minWidth = Scale(92f);
            cellElement.minHeight = Scale(92f);
            ConfigureVertical(cell.transform, 1f, 4, false);
            if (!open)
            {
                AddText(cell.transform, "LOCKED", 14, TextAlignmentOptions.Center, Muted, 76f);
            }
            else if (quantity <= 0)
            {
                AddText(cell.transform, "EMPTY", 14, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.45f), 76f);
            }
            else
            {
                AddIcon(cell.transform, item, 42f);
                var canTake = StationAccess.Has(_part.Config, StationAccess.LocalSteamId, StationPermission.TakeItems);
                AddText(cell.transform, quantity + " / " + ItemMove.MaxStack(item) + (canTake ? "\nTAKE" : "\nNO ACCESS"),
                    13, TextAlignmentOptions.Center, canTake ? Color.white : Muted, 34f);
                if (canTake)
                {
                    var capturedSlot = slot;
                    var button = cell.AddComponent<Button>();
                    button.targetGraphic = cell.GetComponent<Image>();
                    button.onClick.AddListener(() => ContentActions.Send(_part, "take-slot:" + capturedSlot));
                }
            }
        }
    }

    private void BuildUpgrade(UpgradeTrack track, string title, string key, Func<int, string> value)
    {
        var level = ProspectorStationBehaviour.Level(_part.Config, key);
        var max = level >= Balance.MaxLevel;
        var card = Box(title, _customRoot.transform, Card, -1f);
        ConfigureVertical(card.transform, 3f, 7);
        var heading = Row(card.transform, 30f, 5f);
        AddText(heading, title, 18, TextAlignmentOptions.MidlineLeft, Color.white, 28f, 250f);
        AddText(heading, "LEVEL " + level + " / " + Balance.MaxLevel, 16, TextAlignmentOptions.MidlineRight, Gold, 28f, 145f);
        AddText(heading, max ? value(level) + "  MAX" : value(level) + "  →  " + value(level + 1), 16,
            TextAlignmentOptions.MidlineRight, new Color(0.65f, 0.91f, 1f), 25f, 220f);

        var bar = Row(card.transform, 12f, 3f);
        for (var i = 0; i < Balance.MaxLevel; i++) Box("Level " + (i + 1), bar, i < level ? Accent : new Color(0.18f, 0.22f, 0.24f), 47f);

        var bottom = Row(card.transform, 46f, 6f);
        if (max)
        {
            AddText(bottom, "All upgrades installed", 14, TextAlignmentOptions.MidlineLeft, Green, 35f, 500f);
            AddButton(bottom, "MAX", Muted, null, false, 110f);
            return;
        }

        var available = ProspectorStationBehaviour.CombinedAvailable(_part);
        var cost = Balance.Costs[track][level];
        var affordable = true;
        var docked = _part.Config.GetBool("droneDocked", true);
        var mayUpgrade = StationAccess.Has(_part.Config, StationAccess.LocalSteamId, StationPermission.Upgrade);
        foreach (var item in cost)
        {
            available.TryGetValue(item.ItemId, out var count);
            var enough = count >= item.Quantity;
            affordable &= enough;
            var costBox = Box("Cost " + item.ItemId, bottom, new Color(0f, 0f, 0f, 0.16f), 105f);
            var horizontal = costBox.AddComponent<HorizontalLayoutGroup>();
            horizontal.spacing = 3f;
            horizontal.childAlignment = TextAnchor.MiddleCenter;
            horizontal.childControlWidth = true;
            horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = false;
            AddIcon(costBox.transform, item.ItemId, 27f);
            AddText(costBox.transform, count + "/" + item.Quantity, 13, TextAlignmentOptions.Center,
                enough ? Color.white : new Color(1f, 0.42f, 0.36f), 31f, 66f);
        }
        var upgradeLabel = !mayUpgrade ? "NO ACCESS" : docked ? "UPGRADE" : "DOCK FIRST";
        AddButton(bottom, upgradeLabel, affordable && docked && mayUpgrade ? Green : Muted,
            () => ContentActions.Send(_part, "upgrade:" + track), affordable && docked && mayUpgrade, 150f, 42f, 16f);
    }

    private string Fingerprint()
    {
        var values = new List<string>
        {
            _part.Config.GetString("status", ""), _part.Config.GetBool("enabled", false).ToString(),
            _part.Config.GetBool("droneDocked", true).ToString(),
            ProspectorStationBehaviour.Selected(_part.Config).ToString()
        };
        values.Add(_part.Config.GetString("ownerSteamId", ""));
        values.Add(_part.Config.GetString("accessList", ""));
        values.Add(StationAccess.LocalSteamId);
        values.Add(_showAccess.ToString());
        foreach (var key in new[] { "flightLevel", "miningLevel", "cargoLevel", "rangeLevel" })
            values.Add(_part.Config.GetInt(key, 0).ToString());
        for (var slot = 0; slot < Balance.MaxCargoSlots; slot++)
            values.Add(ProspectorStationBehaviour.SlotItem(_part.Config, slot) + ":" + ProspectorStationBehaviour.SlotQuantity(_part.Config, slot));
        var counts = ProspectorStationBehaviour.CombinedAvailable(_part);
        foreach (var item in Balance.ResourceIds)
            values.Add(item + "=" + (counts.TryGetValue(item, out var count) ? count : 0));
        return string.Join("|", values);
    }

    private GameObject Box(string name, Transform parent, Color color, float width)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        var element = go.GetComponent<LayoutElement>();
        if (width > 0f) element.preferredWidth = Scale(width);
        return go;
    }

    private static Transform Row(Transform parent, float height, float spacing)
    {
        var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = Scale(spacing);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        go.GetComponent<LayoutElement>().preferredHeight = Scale(height);
        return go.transform;
    }

    private static void ConfigureVertical(Transform parent, float spacing, int padding, bool expandWidth = true)
    {
        var vertical = parent.gameObject.AddComponent<VerticalLayoutGroup>();
        var scaledPadding = ScaleInt(padding);
        vertical.padding = new RectOffset(scaledPadding, scaledPadding, scaledPadding, scaledPadding);
        vertical.spacing = Scale(spacing);
        vertical.childAlignment = TextAnchor.UpperLeft;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = expandWidth;
        vertical.childForceExpandHeight = false;
    }

    private TMP_Text AddText(Transform parent, string value, float size, TextAlignmentOptions alignment, Color color,
        float height, float width = -1f)
    {
        var source = _template as TMP_Text ?? _template.GetComponent<TMP_Text>() ?? _template.GetComponentInChildren<TMP_Text>(true);
        var clone = new GameObject("Text " + value, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
        clone.layer = parent.gameObject.layer;
        clone.transform.SetParent(parent, false);
        var text = clone.GetComponent<TextMeshProUGUI>();
        if (source != null)
        {
            text.font = source.font;
            text.fontSharedMaterial = source.fontSharedMaterial;
        }
        text.text = value;
        text.fontSize = Scale(size);
        text.enableAutoSizing = false;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        var element = clone.GetComponent<LayoutElement>();
        element.minWidth = 0f;
        element.flexibleWidth = width > 0f ? 0f : 1f;
        element.preferredHeight = Scale(height);
        if (width > 0f) element.preferredWidth = Scale(width);
        return text;
    }

    private void AddButton(Transform parent, string label, Color color, Action action, bool enabled, float width,
        float height = 38f, float fontSize = 14f)
    {
        var go = Box("Button " + label, parent, color, width);
        go.GetComponent<LayoutElement>().preferredHeight = Scale(height);
        var button = go.AddComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();
        button.interactable = enabled;
        if (enabled && action != null) button.onClick.AddListener(() => action());
        var text = AddText(go.transform, label, fontSize, TextAlignmentOptions.Center,
            enabled ? Color.white : new Color(1f, 1f, 1f, 0.42f), height);
        var rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void AddIcon(Transform parent, uint item, float size)
    {
        if (!Icons.TryGetValue(item, out var texture) || texture == null)
        {
            texture = ItemCatalog.Icon(item);
            Icons[item] = texture;
        }
        var go = new GameObject("Item Icon " + item, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(LayoutElement));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        go.GetComponent<RawImage>().texture = texture;
        var element = go.GetComponent<LayoutElement>();
        var scaledSize = Scale(size);
        element.minWidth = scaledSize;
        element.minHeight = scaledSize;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;
        element.preferredWidth = scaledSize;
        element.preferredHeight = scaledSize;
    }

    private static float Scale(float value) => value * UiScale;
    private static int ScaleInt(int value) => Mathf.RoundToInt(value * UiScale);
}
