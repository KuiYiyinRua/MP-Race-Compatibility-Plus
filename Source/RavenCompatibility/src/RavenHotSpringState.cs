using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;
namespace MP_MeowOnlineShop
{
 public sealed class RavenHotSpringState : MapComponent
 {
  private static Type trackerType;
  private static FieldInfo countField,dirtyField,bathingField;
  private List<Pawn> bathing=new List<Pawn>();
  private int count;
  private bool dirty=true;
  public RavenHotSpringState(Map map):base(map){}
  internal static void Validate()
  {
   trackerType=AccessTools.TypeByName("RavenRace.Features.HotSpring.MapComponent_HotSpringTracker")??throw new TypeLoadException("Raven hot spring tracker");
   countField=AccessTools.DeclaredField(trackerType,"cachedHotSpringCellsCount")??throw new MissingFieldException(trackerType.FullName,"cachedHotSpringCellsCount");
   dirtyField=AccessTools.DeclaredField(trackerType,"isDirty")??throw new MissingFieldException(trackerType.FullName,"isDirty");
   var bathType=AccessTools.TypeByName("RavenRace.Features.HotSpring.RavenLiquidHotSpringBathState")??throw new TypeLoadException("Raven hot spring bath state");
   bathingField=AccessTools.DeclaredField(bathType,"BathingPawns")??throw new MissingFieldException(bathType.FullName,"BathingPawns");
  }
  public override void ExposeData()
  {
   base.ExposeData();
   if(trackerType==null)return;
   var tracker=map.components.FirstOrDefault(c=>c.GetType()==trackerType);
   if(Scribe.mode==LoadSaveMode.Saving&&tracker!=null)
   {
    count=(int)countField.GetValue(tracker);dirty=(bool)dirtyField.GetValue(tracker);
    bathing=((HashSet<Pawn>)bathingField.GetValue(null)).Where(p=>p!=null&&p.Map==map).OrderBy(p=>p.thingIDNumber).ToList();
   }
   Scribe_Values.Look(ref count,"ravenMpHotSpringCellCount",0);
   Scribe_Values.Look(ref dirty,"ravenMpHotSpringDirty",true);
   Scribe_Collections.Look(ref bathing,"ravenMpHotSpringBathing",LookMode.Reference);
   if(Scribe.mode==LoadSaveMode.PostLoadInit&&tracker!=null)
   {
    countField.SetValue(tracker,count);dirtyField.SetValue(tracker,dirty);
    var native=(HashSet<Pawn>)bathingField.GetValue(null);
    native.RemoveWhere(p=>p==null||p.Map==map);
    // Pawn spawn registration can finish after MapComponent.PostLoadInit.
    // The owning map was already selected when writing this reference list.
    if(bathing!=null)foreach(var pawn in bathing)if(pawn!=null)native.Add(pawn);
   }
  }
 }
}
