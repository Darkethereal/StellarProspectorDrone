# Building Prospector Drone

**Project edited and maintained by Darkethereal.**

Requirements:

- .NET SDK capable of building `net472` projects.
- A local StellarDrive Demo installation with MelonLoader.
- SDModKit installed through Stellar Mod Manager.

Set `STELLARDRIVE_GAME_DIR` to the game directory, or pass `GameDir` explicitly:

```powershell
dotnet build .\src\ProspectorDrone\ProspectorDrone.csproj -c Release /p:GameDir="E:\SteamLibrary\steamapps\common\StellarDrive Demo"
```

The public repository intentionally does not include StellarDrive, Unity, MelonLoader, or SDModKit dependency DLLs.

