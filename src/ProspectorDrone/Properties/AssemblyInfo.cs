// Prospector Drone — edited and maintained by Darkethereal.
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using SDModKit.Api;
using StellarProspectorDrone;

[assembly: MelonInfo(typeof(Plugin), "Prospector Drone", "0.3.22", "Darkethereal")]
[assembly: MelonGame("CuriousOwlGames", "StellarDrive")]
[assembly: HarmonyDontPatchAll]
[assembly: StellarMod("prospector-drone", "Prospector Drone", Description = "Autonomous mining drone with selectable resources and upgrades.", HostOnly = true)]
[assembly: AssemblyTitle("StellarProspectorDrone")]
[assembly: AssemblyProduct("Prospector Drone")]
[assembly: AssemblyCompany("Darkethereal")]
[assembly: AssemblyCopyright("Copyright © 2026 Darkethereal")]
[assembly: AssemblyVersion("0.3.22.0")]
[assembly: AssemblyFileVersion("0.3.22.0")]
