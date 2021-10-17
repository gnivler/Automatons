using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static Automatons.Helper;
using Random = UnityEngine.Random;

// ReSharper disable RedundantAssignment 
// ReSharper disable InconsistentNaming

namespace Automatons
{
    public static class Patches
    {
        internal static bool Cheat;
        private static readonly Dictionary<MemberAI, float> WaitTimers = new();
        private static readonly Dictionary<Object_Planter, float> PlanterTimers = new();
        private static readonly Dictionary<MemberAI, float> InteractionTimers = new();
        private const float InteractionTickGameSeconds = 30;

        [HarmonyPatch(typeof(ObjectInteraction_Base), "RegisterForInteraction")]
        [HarmonyPrefix]
        public static bool ObjectInteraction_BaseRegisterForInteractionPrefix(ObjectInteraction_Base __instance,
            ref List<Member> ___interactionMembers,
            ref Dictionary<int, float> ___interactionTimers,
            MemberReferenceHolder memberRH,
            ref bool __runOriginal,
            ref bool __result)
        {
            if (__instance.IsInteractionAvailable(memberRH, true))
            {
                ___interactionMembers.Add(memberRH.member);
                if (!___interactionTimers.ContainsKey(memberRH.member.GetID))
                {
                    ___interactionTimers.Add(memberRH.member.GetID, 0f);
                }

                __instance.obj.beingUsed = true;
                __result = true;
            }

            __runOriginal = false;
            return false;
        }

        [HarmonyPatch(typeof(Object_Base), "ChooseValidInteractionPoint")]
        [HarmonyPostfix]
        public static void Object_BaseChooseValidInteractionPointPostfix(Object_Base __instance, ref Transform __result)
        {
            if (!__result)
            {
                Mod.Log("Bugfix - No interaction points on " + __instance.name);
                __result = __instance.GetInteractionTransform(0);
            }
        }

        // way faster than FireManager.GetAllBurnableObjects()
        [HarmonyPatch(typeof(Object_Base), "Awake")]
        [HarmonyPostfix]
        public static void Object_BaseAwakePostfixBurnableObject(Object_Base __instance)
        {
            if (__instance.isBurnable)
            {
                BurnableObjectsMap.Add(__instance, __instance.GetComponent<BurnableObject>());
            }
        }

        [HarmonyPatch(typeof(FireManager), "Awake")]
        [HarmonyPostfix]
        public static void FireManagerAwakePost(FireManager __instance)
        {
            __instance.gameObject.AddComponent<FireTick>();
        }

        [HarmonyPatch(typeof(ObjectInteraction_HarvestTrap), "Awake")]
        [HarmonyPostfix]
        public static void ObjectInteraction_HarvestTrapAwakePostfix(ObjectInteraction_HarvestTrap __instance)
        {
            Traps.Add(__instance);
        }

        [HarmonyPatch(typeof(SaveManager), "LoadFromCurrentSlot")]
        [HarmonyPostfix]
        public static void SaveManagerLoadFromCurrentSlotPostfix()
        {
            ClearGlobals();
        }

        [HarmonyPatch(typeof(OptionsPanel), "OnQuitToMainMenuConfirm")]
        [HarmonyPostfix]
        public static void OptionsPanelOnQuitToMainMenuConfirmPostfix()
        {
            ClearGlobals();
        }

        [HarmonyPatch(typeof(MemberAI), "Wander")]
        [HarmonyPrefix]
        public static bool WanderPrefix(ref bool __runOriginal)
        {
            __runOriginal = false;
            return false;
        }

        [HarmonyPatch(typeof(MemberAI), "ChooseNextAIAction")]
        [HarmonyPostfix]
        public static void ChooseNextAIActionPostfix(MemberAI __instance)
        {
            if (DisabledAutomatonSurvivors.Contains(__instance.memberRH.member))
            {
                return;
            }

            if (BreachManager.instance.inProgress || !MemberManager.instance.IsInitialSpawnComplete)
            {
                return;
            }

            var member = __instance.memberRH.member;
            if (member.m_breakdownController.isHavingBreakdown
                || member.OutOnExpedition
                || !IsJobless(member))
            {
                return;
            }

            if (!WaitTimers.ContainsKey(__instance))
            {
                WaitTimers.Add(__instance, default);
            }

            WaitTimers[__instance] += Time.deltaTime;
            var waitTime = RandomGameSeconds();
            if (WaitTimers[__instance] < waitTime)
            {
                return;
            }

            WaitTimers[__instance] -= waitTime;

            foreach (var jobMethod in ExtraJobs)
            {
                jobMethod.Invoke(null, new object[] { member });
                if (!IsJobless(member))
                {
                    break;
                }
            }

            AccessTools.Method(typeof(MemberAI), "EvaluateNeeds").Invoke(member.memberRH.memberAI, new object[] { });
            member.memberRH.memberAI.FindNeedsJob();

            if (IsJobless(member)
                || lastState(member.memberRH.memberAI) is MemberAI.AiState.Idle or MemberAI.AiState.Wander)
            {
                lastState(member.memberRH.memberAI) = MemberAI.AiState.Rest;
            }
            else
            {
                Mod.Log($"{member.name} needs {member.memberRH.memberAI.lastState}");
            }
        }

        [HarmonyPatch(typeof(Object_Planter), "Update")]
        [HarmonyPostfix]
        public static void Object_PlanterUpdatePostfix(Object_Planter __instance)
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

        [HarmonyPatch(typeof(ObjectManager), "GetNearestObjectOfType")]
        [HarmonyPrefix]
        public static bool ObjectManagerGetNearestObjectOfTypePrefix(ref bool __runOriginal, ref Object_Base __result, ObjectManager.ObjectType type, Vector3 pos)
        {
            __result = ObjectManager.instance.GetNearestObjectsOfType(type, pos).FirstOrDefault();
            __runOriginal = false;
            return false;
        }

        // allow needs to be checked while doing reading/exercise
        [HarmonyPatch(typeof(ObjectInteraction_Base), "UpdateInteraction")]
        public static void Postfix(ObjectInteraction_Base __instance, MemberReferenceHolder memberRH, bool __result)
        {
            if (!__result
                && __instance.interactionType is InteractionTypes.InteractionType.ReadCharismaBook
                    or InteractionTypes.InteractionType.ReadIntelligenceBook
                    or InteractionTypes.InteractionType.ReadPerceptionBook
                    or InteractionTypes.InteractionType.ReadStoryBook
                    or InteractionTypes.InteractionType.Exercise)
            {
                if (!InteractionTimers.TryGetValue(memberRH.memberAI, out _))
                {
                    InteractionTimers.Add(memberRH.memberAI, default);
                }

                InteractionTimers[memberRH.memberAI] += Time.deltaTime;
                if (InteractionTimers[memberRH.memberAI] < InteractionTickGameSeconds)
                {
                    return;
                }

                InteractionTimers[memberRH.memberAI] -= InteractionTickGameSeconds;
                var job = memberRH.member.currentjob;
                var cachedJob = new Job(memberRH, job.obj, job.objInteraction, job.targetTransform);
                foreach (var jobMethod in ExtraJobs)
                {
                    jobMethod.Invoke(null, new object[] { memberRH.member });
                    if (!IsJobless(memberRH.member))
                    {
                        // take "exercise", copy it, stop the interaction, add the cached copy then proceed below
                        // to add the interaction
                        job = memberRH.member.currentjob;
                        var cachedJob2 = new Job(memberRH, job.obj, job.objInteraction, job.targetTransform);
                        memberRH.member.RequestCancelJob(0);
                        memberRH.member.AddJob(cachedJob2);
                        break;
                    }
                }

                if (memberRH.memberAI.currentPriorityNeed is not NeedsStat.NeedsStatType.Max)
                {
                    AccessTools.Method(typeof(MemberAI), "EvaluateNeeds").Invoke(memberRH.memberAI, new object[] { });
                    memberRH.memberAI.FindNeedsJob();

                    // only place allowing 2 jobs
                    memberRH.member.AddJob(cachedJob);
                }
            }
        }

        [HarmonyPatch(typeof(CraftingPanel), "OnRecipeSlotPress")]
        [HarmonyPrefix]
        public static void CraftingPanelOnRecipeSlotPressPrefix(RecipeSlot recipeSlot)
        {
            if (Cheat)
            {
                foreach (var stack in recipeSlot.recipe.ingredients)
                {
                    ShelterInventoryManager.instance.inventory.AddItems(stack);
                }
            }
        }

        [HarmonyPatch(typeof(ObjectInteraction_CarryMeal), "SetFoodTaken"), HarmonyPrefix]
        public static void SetFoodTaken(int id, ItemStack food, Dictionary<int, ItemStack> ___m_selectedFood)
        {
            ___m_selectedFood.Remove(id);
        }
    }
}
