using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

class Program
{
    static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    static TypeDefinition Type(AssemblyDefinition assembly, string name) => assembly.MainModule.Types.Single(t => t.FullName == name);
    static MethodDefinition Method(TypeDefinition type, string name) => type.Methods.Single(m => m.Name == name);
    static int Calls(MethodDefinition method, string type, string name) => method.Body.Instructions.Count(i => i.Operand is MethodReference m && m.DeclaringType.FullName == type && m.Name == name);
    static int Main(string[] args)
    {
        using var mp = AssemblyDefinition.ReadAssembly(args[0]);
        using var game = AssemblyDefinition.ReadAssembly(args[1]);
        using var patch = AssemblyDefinition.ReadAssembly(args[2]);
        Check(Calls(Method(Type(mp, "Multiplayer.Client.DialogTradeCtorPatch"), "Prefix"), "Multiplayer.Client.MpTradeSession", "OpenWindow") == 2, "MP automatic constructor calls changed");
        Check(Calls(Method(Type(mp, "Multiplayer.Client.NodeTreeDialogSync"), "SyncDialogOptionByIndex"), "Verse.WindowStack", "Add") == 1, "MP encounter window call changed");
        Check(Calls(Method(Type(game, "RimWorld.Planet.CaravanArrivalAction_Trade"), "Arrived"), "Verse.CameraJumper", "TryJumpAndSelect") == 1, "arrival camera call changed");
        Check(Type(game, "RimWorld.Planet.CaravanArrivalAction_Trade").Fields.Any(f => f.Name == "settlement" && f.FieldType.FullName == "RimWorld.Planet.Settlement"), "arrival target field changed");
        Check(Type(game, "RimWorld.Planet.Caravan_PathFollower").Fields.Any(f => f.Name == "caravan" && f.FieldType.FullName == "RimWorld.Planet.Caravan"), "pather owner field changed");
        var p = Type(patch, "MP_MeowOnlineShop.Patch_TradeWindowAutoOpen");
        foreach (var m in p.Methods.Where(m => m.HasBody))
            foreach (var called in m.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>())
                Check(called.Name != "TryCreate" && called.Name != "TryExecute" && called.Name != "AddSession" && called.DeclaringType.FullName != "Verse.Rand", "presentation patch directly mutates trade/RNG: " + called);
        Check(Type(patch, "MP_MeowOnlineShop.MpMeowOnlineShopSettings").Fields.Any(f => f.Name == "tradeWindowOnlyForInitiator" && f.FieldType.FullName == "System.Boolean"), "setting missing");
        var expose = Method(Type(patch, "MP_MeowOnlineShop.MpMeowOnlineShopSettings"), "ExposeData");
        Check(expose.Body.Instructions.Any(i => Equals(i.Operand, "mp_meow_modcfg_trade_window_only_for_initiator")), "setting persistence missing");
        string result = "PASS: exact installed targets and call counts, serialized bool setting, presentation-only direct calls; candidate=" + patch.Name.Version;
        Console.WriteLine(result); File.WriteAllText(args[3], result + Environment.NewLine);
        return 0;
    }
}
