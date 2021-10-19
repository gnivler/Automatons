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

        // cheat
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

        // bugfix
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

        [HarmonyPatch(typeof(ObjectInteraction_HarvestTrap), "Awake")]
        [HarmonyPostfix]
        public static void ObjectInteraction_HarvestTrapAwakePostfix(ObjectInteraction_HarvestTrap __instance)
        {
            Traps.Add(__instance);
        }

        // bugfix
        [HarmonyPatch(typeof(ObjectInteraction_CarryMeal), "SetFoodTaken")]
        [HarmonyPrefix]
        public static void ObjectInteraction_CarryMealSetFoodTakenPrefix(int id, ItemStack food, Dictionary<int, ItemStack> ___m_selectedFood)
        {
            ___m_selectedFood.Remove(id);
        }

        [HarmonyPatch(typeof(ObjectManager), "GetNearestObjectOfType")]
        [HarmonyPrefix]
        public static bool ObjectManagerGetNearestObjectOfTypePrefix(ref bool __runOriginal, ref Object_Base __result, ObjectManager.ObjectType type, Vector3 pos)
        {
            __result = ObjectManager.instance.GetNearestObjectsOfType(type, pos).FirstOrDefault();
            __runOriginal = false;
            return false;
        }

        [HarmonyPatch(typeof(OptionsPanel), "OnQuitToMainMenuConfirm")]
        [HarmonyPostfix]
        public static void OptionsPanelOnQuitToMainMenuConfirmPostfix()
        {
            ClearGlobals();
        }

        // way faster than FireManager.GetAllBurnableObjects()
        [HarmonyPatch(typeof(Object_Base), "Awake")]
        [HarmonyPostfix]
        public static void Object_BaseAwakePostfixBurnableObject(Object_Base __instance)
        {
            if (__instance.isBurnable)
            {
                var burnable = __instance.GetComponent<BurnableObject>();
                if (burnable is not null)
                {
                    if (!BurnableObjectsMap.ContainsKey(__instance))
                    {
                        Mod.Log($"Adding burnable {burnable.name}");
                        BurnableObjectsMap.Add(__instance, burnable);
                    }
                }
            }
        }

        // bugfix
        [HarmonyPatch(typeof(Object_Base), "ChooseValidInteractionPoint")]
        [HarmonyPostfix]
        public static void Object_BaseChooseValidInteractionPointPostfix(Object_Base __instance, ref Transform __result)
        {
            if (!__result)
            {
                Mod.Log("Bugfix - No interaction points on " + __instance.name);
                __result = __instance.interactions[0].transform;
            }
        }

        [HarmonyPatch(typeof(Object_Planter), "Update")]
        [HarmonyPostfix]
        public static void Object_PlanterUpdatePostfix(Object_Planter __instance)
        {
            MaintainPlantersList(__instance);
        }

        [HarmonyPatch(typeof(MemberAI), "Wander")]
        [HarmonyPrefix]
        public static bool MemberAIWanderPrefix(ref bool __runOriginal)
        {
            __runOriginal = false;
            return false;
        }

        [HarmonyPatch(typeof(Member), "Update")]
        [HarmonyPostfix]
        public static void MemberUpdatePostfix(Member __instance)
        {
            if (__instance.OutOnExpedition
                || !ReadyToDoJob(__instance))
            {
                return;
            }

            FindJob(__instance);
        }

        [HarmonyPatch(typeof(SaveManager), "LoadFromCurrentSlot")]
        [HarmonyPostfix]
        public static void SaveManagerLoadFromCurrentSlotPostfix()
        {
            ClearGlobals();
        }
    }
}
