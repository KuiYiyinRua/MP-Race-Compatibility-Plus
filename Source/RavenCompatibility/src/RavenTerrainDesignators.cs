using System;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
namespace MP_MeowOnlineShop
{
 internal static class RavenTerrainDesignators
 {
  private static Type drainType;
  internal static void Apply()
  {
   drainType=AccessTools.TypeByName("RavenRace.Features.RavenLiquidLake.Designator_DrainRavenLiquidLake");
   if(drainType==null||AccessTools.Constructor(drainType,new[]{typeof(TerrainDef)})==null)throw new MissingMemberException("Raven drain designator constructor");
   MP.RegisterSyncWorker<Designator_Build>(SyncDrain,drainType,false,false);
  }
  private static void SyncDrain(SyncWorker sync,ref Designator_Build value)
  {
   var terrain=sync.isWriting?value.PlacingDef as TerrainDef:null;sync.Bind(ref terrain);
   if(terrain?.defName!="Raven_Terrain_DrainRavenLiquidLake")throw new InvalidOperationException("Unexpected Raven drain terrain");
   if(!sync.isWriting)value=(Designator_Build)Activator.CreateInstance(drainType,new object[]{terrain});
   var rotationField=AccessTools.Field(typeof(Designator_Place),"placingRot");var rotation=(Rot4)rotationField.GetValue(value);sync.Bind(ref rotation);rotationField.SetValue(value,rotation);
   var field=AccessTools.Field(typeof(Designator_Build),"sourcePrecept");var precept=(Precept)field.GetValue(value);sync.Bind(ref precept);field.SetValue(value,precept);
  }
 }
}
