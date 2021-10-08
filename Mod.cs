using System;
using System.IO;
using BepInEx;
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
        private const string PluginVersion = "1.1.1";

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

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F7))
            {
                MemberManager.instance.GetAllShelteredMembers().Do(m => m.member.jobQueue.Clear());
                MemberManager.instance.GetAllShelteredMembers().Do(m => m.member.aiQueue.Clear());
                MemberManager.instance.GetAllShelteredMembers().Do(m => m.member.currentQueue.Clear());
                MemberManager.instance.GetAllShelteredMembers().Do(m => m.member.currentjob = null);
            }
        }
    }
}
