using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

// ReSharper disable InconsistentNaming

namespace Automatons
{
    public static class Helper
    {
        internal static readonly HashSet<Member> DisabledAutomatonSurvivors = new();
        internal static readonly Dictionary<Object_Base, BurnableObject> BurnableObjectsMap = new();
        internal static readonly HashSet<Object_Planter> PlantersToFarm = new();
        internal static readonly HashSet<ObjectInteraction_HarvestTrap> Traps = new();

        private static readonly AccessTools.FieldRef<MemberAI, MemberAI.AiState> lastState =
            AccessTools.FieldRefAccess<MemberAI, MemberAI.AiState>("lastState");

        private static readonly AccessTools.FieldRef<Object_Base, BurnableObject> m_burnableObject =
            AccessTools.FieldRefAccess<Object_Base, BurnableObject>("m_burnableObject");

        private static readonly AccessTools.FieldRef<BaseCharacter, Vector3> m_healthLossFloatingTextOffset =
            AccessTools.FieldRefAccess<BaseCharacter, Vector3>("m_healthLossFloatingTextOffset");

        private static readonly AccessTools.FieldRef<Object_SnareTrap, int> trappedAnimalID =
            AccessTools.FieldRefAccess<Object_SnareTrap, int>("trappedAnimalID");

        internal static IEnumerable<MethodInfo> ExtraJobs
        {
            get
            {
                if (Mod.Farming.Value)
                {
                    yield return AccessTools.Method(typeof(Helper), nameof(DoFarming));
                }

                if (Mod.Traps.Value)
                {
                    yield return AccessTools.Method(typeof(Helper), nameof(DoHarvestTraps));
                }

                if (Mod.Repair.Value)
                {
                    yield return AccessTools.Method(typeof(Helper), nameof(DoRepairObjects));
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

        internal static void DoExercise(Member member)
        {
            if (!Mod.Exercise.Value)
            {
                return;
            }

            if (member.needs.fatigue.NormalizedValue * 100 > Mod.FatigueThreshold.Value)
            {
                return;
            }

            var equipment = ObjectManager.instance.GetObjectsOfCategory(ObjectManager.ObjectCategory.ExerciseMachine);
            equipment.Shuffle();
            foreach (var objectBase in equipment.Where(e => !e.isBroken))
            {
                var exerciseMachine = (Object_ExerciseMachine)objectBase;
                if (!exerciseMachine.HasActiveInteractionMembers()
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

        internal static void DoFarming(Member member)
        {
            if (!Mod.Farming.Value)
            {
                return;
            }

            foreach (var planter in PlantersToFarm.Where(p => !p.HasActiveInteractionMembers()))
            {
                if (planter.CurrentWaterLevel > 0)
                {
                    DoFarmingJob<ObjectInteraction_HarvestPlant>(planter, member);
                    break;
                }

                if (WaterManager.instance.storedWater >= 1)
                {
                    DoFarmingJob<ObjectInteraction_WaterPlant>(planter, member);
                    break;
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
                && IsBadWeather())
            {
                return;
            }

            Mod.Log($"Sending {member.name} to {planter.name} - {typeof(T)}");
            var interaction = planter.GetComponent<T>();
            var job = new Job(member.memberRH, planter, interaction, planter.GetInteractionTransform(0));
            member.AddJob(job);
            member.currentjob = job;
        }

        internal static void DoFirefighting()
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

        internal static void DoHarvestTraps(Member member)
        {
            if (!Mod.Traps.Value)
            {
                return;
            }

            for (var i = 0; i < Traps.Count; i++)
            {
                var trapInteraction = Traps.ElementAt(i);
                var snareTrap = trapInteraction.obj;
                if (trappedAnimalID((Object_SnareTrap)snareTrap) == -1
                    || snareTrap.HasActiveInteractionMembers()
                    || IsBadWeather())
                {
                    continue;
                }

                Mod.Log($"Sending {member.name} to harvest snare trap {snareTrap.objectId}");
                var job = new Job(member.memberRH, trapInteraction.obj, trapInteraction, snareTrap.GetInteractionTransform(0));
                member.AddJob(job);
                member.currentjob = job;
                break;
            }
        }

        internal static void DoReading(Member member)
        {
            if (!Mod.Reading.Value)
            {
                return;
            }

            if (member.needs.fatigue.NormalizedValue * 100 > Mod.FatigueThreshold.Value)
            {
                return;
            }

            var bookCases = ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.Bookshelf);
            foreach (var bookCase in bookCases.Where(b => !b.HasActiveInteractionMembers()))
            {
                ObjectInteraction_Base objectInteractionBase = default;
                while (objectInteractionBase is null)
                {
                    if (!HasGoodBooks(member))
                    {
                        break;
                    }

                    var bookType = (ItemDef.BookType)Random.Range(2, 5);
                    objectInteractionBase = bookType switch
                    {
                        ItemDef.BookType.Charisma when HasGoodBook(ItemDef.BookType.Charisma, member) => bookCase.GetComponent<ObjectInteraction_ReadCharismaBook>(),
                        ItemDef.BookType.Intelligence when HasGoodBook(ItemDef.BookType.Intelligence, member) => bookCase.GetComponent<ObjectInteraction_ReadIntelligenceBook>(),
                        ItemDef.BookType.Perception when HasGoodBook(ItemDef.BookType.Perception, member) => bookCase.GetComponent<ObjectInteraction_ReadPerceptionBook>(),
                        // ReSharper disable once ExpressionIsAlwaysNull
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

        internal static void DoRepairObjects(Member member)
        {
            if (!Mod.Repair.Value)
            {
                return;
            }

            if (!member.profession.PerceptionSkills.ContainsKey(ProfessionsManager.ProfessionSkillType.AutomaticRepairing))
            {
                return;
            }

            var damagedObject = GetDamagedObjects().FirstOrDefault(o => !o.HasActiveInteractionMembers());
            if (!damagedObject)
            {
                return;
            }

            if (!damagedObject.IsSurfaceObject
                || damagedObject.IsSurfaceObject
                && !IsBadWeather())
            {
                Mod.Log($"Sending {member.name} to repair {damagedObject.name} at {damagedObject.integrityNormalised * 100}");
                var job = new Job(member.memberRH, damagedObject, damagedObject.GetComponent<ObjectInteraction_Repair>(), damagedObject.GetInteractionTransform(0));
                member.AddJob(job);
                member.currentjob = job;
                damagedObject.beingUsed = true;
                lastState(member.memberRH.memberAI) = MemberAI.AiState.Repair;
            }
        }

        private static void ClearJobsForFirefighting()
        {
            if (BurnableObjectsMap.Any(b => b.Value.isBurning)
                && !BurnableObjectsMap.All(b => b.Value.isBeingExtinguished))
            {
                MemberManager.instance.GetAllShelteredMembers().Where(m =>
                        !m.member.m_breakdownController.isHavingBreakdown
                        && !m.member.OutOnExpedition
                        && m.member.currentjob?.jobInteractionType
                            is not InteractionTypes.InteractionType.ExtinguishFire
                            or InteractionTypes.InteractionType.GoHere)
                    .Do(m =>
                    {
                        m.member.CancelAIJobsImmediately();
                        m.member.CancelJobsImmediately();
                    });
            }
        }

        internal static void ClearGlobals()
        {
            DisabledAutomatonSurvivors.Clear();
            BurnableObjectsMap.Clear();
            PlantersToFarm.Clear();
            Traps.Clear();
        }

        private static IEnumerable<Object_Integrity> GetDamagedObjects()
        {
            return ObjectManager.instance.integrityObjects.Where(i =>
                    !i.HasActiveInteractionMembers()
                    && !(bool)AccessTools.Method(typeof(ObjectInteraction_Base), "PreventCaptorsUsingSlaveObjects").Invoke(i.interactions[0], new object[] { }))
                .OrderBy(i => i.integrityNormalised).Where(i => i.integrityNormalised <= 0.25f);
        }

        private static Object_FireExtinguisher GetNearestExtinguisher(Object_Base burningObject)
        {
            var extinguishers = ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.FireExtinguisher);
            Object_FireExtinguisher nearestExtinguisher = default;
            var closest = float.MaxValue;
            var burningTransform = burningObject.GetInteractionTransform(0);
            foreach (var extinguisher in extinguishers.Where(e => !e.HasActiveInteractionMembers()))
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

        private static List<MemberReferenceHolder> GetNearestMembersToFire(Object_Base burningObject)
        {
            return MemberManager.instance.GetAllShelteredMembers()
                .Where(m =>
                    !m.member.m_breakdownController.isHavingBreakdown
                    && !m.member.OutOnExpedition)
                .OrderBy(m => m.member.transform.position.PathDistanceTo(
                    burningObject.GetInteractionTransform(0).position)).ToList();
        }

        private static bool HasGoodBook(ItemDef.BookType type, Member member)
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
                        MinMaxLevels(kvp.Key, out var min, out var max);
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
                        MinMaxLevels(kvp.Key, out var min, out var max);
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
                        MinMaxLevels(kvp.Key, out var min, out var max);
                        if (skillLevel >= min && skillLevel <= max)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool HasGoodBooks(Member member)
        {
            return HasGoodBook(ItemDef.BookType.Charisma, member)
                   || HasGoodBook(ItemDef.BookType.Intelligence, member)
                   || HasGoodBook(ItemDef.BookType.Perception, member);
        }

        private static bool IsBadWeather()
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

        internal static bool IsJobless(Member memberToCheck)
        {
            if (memberToCheck.jobQueueCount == 0)
            {
                lastState(memberToCheck.memberRH.memberAI) = MemberAI.AiState.Idle;
                return true;
            }

            return false;
        }

        private static void MinMaxLevels(int bookLevel, out int min, out int max)
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
        internal static void ShowFloatie(string message, BaseCharacter baseCharacter)
        {
            var offset = m_healthLossFloatingTextOffset(baseCharacter);
            Traverse.Create(baseCharacter).Field<FloatingTextPool_Shelter>(
                "m_floatingTextPool").Value.ShowFloatingText(message, baseCharacter.transform.position + offset, Color.magenta);
        }
    }
}
