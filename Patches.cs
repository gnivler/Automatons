using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace Automatons
{
    public static class Patches
    {
        private static readonly HashSet<Object_Base> BurnableObjects = new();
        private static readonly HashSet<Object_Planter> PlantersToFarm = new();
        private static bool survivorsInitialized;

        [HarmonyPatch(typeof(BurnableObject), "Awake")]
        public static void Postfix(Object_Base ___obj)
        {
            BurnableObjects.Add(___obj);
        }

        [HarmonyPatch(typeof(Member), "UpdateJobs")]
        public static void Postfix(Member __instance)
        {
            if (__instance.currentjob is null
                || __instance.currentjob.jobInteractionType != InteractionTypes.InteractionType.GoHere)
            {
                // Sam Smith is an uninitialized survivor so if any exist, it's not done initializing everyone
                if (!survivorsInitialized
                    && MemberManager.instance.GetAllShelteredMembers().All(m => m.name != "Sam Smith"))
                {
                    survivorsInitialized = true;
                }

                if (!survivorsInitialized)
                {
                    return;
                }

                foreach (var burningObject in BurnableObjects)
                {
                    try
                    {
                        if (!burningObject.isBurning
                            || burningObject.beingUsed
                            || burningObject.IsSurfaceObject
                            && WeatherManager.instance.IsRaining())
                        {
                            continue;
                        }

                        // anyone we can use to fight fires
                        var members = MemberManager.instance.GetAllShelteredMembers()
                            .Where(m =>
                                !m.member.OutOnExpedition
                                && (m.member.currentjob is null
                                    || m.member.currentjob.jobInteractionType is InteractionTypes.InteractionType.GoHere)
                                && m.member.currentQueue.Count == 0)
                            .ToList();

                        if (members.Count == 0)
                        {
                            continue;
                        }

                        // nearest extinguisher and then the nearest member to it, who must be this
                        var extinguisher = Helper.GetNearestFireExtinguisher(burningObject);
                        var member = Helper.GetNearestMember(extinguisher, members, burningObject, out var nearestButt);
                        if (member is null || __instance != member)
                        {
                            continue;
                        }

                        Helper.DoFirefightingJob(extinguisher, member, burningObject, nearestButt);
                    }
                    catch (Exception ex)
                    {
                        Mod.Log(ex);
                    }
                }

                foreach (var planter in PlantersToFarm)
                {
                    ObjectInteraction_Base interaction = planter.GetComponent<ObjectInteraction_WaterPlant>();
                    if (planter.CurrentWaterLevel > 0)
                    {
                        Helper.DoFarmingJob<ObjectInteraction_HarvestPlant>(planter);
                        continue;
                    }

                    Helper.DoFarmingJob<ObjectInteraction_WaterPlant>(planter);
                }

                if (__instance.currentjob is null
                    || __instance.currentjob.jobInteractionType == InteractionTypes.InteractionType.GoHere)
                {
                    if (__instance.profession.PerceptionSkills.ContainsKey(ProfessionsManager.ProfessionSkillType.AutomaticRepairing))
                    {
                        try
                        {
                            var objectIntegrity = ObjectManager.instance.GetMostDegradedObject();
                            var percent = objectIntegrity.integrity / objectIntegrity.maxIntegrity * 100;
                            const float threshold = 25;
                            if (percent <= threshold)
                            {
                                Mod.Log($"{objectIntegrity.name} needs repair");
                                var job = new Job
                                {
                                    jobInteractionType = InteractionTypes.InteractionType.Repair,
                                    memberRH = __instance.memberRH,
                                    obj = objectIntegrity,
                                    objInteraction = objectIntegrity.GetComponentInParent<ObjectInteraction_Repair>(),
                                };

                                __instance.AddJob(job);
                                __instance.currentjob = job;
                                objectIntegrity.beingUsed = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            Mod.Log(ex);
                        }
                    }
                }
            }
        }

        private static readonly AccessTools.FieldRef<Object_Integrity, float> m_integrity = AccessTools.FieldRefAccess<Object_Integrity, float>("m_integrity");

        [HarmonyPatch(typeof(Object_Planter), "Update")]
        public static void Postfix(Object_Planter __instance)
        {
            var planter = __instance;
            if (planter.GStage != Object_Planter.GrowingStage.NoSeed
                && planter.CurrentWaterLevel <= 0
                || planter.GStage == Object_Planter.GrowingStage.Harvestable)
            {
                PlantersToFarm.Add(planter);
                return;
            }

            PlantersToFarm.Remove(planter);
        }

        [HarmonyPatch(typeof(ObjectManager), "GetNearestObjectsOfType")]
        [HarmonyPrefix]
        public static bool GetNearestObjectsOfTypePostfix(ref bool __runOriginal, ref List<Object_Base> __result, ObjectManager.ObjectType type, Vector3 pos)
        {
            var objectsOfType = ObjectManager.instance.GetObjectsOfType(type);
            __result = objectsOfType.OrderBy(o => o.ChooseValidInteractionPoint().position, new Helper.CompareVector3()).ToList();
            __runOriginal = false;
            return false;
        }

        [HarmonyPatch(typeof(ObjectManager), "GetNearestObjectOfType")]
        [HarmonyPrefix]
        public static bool GetNearestObjectOfTypePostfix(ref bool __runOriginal, ref Object_Base __result, ObjectManager.ObjectType type, Vector3 pos)
        {
            __result = ObjectManager.instance.GetNearestObjectsOfType(type, pos).FirstOrDefault();
            __runOriginal = false;
            return false;
        }
    }
}
