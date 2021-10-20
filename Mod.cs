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
        private const string PluginVersion = "1.4.1";
        private static string LogFile;
        private static bool dev;

        internal static ConfigEntry<bool> Firefighting;
        internal static ConfigEntry<bool> Farming;
        internal static ConfigEntry<bool> Traps;
        internal static ConfigEntry<bool> Repair;
        internal static ConfigEntry<bool> SolarCleaning;
        internal static ConfigEntry<bool> Exercise;
        internal static ConfigEntry<bool> Reading;
        internal static ConfigEntry<bool> EnvironmentSeeking;
        internal static ConfigEntry<bool> NeedRepairSkillToRepair;
        internal static ConfigEntry<int> CleaningThreshold;
        internal static ConfigEntry<int> RepairThreshold;
        internal static ConfigEntry<int> FatigueThreshold;
        private static ConfigEntry<KeyboardShortcut> ToggleSingle;
        private static ConfigEntry<KeyboardShortcut> ToggleAll;
        private static ConfigEntry<KeyboardShortcut> Clear;

        private void Awake()
        {
            Firefighting = Config.Bind("Toggle Jobs", "Firefighting", true);
            Farming = Config.Bind("Toggle Jobs", "Farming", true);
            Traps = Config.Bind("Toggle Jobs", "Traps", true);
            Repair = Config.Bind("Toggle Jobs", "Repair", true);
            SolarCleaning = Config.Bind("Toggle Jobs", "Solar Panel Cleaning", true);
            Exercise = Config.Bind("Toggle Jobs", "Exercise", true);
            Reading = Config.Bind("Toggle Jobs", "Reading", true);
            EnvironmentSeeking = Config.Bind("Toggle Jobs", "Avoid idling in bad weather and inclement areas when possible", true);
            NeedRepairSkillToRepair = Config.Bind("Adjustments", "Automatic Repairing skill needed to repair", true);
            FatigueThreshold = Config.Bind("Adjustments", "Fatigue Threshold", 50, new ConfigDescription("Survivors wont exercise or read past this percentage of fatigue", new AcceptableValueRange<int>(0, 75)));
            RepairThreshold = Config.Bind("Adjustments", "Repair Threshold", 25, new ConfigDescription("Survivors wont repair objects until they reach this percentage integrity", new AcceptableValueRange<int>(0, 90)));
            CleaningThreshold = Config.Bind("Adjustments", "Solar Panel Cleaning Threshold", 25, new ConfigDescription("Survivors wont clean panels until this percentage dusty", new AcceptableValueRange<int>(0, 90)));
            ToggleSingle = Config.Bind("Hotkey", "Toggle automation for selected survivor", new KeyboardShortcut(KeyCode.Comma));
            ToggleAll = Config.Bind("Hotkey", "Toggle automation for all survivors", new KeyboardShortcut(KeyCode.Period));
            Clear = Config.Bind("Hotkey", "Emergency clear all object and member actions", new KeyboardShortcut(KeyCode.F8));
            Harmony harmony = new("ca.gnivler.sheltered2.Automatons");
            LogFile = Path.Combine(new FileInfo(Assembly.GetExecutingAssembly().Location).DirectoryName!, "log.txt");
            dev = SystemInfo.deviceName == "MEOWMEOW";
            Log("Automatons Startup");
            harmony.PatchAll(typeof(Patches));
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
                    //Patches.ChooseNextAIActionPostfix(member.memberAI);
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
                        //Patches.ChooseNextAIActionPostfix(m);
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

            if (Input.GetKeyDown(Clear.Value.MainKey)
                && ToggleAll.Value.Modifiers.All(Input.GetKey))
            {
                if (ObjectManager.instance is not null)
                {
                    foreach (var obj in ObjectManager.instance.GetAllObjects().Where(o => o.interactions.Count > 0))
                    {
                        if (obj.interactions.Count > 0)
                        {
                            foreach (var interaction in obj.interactions)
                            {
                                interaction.CancelAllJobs();
                            }
                        }
                    }
                }

                if (MemberManager.instance is not null)
                {
                    var everyone = MemberManager.instance.currentMembers.Concat(SlaveManager.instance.m_currentSlaves).Concat(NPCVisitManager.instance.currentVisitors).Concat(NPCVisitManager.instance.deadVisitors);
                    foreach (var member in everyone)
                    {
                        Helper.ShowFloatie("Everything cleared!", member.baseCharacter);
                        member.member.m_carriedFood = null;
                        member.ForcefullyExitAnimationSubStates();
                        member.StopAllCoroutines();
                        member.CancelInvoke();
                        member.member.CancelJobsImmediately();
                        member.member.CancelAIJobsImmediately();
                    }
                }

                return;
            }

            if (!dev)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.F5))
            {
                try
                {
                    QuestManager.instance.SpawnFactionQuestWithId("CTKMobStage3");
                    //QuestManager.instance.m_currentQuests.Where(q => q.IsActive()).Do(q =>
                    //{
                    //    Log($"{q.definition.id}");
                    //    q.FinishQuest(true);
                    //});
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }

            if (Input.GetKeyDown(KeyCode.F6))
            {
                ObjectManager.instance.GetAllObjects().Do(b =>
                {
                    MemberManager.instance.GetAllShelteredMembers().Do(m => Helper.ShowFloatie("All object interactions cleared", m.baseCharacter));
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
                FileLog.Log($"[Automatons] {input}");
                //File.AppendAllText(LogFile, $"{input ?? "null"}\n");
            }
        }
    }
}
