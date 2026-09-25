using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Meow.RavenIndustryOptimization
{
    internal static class SchedulerCompaction
    {
        private static Action<object, int> process;

        internal static void Install(Harmony harmony)
        {
            Type scheduler = AccessTools.TypeByName("RavenRace.Features.IndustrialScheduling.MapComponent_RavenIndustrialScheduler") ?? throw new TypeLoadException("Industrial scheduler");
            FieldInfo buckets = AccessTools.Field(scheduler, "buckets"), states = AccessTools.Field(scheduler, "states");
            Type eventType = buckets.FieldType.GetElementType().GetGenericArguments()[0];
            Type[] stateTypes = states.FieldType.GetGenericArguments();
            Type worker = typeof(Worker<,,>).MakeGenericType(eventType, stateTypes[0], stateTypes[1]);
            worker.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { scheduler });
            var dm = new DynamicMethod("RavenIndustryProcessBucket", null, new[] { typeof(object), typeof(int) }, typeof(SchedulerCompaction).Module, true);
            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, scheduler); il.Emit(OpCodes.Ldfld, buckets);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, scheduler); il.Emit(OpCodes.Ldfld, states);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, worker.GetMethod("Process", BindingFlags.Static | BindingFlags.Public));
            il.Emit(OpCodes.Ret);
            process = (Action<object, int>)dm.CreateDelegate(typeof(Action<object, int>));
            var target = IndustryBootstrap.Method(scheduler, "ProcessBucket");
            IndustryBootstrap.RequireUnpatched(target);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(SchedulerCompaction), nameof(BeforeBucket)));
        }

        private static bool BeforeBucket(object __instance, int currentTick)
        {
            if (!IndustrySettings.Active) return true;
            process(__instance, currentTick);
            return false;
        }

        // Concrete private Raven types are bound once at startup. The hot loop uses
        // typed dictionaries/delegates: no FieldInfo.GetValue, boxing or Invoke.
        private static class Worker<TEvent, TMachine, TState> where TMachine : class where TState : class
        {
            private static Func<TEvent, TMachine> machine;
            private static Func<TEvent, int> due, revision;
            private static Func<TState, int> stateDue, stateRevision;
            private static Action<TState, int> setDue;
            private static Func<TMachine, bool> valid;
            private static Action<TMachine, int> execute;
            private static Action<object, TMachine> deregister;

            public static void Initialize(Type scheduler)
            {
                machine = IndustryBootstrap.Getter<TEvent, TMachine>(AccessTools.Field(typeof(TEvent), "Machine"));
                due = IndustryBootstrap.Getter<TEvent, int>(AccessTools.Field(typeof(TEvent), "DueTick"));
                revision = IndustryBootstrap.Getter<TEvent, int>(AccessTools.Field(typeof(TEvent), "Revision"));
                stateDue = IndustryBootstrap.Getter<TState, int>(AccessTools.Field(typeof(TState), "DueTick"));
                stateRevision = IndustryBootstrap.Getter<TState, int>(AccessTools.Field(typeof(TState), "Revision"));
                var setter = new DynamicMethod("RavenIndustrySetDue", null, new[] { typeof(TState), typeof(int) }, typeof(SchedulerCompaction).Module, true);
                var il = setter.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, AccessTools.Field(typeof(TState), "DueTick")); il.Emit(OpCodes.Ret);
                setDue = (Action<TState, int>)setter.CreateDelegate(typeof(Action<TState, int>));
                valid = BindInstance<Func<TMachine, bool>>(AccessTools.PropertyGetter(typeof(TMachine), "IsScheduledMachineValid"), typeof(TMachine), typeof(bool));
                execute = BindInstance<Action<TMachine, int>>(IndustryBootstrap.Method(typeof(TMachine), "ProcessScheduledIndustrialTick"), typeof(TMachine), typeof(void), typeof(int));
                deregister = BindInstance<Action<object, TMachine>>(IndustryBootstrap.Method(scheduler, "Deregister"), typeof(object), typeof(void), typeof(TMachine));
            }

            private static TDelegate BindInstance<TDelegate>(MethodInfo method, Type target, Type result, params Type[] arguments) where TDelegate : class
            {
                var signature = new Type[arguments.Length + 1]; signature[0] = target;
                Array.Copy(arguments, 0, signature, 1, arguments.Length);
                var dm = new DynamicMethod("RavenIndustryCall_" + method.Name, result, signature, typeof(SchedulerCompaction).Module, true);
                var il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                if (target != method.DeclaringType) il.Emit(OpCodes.Castclass, method.DeclaringType);
                for (int i = 1; i < signature.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
                il.Emit(OpCodes.Callvirt, method); il.Emit(OpCodes.Ret);
                return dm.CreateDelegate(typeof(TDelegate)) as TDelegate;
            }

            public static void Process(object scheduler, List<TEvent>[] buckets, Dictionary<TMachine, TState> states, int tick)
            {
                var list = buckets[tick & 511];
                int write = 0, count = list.Count;
                for (int i = 0; i < count; i++)
                {
                    TEvent item = list[i];
                    TMachine target = machine(item);
                    int itemDue = due(item);
                    // Difference from Raven: invalidate BEFORE carrying a future event.
                    // Execution order and all live events remain unchanged.
                    if (target == null || !states.TryGetValue(target, out TState state) ||
                        stateRevision(state) != revision(item) || stateDue(state) != itemDue) continue;
                    if (itemDue > tick) { list[write++] = item; continue; }
                    setDue(state, -1);
                    if (!valid(target)) deregister(scheduler, target);
                    else execute(target, tick);
                }
                // Callbacks may append to THIS bucket. Preserve the original suffix
                // and its order instead of processing newly scheduled work too early.
                int appended = list.Count - count;
                if (write < count && appended > 0)
                {
                    for (int i = 0; i < appended; i++) list[write + i] = list[count + i];
                    write += appended;
                }
                if (write < list.Count) list.RemoveRange(write, list.Count - write);
            }
        }
    }
}
