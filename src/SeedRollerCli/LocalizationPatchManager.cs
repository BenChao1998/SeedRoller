using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace SeedRollerCli;

internal static class LocalizationPatchManager
{
    private static bool _patched;
    private static Harmony? _harmony;

    public static void EnsurePatched()
    {
        if (_patched)
        {
            return;
        }

        _harmony = new Harmony("seedroller.localization");
        _harmony.PatchAll(typeof(LocalizationPatchManager).Assembly);
        LocalizationStub.Initialize();
        _patched = true;
    }

    private static class LocalizationStub
    {
        public static void Initialize()
        {
            var managerType = typeof(LocManager);
            var instanceProperty = managerType.GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (instanceProperty == null)
            {
                return;
            }

            var existing = instanceProperty.GetValue(null);
            if (existing != null)
            {
                return;
            }

            var stub = FormatterServices.GetUninitializedObject(managerType);
            SetField(stub, "_tables", new Dictionary<string, LocTable>(StringComparer.OrdinalIgnoreCase));
            SetField(stub, "_engTables", new Dictionary<string, LocTable>(StringComparer.OrdinalIgnoreCase));
            SetField(stub, "_stateBeforeOverridingWithEnglish", null);
            SetField(stub, "_languageKeyCount", new Dictionary<string, int>());
            SetField(stub, "_localeChangeCallbacks", new List<LocManager.LocaleChangeCallback>());

            SetProperty(stub, "OverridesActive", false);
            SetProperty(stub, "ValidationErrors", Array.Empty<LocValidationError>());
            SetProperty(stub, "Language", "eng");
            SetProperty(stub, "CultureInfo", CultureInfo.GetCultureInfo("en-US"));

            instanceProperty.SetValue(null, stub);
        }

        private static void SetField(object target, string name, object? value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            field?.SetValue(target, value);
        }

        private static void SetProperty(object target, string name, object? value)
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            property?.SetValue(target, value);
        }
    }

    [HarmonyPatch(typeof(LocManager), "GetTable")]
    private static class LocManagerGetTablePatch
    {
        private static readonly FieldInfo TablesField =
            typeof(LocManager).GetField("_tables", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Harmony convention
        // ReSharper disable once InconsistentNaming
        private static bool Prefix(LocManager __instance, string name, ref LocTable __result)
        {
            var tables = (Dictionary<string, LocTable>)TablesField.GetValue(__instance)!;
            if (!tables.TryGetValue(name, out var table))
            {
                table = new LocTable(name, new Dictionary<string, string>(), null);
                tables[name] = table;
            }

            __result = table;
            return false;
        }
    }

    [HarmonyPatch(typeof(LocString), "GetFormattedText")]
    private static class LocStringGetFormattedTextPatch
    {
        private static bool Prefix(LocString __instance, ref string __result)
        {
            __result = __instance.GetRawText();
            return false;
        }
    }

    [HarmonyPatch(typeof(LocTable), "GetRawText")]
    private static class LocTableGetRawTextPatch
    {
        private static readonly FieldInfo TranslationsField =
            typeof(LocTable).GetField("_translations", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static bool Prefix(LocTable __instance, string key, ref string __result)
        {
            var translations = (Dictionary<string, string>)TranslationsField.GetValue(__instance)!;
            if (translations.TryGetValue(key, out var value))
            {
                __result = value;
                return false;
            }

            __result = key;
            return false;
        }
    }

    [HarmonyPatch(typeof(LocTable), "HasEntry")]
    private static class LocTableHasEntryPatch
    {
        private static bool Prefix(ref bool __result)
        {
            __result = true;
            return false;
        }
    }
}
