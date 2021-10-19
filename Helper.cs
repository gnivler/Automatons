using System;
using System.Collections.Generic;
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
        internal static readonly HashSet<Member> DisabledAutomatonSurvivors = new();
        private static readonly Dictionary<Member, float> MemberTimers = new();
        private static readonly HashSet<Object_Planter> PlantersToFarm = new();
        private static readonly Dictionary<Object_Planter, float> PlanterTimers = new();
        internal static readonly HashSet<ObjectInteraction_HarvestTrap> Traps = new();
        private const int WaitDuration = 3;

        private static readonly AccessTools.FieldRef<MemberAI, MemberAI.AiState> lastState =
            AccessTools.FieldRefAccess<MemberAI, MemberAI.AiState>("lastState");

        private static readonly AccessTools.FieldRef<BaseCharacter, Vector3> m_healthLossFloatingTextOffset =
            AccessTools.FieldRefAccess<BaseCharacter, Vector3>("m_healthLossFloatingTextOffset");

        private static readonly AccessTools.FieldRef<Object_SnareTrap, int> trappedAnimalID =
            AccessTools.FieldRefAccess<Object_SnareTrap, int>("trappedAnimalID");

        private static IEnumerable<MethodInfo> ExtraJobs
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

                if (Mod.EnvironmentSeeking.Value)
                {
                    yield return AccessTools.Method(typeof(Helper), nameof(DoEnvironmentSeeking));
                }
            }
        }

        internal static void ClearGlobals()
        {
            DisabledAutomatonSurvivors.Clear();
            BurnableObjectsMap.Clear();
            PlantersToFarm.Clear();
            Traps.Clear();
        }

        internal static void DoEnvironmentSeeking(Member member)
        {
            if (member.isOutside
                && WeatherManager.instance.weatherActive
                && WeatherManager.instance.currentDaysWeather
                    is WeatherManager.WeatherState.BlackRain
                    or WeatherManager.WeatherState.SandStorm
                    or WeatherManager.WeatherState.LightSandStorm
                    or WeatherManager.WeatherState.HeavySandStorm)
            {
                Mod.Log($"Sending {member.name} out of the bad weather");
                var job = new Job_GoHere(member.memberRH, ReturnAdjustedAreaPosition(AreaManager.instance.areas[1]));
                member.AddJob(job);
                return;
            }

            if (member.CurrentArea.tempRating is not TemperatureRating.Okay or TemperatureRating.Warm)
            {
                var area = FindAreaOfTemperature(TemperatureRating.Okay).FirstOrDefault()
                           ?? FindAreaOfTemperature(TemperatureRating.Warm).FirstOrDefault();

                if (area is not null
                    && area != member.CurrentArea)
                {
                    Mod.Log($"Sending {member.name} to better temperatures");
                    var job = new Job_GoHere(member.memberRH, ReturnAdjustedAreaPosition(area));
                    member.AddJob(job);
                }
            }
        }

        internal static void DoExercise(Member member)
        {
            if (!member.HasEmptyQueues()
                || member.needs.fatigue.NormalizedValue * 100 > Mod.FatigueThreshold.Value)
            {
                return;
            }

            var equipment = ObjectManager.instance.GetObjectsOfCategory(ObjectManager.ObjectCategory.ExerciseMachine).Where(o => o.Status is Object_Base.ObjectStatus.Neutral).ToList();
            equipment.Shuffle();
            foreach (var objectBase in equipment.Where(e => !e.isBroken))
            {
                var exerciseMachine = (Object_ExerciseMachine)objectBase;
                if (!exerciseMachine.HasActiveInteractionMembers()
                    && (exerciseMachine.InLevelRange(member.memberRH)
                        || exerciseMachine.InLevelRangeFortitude(member.memberRH)))
                {
                    Mod.Log($"Sending {member.name} to exercise at {exerciseMachine.name} with {member.needs.fatigue.NormalizedValue * 100:f1}% fatigue");
                    var exerciseInteraction = exerciseMachine.GetInteractionByType(InteractionTypes.InteractionType.Exercise);
                    var job = new Job(member.memberRH, exerciseMachine, exerciseInteraction, exerciseMachine.GetInteractionTransform(0));
                    member.AddJob(job);
                    break;
                }
            }
        }

        internal static void DoFarming(Member member)
        {
            if (!member.HasEmptyQueues())
            {
                return;
            }

            foreach (var planter in PlantersToFarm.Where(p =>
                p.Status is Object_Base.ObjectStatus.Neutral
                && !p.HasActiveInteractionMembers()))
            {
                if (planter.CurrentWaterLevel > 0)
                {
                    DoFarmingJob<ObjectInteraction_HarvestPlant>(planter, member);
                    return;
                }

                if (WaterManager.instance.storedWater >= 1)
                {
                    DoFarmingJob<ObjectInteraction_WaterPlant>(planter, member);
                    return;
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

            Mod.Log($"Sending {member.name} to {planter} - {typeof(T)}");
            var interaction = planter.GetComponent<T>();
            var job = new Job(member.memberRH, planter, interaction, planter.GetInteractionTransform(0));
            member.AddJob(job);
        }

        private static void DoFirefighting(Member member)
        {
            try
            {
                if (!Mod.Firefighting.Value
                    || !MemberManager.instance.IsInitialSpawnComplete
                    || DisabledAutomatonSurvivors.Contains(member))
                {
                    return;
                }

                if (member.currentjob?.jobInteractionType is InteractionTypes.InteractionType.ExtinguishFire)
                {
                    return;
                }

                foreach (var burningObject in BurnableObjectsMap.Values.Where(v => v is not null))
                {
                    if (!burningObject.isBurning
                        || burningObject.isBeingExtinguished)
                    {
                        continue;
                    }

                    Mod.Log($"{burningObject.name} is burning and not being extinguished");
                    Job job;
                    var extinguishingSource = GetNearestExtinguisher(burningObject.obj);
                    if (extinguishingSource is not null)
                    {
                        Mod.Log($"Sending {member.name} to {burningObject.name} with extinguisher");
                        extinguishingSource.beingUsed = true;
                        var interaction = burningObject.GetComponent<ObjectInteraction_UseFireExtinguisher>();
                        job = new Job_UseFireExtinguisher(
                            member.memberRH, interaction, burningObject.obj, extinguishingSource.GetInteractionTransform(0), extinguishingSource);
                    }
                    else
                    {
                        Mod.Log($"Sending {member.name} to extinguish {burningObject.name}");
                        var interaction = burningObject.GetComponent<ObjectInteraction_ExtinguishFire>();
                        var butt = ObjectManager.instance.GetNearestObjectOfType(
                            ObjectManager.ObjectType.SmallWaterButt, burningObject.obj.GetInteractionTransform(0).position);
                        job = new Job_ExtinguishFire(
                            member.memberRH, interaction, burningObject.obj, butt.GetInteractionTransform(0), butt);
                    }

                    Mod.Log("Cancelling jobs");
                    member.CancelJobsImmediately();
                    member.CancelAIJobsImmediately();
                    member.AddJob(job);
                    //JobUI.instance.UpdateJobIcons();
                    burningObject.isBeingExtinguished = true;
                    break;
                }
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }
        }

        internal static void DoHarvestTraps(Member member)
        {
            if (!member.HasEmptyQueues())
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
                break;
            }
        }

        internal static void DoReading(Member member)
        {
            if (!member.HasEmptyQueues()
                || member.needs.fatigue.NormalizedValue * 100 > Mod.FatigueThreshold.Value)
            {
                return;
            }

            var bookCases = ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.Bookshelf);
            foreach (var bookCase in bookCases.Where(b =>
                b.Status is Object_Base.ObjectStatus.Neutral
                && !b.HasActiveInteractionMembers()))
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

                Mod.Log($"Sending {member.name} to {objectInteractionBase.interactionType} with {member.needs.fatigue.NormalizedValue * 100:f1} fatigue");
                var job = new Job(member.memberRH, bookCase, objectInteractionBase, bookCase.GetInteractionTransform(0));
                member.AddJob(job);
                break;
            }
        }

        internal static void DoRepairObjects(Member member)
        {
            if (!member.HasEmptyQueues())
            {
                return;
            }

            var hasRepairSkill = member.profession.PerceptionSkills.ContainsKey(ProfessionsManager.ProfessionSkillType.AutomaticRepairing);
            if (Mod.NeedRepairSkillToRepair.Value
                && !hasRepairSkill)
            {
                return;
            }

            var damagedObject = GetDamagedObjects().OrderBy(o =>
                    o.transform.position.PathDistanceTo(member.transform.position))
                .FirstOrDefault(o => !o.HasActiveInteractionMembers());
            if (damagedObject is null)
            {
                return;
            }

            if (!damagedObject.IsSurfaceObject
                || damagedObject.IsSurfaceObject
                && !IsBadWeather())
            {
                Mod.Log($"Sending {member.name} to repair {damagedObject.name} at {damagedObject.integrityNormalised * 100:f1}%");
                var inter = damagedObject.GetComponent<ObjectInteraction_Repair>();
                var job = new Job(member.memberRH, damagedObject, inter, damagedObject.interactions[0].transform);
                member.AddJob(job);
                damagedObject.beingUsed = true;
                lastState(member.memberRH.memberAI) = MemberAI.AiState.Repair;
            }
        }

        private static IEnumerable<Area> FindAreaOfTemperature(TemperatureRating temperature)
        {
            var areas = AreaManager.instance.areas.Where(a => a.tempRating == temperature);
            foreach (var area in areas)
            {
                if (area.isSurfaceArea && IsBadWeather())
                {
                    continue;
                }

                yield return area;
            }
        }

        internal static void FindJob(Member member)
        {
            if (member.currentjob?.jobInteractionType is InteractionTypes.InteractionType.ExtinguishFire)
            {
                return;
            }

            if (BurnableObjectsMap.Any(o => o.Key.isBurning && !o.Key.isBurntOut && !o.Value.isBeingExtinguished))
            {
                Mod.Log("FindJobs cancel jobs");
                member.CancelJobsImmediately();
                member.CancelAIJobsImmediately();
                DoFirefighting(member);
                return;
            }

            if (member.currentjob is not null)
            {
                return;
            }

            foreach (var methodInfo in ExtraJobs)
            {
                if (member.HasEmptyQueues())
                {
                    AccessTools.Method(typeof(MemberAI), "EvaluateNeeds").Invoke(member.memberRH.memberAI, new object[] { });
                    member.memberRH.memberAI.FindNeedsJob();
                    if (!member.HasEmptyQueues())
                    {
                        break;
                    }
                }

                if (member.HasEmptyQueues())
                {
                    methodInfo.Invoke(null, new object[] { member });
                    if (!member.HasEmptyQueues())
                    {
                        break;
                    }
                }
            }
        }

        private static IEnumerable<Object_Integrity> GetDamagedObjects()
        {
            return ObjectManager.instance.integrityObjects.Where(i =>
                    !i.HasActiveInteractionMembers()
                    && !i.IsWithinHoldingCell
                    && i.integrityNormalised <= Mod.RepairThreshold.Value / 100f)
                .OrderBy(i => i.integrityNormalised);
        }

        private static Object_FireExtinguisher GetNearestExtinguisher(Object_Base burningObject)
        {
            var extinguishers = ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.FireExtinguisher);
            Object_FireExtinguisher nearestExtinguisher = default;
            var closest = float.MaxValue;
            var burningTransform = burningObject.GetInteractionTransform(0);
            foreach (var extinguisher in extinguishers.Where(e =>
                e.Status is Object_Base.ObjectStatus.Neutral
                && ((Object_FireExtinguisher)e).CurrentUses > 0
                && !e.HasActiveInteractionMembers()))
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

        internal static void MaintainPlantersList(Object_Planter __instance)
        {
            const float waitTime = 0.25f;
            var planter = __instance;
            if (!PlanterTimers.TryGetValue(planter, out _))
            {
                PlanterTimers.Add(planter, default);
            }

            PlanterTimers[planter] += Time.deltaTime;
            if (PlanterTimers[planter] < waitTime)
            {
                return;
            }

            PlanterTimers[planter] -= waitTime;
            if (planter.GStage is not Object_Planter.GrowingStage.NoSeed
                && planter.CurrentWaterLevel <= 0
                || planter.GStage == Object_Planter.GrowingStage.Harvestable)
            {
                PlantersToFarm.Add(planter);
                return;
            }

            PlantersToFarm.Remove(planter);
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

        internal static bool ReadyToDoJob(Member member)
        {
            if (DisabledAutomatonSurvivors.Contains(member))
            {
                return false;
            }

            if (BreachManager.instance is null
                || BreachManager.instance.inProgress
                || MemberManager.instance is null
                || !MemberManager.instance.IsInitialSpawnComplete)
            {
                return false;
            }

            if (!MemberTimers.ContainsKey(member))
            {
                MemberTimers.Add(member, default);
            }

            MemberTimers[member] += Time.deltaTime;
            var variance = Random.Range(0, 2f);
            if (MemberTimers[member] < WaitDuration + variance)
            {
                return false;
            }

            MemberTimers[member] -= WaitDuration + variance;
            return true;
        }

        private static Vector3 ReturnAdjustedAreaPosition(Area area)
        {
            // todo vary X, reduce sample distance?!
            //var width = area.areaCollider.bounds.extents.x;
            var position = area.transform.position;
            NavMesh.SamplePosition(position, out var hit, 50f, -1);
            return hit.position;
            // :O
        }

        internal static void ShowFloatie(string message, BaseCharacter baseCharacter)
        {
            var offset = m_healthLossFloatingTextOffset(baseCharacter);
            Traverse.Create(baseCharacter).Field<FloatingTextPool_Shelter>(
                "m_floatingTextPool").Value.ShowFloatingText(message, baseCharacter.transform.position + offset, Color.magenta);
        }
    }
}
