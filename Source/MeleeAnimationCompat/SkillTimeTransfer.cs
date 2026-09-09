using System;
using System.Reflection;
using System.Collections.Generic;
using AM.Idle;
using AM.UniqueSkills;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop.MeleeAnimation
{
    // Multiplayer visits a pawn's saved objects while converting its absolute
    // timestamps between map and world clocks. Skill cooldowns use that same clock.
    [HarmonyPatch]
    internal static class SkillTimeTransfer
    {
        private static FieldInfo offset;
        private static readonly AccessTools.FieldRef<IdleControllerComp,UniqueSkillInstance[]> savedSkills=
            AccessTools.FieldRefAccess<IdleControllerComp,UniqueSkillInstance[]>("skills");
        private static MethodBase TargetMethod()
        {
            var type=AccessTools.TypeByName("Multiplayer.Client.Patches.TimestampFixer");
            offset=AccessTools.Field(type,"currentOffset");
            var method=AccessTools.Method(type,"ProcessExposable",new[]{typeof(IExposable)});
            if(offset==null || offset.FieldType!=typeof(int?) || method==null)
                throw new InvalidOperationException("Multiplayer skill timestamp transfer target unavailable");
            return method;
        }
        private static void Postfix(IExposable exposable)
        {
            if(!Bootstrap.Active || !(exposable is Pawn pawn)) return;
            var delta=(int?)offset.GetValue(null);
            var comp=pawn.GetComp<IdleControllerComp>();
            if(!delta.HasValue || comp==null) return;
            var skills=savedSkills(comp);
            if(skills==null) return;
            // Visit existing instances even when this pawn no longer qualifies to
            // expose skills. Reading GetSkills here could create new instances.
            var seen=new HashSet<UniqueSkillInstance>();
            foreach(var skill in skills)
                if(skill!=null && seen.Add(skill)) skill.TickLastTriggered+=delta.Value;
        }
    }
}
