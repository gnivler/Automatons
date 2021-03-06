using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

// ReSharper disable UnusedParameter.Local   out _ 
// ReSharper disable InconsistentNaming

namespace Automatons
{
    public static class Helper
    {
        internal static List<BurnableObject> BurnableObjects = new();
        internal static readonly HashSet<Member> DisabledAutomatonSurvivors = new();
        private static readonly Dictionary<Member, float> MemberTimers = new();
        internal static readonly HashSet<ObjectInteraction_HarvestTrap> Traps = new();
        private static float fireCheckTimer;
        private const int WaitDuration = 4;

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

        internal static void CancelEverythingRelatedToMemberActivity(Member member)
        {
            // check every interaction by member jobs and cancel that
            foreach (var job in member.jobQueue)
            {
                if (job.obj is not null)
                {
                    try
                    {
                        job.obj.interactions.Do(i => i.CancelAllJobs());
                    }
                    catch (Exception ex)
                    {
                        Mod.Log(ex);
                    }
                }
            }

            if (member.currentjob?.obj is not null)
            {
                member.currentjob.obj.interactions.Do(i => i.CancelAllJobs());
            }

            member.CancelJobsImmediately();
        }

        internal static void ClearGlobals()
        {
            DisabledAutomatonSurvivors.Clear();
            BurnableObjects.Clear();
            Traps.Clear();
        }

        internal static void DoEnvironmentSeeking(Member member, bool _)
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

            if (member.jobQueueCount == 0
                && member.currentTemperature is not TemperatureRating.Okay)
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

        internal static void DoExercise(Member member, bool _)
        {
            if (member.jobQueueCount > 0
                || member.needs.fatigue.NormalizedValue * 100 > Mod.FatigueThreshold.Value)
            {
                return;
            }

            var equipment = ObjectManager.instance.GetObjectsOfCategory(ObjectManager.ObjectCategory.ExerciseMachine).ToList();
            equipment.Shuffle();
            foreach (var objectBase in equipment.Where(e => !e.isBroken && !e.beingUsed && e.GetComponent<Object_Integrity>().integrityNormalised * 100 > 0))
            {
                var exerciseMachine = (Object_ExerciseMachine)objectBase;
                if (!exerciseMachine.HasActiveJobs()
                    && (exerciseMachine.InLevelRange(member.memberRH)
                        || exerciseMachine.InLevelRangeFortitude(member.memberRH)))
                {
                    Mod.Log($"Sending {member.name} to exercise at {exerciseMachine.name} with {member.needs.fatigue.NormalizedValue * 100:f1}% fatigue");
                    var exerciseInteraction = exerciseMachine.GetInteractionByType(InteractionTypes.InteractionType.Exercise);
                    var job = new Job(member.memberRH, exerciseMachine, exerciseInteraction, exerciseMachine.GetInteractionTransform(0));
                    member.AddJob(job);

                    exerciseMachine.beingUsed = true;
                    break;
                }
            }
        }

        internal static void DoFarming(Member member, bool _)
        {
            if (member.jobQueueCount > 0)
            {
                return;
            }

            var planters = GetAllPlanters().Cast<Object_Planter>().Where(planter =>
                    !planter.isBroken
                    && planter.GStage is not Object_Planter.GrowingStage.NoSeed
                    && !planter.HasActiveJobs()
                    && (planter.CurrentWaterLevel <= Mod.RepairThreshold.Value
                        || planter.GStage == Object_Planter.GrowingStage.Harvestable))
                .ToList();

            if (IsBadWeather()
                && planters.All(planter => planter.IsSurfaceObject))
            {
                return;
            }

            foreach (var planter in planters)
            {
                if (IsBadWeather()
                    && planter.IsSurfaceObject
                    || !planter.isPowered)
                {
                    continue;
                }

                if (planter.GStage == Object_Planter.GrowingStage.Harvestable)
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
            var job = new Job(member.memberRH, planter, interaction, planter.ChooseValidInteractionPoint());
            member.AddJob(job);
        }

        private static void DoFirefighting(Member member, bool _)
        {
            try
            {
                if (!Mod.Firefighting.Value
                    || member.currentjob?.jobInteractionType is InteractionTypes.InteractionType.ExtinguishFire)
                {
                    return;
                }

                BurnableObjects = BurnableObjects.Where(b => b is not null).ToList();
                foreach (var burningObject in BurnableObjects)
                {
                    if (!burningObject.isBurning
                        || burningObject.isBeingExtinguished)
                    {
                        continue;
                    }

                    Mod.Log($"{burningObject.name} is burning and not being extinguished");
                    Job job;
                    var extinguisher = (Object_FireExtinguisher)ObjectManager.instance.GetNearestObjectsOfType(ObjectManager.ObjectType.FireExtinguisher, member.transform.position)
                        .FirstOrDefault(o => !((Object_FireExtinguisher)o).InUse);
                    if (extinguisher is not null)
                    {
                        Mod.Log($"Sending {member.name} to {burningObject.name} with extinguisher");
                        extinguisher.beingUsed = true;
                        var useFireExtinguisher = burningObject.GetComponent<ObjectInteraction_FireExtinguisherAction>();
                        job = new Job_UseFireExtinguisher(member.memberRH, useFireExtinguisher, burningObject.GetComponent<Object_Base>(), extinguisher.ChooseValidInteractionPoint(), extinguisher);
                    }
                    else
                    {
                        Mod.Log($"Sending {member.name} to extinguish {burningObject.name}");
                        var interaction = burningObject.GetComponent<ObjectInteraction_ExtinguishFire>();
                        var source = ObjectManager.instance.GetNearestObjectOfCategory(ObjectManager.ObjectCategory.WaterButt, member.transform.position);
                        job = new Job_ExtinguishFire(member.memberRH, interaction, burningObject.GetComponent<Object_Base>(), source.ChooseValidInteractionPoint(), source);
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

        internal static void DoFirstAid(Member member, bool _)
        {
            if (!member.illness.IsIll()
                || member.jobQueue.Any(job => job.jobInteractionType != InteractionTypes.InteractionType.Medicate))
            {
                return;
            }

            Object_Base closestCabinet = default;
            ItemStack firstAidItem = default;
            if (member.illness.bleeding.isActive)
            {
                Mod.Log($"{member.name} bleeding");
                closestCabinet = GetClosestCabinetWithItem(member, "bandages");
                if (closestCabinet is null)
                {
                    return;
                }

                firstAidItem = ((Object_MedicineCabinet)closestCabinet).inventory.inventory["bandages"].First();
            }
            else if (member.illness.radiation.isActive)
            {
                Mod.Log($"{member.name} radiated");
                closestCabinet = GetClosestCabinetWithItem(member, "antirad");
                if (closestCabinet is null)
                {
                    return;
                }

                firstAidItem = ((Object_MedicineCabinet)closestCabinet).inventory.inventory["antirad"].First();
            }
            else if (member.illness.foodPoisoning.isActive)
            {
                Mod.Log($"{member.name} sick");
                closestCabinet = GetClosestCabinetWithItem(member, "antiemetic");
                if (closestCabinet is null)
                {
                    return;
                }

                firstAidItem = ((Object_MedicineCabinet)closestCabinet).inventory.inventory["antiemetic"].First();
            }

            if (closestCabinet is null
                || firstAidItem == null)
            {
                return;
            }

            var interaction = closestCabinet.GetInteractionByType(InteractionTypes.InteractionType.Medicate);
            ((ObjectInteraction_Medicate)interaction).SetMedicineToBeTaken(firstAidItem);
            Mod.Log($"Sending {member} to use {firstAidItem.def.name}");
            member.CancelJobsImmediately();
            member.AddJob(new Job(member.memberRH, closestCabinet, closestCabinet.GetInteractionByType(InteractionTypes.InteractionType.Medicate), closestCabinet.ChooseValidInteractionPoint()));
        }

        private static Object_Base GetClosestCabinetWithItem(Member member, string item)
        {
            return ObjectManager.instance.GetObjectsOfCategory(ObjectManager.ObjectCategory.MedicineCabinet)
                .Where(cabinet => ((Object_MedicineCabinet)cabinet).inventory.ContainsItems(item, 1)
                                  && cabinet.GetInteractionByType(InteractionTypes.InteractionType.Medicate).JobCount == 0)
                .OrderBy(cabinet => cabinet.transform.position.PathDistanceTo(member.transform.position))
                .FirstOrDefault();
        }

        internal static void DoHarvestTraps(Member member, bool _)
        {
            if (member.jobQueueCount > 0)
            {
                return;
            }

            for (var i = 0; i < Traps.Count; i++)
            {
                var trapInteraction = Traps.ElementAt(i);
                var snareTrap = (Object_SnareTrap)trapInteraction.obj;
                if (snareTrap.CanTrapAnimals()
                    || snareTrap.HasActiveJobs()
                    || IsBadWeather())
                {
                    continue;
                }

                Mod.Log($"Sending {member.name} to harvest snare trap {snareTrap.objectId}");
                var job = new Job(member.memberRH, trapInteraction.obj, trapInteraction, snareTrap.ChooseValidInteractionPoint());
                member.AddJob(job);
                break;
            }
        }

        internal static void DoReading(Member member, bool idleReading)
        {
            if (member.jobQueueCount > 0 ||
                member.needs.fatigue.NormalizedValue * 100 > Mod.FatigueThreshold.Value)
            {
                return;
            }

            var bookCases = ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.Bookshelf)
                .Where(o => o.Status is not Object_Base.ObjectStatus.Construction or Object_Base.ObjectStatus.Deconstruction);
            foreach (var bookCase in bookCases.Where(b => !b.HasActiveJobs()))
            {
                ObjectInteraction_Base objectInteractionBase = default;
                while (objectInteractionBase is null)
                {
                    if (!HasBooks(member, idleReading))
                    {
                        break;
                    }

                    var bookType = (ItemDef.BookType)Random.Range(2, 5);
                    objectInteractionBase = bookType switch
                    {
                        ItemDef.BookType.Charisma when HasBook(ItemDef.BookType.Charisma, member, idleReading) => bookCase.GetComponent<ObjectInteraction_ReadCharismaBook>(),
                        ItemDef.BookType.Intelligence when HasBook(ItemDef.BookType.Intelligence, member, idleReading) => bookCase.GetComponent<ObjectInteraction_ReadIntelligenceBook>(),
                        ItemDef.BookType.Perception when HasBook(ItemDef.BookType.Perception, member, idleReading) => bookCase.GetComponent<ObjectInteraction_ReadPerceptionBook>(),
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

        internal static void DoRepairObjects(Member member, bool _)
        {
            var hasRepairSkill = member.profession.PerceptionSkills.ContainsKey(ProfessionsManager.ProfessionSkillType.AutomaticRepairing);
            if (Mod.NeedRepairSkillToRepair.Value
                && !hasRepairSkill)
            {
                return;
            }

            var damagedObjects = ObjectManager.instance.integrityObjects.Where(i =>
                    !i.IsWithinHoldingCell
                    && i.integrityNormalised * 100 <= Mod.RepairThreshold.Value)
                .OrderBy(i => i.transform.position.PathDistanceTo(member.transform.position))
                .ToList();

            for (var index = 0; index < damagedObjects.Count; index++)
            {
                var damagedObject = damagedObjects[index];
                if (damagedObject.HasActiveJobs())
                {
                    continue;
                }

                if (!damagedObject.IsSurfaceObject
                    || damagedObject.IsSurfaceObject
                    && !IsBadWeather())
                {
                    Mod.Log($"Sending {member.name} to repair {damagedObject.name} at {damagedObject.integrityNormalised * 100:f1}%");
                    var i = damagedObject.GetComponent<ObjectInteraction_Repair>();
                    var job = new Job(member.memberRH, damagedObject, i, i.transform);
                    member.AddJob(job);
                    damagedObject.beingUsed = true;
                    break;
                }
            }
        }

        internal static void DoRest(Member member, bool _)
        {
            if (!Mod.RestUp.Value
                || member.jobQueueCount > 0
                || Traverse.Create(member.memberRH.memberAI).Field<MemberAI.AiState>("lastState").Value == MemberAI.AiState.Rest)
            {
                return;
            }

            var bed = ObjectManager.instance.GetNearestObjectsOfCategory(ObjectManager.ObjectCategory.Bed, member.transform.position)
                .FirstOrDefault(b => b.IsUsable() && !b.IsWithinHoldingCell);
            if (bed is not null
                && bed.GetInteractionByType(InteractionTypes.InteractionType.Sleep).InteractionMemberCount == 0)
            {
                Job job = default;
                if (member.needs.fatigue.Value > 20)
                {
                    job = new Job(member.memberRH, bed, bed.GetComponent<ObjectInteraction_Sleep>(), bed.GetInteractionByType(InteractionTypes.InteractionType.Sleep).transform);
                    Traverse.Create(member.memberRH.memberAI).Field<MemberAI.AiState>("lastState").Value = MemberAI.AiState.Rest;
                }
                else if (member.healthNormalised * 100 < 100)
                {
                    job = new Job(member.memberRH, bed, bed.GetComponent<ObjectInteraction_Rest>(), bed.GetInteractionByType(InteractionTypes.InteractionType.Sleep).transform);
                    Traverse.Create(member.memberRH.memberAI).Field<MemberAI.AiState>("lastState").Value = MemberAI.AiState.Rest;
                }

                if (job is not null)
                {
                    Mod.Log($"Sending {member.name} to {job.jobInteractionType}");

                    member.AddJob(job);
                }
            }
        }

        internal static void DoShelterCleaning(Member member, bool _)
        {
            if (!Mod.ShelterCleaning.Value
                || EffectsManager.instance is null
                || member.jobQueueCount > 0
                || member.needs.fatigue.NormalizedValue * 100 > Mod.FatigueThreshold.Value)
            {
                return;
            }

            var manager = AreaManager.instance;
            float tableDirt = default;
            float generatorDirt = default;
            var tables = ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.TableAndChairs);
            var generators = ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.Generator);
            foreach (var area in manager.areas)
            {
                // copied from CalculateDirtValue
                foreach (var table in tables)
                {
                    var bounds = area.areaCollider.bounds;
                    if (bounds.Contains(table.transform.position))
                        tableDirt += 5f * table.GetComponent<Object_TableAndChairs>().DirtyPlates;
                }

                foreach (var table in generators)
                {
                    var bounds = area.areaCollider.bounds;
                    if (bounds.Contains(table.transform.position))
                        generatorDirt += 5f * table.GetComponent<Object_Generator>().WasteCount;
                }
            }

            var dirt = manager.areas.Sum(area => (float) AccessTools.Method(typeof(AreaManager), "CalculateDirtValue").Invoke(manager, new object[] { area }));

            if (dirt + tableDirt + generatorDirt < Mod.ShelterCleaningThreshold.Value)
            {
                return;
            }

            Job job;
            if (generatorDirt > 0)
            {
                foreach (var generator in generators.Where(gen =>
                    ((Object_Generator)gen).WasteCount > 0
                    && gen.GetComponent<ObjectInteraction_CleanObject>().JobCount == 0))
                {
                    Mod.Log($"Sending {member.name} to clean up fuel cans");
                    job = new Job(member.memberRH, generator, generator.GetInteractionByType(InteractionTypes.InteractionType.Cleaning), generator.ChooseValidInteractionPoint());
                    member.AddJob(job);
                    return;
                }
            }

            if (tableDirt > 0)
            {
                foreach (var table in tables.Where(table =>
                    ((Object_TableAndChairs)table).DirtyPlates > 0
                    && table.GetComponent<ObjectInteraction_CleanObject>().JobCount == 0))
                {
                    Mod.Log($"Sending {member.name} to clean table");
                    job = new Job(member.memberRH, table, table.GetInteractionByType(InteractionTypes.InteractionType.Cleaning), table.ChooseValidInteractionPoint());
                    member.AddJob(job);
                    return;
                }
            }

            var mop = ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.Mop).FirstOrDefault(m => m.interactions.Sum(interactionBase => interactionBase.JobCount) == 0);
            if (mop is not null
                && dirt + tableDirt + generatorDirt > Mod.ShelterCleaningThreshold.Value)
            {
                Mod.Log($"Sending {member.name} to clean the shelter");
                job = new Job_CleanShelter(member.memberRH, mop.GetComponent<ObjectInteraction_CleanShelter>(), (Object_MopAndBucket)mop, mop.transform, true);
                member.AddJob(job);
            }
        }

        private static void DoSolarPanelCleaning(Member member, bool _)
        {
            var panels = ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.SolarPanel);
            foreach (var obj in panels.OrderBy(panel => panel.transform.position.PathDistanceTo(member.transform.position)))
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

            fireCheckTimer += Time.deltaTime;
            bool onlyBurningOnSurface = false;
            bool anythingUnhandled = false;
            if (fireCheckTimer > 1)
            {
                onlyBurningOnSurface = BurnableObjects.Where(o => o.isBurning && !o.isBeingExtinguished).All(b => b.GetComponent<Object_Base>().IsSurfaceObject);
                anythingUnhandled = BurnableObjects.Any(o => o.isBurning && !o.isBeingExtinguished);
            }

            fireCheckTimer--;
            if (BreachManager.instance.inProgress
                && BurnableObjects.Count(o => o.isBurning && !o.isBeingExtinguished) > 0
                && !onlyBurningOnSurface
                || !BreachManager.instance.inProgress
                && anythingUnhandled)
            {
                Mod.Log($"Cancelling everything for {member.name}");
                // check every interaction by member jobs and cancel that
                foreach (var job in member.jobQueue)
                {
                    if (job.obj is not null)
                    {
                        job.obj.beingUsed = false;
                        job.obj.interactions.Do(i =>
                        {
                            if (i.interactionType is not InteractionTypes.InteractionType.ExtinguishFire)
                            {
                                i.CancelAllJobs();
                            }
                        });
                    }
                }

                member.CancelJobsImmediately();
                DoFirefighting(member, false);
                return;
            }

            if (BreachManager.instance.inProgress
                || member.jobQueueCount > 0
                || member.memberRH.ExternalMeshLinker.SleepParticles.isEmitting)
            {
                return;
            }

            if (WaitingOnTimer(member))
            {
                return;
            }

            Mod.Log($"{member.name} done waiting on timer");
            foreach (var methodInfo in ExtraJobs)
            {
                if (member.aiQueueCount + member.jobQueueCount == 0)
                {
                    if (Mod.FirstAid.Value)
                    {
                        DoFirstAid(member, false);
                    }

                    if (member.aiQueueCount + member.jobQueueCount > 0)
                    {
                        return;
                    }

                    AccessTools.Method(typeof(MemberAI), "EvaluateNeeds").Invoke(member.memberRH.memberAI, new object[] { });
                    member.memberRH.memberAI.FindNeedsJob();
                    if (member.aiQueueCount + member.jobQueueCount > 0)
                    {
                        Mod.Log($"{member.name} found needs job {member.aiQueue[0]?.jobInteractionType}");
                        return;
                    }
                }

                if (member.aiQueueCount + member.jobQueueCount == 0)
                {
                    //Mod.Log($"{member.name} {methodInfo.Name}");
                    methodInfo.Invoke(null, new object[] { member, false });
                    if (member.jobQueueCount > 0)
                    {
                        //Mod.Log($"{member.name} found job {member.jobQueue[0]?.jobInteractionType}");
                        return;
                    }
                }
            }
        }

        private static bool HasBook(ItemDef.BookType type, Member member, bool idleReading)
        {
            if (type == ItemDef.BookType.Charisma)
            {
                if (BookManager.Instance.NumberOfUniqueCharismaBooks() == 0)
                {
                    return false;
                }

                var skillLevel = member.baseStats.Charisma.Level;
                if (skillLevel == member.baseStats.Charisma.LevelCap)
                {
                    return false;
                }

                if (idleReading)
                {
                    //BookManager.Instance.RemoveCharismaBook(BookManager.Instance.CharismaBooks.Max(kvp => kvp.Key));
                    return true;
                }

                foreach (var kvp in BookManager.Instance.CharismaBooks)
                {
                    if (kvp.Value != 0)
                    {
                        MinMaxLevels(kvp.Key, out var min, out var max);
                        if (skillLevel >= min && skillLevel <= max)
                        {
                            // current game build doesn't appear to care how many of a book you have
                            //BookManager.Instance.RemoveCharismaBook(kvp.Key);
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

                if (idleReading)
                {
                    //BookManager.Instance.RemoveCharismaBook(BookManager.Instance.IntelligenceBooks.Max(kvp => kvp.Key));
                    return true;
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

                if (idleReading)
                {
                    //BookManager.Instance.RemoveCharismaBook(BookManager.Instance.PerceptionBooks.Max(kvp => kvp.Key));
                    return true;
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

        private static bool HasBooks(Member member, bool idleReading)
        {
            return HasBook(ItemDef.BookType.Charisma, member, idleReading)
                   || HasBook(ItemDef.BookType.Intelligence, member, idleReading)
                   || HasBook(ItemDef.BookType.Perception, member, idleReading);
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
            result.Do(o => Mod.Log(o.GetType().Name));
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
            var variance = Random.Range(0, 3f);
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
            float x, y;
            if (area.isSurfaceArea)
            {
                x = size.normalized.x * 100;
                x = Random.Range(x / 2 - 10, x);
                y = size.normalized.y * 100;
            }
            else
            {
                x = Random.Range(areaTransform.x / 2 - 10, areaTransform.x);
                y = area.transform.position.y;
            }

            var position = new Vector3(x, y, areaTransform.z);
            NavMesh.SamplePosition(position, out var hit, 5f, -1);
            return hit.position;
        }

        internal static void ShowFloatie(string message, BaseCharacter baseCharacter)
        {
            var offset = baseCharacter.healthLossFloatingTextOffset;
            baseCharacter.floatingTextPool.ShowFloatingText(message, baseCharacter.transform.position + offset, Color.magenta);
        }
    }
}
