using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using Random = System.Random;

// ReSharper disable InconsistentNaming

namespace Automatons
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Mod : BaseUnityPlugin
    {
        private const string PluginGUID = "ca.gnivler.sheltered2.Automatons";
        private const string PluginName = "Automatons";
        private const string PluginVersion = "1.6.2";
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
        internal static ConfigEntry<int> SolarCleaningThreshold;
        internal static ConfigEntry<int> RepairThreshold;
        internal static ConfigEntry<int> FatigueThreshold;
        private static ConfigEntry<KeyboardShortcut> ToggleSingle;
        private static ConfigEntry<KeyboardShortcut> ToggleAll;
        private static ConfigEntry<KeyboardShortcut> Clear;
        internal static ConfigEntry<bool> ShelterCleaning;
        internal static ConfigEntry<int> ShelterCleaningThreshold;
        internal static ConfigEntry<bool> RestUp;
        internal static ConfigEntry<bool> FirstAid;

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
            ShelterCleaning = Config.Bind("Toggle Jobs", "Clean the shelter", true);
            RestUp = Config.Bind("Toggle Jobs", "Rest up when nothing else to do", true);
            FirstAid = Config.Bind("Toggle Jobs", "Apply treatments for nausea, bleeding or radiation poisoning", true);
            NeedRepairSkillToRepair = Config.Bind("Adjustments", "Automatic Repairing skill needed to repair", true);
            FatigueThreshold = Config.Bind("Adjustments", "Fatigue Threshold", 50, new ConfigDescription("Survivors wont exercise or read past this percentage of fatigue", new AcceptableValueRange<int>(0, 75)));
            RepairThreshold = Config.Bind("Adjustments", "Repair Threshold", 25, new ConfigDescription("Survivors wont repair objects until they reach this percentage integrity", new AcceptableValueRange<int>(0, 90)));
            SolarCleaningThreshold = Config.Bind("Adjustments", "Solar Panel Cleaning Threshold", 25, new ConfigDescription("Survivors wont clean panels until this percentage dusty", new AcceptableValueRange<int>(0, 90)));
            ShelterCleaningThreshold = Config.Bind("Adjustments", "Shelter Cleaning Threshold", 100, new ConfigDescription("Survivors wont clean until this arbitrary dirtiness measurement", new AcceptableValueRange<int>(10, 1000)));
            ToggleSingle = Config.Bind("Hotkey", "Toggle automation for selected survivor", new KeyboardShortcut(KeyCode.Comma));
            ToggleAll = Config.Bind("Hotkey", "Toggle automation for all survivors", new KeyboardShortcut(KeyCode.Period));
            Clear = Config.Bind("Hotkey", "Emergency clear all object and member actions", new KeyboardShortcut(KeyCode.F8));
            Harmony harmony = new("ca.gnivler.sheltered2.Automatons");
            LogFile = Path.Combine(new FileInfo(Assembly.GetExecutingAssembly().Location).DirectoryName!, "log.txt");
            dev = SystemInfo.deviceName == "MEOWMEOW";
            Log("Automatons Startup");
            Log(new string('=', 80));
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
                    Helper.ShowFloatie("Automation enabled", member.baseCharacter);
                    return;
                }

                Helper.ShowFloatie("Automation disabled", member.baseCharacter);
                Helper.DisabledAutomatonSurvivors.Add(member.member);
                Log($"Cancelling everything related to {member.name}");
                Helper.CancelEverythingRelatedToMemberActivity(member.member);
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
                        Helper.ShowFloatie("Automation enabled", m.memberRH.baseCharacter);
                    });
                    return;
                }

                if (MemberManager.instance is not null)
                {
                    MemberManager.instance.GetAllShelteredMembers().Do(m =>
                    {
                        Helper.DisabledAutomatonSurvivors.Add(m.member);
                        Log($"Cancelling everything related to {m.name}");
                        Helper.CancelEverythingRelatedToMemberActivity(m.member);
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
                    MemberManager.instance.currentMembers[0].member.SetLeaderStatus(Member.LeaderStatus.General);
                    var everyone = MemberManager.instance.currentMembers.Where(member => member.member.isShelterMember);
                    foreach (var member in everyone)
                    {
                        if (!member.member.OutOnExpedition
                            && member.member.OutOnLoan)
                        {
                            try
                            {
                                Helper.ClearGlobals();
                                Traverse.Create(BreachManager.instance).Method("ResetSpawnTime");
                                Traverse.Create(member.member).Field<bool>("m_isUnconscious").Value = false;
                                member.member.Heal(500);
                                member.member.OutOnExpedition = false;
                                member.member.InBreachParty = false;
                                Traverse.Create(member.member).Field<bool>("m_outOnLoan").Value = false;
                                member.ForcefullyExitAnimationSubStates();
                                Log($"Cancelling everything related to {member.name}");
                                Helper.CancelEverythingRelatedToMemberActivity(member.member);
                                NavMesh.SamplePosition(AreaManager.instance.SurfaceArea.areaCollider.transform.position, out var hit, 20f, -1);
                                member.member.transform.position = hit.position;
                                var corpse = member.GetComponent<Obj_Corpse>();
                                if (corpse is not null)
                                {
                                    corpse.RemoveFromArea();
                                }

                                foreach (var obj in ObjectManager.instance.GetAllObjects())
                                {
                                    foreach (var interaction in obj.interactions)
                                    {
                                        try
                                        {
                                            interaction.ForceUnregister(member);
                                        }
                                        catch (Exception ex)
                                        {
                                            Log(ex);
                                        }
                                    }
                                }

                                Helper.ShowFloatie("Everything cleared!", member.baseCharacter);
                            }
                            catch (Exception ex)
                            {
                                Log(ex);
                            }
                        }
                        else
                        {
                            ExplorationManager.Instance.Parties.Do(party => party.RecallParty());
                        }
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
                    Mod.Log("F5");
                    //var def = QuestManager.instance.GetQuestInstanceFromDefKey("LosMuertosStage1");
                    //QuestManager.instance.FinishQuest(def.id, true);
                    //def = QuestManager.instance.GetQuestInstanceFromDefKey("LosMuertosStage2");
                    //QuestManager.instance.FinishQuest(def.id, true);
                    //Mod.Log(def);
                    //def.BecomeLikeNew();
                    //QuestManager.instance.SpawnQuestOrScenario(def.definition);
                    //QuestManager.instance.GetCurrentQuests(true, true, true).Do(q => Log(q.definition.id));
                    //QuestManager.instance.m_currentQuests.Remove(def);

                    //QuestManager.instance.SpawnFactionQuestWithId("LosMuertosStage2");
                    //QuestManager.instance.GetCurrentQuests(true, true, true).Do(q => Log(q.definition.id));
                    //var remove = MapManager.instance.AllQuestPointsOfInterest.First(p => p.Encounter == EncounterManager.EncounterType.Quest
                    //&& p.NameKey.Contains("Los Muertos"));
                    //Log(remove);
                    //MapManager.instance.AllPointsOfInterest.Remove(remove);

                    //QuestManager.instance.SpawnFactionQuestWithId("LosMuertosStage3");
                    //def = QuestManager.instance.GetQuestInstanceFromDefKey("LosMuertosStage3");
                    //QuestManager.instance.SpawnQuestInstances_Recursive(def.definition, def.parent, 5, out _);
                    //QuestManager.instance.FinishQuest(def.id, true);
                    //QuestManager.instance.SpawnFactionQuestWithId("LosMuertosStage4");
                    //QuestManager.instance.SpawnQuestOrScenario(def);
                    //def.m_daysUntilCompletion = 0.1f;
                    //QuestManager.instance.SpawnFactionQuestWithId("LosMuertosStage2");
                    //QuestManager.instance.SpawnQuestOrScenario(def.definition);
                    //def.FinishQuest(true);
                    // QuestManager.instance.AddRemoveQuestInstances_Recursive(def, true);
                    //QuestManager.instance.m_currentQuests.First(x => x.definition.id == "LosMuertosStage2")
                    //QuestManager.instance.m_completedFactionQuests.Do( Log);
                    //QuestManager.instance.m_currentQuests.Do(q => Log(q.definition.id));
                    //QuestManager.instance.SpawnFactionQuestWithId("LosMuertosStage1");
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
                Mod.Log("F6");
                //var everyone = MemberManager.instance!.currentMembers;
                //foreach (var member in everyone)
                //{
                //    try
                //    {
                //        Helper.ClearGlobals();
                //        BreachManager.instance.ResetSpawnTime();
                //        member.member.m_isUnconscious = false;
                //        member.member.Heal(500);
                //        member.member.OutOnExpedition = false;
                //        member.member.InBreachParty = false;
                //        member.member.OutOnLoan = false;
                //        member.ForcefullyExitAnimationSubStates();
                //        Log($"Cancelling everything related to {member.name}");
                //        Helper.CancelEverythingRelatedToMemberActivity(member.member);
                //        NavMesh.SamplePosition(AreaManager.instance.m_surfaceArea.areaCollider.transform.position, out var hit, 20f, -1);
                //        member.member.transform.position = hit.position;
                //        if (member.member.name == "Andrew Bowman")
                //        {
                //            Log("ping");
                //            //member.member.m_isCarryingCorpse = true;
                //            //member.member.PutDownCorpse();
                //            //member.member.m_bodybag.SetActive(false);
                //            //member.member.m_isCarryingCorpse = false;
                //        }
                //
                //        MemberManager.instance!.deadMembers.Do(m =>
                //        {
                //            Log(m.name);
                //            m.GetComponent<Obj_Corpse>().RemoveFromArea();
                //        });
                //        foreach (var obj in ObjectManager.instance.GetAllObjects())
                //        {
                //            foreach (var interaction in obj.interactions)
                //            {
                //                try
                //                {
                //                    interaction.ForceUnregister(member);
                //                }
                //                catch (Exception ex)
                //                {
                //                    Log(ex);
                //                }
                //            }
                //        }
                //
                //        ExplorationManager.Instance.Parties.Do(p => p.SetPartyToReturnToShelter(null));
                //        Helper.ShowFloatie("Everything cleared!", member.baseCharacter);
                //    }
                //    catch (Exception ex)
                //    {
                //        Log(ex);
                //    }
                //}

                return;
            }


            if (Input.GetKeyDown(KeyCode.F9))
            {
                Log("F9");
                foreach (var m in MemberManager.instance.currentMembers)
                {
                    if (UnityEngine.Random.Range(0, 5) == 0)
                    {
                        m.member.illness.bleeding.SetActive(true);
                    }
                }
            }

            if (Input.GetKeyDown(KeyCode.F10))
            {
                Log("F10");
                foreach (var m in MemberManager.instance.currentMembers)
                {
                    if (UnityEngine.Random.Range(0, 5) == 0)
                    {
                        Traverse.Create(m.member.illness.radiation).Field<float>("m_currentRads").Value = 200;
                        m.member.illness.radiation.SetActive(true);
                    }
                }
            }

            if (Input.GetKeyDown(KeyCode.F11))
            {
                Log("F11");
                foreach (var m in MemberManager.instance.currentMembers)
                {
                    if (UnityEngine.Random.Range(0, 5) == 0)
                    {
                        m.member.illness.foodPoisoning.SetActive(true);
                    }
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
