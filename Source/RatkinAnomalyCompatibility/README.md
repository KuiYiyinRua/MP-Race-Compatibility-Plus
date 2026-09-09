# Ratkin compatibility module

`Meow.RatkinCompatibility.dll` loads from `1.6/Assemblies`. It discovers Anomaly and Underground types only when their package IDs are active. The existing core DLL is retained.

Build with .NET SDK and RimWorld 1.6 reference assemblies:

```powershell
dotnet build Source/RatkinAnomalyCompatibility/RatkinAnomalyCompatibility.csproj -c Release -p:GameRoot="<RimWorld directory>" -p:ModsRoot="<Mods directory>"
dotnet build Source/RatkinUndergroundCompatibility/RatkinUndergroundCompatibility.csproj -c Release -p:GameRoot="<RimWorld directory>" -p:ModsRoot="<Mods directory>"
```

The second DLL has a direct reference to Ratkin Underground. Install it under `RatkinUnderground/Assemblies`, with the conditional `LoadFolders.xml` entry and `RatkinUnderground/Defs/Letters.xml`. Do not place it in the unconditional assemblies folder.

Mod reference folders: Harmony `2009463077/Current`, Multiplayer `Multiplayer/1.6`, Underground `3613814532/1.6`. Override `ModsRoot` when building from a clone. No third-party DLL is redistributed.

See [implementation and runtime evidence](../../Docs/Ratkin-Expansions-Multiplayer.md).
