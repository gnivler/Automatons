using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace Automatons
{
    public static class Helper
    {
        private static bool survivorsInitialized;
        internal static readonly HashSet<Object_Base> BurnableObjects = new();
        internal static readonly HashSet<Object_Planter> PlantersToFarm = new();
        internal static readonly HashSet<ObjectInteraction_HarvestTrap> Traps = new();

        private static readonly AccessTools.FieldRef<Object_SnareTrap, int> trappedAnimalID =
            AccessTools.FieldRefAccess<Object_SnareTrap, int>("trappedAnimalID");

        public class CompareVector3 : Comparer<Vector3>
        {
            public override int Compare(Vector3 x, Vector3 y)
            {
                if (x.magnitude > y.magnitude)
                {
                    return 1;
                }

                if (x.magnitude < y.magnitude)
                {
                    return -1;
                }

                return 0;
            }
        }

        internal static void DoFarmingJob<T>(Object_Base planter) where T : ObjectInteraction_Base
        {
            if (!survivorsInitialized
                && MemberManager.instance.GetAllShelteredMembers().All(m => m.name != "Sam Smith"))
            {
                survivorsInitialized = true;
            }

            if (!survivorsInitialized
                || planter.beingUsed)
            {
                return;
            }

            if (typeof(T) == typeof(ObjectInteraction_WaterPlant)
                && planter.IsSurfaceObject
                && WeatherManager.instance.IsRaining())
            {
                return;
            }

            var member = MemberManager.instance.GetAllShelteredMembers()
                .Where(m => IsAvailable(m.member))
                .OrderBy(m =>
                {
                    var point = planter.ChooseValidInteractionPoint(m.memberNavigation);
                    return point.position.PathDistanceTo(m.transform.position);
                }).FirstOrDefault();

            if (member is null)
            {
                return;
            }

            Mod.Log($"Sending {member.name} to {planter.name} - {typeof(T)}");
            var interaction = planter.GetComponent<T>();
            var job = new Job(member, planter, interaction, planter.ChooseValidInteractionPoint(member.memberNavigation));
            member.member.AddJob(job);
            member.member.currentjob = job;
            planter.beingUsed = true;
        }

        internal static void DoFirefightingJob(Object_FireExtinguisher extinguisher, Member member, Object_Base burningObject)
        {
            if (burningObject.beingUsed)
            {
                return;
            }

            Job job;
            if (extinguisher is not null)
            {
                Mod.Log($"Sending {member.name} to {burningObject.GetObjectName()} with extinguisher");
                extinguisher.beingUsed = true;
                job = new Job_UseFireExtinguisher(
                    member.memberRH,
                    burningObject.GetComponent<ObjectInteraction_UseFireExtinguisher>(),
                    burningObject,
                    extinguisher.ChooseValidInteractionPoint(member.memberRH.memberNavigation),
                    extinguisher);
            }
            else
            {
                Mod.Log($"Sending {member.name} to extinguish {burningObject.name}");
                var interaction = burningObject.GetComponent<ObjectInteraction_ExtinguishFire>();
                var butt = ObjectManager.instance.GetNearestObjectOfCategory(ObjectManager.ObjectCategory.WaterButt, member.transform.position);
                job = new Job_ExtinguishFire(member.memberRH, interaction, burningObject, burningObject.ChooseValidInteractionPoint(member.memberRH.memberNavigation), butt);
                member.ForceInterruptionJob(job);
            }

            burningObject.beingUsed = true;
            member.AddJob(job);
        }

        internal static Member GetNearestMemberToFireExtinguisher(Object_FireExtinguisher extinguisher, Object_Base burningObject)
        {
            Member member;
            var members = MemberManager.instance.GetAllShelteredMembers();
            if (extinguisher is not null)
            {
                Mod.Log(1);
                member = members.OrderBy(m =>
                    m.member.transform.position.PathDistanceTo(extinguisher.ChooseValidInteractionPoint().position)).FirstOrDefault()?.member;
            }
            else
            {
                Mod.Log(2);
                var point = burningObject.ChooseValidInteractionPoint().position;
                member = members.OrderBy(m => point.PathDistanceTo(m.transform.position)).First().member;
            }

            Mod.Log(3);

            return member;
        }

        internal static Object_FireExtinguisher GetNearestFireExtinguisherToFire(Object_Base burningObject)
        {
            // get the nearest fire extinguisher if any
            var extinguishers = ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.FireExtinguisher);
            Object_FireExtinguisher closestExtinguisher = default;
            var closest = float.MaxValue;
            foreach (var extinguisher in extinguishers)
            {
                var point = extinguisher.ChooseValidInteractionPoint().position;
                var extinguisherDistanceToFire = point.PathDistanceTo(burningObject.ChooseValidInteractionPoint().position);
                if (extinguisherDistanceToFire < closest)
                {
                    closest = extinguisherDistanceToFire;
                    closestExtinguisher = extinguisher as Object_FireExtinguisher;
                }
            }

            return closestExtinguisher;
        }

        internal static bool IsAvailable(Member memberToCheck)
        {
            return memberToCheck.currentjob is null
                   && memberToCheck.currentQueue.Count == 0
                   && memberToCheck.jobQueueCount == 0
                   && memberToCheck.aiQueueCount == 0
                   && !memberToCheck.m_breakdownController.isHavingBreakdown
                   && !memberToCheck.OutOnExpedition
                   || memberToCheck.currentjob?.jobInteractionType == InteractionTypes.InteractionType.GoHere
                   && !memberToCheck.m_breakdownController.isHavingBreakdown
                   && !memberToCheck.OutOnExpedition;
        }

        internal static void ProcessBurningObjects(Member __instance)
        {
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
                    var member = GetNearestMemberToFireExtinguisher(extinguisher, burningObject);
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
        }

        internal static void ClearGlobals()
        {
            BurnableObjects.Clear();
            PlantersToFarm.Clear();
            Traps.Clear();
            survivorsInitialized = false;
            survivorsInitialized = false;
        }

        internal static void HarvestTraps(Member __instance)
        {
            for (var i = 0; i < Traps.Count; i++)
            {
                try
                {
                    var trapInteraction = Traps.ElementAt(i);
                    var snareTrap = trapInteraction.obj;
                    if (trappedAnimalID((Object_SnareTrap)snareTrap) == -1
                        || snareTrap.beingUsed
                        || WeatherManager.instance.IsRaining()
                        && WeatherManager.instance.currentDaysWeather == WeatherManager.WeatherState.BlackRain)
                    {
                        continue;
                    }

                    var members = MemberManager.instance.GetAllShelteredMembers();
                    var member = members.Where(m => IsAvailable(m.member))
                        .OrderBy(m =>
                        {
                            var position = m.transform.position;
                            var transform = snareTrap.ChooseValidInteractionPoint(m.memberNavigation);
                            if (transform is null)
                            {
                                return float.MaxValue;
                            }

                            return position.PathDistanceTo(transform.position);
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
                    var job = new Job(member, trapInteraction.obj, trapInteraction, snareTrap.ChooseValidInteractionPoint(member.memberNavigation));
                    __instance.AddJob(job);
                    __instance.currentjob = job;
                    snareTrap.beingUsed = true;
                }
                catch (Exception ex)
                {
                    Mod.Log(ex);
                }
            }
        }

        internal static void RepairObjects(Member __instance)
        {
            if (__instance.profession.PerceptionSkills.ContainsKey(ProfessionsManager.ProfessionSkillType.AutomaticRepairing))
            {
                try
                {
                    // modified copy of GetMostDegradedObject()
                    Object_Integrity GetIntegrityObject(int skip = 0)
                    {
                        const float threshold = 25;
                        var integrityObjects = ObjectManager.instance.integrityObjects;
                        Object_Integrity objectIntegrity = null;
                        for (int index = skip; index < integrityObjects.Count; ++index)
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

                    // increment through objects so it doesn't get stuck on one which should be ignored for now (black rain)
                    Object_Integrity objectIntegrity = default;
                    for (var skip = 0; skip < ObjectManager.instance.integrityObjects.Count; skip++)
                    {
                        objectIntegrity = GetIntegrityObject(skip);
                        var isRaining = WeatherManager.instance.IsRaining();
                        if (objectIntegrity is not null
                            && !objectIntegrity.IsSurfaceObject
                            && isRaining
                            && WeatherManager.instance.currentDaysWeather is WeatherManager.WeatherState.BlackRain
                            || objectIntegrity is not null
                            && !isRaining)
                        {
                            break;
                        }
                    }

                    if (objectIntegrity is null)
                    {
                        return;
                    }

                    // TODO Order by member distance
                    var members = MemberManager.instance.GetAllShelteredMembers();
                    var member = members.Where(m => IsAvailable(m.member))
                        .OrderBy(m =>
                        {
                            var position = m.transform.position;
                            var interaction = objectIntegrity.ChooseValidInteractionPoint(m.memberNavigation);
                            if (interaction is null)
                            {
                                return float.MaxValue;
                            }

                            return position.PathDistanceTo(objectIntegrity.ChooseValidInteractionPoint(m.memberNavigation).position);
                        }).FirstOrDefault();

                    if (member is not null
                        && member.member == __instance)
                    {
                        Mod.Log($"Sending {__instance.name} to repair {objectIntegrity.name}");
                        var job = new Job(__instance.memberRH, objectIntegrity, objectIntegrity.GetComponent<ObjectInteraction_Repair>(), objectIntegrity.ChooseValidInteractionPoint(__instance.memberRH.memberNavigation));
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

        internal static void ProcessFarming()
        {
            foreach (var planter in PlantersToFarm)
            {
                if (planter is null)
                {
                    Mod.Log("Null");
                    continue;
                }

                if (planter.CurrentWaterLevel > 0)
                {
                    DoFarmingJob<ObjectInteraction_HarvestPlant>(planter);
                    continue;
                }

                DoFarmingJob<ObjectInteraction_WaterPlant>(planter);
            }
        }
    }
}
