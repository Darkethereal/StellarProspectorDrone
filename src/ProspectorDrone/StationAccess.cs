// Prospector Drone — edited and maintained by Darkethereal.
using System;
using System.Collections.Generic;
using System.Linq;
using Players;
using Players.Services;
using SDModKit.Content;
using SDModKit.Game;

namespace StellarProspectorDrone;

[Flags]
internal enum StationPermission
{
    None = 0,
    Upgrade = 1,
    TakeItems = 2
}

internal readonly struct StationPlayer
{
    internal readonly string SteamId;
    internal readonly string Name;

    internal StationPlayer(string steamId, string name)
    {
        SteamId = steamId;
        Name = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();
    }
}

internal static class StationAccess
{
    private const string OwnerIdKey = "ownerSteamId";
    private const string AccessKey = "accessList";

    internal static string SteamId(TrackedPlayerServer player) => player?.SteamUserId.ToString() ?? "";
    internal static string LocalSteamId => PlayerAccess.LocalNetworkState?.SteamUserId.ToString() ?? "";

    internal static void EnsureOwner(PartConfig config, TrackedPlayerServer player)
    {
        if (!string.IsNullOrWhiteSpace(config.GetString(OwnerIdKey, ""))) return;
        var id = player == null ? LocalSteamId : SteamId(player);
        if (string.IsNullOrWhiteSpace(id) || id == "0") return;
        config.Set(OwnerIdKey, id);
        var name = player?.Name ?? PlayerAccess.LocalNetworkState?.PlayerName;
        if (!string.IsNullOrWhiteSpace(name)) config.Set("ownerName", name.Trim());
    }

    internal static bool IsOwner(PartConfig config, string steamId)
    {
        var owner = config.GetString(OwnerIdKey, "");
        return !string.IsNullOrWhiteSpace(owner) && string.Equals(owner, steamId, StringComparison.Ordinal);
    }

    internal static bool CanOpen(PartConfig config, string steamId) =>
        IsOwner(config, steamId) || Permission(config, steamId) != StationPermission.None;

    internal static bool Has(PartConfig config, string steamId, StationPermission permission) =>
        IsOwner(config, steamId) || (Permission(config, steamId) & permission) != 0;

    internal static StationPermission Permission(PartConfig config, string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId)) return StationPermission.None;
        return Parse(config).TryGetValue(steamId, out var value) ? value.Permission : StationPermission.None;
    }

    internal static string SavedName(PartConfig config, string steamId) =>
        Parse(config).TryGetValue(steamId, out var value) ? value.Name : "Player";

    internal static void Set(PartConfig config, string steamId, string name, StationPermission permission, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(steamId) || IsOwner(config, steamId)) return;
        var entries = Parse(config);
        entries.TryGetValue(steamId, out var existing);
        var next = enabled ? existing.Permission | permission : existing.Permission & ~permission;
        if (next == StationPermission.None) entries.Remove(steamId);
        else entries[steamId] = (string.IsNullOrWhiteSpace(name) ? existing.Name ?? "Player" : name.Trim(), next);
        Save(config, entries);
    }

    internal static IReadOnlyList<StationPlayer> ConnectedPlayers()
    {
        var tracker = GameRefs.Service<PlayersServerTracker>();
        if (tracker?.ActivePlayers == null) return Array.Empty<StationPlayer>();
        return tracker.ActivePlayers
            .Where(player => player != null && player.SteamUserId != 0)
            .Select(player => new StationPlayer(player.SteamUserId.ToString(), player.Name))
            .GroupBy(player => player.SteamId)
            .Select(group => group.First())
            .OrderBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Dictionary<string, (string Name, StationPermission Permission)> Parse(PartConfig config)
    {
        var result = new Dictionary<string, (string, StationPermission)>(StringComparer.Ordinal);
        var raw = config.GetString(AccessKey, "");
        foreach (var record in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.Split('|');
            if (fields.Length != 3 || !int.TryParse(fields[2], out var flags)) continue;
            result[fields[0]] = (Uri.UnescapeDataString(fields[1]), (StationPermission)flags);
        }
        return result;
    }

    private static void Save(PartConfig config, Dictionary<string, (string Name, StationPermission Permission)> entries)
    {
        config.Set(AccessKey, string.Join(";", entries.OrderBy(pair => pair.Key).Select(pair =>
            pair.Key + "|" + Uri.EscapeDataString(pair.Value.Name ?? "Player") + "|" + (int)pair.Value.Permission)));
    }
}
