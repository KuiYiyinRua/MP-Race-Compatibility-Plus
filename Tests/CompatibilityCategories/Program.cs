using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using MP_MeowOnlineShop;

namespace Verse
{
    public static class Log { public static void Message(string text) { } }
    public sealed class Listing_Standard
    {
        public bool Restore;
        public readonly List<string> Labels = new List<string>();
        public void Label(string text) { Labels.Add(text); }
        public void CheckboxLabeled(string text, ref bool value) { }
        public bool ButtonText(string text) { return Restore; }
        public void GapLine() { }
    }
}
namespace MP_MeowOnlineShop
{
    public sealed class MpMeowOnlineShopSettings
    {
        public bool compatibilityPatchesEnabled = true;
        public List<string> disabledCompatibilityCategories = new List<string>();
    }
}
internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }
    private static void Fresh(MpMeowOnlineShopSettings settings)
    {
        typeof(CompatibilityPatchCategories).GetField("disabledAtStartup", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        CompatibilityPatchCategories.Capture(settings);
    }
    private static void Main(string[] args)
    {
        var ids = (string[])typeof(CompatibilityPatchCategories).GetField("Ids", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        Check(ids.Length == 17 && ids.Distinct().Count() == ids.Length, "Unique complete categories");
        Fresh(new MpMeowOnlineShopSettings());
        Check(ids.All(CompatibilityPatchCategories.IsEnabled), "Existing/default configs keep all categories enabled");
        foreach (string disabled in ids)
        {
            var settings = new MpMeowOnlineShopSettings();
            settings.disabledCompatibilityCategories.Add(disabled);
            Fresh(settings);
            foreach (string id in ids)
            {
                int calls = 0;
                CompatibilityPatchCategories.Apply(id, () => calls++);
                Check(calls == (id == disabled ? 0 : 1), "Independent registration gate: " + disabled + "/" + id);
            }
            settings.disabledCompatibilityCategories.Clear();
            settings.compatibilityPatchesEnabled = false;
            CompatibilityPatchCategories.Capture(settings);
            Check(!CompatibilityPatchCategories.IsEnabled(disabled), "Snapshot must not be recaptured");
            Check(ids.Where(id => id != disabled).All(CompatibilityPatchCategories.IsEnabled), "UI changes cannot alter running registrations");
            var listing = new Verse.Listing_Standard();
            CompatibilityPatchCategories.DrawSettings(listing, settings);
            Check(listing.Labels.Any(s => s.StartsWith("设置已更改")), "Pending restart is visible");
        }
        var masterOff = new MpMeowOnlineShopSettings { compatibilityPatchesEnabled = false };
        masterOff.disabledCompatibilityCategories.Add("rjw");
        Fresh(masterOff);
        foreach (string id in ids)
        {
            CompatibilityPatchCategories.Apply(id, () => { throw new Exception("Master gate leaked " + id); });
            Check(!CompatibilityPatchCategories.IsEnabled(id), "Master disables " + id);
        }
        var restore = new Verse.Listing_Standard { Restore = true };
        CompatibilityPatchCategories.DrawSettings(restore, masterOff);
        Check(masterOff.compatibilityPatchesEnabled && masterOff.disabledCompatibilityCategories.Count == 0, "Restore changes saved preferences");
        Check(ids.All(id => !CompatibilityPatchCategories.IsEnabled(id)), "Restore does not hot-install patches");
        Fresh(masterOff);
        Check(ids.All(CompatibilityPatchCategories.IsEnabled), "New startup applies restored preferences");
        bool rejected = false;
        try { CompatibilityPatchCategories.IsEnabled("typo"); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Unknown categories fail explicitly");
        if (args.Length == 1)
        {
            var files = Directory.GetFiles(args[0], "*.dll", SearchOption.AllDirectories);
            int verified = 0;
            foreach (var file in files)
            {
                using var stream = File.OpenRead(file);
                using var pe = new PEReader(stream);
                var metadata = pe.GetMetadataReader();
                string assembly = metadata.GetString(metadata.GetAssemblyDefinition().Name);
                if (assembly == "MP_MeowOnlineShop")
                {
                    Check(metadata.TypeDefinitions.Any(h => metadata.GetString(metadata.GetTypeDefinition(h).Name) == "CompatibilityPatchCategories"), "Main binary contains category gate");
                    Check(metadata.FieldDefinitions.Any(h => metadata.GetString(metadata.GetFieldDefinition(h).Name) == "disabledCompatibilityCategories"), "Main binary contains persisted categories");
                }
                else
                {
                    bool found = metadata.MemberReferences.Any(h =>
                    {
                        var member = metadata.GetMemberReference(h);
                        if (metadata.GetString(member.Name) != "IsEnabled" || member.Parent.Kind != HandleKind.TypeReference) return false;
                        return metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)member.Parent).Name) == "CompatibilityPatchCategories";
                    });
                    Check(found, "Companion binary references category gate: " + assembly);
                }
                verified++;
            }
            Check(verified == 15, "Expected 15 release assemblies, found " + verified);
            Console.WriteLine("PASS: metadata checked for " + verified + " release assemblies.");
        }
        Console.WriteLine("PASS: " + assertions + " assertions; 17 independent categories, master gate, startup snapshot and restart UI.");
    }
}
