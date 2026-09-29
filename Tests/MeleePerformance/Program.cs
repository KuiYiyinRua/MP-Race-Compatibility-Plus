using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using AM;
using AM.AMSettings;
using AM.Idle;
using AM.UniqueSkills;
using HarmonyLib;
using MP_MeowOnlineShop.MeleeAnimation;
using Verse;

// Offline contract/performance tests. Game/AM/MP are stubs; Harmony is real.
// These tests do not claim Unity or host/client execution coverage.
static class Program
{
    static int failures;
    static void Check(bool ok, string name)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) failures++;
    }
    static Game NewGame(bool enabled)
    {
        var game = new Game();
        game.Component = new MeleeSessionState(game);
        game.Component.Rules.EnableUniqueSkills = enabled;
        Current.Game = game;
        return game;
    }
    static int Main()
    {
        new Harmony("melee.performance.offline").CreateClassProcessor(typeof(InitializeSkills)).Patch();
        Bootstrap.Active = true;
        Core.Settings = new Settings { EnableUniqueSkills = true };
        var first = NewGame(false);
        for (int i = 0; i < 10000; i++) MeleeSessionState.CurrentRules();
        Check(first.Lookups == 1, "10000 rule reads perform one component lookup (actual=" + first.Lookups + ")");
        var replacement = new Settings { EnableUniqueSkills = false };
        first.Component.Rules = replacement;
        Check(ReferenceEquals(MeleeSessionState.CurrentRules(), replacement), "rule replacement visible immediately");
        var second = NewGame(true);
        Check(ReferenceEquals(MeleeSessionState.CurrentRules(), second.Component.Rules), "new game uses own rules");
        Current.Game = first;
        Check(ReferenceEquals(MeleeSessionState.CurrentRules(), replacement), "MP game swap restores correct rules");
        replacement = new Settings { EnableUniqueSkills = false };
        first.Component = new MeleeSessionState(first) { Rules = replacement };
        first.Component.LoadedGame();
        Check(ReferenceEquals(MeleeSessionState.CurrentRules(), first.Component.Rules), "same-game load refreshes component cache");
        Bootstrap.Active = false;
        Check(ReferenceEquals(MeleeSessionState.CurrentRules(), Core.Settings), "singleplayer uses live local settings");
        Bootstrap.Active = true;
        Current.Game = null;
        Check(ReferenceEquals(MeleeSessionState.CurrentRules(), Core.Settings), "no game falls back safely");
        var constructing = new Game();
        Current.Game = constructing;
        MeleeSessionState.CurrentRules();
        constructing.Component = new MeleeSessionState(constructing);
        Check(ReferenceEquals(MeleeSessionState.CurrentRules(), constructing.Component.Rules), "construction miss does not poison cache");
        Current.Game = first;
        var idle = new IdleControllerComp();
        for (int i = 0; i < 10000; i++) idle.CompTick();
        Check(idle.Calls == 0, "disabled skills: zero GetSkills calls (actual=" + idle.Calls + ")");
        replacement.EnableUniqueSkills = true;
        idle.CompTick();
        Check(idle.Skills != null && idle.Ticks == 1, "eligible skills initialized before first skill tick");
        int calls = idle.Calls;
        for (int i = 0; i < 10000; i++) idle.CompTick();
        Check(idle.Calls == calls && idle.Ticks == 10001, "initialized skills keep ticking without GetSkills calls");
        var recruit = new IdleControllerComp { Eligible = false };
        recruit.CompTick();
        Check(recruit.Skills == null, "ineligible pawn remains uninitialized");
        recruit.Eligible = true;
        recruit.CompTick();
        Check(recruit.Skills != null && recruit.Ticks == 1, "recruitment initializes on same eligible tick");
        var loaded = new IdleControllerComp();
        loaded.LoadSkills();
        loaded.CompTick();
        Check(loaded.Calls == 0 && loaded.Ticks == 1, "loaded skills preserved and ticked");
        Bootstrap.Active = false;
        var single = new IdleControllerComp();
        single.CompTick();
        Check(single.Calls == 0, "singleplayer initialization left to original mod");
        Bootstrap.Active = true;
        replacement.EnableUniqueSkills = false;
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < 1000000; i++) { MeleeSessionState.CurrentRules(); idle.CompTick(); }
        timer.Stop();
        Console.WriteLine("OFFLINE 1000000 warm rules+initialized ticks ms=" + timer.Elapsed.TotalMilliseconds);
        Console.WriteLine("Failures=" + failures);
        return failures == 0 ? 0 : 1;
    }
}
namespace MP_MeowOnlineShop.MeleeAnimation { static class Bootstrap { internal static bool Active; } }
namespace Verse
{
    public class Game
    {
        public MeleeSessionState Component;
        public int Lookups;
        // Model the existing linear component search with 100 unrelated entries.
        readonly List<object> components = new List<object>(new object[100]);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public T GetComponent<T>() where T : class
        {
            Lookups++;
            foreach (var item in components) if (item is T value) return value;
            return Component as T;
        }
    }
    public static class Current { public static Game Game; }
    public class GameComponent { public virtual void ExposeData() {} public virtual void LoadedGame() {} }
    public enum LoadSaveMode { Inactive, PostLoadInit }
    public static class Scribe { public static LoadSaveMode mode; }
    public static class Scribe_Deep { public static void Look<T>(ref T value, string label) {} }
}
namespace AM.AMSettings
{
    public class Settings
    {
        public bool EnableUniqueSkills;
        private Dictionary<string, AnimDef.SettingsData> animSettings = new Dictionary<string, AnimDef.SettingsData>();
    }
}
namespace AM
{
    public static class Core { public static Settings Settings; }
    public class AnimDef { public class SettingsData { public bool Enabled; public float Probability; } }
}
namespace AM.UniqueSkills { public class UniqueSkillInstance {} }
namespace AM.Idle
{
    public class IdleControllerComp
    {
        private UniqueSkillInstance[] skills;
        public UniqueSkillInstance[] Skills => skills;
        public bool Eligible = true;
        public int Calls, Ticks;
        public void LoadSkills() { skills = new[] { new UniqueSkillInstance() }; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public IReadOnlyList<UniqueSkillInstance> GetSkills()
        {
            Calls++;
            if (skills == null)
            {
                if (!MeleeSessionState.CurrentRules().EnableUniqueSkills || !Eligible)
                    return Array.Empty<UniqueSkillInstance>();
                LoadSkills();
            }
            return skills;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void CompTick() { if (skills != null) Ticks++; }
    }
}
