using System.IO;
using BepInEx;
using HarmonyLib;

// ReSharper disable InconsistentNaming

namespace Automatons
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Mod : BaseUnityPlugin
    {
        private const string PluginGUID = "ca.gnivler.sheltered2.Automatons";
        private const string PluginName = "Automatons";
        private const string PluginVersion = "1.0.1";

        private void Awake()
        {
            Harmony harmony = new("ca.gnivler.sheltered2.Automatons");
            Log("Automatons Startup");
            harmony.PatchAll(typeof(Patches));
        }

        internal static void Log(object input)
        {
            File.AppendAllText("log.txt", $"{input ?? "null"}\n");
        }
    }
}
