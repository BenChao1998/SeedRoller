using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace SeedRollerCli;

[HarmonyPatch(typeof(Logger), "GetIsRunningFromGodotEditor")]
internal static class LoggerGetIsRunningFromGodotEditorPatch
{
    // Prevent Logger from touching Godot.OS
    private static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(ConsoleLogPrinter), "Print")]
internal static class ConsoleLogPrinterPrintPatch
{
    private static bool Prefix()
    {
        // Skip Godot.GD.Print
        return false;
    }
}
