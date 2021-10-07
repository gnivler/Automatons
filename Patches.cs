using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static Automatons.Helper;
using Object = UnityEngine.Object;

// ReSharper disable InconsistentNaming

namespace Automatons
{
    public static class Patches
    {
        private static readonly HashSet<Object_Base> BurnableObjects = new();
        private static readonly HashSet<Object_Planter> PlantersToFarm = new();
        private static readonly HashSet<ObjectInteraction_HarvestTrap> Traps = new();

        private static readonly AccessTools.FieldRef<Object_SnareTrap, int> trappedAnimalID =
            AccessTools.FieldRefAccess<Object_SnareTrap, int>("trappedAnimalID");

        private static readonly AccessTools.FieldRef<Object_Integrity, float> m_integrity =
            AccessTools.FieldRefAccess<Object_Integrity, float>("m_integrity");

        private static bool survivorsInitialized;

        [HarmonyPatch(typeof(BurnableObject), "Awake")]
        public static void Postfix(Object_Base ___obj)
        {
            BurnableObjects.Add(___obj);
        }

        [HarmonyPatch(typeof(ObjectInteraction_HarvestTrap), "Awake")]
        public static void Postfix(ObjectInteraction_HarvestTrap __instance)
        {
            Traps.Add(__instance);
        }

        [HarmonyPatch(typeof(SaveManager), "LoadFromCurrentSlot")]
        public static void LoadPostfix()
        {
            BurnableObjects.Clear();
            PlantersToFarm.Clear();
            Traps.Clear();
            survivorsInitialized = false;
            SurvivorsInitialized = false;
        }

        [HarmonyPatch(typeof(OptionsPanel), "OnQuitToMainMenuConfirm")]
        [HarmonyPostfix]
        public static void QuitPostfix()
        {
            BurnableObjects.Clear();
            PlantersToFarm.Clear();
            Traps.Clear();
            survivorsInitialized = false;
            SurvivorsInitialized = false;
        }

        [HarmonyPatch(typeof(Member), "UpdateJobs")]
        public static void Postfix(Member __instance)
        {
            if (BreachManager.instance.inProgress)
            {
                return;
            }

            if (IsAvailable(__instance))
            {
                // Sam Smith is an uninitialized survivor so if any exist, it's not done initializing everyone
                if (!survivorsInitialized
                    && MemberManager.instance.GetAllShelteredMembers().All(m => m.name != "Sam Smith"))
                {
                    survivorsInitialized = true;
                }

                // have to force it to invoke again to populate all survivors
                if (!survivorsInitialized)
                {
                    return;
                }

                var members = MemberManager.instance.GetAllShelteredMembers();
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

                        // nearest extinguisher and then the nearest member to it, who must be this
                        var extinguisher = GetNearestFireExtinguisherToFire(burningObject);
                        var member = GetNearestMemberToFireExtinguisher(extinguisher, members, burningObject);
                        if (__instance == member
                            && IsAvailable(member))
                        {
                            DoFirefightingJob(extinguisher, member, burningObject);
                        }
                    }
                    catch (Exception ex)
                    {
                        Mod.Log(ex);
                    }
                }

                if (!IsAvailable(__instance))
                {
                    return;
                }

                // farm stuff
                foreach (var planter in PlantersToFarm)
                {
                    if (planter.CurrentWaterLevel > 0)
                    {
                        DoFarmingJob<ObjectInteraction_HarvestPlant>(planter);
                        continue;
                    }

                    DoFarmingJob<ObjectInteraction_WaterPlant>(planter);
                }

                if (!IsAvailable(__instance))
                {
                    return;
                }

                // harvest traps
                for (var i = 0; i < Traps.Count; i++)
                {
                    try
                    {
                        var trapInteraction = Traps.ElementAt(i);
                        var snareTrap = trapInteraction.obj;
                        if (trappedAnimalID((Object_SnareTrap)snareTrap) == -1
                            || snareTrap.beingUsed)
                        {
                            continue;
                        }

                        var member = members.Where(m => IsAvailable(m.member))
                            .OrderBy(m =>
                            {
                                var position = m.transform.position;
                                return position.PathDistanceTo(snareTrap.GetInteractionTransform(0).position);
                            }).FirstOrDefault();

                        if (member is null
                            || member.member != __instance
                            || !IsAvailable(member.member)
                            || WeatherManager.instance.IsRaining()
                            && WeatherManager.instance.currentDaysWeather == WeatherManager.WeatherState.BlackRain)
                        {
                            continue;
                        }

                        Mod.Log($"Sending {__instance.name} to harvest snare trap {snareTrap.objectId}");
                        var job = new Job(member, trapInteraction.obj, trapInteraction, snareTrap.GetInteractionTransform(0));
                        __instance.AddJob(job);
                        __instance.currentjob = job;
                        snareTrap.beingUsed = true;
                    }
                    catch (Exception ex)
                    {
                        Mod.Log(ex);
                    }
                }

                if (!IsAvailable(__instance))
                {
                    return;
                }

                // repair stuff 
                if (__instance.profession.PerceptionSkills.ContainsKey(ProfessionsManager.ProfessionSkillType.AutomaticRepairing))
                {
                    try
                    {
                        // modified copy of GetMostDegradedObject()
                        Object_Integrity GetIntegrityObject()
                        {
                            const float threshold = 25;
                            var integrityObjects = ObjectManager.instance.integrityObjects;
                            Object_Integrity objectIntegrity = null;
                            for (int index = 0; index < integrityObjects.Count; ++index)
                            {
                                if (objectIntegrity != null)
                                {
                                    if (integrityObjects[index].integrity < objectIntegrity.integrity
                                        && !integrityObjects[index].beingUsed)
                                    {
                                        objectIntegrity = integrityObjects[index];
                                    }
                                }
                                else
                                {
                                    objectIntegrity = integrityObjects[index].beingUsed ? null : integrityObjects[index];
                                }

                                if (objectIntegrity is not null
                                    && objectIntegrity.integrity / objectIntegrity.maxIntegrity * 100 <= threshold)
                                {
                                    return objectIntegrity;
                                }
                            }

                            return null;
                        }

                        var objectIntegrity = GetIntegrityObject();
                        if (objectIntegrity is null)
                        {
                            return;
                        }

                        // TODO Order by member distance
                        var member = members.Where(m => IsAvailable(m.member))
                            .OrderBy(m =>
                            {
                                var position = m.transform.position;
                                return position.PathDistanceTo(objectIntegrity.GetInteractionTransform(0).position);
                            }).FirstOrDefault();

                        if (member is not null
                            && member.member == __instance)
                        {
                            Mod.Log($"Sending {__instance.name} to repair {objectIntegrity.name}");
                            var job = new Job(__instance.memberRH, objectIntegrity, objectIntegrity.GetComponent<ObjectInteraction_Repair>(), objectIntegrity.GetInteractionTransform(0));
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
            __result = objectsOfType.OrderBy(o => o.ChooseValidInteractionPoint().position, new CompareVector3()).ToList();
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


        //[HarmonyPatch(typeof(CraftingPanel), "OnRecipeSlotPress")]
        //public static void Prefix(RecipeSlot recipeSlot)
        //{
        //    foreach (var stack in recipeSlot.recipe.ingredients)
        //    {
        //        ShelterInventoryManager.instance.inventory.AddItems(stack);
        //    }
        //}
    }
}
