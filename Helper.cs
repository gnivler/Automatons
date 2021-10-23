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
        internal static readonly HashSet<BurnableObject> BurnableObjects = new();
        internal static readonly HashSet<Member> DisabledAutomatonSurvivors = new();
        private static readonly Dictionary<Member, float> MemberTimers = new();
        internal static readonly HashSet<ObjectInteraction_HarvestTrap> Traps = new();
        private const int WaitDuration = 3;
        private static float FireCheckTimer;

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

                if (Mod.SolarCleaning.Value)
                {
                    yield return AccessTools.Method(typeof(Helper), nameof(DoSolarPanelCleaning));
                }

                if (Mod.Exercise.Value)
                {
                    yield return AccessTools.Method(typeof(Helper), nameof(DoExercise));
                }

                if (Mod.Reading.Value)
                {
                    yield return AccessTools.Method(typeof(Helper), nameof(DoReading));
                }

                if (Mod.ShelterCleaning.Value)
                {
                    yield return AccessTools.Method(typeof(Helper), nameof(DoShelterCleaning));
                }

                if (Mod.EnvironmentSeeking.Value)
                {
                    yield return AccessTools.Method(typeof(Helper), nameof(DoEnvironmentSeeking));
                }

                if (Mod.RestUp.Value)
                {
                    yield return AccessTools.Method(typeof(Helper), nameof(DoRest));
                }
            }
        }

        internal static void ClearGlobals()
        {
            DisabledAutomatonSurvivors.Clear();
            BurnableObjects.Clear();
            Traps.Clear();
        }

        internal static void DoEnvironmentSeeking(Member member)
        {
            if (member.isOutside
                && WeatherManager.instance is not null
                && WeatherManager.instance.weatherActive
                && WeatherManager.instance.currentDaysWeather
                    is WeatherManager.WeatherState.BlackRain
                    or WeatherManager.WeatherState.SandStorm
                    or WeatherManager.WeatherState.LightSandStorm
                    or WeatherManager.WeatherState.HeavySandStorm)
            {
                Mod.Log($"Sending {member.name} out of the bad weather at {AreaManager.instance.areas[1].name} position {ReturnAdjustedAreaPosition(AreaManager.instance.areas[1])}");
                var job = new Job_GoHere(member.memberRH, ReturnAdjustedAreaPosition(AreaManager.instance.areas[1]));
                member.AddJob(job);
                return;
            }

            if (member.HasEmptyQueues()
                && member.currentjob is null
                && member.currentTemperature is not TemperatureRating.Okay or TemperatureRating.Warm)
            {
                var area = FindAreaOfTemperature(TemperatureRating.Okay).FirstOrDefault()
                           ?? FindAreaOfTemperature(TemperatureRating.Warm).FirstOrDefault()
                           ?? FindAreaOfTemperature(TemperatureRating.Cold).FirstOrDefault();

                if (area is not null
                    && member.CurrentArea is not null
                    && area != member.CurrentArea
                    && area.tempRating != member.CurrentArea.tempRating)
                {
                    var position = ReturnAdjustedAreaPosition(area);
                    Mod.Log($"Sending {member.name} to better temperatures at {area.name} position {position}");
                    var job = new Job_GoHere(member.memberRH, position);
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

            var equipment = ObjectManager.instance.GetObjectsOfCategory(ObjectManager.ObjectCategory.ExerciseMachine).ToList();
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
            if (!member.HasEmptyQueues()
                || member.currentjob is not null
                || IsBadWeather())
            {
                return;
            }

            var collection = GetAllPlanters().Cast<Object_Planter>().Where(p =>
                !p.HasActiveInteractionMembers()
                && !p.beingUsed
                && p.GStage is not Object_Planter.GrowingStage.NoSeed
                && (p.CurrentWaterLevel <= 0
                    || p.GStage == Object_Planter.GrowingStage.Harvestable));

            foreach (var obj in collection)
            {
                var planter = obj;
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
            planter.beingUsed = true;
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

                foreach (var burningObject in BurnableObjects)
                {
                    if (!burningObject.isBurning
                        || burningObject.isBeingExtinguished
                        || burningObject.obj.beingUsed)
                    {
                        continue;
                    }

                    Mod.Log($"{burningObject.name} is burning and not being extinguished");
                    Job job;
                    var extinguishingSource = ObjectManager.instance.GetNearestObjectsOfType(ObjectManager.ObjectType.FireExtinguisher, member.transform.position)
                        .FirstOrDefault(o => !o.beingUsed);
                    if (extinguishingSource is not null)
                    {
                        Mod.Log($"Sending {member.name} to {burningObject.name} with extinguisher");
                        extinguishingSource.beingUsed = true;
                        var interaction = burningObject.GetComponent<ObjectInteraction_UseFireExtinguisher>();
                        job = new Job_UseFireExtinguisher(
                            member.memberRH, interaction, burningObject.obj, extinguishingSource.GetInteractionTransform(0), (Object_FireExtinguisher)extinguishingSource);
                    }
                    else
                    {
                        Mod.Log($"Sending {member.name} to extinguish {burningObject.name}");
                        var interaction = burningObject.GetComponent<ObjectInteraction_ExtinguishFire>();
                        var source = ObjectManager.instance.GetNearestObjectOfCategory(ObjectManager.ObjectCategory.WaterButt, member.transform.position);
                        job = new Job_ExtinguishFire(member.memberRH, interaction, burningObject.obj, source.GetInteractionTransform(0), source);
                    }

                    member.AddJob(job);
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
                || member.currentjob is not null)
            {
                return;
            }

            if (member.needs.fatigue.NormalizedValue * 100 > Mod.FatigueThreshold.Value)
            {
                return;
            }

            var bookCases = ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.Bookshelf)
                .Where(o => o.Status is not Object_Base.ObjectStatus.Construction or Object_Base.ObjectStatus.Deconstruction);
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
                        _ => null
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
            var hasRepairSkill = member.profession.PerceptionSkills.ContainsKey(ProfessionsManager.ProfessionSkillType.AutomaticRepairing);
            if (Mod.NeedRepairSkillToRepair.Value
                && !hasRepairSkill)
            {
                return;
            }

            var damagedObjects = ObjectManager.instance.integrityObjects.Where(i =>
                    !i.IsWithinHoldingCell
                    && i.integrityNormalised <= Mod.RepairThreshold.Value / 100f
                    && !i.HasActiveInteractionMembers()
                    && !i.beingUsed)
                .OrderBy(i => i.transform.position.PathDistanceTo(member.transform.position));

            foreach (var damagedObject in damagedObjects)
            {
                if (!damagedObject.IsSurfaceObject
                    || damagedObject.IsSurfaceObject
                    && !IsBadWeather())
                {
                    Mod.Log($"Sending {member.name} to repair {damagedObject.name} at {damagedObject.integrityNormalised * 100:f1}%");
                    var inter = damagedObject.GetComponent<ObjectInteraction_Repair>();
                    var job = new Job(member.memberRH, damagedObject, inter, damagedObject.GetInteractionTransform(0));
                    member.AddJob(job);
                    damagedObject.beingUsed = true;
                    break;
                }
            }
        }

        internal static void DoRest(Member member)
        {
            if (!member.HasEmptyQueues()
                || member.currentjob is not null
                || member.memberRH.memberAI.lastState == MemberAI.AiState.Rest)
            {
                return;
            }

            var bed = ObjectManager.instance.GetNearestObjectsOfCategory(ObjectManager.ObjectCategory.Bed, member.transform.position).FirstOrDefault(b => !b.beingUsed);
            if (bed is not null)
            {
                Job job = default;
                if (member.needs.fatigue.value > 20)
                {
                    job = new Job(member.memberRH, bed, bed.GetComponent<ObjectInteraction_Sleep>(), bed.transform);
                }
                else if (member.healthNormalised * 100 < 100)
                {
                    job = new Job(member.memberRH, bed, bed.GetComponent<ObjectInteraction_Rest>(), bed.transform);
                    lastState(member.memberRH.memberAI) = MemberAI.AiState.Rest;
                }

                if (job is not null)
                {
                    Mod.Log($"Sending {member.name} to rest up to zero fatigue");
                    // TODO damn dictionary to avoid spamming it
                    member.AddJob(job);
                    bed.beingUsed = true;
                }
            }
        }

        internal static void DoShelterCleaning(Member member)
        {
            if (EffectsManager.instance is null
                || !member.HasEmptyQueues()
                || member.needs.fatigue.NormalizedValue * 100 > Mod.FatigueThreshold.Value)
            {
                return;
            }

            var manager = AreaManager.instance;
            try
            {
                var mop = ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.Mop).FirstOrDefault(m => !m.beingUsed);
                var dirt = manager.areas.Sum(a => manager.CalculateDirtValue(a));
                if (mop is not null
                    && dirt > Mod.ShelterCleaningThreshold.Value)
                {
                    Mod.Log($"Sending {member.name} to clean the shelter");
                    var job = new Job_CleanShelter(member.memberRH, mop.GetComponent<ObjectInteraction_CleanShelter>(), (Object_MopAndBucket)mop, mop.transform);
                    member.AddJob(job);
                    mop.beingUsed = true;
                }
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }
        }

        private static void DoSolarPanelCleaning(Member member)
        {
            var panels = ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.SolarPanel);
            foreach (var obj in panels.OrderBy(p => p.transform.position.PathDistanceTo(member.transform.position)))
            {
                if (obj.beingUsed
                    || obj.IsSurfaceObject
                    && IsBadWeather())
                {
                    continue;
                }

                var panel = (Object_SolarPanel)obj;
                if (panel.dustPercent >= Mod.SolarCleaningThreshold.Value)
                {
                    Mod.Log($"Sending {member.name} to clean solar panel");
                    var job = new Job(member.memberRH, obj, panel.GetComponent<ObjectInteraction_CleanSolarPanel>(), obj.GetInteractionTransform(0));
                    member.AddJob(job);
                    panel.beingUsed = true;
                }
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

            List<BurnableObject> burning = new();
            FireCheckTimer += Time.deltaTime;
            if (FireCheckTimer > 1)
            {
                burning = BurnableObjects.Where(b => b.isBurning && !b.isBurntOut && !b.isBeingExtinguished).ToList();
            }

            FireCheckTimer--;
            if (BreachManager.instance.inProgress
                && !burning.All(b => b.obj.IsSurfaceObject)
                || !BreachManager.instance.inProgress
                && burning.Any())
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

            if (WaitingOnTimer(member))
            {
                return;
            }

            //Mod.Log($"{member.firstName} tick");
            foreach (var methodInfo in ExtraJobs)
            {
                if (member.HasEmptyQueues()
                    && member.currentjob is null)
                {
                    AccessTools.Method(typeof(MemberAI), "EvaluateNeeds").Invoke(member.memberRH.memberAI, new object[] { });
                    member.memberRH.memberAI.FindNeedsJob();
                    if (!member.HasEmptyQueues())
                    {
                        Mod.Log($"{member.name} found needs job {member.aiQueue[0]?.jobInteractionType}");
                    }
                }

                if (member.HasEmptyQueues()
                    && member.currentjob is null)
                {
                    //Mod.Log($"{member.name} {methodInfo.Name}");
                    methodInfo.Invoke(null, new object[] { member });
                    if (!member.HasEmptyQueues()
                        || member.currentjob is not null)
                    {
                        break;
                    }
                }
            }
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

        private static IEnumerable<Object_Base> GetAllPlanters()
        {
            List<Object_Base> result = new();
            result.AddRange(ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.Planter));
            result.AddRange(ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.EfficientPlanter));
            result.AddRange(ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.HydroponicPlanter));
            result.AddRange(ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.SmallPlanter));
            result.AddRange(ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.SurfacePlanter));
            result.AddRange(ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.Greenhouse));
            result.AddRange(ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.GreenhouseCamo));
            result.AddRange(ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.HugeGreenhouse));
            result.AddRange(ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.LargeGreenhouse));
            return result;
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

        private static bool WaitingOnTimer(Member member)
        {
            if (!MemberTimers.ContainsKey(member))
            {
                MemberTimers.Add(member, default);
            }

            MemberTimers[member] += Time.deltaTime;
            var variance = Random.Range(0, 2f);
            if (MemberTimers[member] < WaitDuration + variance)
            {
                return true;
            }

            MemberTimers[member] -= WaitDuration + variance;
            return false;
        }

        private static Vector3 ReturnAdjustedAreaPosition(Area area)
        {
            var areaTransform = area.transform.position;
            var size = area.areaCollider.size;
            var yPoint = size.normalized.y * 100;
            float height;
            if (area.isSurfaceArea)
            {
                height = yPoint;
            }
            else
            {
                height = area.transform.position.y;
            }

            var position = new Vector3(Random.Range(0, area.areaCollider.size.x), height, areaTransform.z);
            NavMesh.SamplePosition(position, out var hit, 5f, -1);
            return hit.position;
        }

        internal static void ShowFloatie(string message, BaseCharacter baseCharacter)
        {
            var offset = m_healthLossFloatingTextOffset(baseCharacter);
            Traverse.Create(baseCharacter).Field<FloatingTextPool_Shelter>(
                "m_floatingTextPool").Value.ShowFloatingText(message, baseCharacter.transform.position + offset, Color.magenta);
        }
    }
}
