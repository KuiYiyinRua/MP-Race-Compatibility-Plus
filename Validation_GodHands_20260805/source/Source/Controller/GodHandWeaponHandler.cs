using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

namespace GodHandMod
{
    // 武器模式处理器
    public class GodHandWeaponHandler
    {
        private readonly GodHandController core;
        private bool shootingMode;
        private bool meleeMode;
        private Vector3 fixedWeaponPos;
        private bool waitForRelease;

        private int burstLeft;
        private int nextShotTick;
        private int cooldownTicks;
        private LocalTargetInfo burstTarget;
        private Verb activeVerb;

        private readonly HashSet<Thing> hitList = new HashSet<Thing>();

        public bool IsShootingMode => shootingMode;
        public bool IsMeleeMode => meleeMode;
        public Vector3 FixedWeaponPos => fixedWeaponPos;

        public GodHandWeaponHandler(GodHandController core) => this.core = core;

        public void ToggleShootingMode()
        {
            if (!GodHandModMain.Settings.godHandEnableShootingMode) return;
            if (GetActiveWeapon(false) == null) return;
            meleeMode = false;
            shootingMode = !shootingMode;
            if (shootingMode) { fixedWeaponPos = UI.MouseMapPosition(); waitForRelease = true; }
        }

        public void ToggleMeleeMode()
        {
            if (!GodHandModMain.Settings.godHandEnableMeleeMode) return;
            if (GetActiveWeapon(true) == null) return;
            shootingMode = false;
            meleeMode = !meleeMode;
            hitList.Clear();
        }

        public void Update(Map map, Vector3 mousePos)
        {
            if (meleeMode) UpdateMelee(map, mousePos);
            if (shootingMode) ProcessBurst(map);
        }

        private void UpdateMelee(Map map, Vector3 pos)
        {
            var weapon = GetActiveWeapon(true);
            if (weapon == null) return;
            Pawn target = GrabbingUtils.GetPawnAt(map, pos.ToIntVec3());
            if (target != null && !target.Dead && hitList.Add(target)) ApplyDamage(target, weapon);
            else if (target == null) hitList.Clear();
        }

        private void ApplyDamage(Pawn t, Thing w)
        {
            var v = w.TryGetComp<CompEquippable>()?.AllVerbs.FirstOrDefault(x => x.verbProps.IsMeleeAttack);
            if (v == null) return;
            t.TakeDamage(new DamageInfo(v.verbProps.meleeDamageDef ?? DamageDefOf.Blunt, v.verbProps.meleeDamageBaseAmount, 0, -1, w));
            SoundDefOf.Pawn_Melee_Punch_HitPawn.PlayOneShot(new TargetInfo(t.Position, t.Map));
        }

        private void ProcessBurst(Map map)
        {
            if (burstLeft <= 0 || Find.TickManager.TicksGame < nextShotTick) return;
            FireShot(map);
            burstLeft--;
            if (burstLeft > 0) nextShotTick = Find.TickManager.TicksGame + activeVerb.verbProps.ticksBetweenBurstShots;
            else cooldownTicks = Find.TickManager.TicksGame + (int)(activeVerb.verbProps.AdjustedCooldown(activeVerb, null) * 60);
        }

        private void FireShot(Map map)
        {
            Projectile p = (Projectile)GenSpawn.Spawn(activeVerb.verbProps.defaultProjectile, fixedWeaponPos.ToIntVec3(), map);
            p.Launch(core.GrabbedThings.FirstOrDefault(), fixedWeaponPos, burstTarget, burstTarget, ProjectileHitFlags.All);
            activeVerb.verbProps.soundCast?.PlayOneShot(new TargetInfo(fixedWeaponPos.ToIntVec3(), map));
        }

        public void TryShoot(Map map, IntVec3 target)
        {
            if (!shootingMode || waitForRelease || burstLeft > 0 || Find.TickManager.TicksGame < cooldownTicks) return;
            var v = GetActiveWeapon(false)?.TryGetComp<CompEquippable>()?.AllVerbs.FirstOrDefault(x => !x.verbProps.IsMeleeAttack);
            if (v == null) return;
            activeVerb = v; burstTarget = target; burstLeft = v.verbProps.burstShotCount; nextShotTick = Find.TickManager.TicksGame;
        }

        public void Draw() { if (shootingMode) GenDraw.DrawRadiusRing(fixedWeaponPos.ToIntVec3(), GetWeaponRange()); }

        public float GetWeaponRange() => GetActiveWeapon(false)?.TryGetComp<CompEquippable>()?.PrimaryVerb.verbProps.range ?? 0;
        public bool IsHoldingWeapon() => core.GrabbedThings.Any(t => t.def.IsWeapon);
        public bool IsHoldingMeleeWeapon() => GetActiveWeapon(true) != null;
        public bool IsHoldingRangedWeapon() => GetActiveWeapon(false) != null;
        public bool IsHoldingHybridWeapon() => IsHoldingMeleeWeapon() && IsHoldingRangedWeapon();

        private Thing GetActiveWeapon(bool melee) => core.GrabbedThings.FirstOrDefault(t => melee ? t.def.IsMeleeWeapon : t.def.IsRangedWeapon);

        public void ResetMouseRelease() => waitForRelease = false;
        public void ForceClear() { shootingMode = meleeMode = false; hitList.Clear(); }
    }
}
