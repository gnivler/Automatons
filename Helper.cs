using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

// ReSharper disable InconsistentNaming

namespace Automatons
{
    public static class Helper
    {
        internal static readonly Dictionary<Object_Base, BurnableObject> BurnableObjectsMap = new();
        internal static readonly HashSet<Object_Planter> PlantersToFarm = new();
        internal static readonly HashSet<ObjectInteraction_HarvestTrap> Traps = new();
        private static readonly AccessTools.FieldRef<Object_Base, BurnableObject> m_burnableObject = AccessTools.FieldRefAccess<Object_Base, BurnableObject>("m_burnableObject");

        private static readonly AccessTools.FieldRef<Object_SnareTrap, int> trappedAnimalID =
            AccessTools.FieldRefAccess<Object_SnareTrap, int>("trappedAnimalID");

        internal static readonly AccessTools.FieldRef<MemberAI, MemberAI.AiState> lastState =
            AccessTools.FieldRefAccess<MemberAI, MemberAI.AiState>("lastState");

        internal static IEnumerable<MethodInfo> ExtraJobs
        {
            get
            {
                if (Mod.Farming.Value)
                {
                    yield return AccessTools.Method(typeof(Helper), nameof(ProcessFarming));
                }

                if (Mod.Traps.Value)
                {
                    yield return AccessTools.Method(typeof(Helper), nameof(HarvestTraps));
                }

                if (Mod.Repair.Value)
                {
                    yield return AccessTools.Method(typeof(Helper), nameof(RepairObjects));
                }

                if (Mod.Exercise.Value)
                {
                    yield return AccessTools.Method(typeof(Helper), nameof(DoExercise));
                }

                if (Mod.Reading.Value)
                {
                    yield return AccessTools.Method(typeof(Helper), nameof(DoReading));
                }
            }
        }

        private static void DoFarmingJob<T>(Object_Base planter, Member member) where T : ObjectInteraction_Base
        {
            if (typeof(T) == typeof(ObjectInteraction_WaterPlant)
                && planter.IsSurfaceObject
                && WeatherManager.instance.IsRaining())
            {
                return;
            }

            if (typeof(T) == typeof(ObjectInteraction_HarvestPlant)
                && planter.IsSurfaceObject
                && BadWeather())
            {
                return;
            }

            Mod.Log($"Sending {member.name} to {planter.name} - {typeof(T)}");
            var interaction = planter.GetComponent<T>();
            var job = new Job(member.memberRH, planter, interaction, planter.GetInteractionTransform(0));
            member.AddJob(job);
            member.currentjob = job;
        }

        private static List<MemberReferenceHolder> GetNearestMembersToFire(Object_Base burningObject)
        {
            return MemberManager.instance.GetAllShelteredMembers()
                .Where(m =>
                    !m.member.m_breakdownController.isHavingBreakdown
                    && !m.member.OutOnExpedition)
                .OrderBy(m => m.member.transform.position.PathDistanceTo(
                    burningObject.GetInteractionTransform(0).position)).ToList();
        }

        private static Object_FireExtinguisher GetNearestExtinguisher(Object_Base burningObject)
        {
            var extinguishers = ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.FireExtinguisher);
            Object_FireExtinguisher nearestExtinguisher = default;
            var closest = float.MaxValue;
            var burningTransform = burningObject.GetInteractionTransform(0);
            foreach (var extinguisher in extinguishers.Where(e => e.interactions.Sum(i => i.InteractionMemberCount) == 0))
            {
                var extinguisherDistanceToFire = extinguisher.transform.position.PathDistanceTo(burningTransform.position);
                if (extinguisherDistanceToFire < closest)
                {
                    closest = extinguisherDistanceToFire;
                    nearestExtinguisher = extinguisher as Object_FireExtinguisher;
                }
            }

            return nearestExtinguisher;
        }

        internal static bool IsJobless(Member memberToCheck)
        {
            if (memberToCheck.jobQueueCount == 0)
            {
                lastState(memberToCheck.memberRH.memberAI) = MemberAI.AiState.Idle;
                return true;
            }

            return false;
        }

        internal static void ProcessBurningObjects()
        {
            if (!Mod.Firefighting.Value)
            {
                return;
            }

            for (var index = 0; index < BurnableObjectsMap.Count; index++)
            {
                var element = BurnableObjectsMap.ElementAt(index);
                if (!element.Key.isBurnable)
                {
                    BurnableObjectsMap.Remove(element.Key);
                }
            }

            ClearJobsForFirefighting();
            foreach (var burningObject in BurnableObjectsMap)
            {
                if (!burningObject.Value.isBurning
                    || m_burnableObject(burningObject.Key).isBeingExtinguished)
                {
                    continue;
                }

                var nearestMember = GetNearestMembersToFire(burningObject.Key).FirstOrDefault(m =>
                    m.member.currentjob?.jobInteractionType is not InteractionTypes.InteractionType.ExtinguishFire)?.member;
                if (nearestMember)
                {
                    Job job;
                    var extinguishingSource = GetNearestExtinguisher(burningObject.Key);
                    if (extinguishingSource is not null)
                    {
                        Mod.Log($"Sending {nearestMember.name} to {burningObject.Key.name} with extinguisher");
                        extinguishingSource.beingUsed = true;
                        var interaction = burningObject.Key.GetComponent<ObjectInteraction_UseFireExtinguisher>();
                        job = new Job_UseFireExtinguisher(
                            nearestMember.memberRH, interaction, burningObject.Key, extinguishingSource.GetInteractionTransform(0), extinguishingSource);
                    }
                    else
                    {
                        Mod.Log($"Sending {nearestMember.name} to extinguish {burningObject.Key.name}");
                        var interaction = burningObject.Key.GetComponent<ObjectInteraction_ExtinguishFire>();
                        var butt = ObjectManager.instance.GetNearestObjectOfType(
                            ObjectManager.ObjectType.SmallWaterButt, burningObject.Key.GetInteractionTransform(0).position);
                        job = new Job_ExtinguishFire(
                            nearestMember.memberRH, interaction, burningObject.Key, butt.GetInteractionTransform(0), butt);
                    }

                    nearestMember.AddJob(job);
                    nearestMember.currentjob = job;
                    JobUI.instance.UpdateJobIcons();
                    m_burnableObject(burningObject.Key).isBeingExtinguished = true;
                }
            }
        }

        internal static void ClearGlobals()
        {
            BurnableObjectsMap.Clear();
            PlantersToFarm.Clear();
            Traps.Clear();
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
                        || snareTrap.HasActiveInteractionMembers()
                        || BadWeather())
                    {
                        continue;
                    }

                    var members = MemberManager.instance.GetAllShelteredMembers();
                    var member = members.Where(m => IsJobless(m.member))
                        .OrderBy(m =>
                        {
                            var position = m.transform.position;
                            var transform = snareTrap.GetInteractionTransform(0);
                            if (transform is null)
                            {
                                return float.MaxValue;
                            }

                            return position.PathDistanceTo(transform.position);
                        }).FirstOrDefault();

                    if (member is null
                        || member.member != __instance
                        || BadWeather())
                    {
                        continue;
                    }

                    Mod.Log($"Sending {member.name} to harvest snare trap {snareTrap.objectId}");
                    var job = new Job(member, trapInteraction.obj, trapInteraction, snareTrap.GetInteractionTransform(0));
                    member.member.AddJob(job);
                    member.member.currentjob = job;
                    //trappedAnimalID((Object_SnareTrap)snareTrap) = 0;
                }
                catch (Exception ex)
                {
                    Mod.Log(ex);
                }
            }
        }

        internal static void RepairObjects(Member member)
        {
            if (member.profession.PerceptionSkills.ContainsKey(ProfessionsManager.ProfessionSkillType.AutomaticRepairing))
            {
                // modified copy of GetMostDegradedObject()
                Object_Integrity damagedObject = default;
                // increment through objects so it doesn't get stuck on one which should be ignored for now (black rain)
                for (var skip = 0; skip < ObjectManager.instance.integrityObjects.Count; skip++)
                {
                    damagedObject = GetDamagedObject(skip);
                    if (damagedObject is not null)
                    {
                        break;
                    }
                }

                if (damagedObject is null)
                {
                    return;
                }

                var members = MemberManager.instance.GetAllShelteredMembers();
                var nearestMember = members.Where(m => IsJobless(m.member))
                    .OrderBy(m =>
                    {
                        var position = m.transform.position;
                        var interaction = damagedObject.GetInteractionTransform(0);
                        if (interaction is null)
                        {
                            return float.MaxValue;
                        }

                        return position.PathDistanceTo(damagedObject.GetInteractionTransform(0).position);
                    }).FirstOrDefault();

                if (nearestMember is not null
                    && nearestMember.member == member)
                {
                    Mod.Log($"Sending {member.name} to repair {damagedObject.name} at {damagedObject.integrityNormalised * 100}");
                    var job = new Job(member.memberRH, damagedObject, damagedObject.GetComponent<ObjectInteraction_Repair>(), damagedObject.GetInteractionTransform(0));
                    member.AddJob(job);
                    member.currentjob = job;
                    damagedObject.beingUsed = true;
                    lastState(member.memberRH.memberAI) = MemberAI.AiState.Repair;
                }
            }
        }

        internal static void ProcessFarming(Member member)
        {
            foreach (var planter in PlantersToFarm.Where(p => !p.HasActiveInteractionMembers()))
            {
                var nearestMember = MemberManager.instance.GetAllShelteredMembers()
                    .Where(m => IsJobless(m.member))
                    .OrderBy(m =>
                    {
                        var point = planter.GetInteractionTransform(0);
                        return point.position.PathDistanceTo(m.transform.position);
                    }).FirstOrDefault()?.member;

                if (nearestMember is null || nearestMember != member)
                {
                    return;
                }

                if (planter.CurrentWaterLevel > 0)
                {
                    DoFarmingJob<ObjectInteraction_HarvestPlant>(planter, member);
                    continue;
                }

                if (WaterManager.instance.storedWater >= 1)
                {
                    DoFarmingJob<ObjectInteraction_WaterPlant>(planter, member);
                }
            }
        }

        internal static Object_Integrity GetDamagedObject(int skip)
        {
            var integrityObjects = ObjectManager.instance.integrityObjects;
            const float threshold = 25;
            Object_Integrity objectIntegrity = default;
            for (int index = skip; index < integrityObjects.Count; index++)
            {
                if (integrityObjects[index].beingUsed
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    || integrityObjects[index].integrityNormalised == 1)
                {
                    continue;
                }

                if (objectIntegrity is not null)
                {
                    if (integrityObjects[index].integrity < objectIntegrity.integrity)
                    {
                        objectIntegrity = integrityObjects[index];
                    }
                }
                else
                {
                    objectIntegrity = integrityObjects[index];
                }

                if (objectIntegrity.integrityNormalised * 100 <= threshold)
                {
                    return objectIntegrity;
                }
            }

            return null;
        }

        internal static void DoExercise(Member member)
        {
            if (member.needs.fatigue.NormalizedValue * 100 > Mod.FatigueThreshold.Value)
            {
                return;
            }

            var equipment = ObjectManager.instance.GetObjectsOfCategory(ObjectManager.ObjectCategory.ExerciseMachine);
            equipment.Shuffle();
            foreach (var objectBase in equipment.Where(e => !e.isBroken))
            {
                var exerciseMachine = (Object_ExerciseMachine)objectBase;
                if (exerciseMachine.interactions.Sum(i => i.InteractionMemberCount) == 0
                    && (exerciseMachine.InLevelRange(member.memberRH)
                        || exerciseMachine.InLevelRangeFortitude(member.memberRH)))
                {
                    Mod.Log($"Sending {member.name} to exercise at {exerciseMachine.name} with {member.needs.fatigue.NormalizedValue * 100} fatigue");
                    var exerciseInteraction = exerciseMachine.GetInteractionByType(InteractionTypes.InteractionType.Exercise);
                    var job = new Job(member.memberRH, exerciseMachine, exerciseInteraction, exerciseMachine.GetInteractionTransform(0));
                    member.AddJob(job);
                    member.currentjob = job;
                    lastState(member.memberRH.memberAI) = MemberAI.AiState.Exercise;
                    break;
                }
            }
        }

        internal static void DoReading(Member member)
        {
            try
            {
                if (member.needs.fatigue.NormalizedValue * 100 > Mod.FatigueThreshold.Value)
                {
                    return;
                }

                var bookCases = ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.Bookshelf);
                foreach (var bookCase in bookCases.Where(b => b.interactions.Sum(i => i.InteractionMemberCount) == 0))
                {
                    ObjectInteraction_Base objectInteractionBase = default;
                    while (objectInteractionBase is null)
                    {
                        if (!HasGoodBooks())
                        {
                            break;
                        }

                        var bookType = (ItemDef.BookType)Random.Range(2, 5);
                        objectInteractionBase = bookType switch
                        {
                            ItemDef.BookType.Charisma when HasGoodBook(ItemDef.BookType.Charisma) => bookCase.GetComponent<ObjectInteraction_ReadCharismaBook>(),
                            ItemDef.BookType.Intelligence when HasGoodBook(ItemDef.BookType.Intelligence) => bookCase.GetComponent<ObjectInteraction_ReadIntelligenceBook>(),
                            ItemDef.BookType.Perception when HasGoodBook(ItemDef.BookType.Perception) => bookCase.GetComponent<ObjectInteraction_ReadPerceptionBook>(),
                            _ => objectInteractionBase
                        };
                    }

                    if (objectInteractionBase is null)
                    {
                        continue;
                    }

                    Mod.Log($"Sending {member.name} to {objectInteractionBase.interactionType}");
                    var job = new Job(member.memberRH, bookCase, objectInteractionBase, bookCase.GetInteractionTransform(0));
                    member.AddJob(job);
                    break;
                }
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }

            bool HasGoodBooks()
            {
                return HasGoodBook(ItemDef.BookType.Charisma)
                       || HasGoodBook(ItemDef.BookType.Intelligence)
                       || HasGoodBook(ItemDef.BookType.Perception);
            }

            // TODO DRY
            bool HasGoodBook(ItemDef.BookType type)
            {
                if (type == ItemDef.BookType.Charisma)
                {
                    if (BookManager.Instance.CharismaBooks.Values.All(v => v == 0))
                    {
                        return false;
                    }

                    var skillLevel = member.baseStats.Charisma.Level;
                    if (skillLevel == member.baseStats.Charisma.LevelCap)
                    {
                        return false;
                    }

                    foreach (var kvp in BookManager.Instance.CharismaBooks)
                    {
                        if (kvp.Value != 0)
                        {
                            Range(kvp.Key, out var min, out var max);
                            if (skillLevel >= min && skillLevel <= max)
                            {
                                return true;
                            }
                        }
                    }
                }

                if (type == ItemDef.BookType.Intelligence)
                {
                    if (BookManager.Instance.IntelligenceBooks.Values.All(v => v == 0))
                    {
                        return false;
                    }

                    var skillLevel = member.baseStats.Intelligence.Level;
                    if (skillLevel == member.baseStats.Intelligence.LevelCap)
                    {
                        return false;
                    }

                    foreach (var kvp in BookManager.Instance.IntelligenceBooks)
                    {
                        if (kvp.Value != 0)
                        {
                            Range(kvp.Key, out var min, out var max);
                            if (skillLevel >= min && skillLevel <= max)
                            {
                                return true;
                            }
                        }
                    }
                }

                if (type == ItemDef.BookType.Perception)
                {
                    if (BookManager.Instance.PerceptionBooks.Values.All(v => v == 0))
                    {
                        return false;
                    }

                    var skillLevel = member.baseStats.Perception.Level;
                    if (skillLevel == member.baseStats.Perception.LevelCap)
                    {
                        return false;
                    }

                    foreach (var kvp in BookManager.Instance.PerceptionBooks)
                    {
                        if (kvp.Value != 0)
                        {
                            Range(kvp.Key, out var min, out var max);
                            if (skillLevel >= min && skillLevel <= max)
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
        }

        private static void Range(int bookLevel, out int min, out int max)
        {
            min = -1;
            max = -1;
            switch (bookLevel)
            {
                case 1:
                {
                    min = 1;
                    max = 5;
                    return;
                }
                case 2:
                {
                    min = 6;
                    max = 10;
                    return;
                }

                case 3:
                {
                    min = 11;
                    max = 15;
                    return;
                }
                case 4:
                {
                    min = 16;
                    max = 20;
                    return;
                }
            }
        }

        private static bool BadWeather()
        {
            return WeatherManager.instance.weatherActive
                   && WeatherManager.instance.currentDaysWeather
                       is WeatherManager.WeatherState.BlackRain
                       or WeatherManager.WeatherState.SandStorm
                       or WeatherManager.WeatherState.ThunderStorm
                       or WeatherManager.WeatherState.HeavySandStorm
                       or WeatherManager.WeatherState.HeavyThunderStorm
                       or WeatherManager.WeatherState.LightSandStorm
                       or WeatherManager.WeatherState.LightThunderStorm;
        }

        private static void ClearJobsForFirefighting()
        {
            if (BurnableObjectsMap.Any(b => b.Value.isBurning)
                && !BurnableObjectsMap.All(b => b.Value.isBeingExtinguished))
            {
                MemberManager.instance.GetAllShelteredMembers().Where(m =>
                        !m.member.m_breakdownController.isHavingBreakdown
                        && !m.member.OutOnExpedition
                        && m.member.currentjob?.jobInteractionType is not InteractionTypes.InteractionType.ExtinguishFire or InteractionTypes.InteractionType.GoHere)
                    .Do(m =>
                    {
                        m.member.CancelAIJobsImmediately();
                        m.member.CancelJobsImmediately();
                    });
            }
        }

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
    }
}
