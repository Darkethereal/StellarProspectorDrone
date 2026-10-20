// Prospector Drone — edited and maintained by Darkethereal.
using MelonLoader;
using SDModKit;
using SDModKit.Configuration;

namespace StellarProspectorDrone;

public sealed class Plugin : MelonMod
{
    public override void OnInitializeMelon()
    {
        ModKit.Register(new ProspectorMod(), ConfigFile.For("stellardrive.prospectordrone"));
    }
}
