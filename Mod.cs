using System;
using System.Collections.Generic;
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
        private const string PluginVersion = "1.2.2";
        private static string LogFile;
        private static bool dev;

        internal static ConfigEntry<bool> Firefighting;
        internal static ConfigEntry<bool> Farming;
        internal static ConfigEntry<bool> Traps;
        internal static ConfigEntry<bool> Repair;
        internal static ConfigEntry<bool> Exercise;
        internal static ConfigEntry<bool> Reading;
        internal static ConfigEntry<int> FatigueThreshold;
        private static ConfigEntry<KeyboardShortcut> ToggleSingle;
        private static ConfigEntry<KeyboardShortcut> ToggleAll;


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
                ToggleSingle = Config.Bind("Hotkey", "Toggle automation for selected survivor.", new KeyboardShortcut(KeyCode.Comma));
                ToggleAll = Config.Bind("Hotkey", "Toggle automation for all survivors.", new KeyboardShortcut(KeyCode.Period));
            }
            catch (Exception ex)
            {
                FileLog.Log(ex.ToString());
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(ToggleSingle.Value.MainKey)
                && ToggleSingle.Value.Modifiers.All(Input.GetKey)
                && InteractionManager.instance.SelectedMember is not null)
            {
                var member = InteractionManager.instance.SelectedMember;
                if (Helper.DisabledAutomatonSurvivors.Contains(member.member))
                {
                    Helper.DisabledAutomatonSurvivors.Remove(member.member);
                    Patches.ChooseNextAIActionPostfix(member.memberAI);
                    Helper.ShowFloatie("Automation enabled", member.baseCharacter);
                    return;
                }

                Helper.ShowFloatie("Automation disabled", member.baseCharacter);
                Helper.DisabledAutomatonSurvivors.Add(member.member);
                member.member.CancelJobsImmediately();
                member.member.CancelAIJobsImmediately();
                return;
            }

            if (Input.GetKeyDown(ToggleAll.Value.MainKey)
                && ToggleAll.Value.Modifiers.All(Input.GetKey)
                && InteractionManager.instance is not null)
            {
                if (Helper.DisabledAutomatonSurvivors.Count > 0)
                {
                    Helper.DisabledAutomatonSurvivors.Clear();
                    MemberManager.instance.GetAllShelteredMembers().Select(m => m.memberAI).Do(m =>
                    {
                        Patches.ChooseNextAIActionPostfix(m);
                        Helper.ShowFloatie("Automation enabled", m.memberRH.baseCharacter);
                    });
                    return;
                }

                if (MemberManager.instance is not null)
                {
                    MemberManager.instance.GetAllShelteredMembers().Do(m =>
                    {
                        Helper.DisabledAutomatonSurvivors.Add(m.member);
                        m.member.CancelJobsImmediately();
                        m.member.CancelAIJobsImmediately();
                        Helper.ShowFloatie("Automation disabled", m.baseCharacter);
                    });
                    return;
                }
            }

            if (!dev)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.F5))
            {
            }

            if (Input.GetKeyDown(KeyCode.F6))
            {
                ObjectManager.instance.GetAllObjects().Do(b =>
                {
                    b.interactions.Do(i => Traverse.Create(i).Field<List<Member>>("interactionMembers").Value.Clear());
                    b.beingUsed = false;
                    b.interactions.Do(i => i.CancelAllJobs());
                });
            }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                Patches.Cheat = !Patches.Cheat;
            }

            if (Input.GetKeyDown(KeyCode.F8))
            {
                if (MemberManager.instance is not null)
                {
                    MemberManager.instance.currentMembers.Do(m => m.member.needs.GetStatsList.Do(s => s.Set(0)));
                }
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
