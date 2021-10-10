using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace Automatons
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Mod : BaseUnityPlugin
    {
        private const string PluginGUID = "ca.gnivler.sheltered2.Automatons";
        private const string PluginName = "Automatons";
        private const string PluginVersion = "1.2.0";
        private static string LogFile;
        private static bool dev;

        internal static ConfigEntry<bool> Firefighting;
        internal static ConfigEntry<bool> Farming;
        internal static ConfigEntry<bool> Traps;
        internal static ConfigEntry<bool> Repair;
        internal static ConfigEntry<bool> Exercise;
        internal static ConfigEntry<bool> Reading;
        internal static ConfigEntry<int> FatigueThreshold;


        private void Awake()
        {
            try
            {
                Harmony harmony = new("ca.gnivler.sheltered2.Automatons");
                LogFile = Path.Combine(new FileInfo(Assembly.GetExecutingAssembly().Location).DirectoryName!, "log.txt");
                dev = SystemInfo.deviceName == "MEOWMEOW";
                Log("Automatons Startup");
                harmony.PatchAll(typeof(Patches));
                Firefighting = Config.Bind("Toggle Jobs", "Firefighting", true);
                Farming = Config.Bind("Toggle Jobs", "Farming", true);
                Traps = Config.Bind("Toggle Jobs", "Traps", true);
                Repair = Config.Bind("Toggle Jobs", "Repair", true);
                Exercise = Config.Bind("Toggle Jobs", "Exercise", true);
                Reading = Config.Bind("Toggle Jobs", "Reading", true);
                FatigueThreshold = Config.Bind("Adjustments", "Fatigue Threshold",
                    50, new ConfigDescription("Survivors won't exercise or read if too fatigued.", new AcceptableValueRange<int>(0, 100)));
            }
            catch (Exception ex)
            {
                FileLog.Log(ex.ToString());
            }
        }

        private void Update()
        {
            if (!dev)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                Patches.Cheat = !Patches.Cheat;
            }

            if (Input.GetKeyDown(KeyCode.F8))
            {
                MemberManager.instance.currentMembers.Do(m => m.member.needs.GetStatsList.Do(s => s.Set(0)));
            }
        }

        internal static void Log(object input)
        {
            if (dev)
            {
                File.AppendAllText(LogFile, $"{input ?? "null"}\n");
            }
        }
    }
}
