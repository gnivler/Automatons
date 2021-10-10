using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static Automatons.Helper;

// ReSharper disable RedundantAssignment 
// ReSharper disable InconsistentNaming

namespace Automatons
{
    public static class Patches
    {
        internal static bool Cheat;

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
        }

        [HarmonyPatch(typeof(Object_Planter), "Update")]
        [HarmonyPostfix]
        public static void Object_PlanterUpdatePostfix(Object_Planter __instance)
        {
            var planter = __instance;
            if (planter.GStage is not Object_Planter.GrowingStage.NoSeed
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
        public static bool ObjectManagerGetNearestObjectsOfTypePostfix(ref bool __runOriginal, ref List<Object_Base> __result, ObjectManager.ObjectType type, Vector3 pos)
        {
            var objectsOfType = ObjectManager.instance.GetObjectsOfType(type);
            __result = objectsOfType.OrderBy(o => o.transform.position, new CompareVector3()).ToList();
            __runOriginal = false;
            return false;
        }

        [HarmonyPatch(typeof(ObjectManager), "GetNearestObjectOfType")]
        [HarmonyPrefix]
        public static bool ObjectManagerGetNearestObjectOfTypePostfix(ref bool __runOriginal, ref Object_Base __result, ObjectManager.ObjectType type, Vector3 pos)
        {
            __result = ObjectManager.instance.GetNearestObjectsOfType(type, pos).FirstOrDefault();
            __runOriginal = false;
            return false;
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
    }
}
