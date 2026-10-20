// Prospector Drone — edited and maintained by Darkethereal.
using System.Reflection;
using SDModKit.Api;
using SDModKit.Content;

namespace StellarProspectorDrone;

public sealed class ProspectorMod : SDMod
{
    public override string Id => "prospector-drone";
    public override string Name => "Prospector Drone";
    public override bool HostOnly => true;

    public override void OnRegistered()
    {
        ProspectorPanel.Install(Log);
        DroneYieldRouter.Install();
        ContentRegistry.Behaviour("prospector-station", () => new ProspectorStationBehaviour());
        var pack = ContentRegistry.LoadEmbedded(Assembly.GetExecutingAssembly(), "prospector-content.json");
        Log.Info($"[ProspectorDrone] Content registered: {pack?.Parts.Count ?? 0} station part, {pack?.Research.Count ?? 0} research unlock.");
        if (GlbModelLoader.TryPrewarm(out var modelError))
            Log.Info("[ProspectorDrone] Mining Drone Mk VII model and textures loaded.");
        else
            Log.Warn("[ProspectorDrone] Mining Drone Mk VII preflight failed; the procedural fallback will be used. " + modelError);
    }

    public override void OnWorldEnter()
    {
        StationVisuals.Install(Log);
    }
}
