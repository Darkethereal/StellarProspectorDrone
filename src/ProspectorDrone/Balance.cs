// Prospector Drone — edited and maintained by Darkethereal.
using System.Collections.Generic;
using Items.Model;

namespace StellarProspectorDrone;

internal static class Balance
{
    internal const uint Iron = 101;
    internal const uint Glass = 102;
    internal const uint Wood = 110;
    internal const uint Copper = 112;

    internal static readonly uint[] ResourceIds = { Wood, Copper, Glass, Iron };
    internal static readonly string[] ResourceNames = { "Wood", "Copper", "Glass", "Iron" };

    internal const int MaxLevel = 10;
    internal const int MaxCargoSlots = 6;
    internal const float BaseFlightSpeed = 8f;
    internal const float BaseMiningDps = 4f;
    internal const float BaseRange = 100f;

    internal static float FlightSpeed(int level) => BaseFlightSpeed * (1f + 0.20f * level);
    internal static float MiningDps(int level) => BaseMiningDps * (1f + 0.25f * level);
    internal static int CargoSlots(int level) => System.Math.Min(MaxCargoSlots, 1 + (level + 1) / 2);
    internal static float Range(int level) => BaseRange + 50f * level;

    internal static readonly Dictionary<UpgradeTrack, ItemDrop[][]> Costs = new()
    {
        [UpgradeTrack.Flight] = Rows((20,10,0,0), (40,20,0,0), (65,35,10,0), (90,50,15,5), (120,70,20,8), (150,90,25,12), (180,110,30,16), (210,135,40,22), (235,165,50,30), (250,200,65,40)),
        [UpgradeTrack.Mining] = Rows((25,15,0,0), (50,30,0,0), (75,45,10,0), (105,65,15,5), (140,85,20,8), (170,105,25,12), (200,130,35,18), (225,155,45,24), (240,185,55,32), (250,220,70,45)),
        [UpgradeTrack.Cargo] = Rows((20,0,10,0), (35,15,15,0), (55,25,20,0), (75,35,25,5), (100,50,35,8), (130,65,45,12), (160,85,55,16), (190,110,70,22), (220,140,85,30), (250,175,100,40)),
        [UpgradeTrack.Range] = Rows((15,10,10,0), (35,20,15,0), (60,35,20,0), (85,50,30,5), (115,70,40,8), (145,90,50,12), (175,115,65,16), (205,140,80,22), (230,170,95,30), (250,205,115,40))
    };

    private static ItemDrop[][] Rows(params (int iron, int copper, int wood, int glass)[] rows)
    {
        var result = new ItemDrop[rows.Length][];
        for (var i = 0; i < rows.Length; i++)
        {
            var list = new List<ItemDrop>();
            if (rows[i].iron > 0) list.Add(Drop(Iron, rows[i].iron));
            if (rows[i].copper > 0) list.Add(Drop(Copper, rows[i].copper));
            if (rows[i].wood > 0) list.Add(Drop(Wood, rows[i].wood));
            if (rows[i].glass > 0) list.Add(Drop(Glass, rows[i].glass));
            result[i] = list.ToArray();
        }
        return result;
    }

    private static ItemDrop Drop(uint item, int count) => new() { ItemId = item, Quantity = (byte)count };
}

internal enum UpgradeTrack
{
    Flight,
    Mining,
    Cargo,
    Range
}
