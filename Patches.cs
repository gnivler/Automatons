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

        [HarmonyPatch(typeof(ObjectManager), "GetNearestObjectsOfType")]
        [HarmonyPrefix]
        public static bool ObjectManagerGetNearestObjectsOfTypePrefix(ref bool __runOriginal, ref List<Object_Base> __result,
            Dictionary<ObjectManager.ObjectType, List<Object_Base>> ___objects, ObjectManager.ObjectType type, Vector3 pos)
        {
            __runOriginal = false;
            var objectsOfType = ___objects[type];
            objectsOfType.Sort((left, right) => CompareFloats(left.transform.position.PathDistanceTo(pos), right.transform.position.PathDistanceTo(pos)));
            __result = objectsOfType;
            return false;

            int CompareFloats(float left, float right)
            {
                if (left < right)
                {
                    return -1;
                }

                if (left > right)
                {
                    return 1;
                }

                return 0;
            }
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
        [HarmonyPatch(typeof(BurnableObject), "Awake")]
        [HarmonyPostfix]
        public static void BurnableObjectAwakePostfix(BurnableObject __instance)
        {
            BurnableObjects.Add(__instance);
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

        [HarmonyPatch(typeof(MemberAI), "Awake")]
        [HarmonyPostfix]
        public static void MemberAIAwakePatch(MemberAI __instance)
        {
            var go = __instance.gameObject.AddComponent<InteractionNeeds>();
            go.Member = __instance.memberRH.member;
        }

        [HarmonyPatch(typeof(Member), "Update")]
        [HarmonyPostfix]
        public static void MemberUpdatePostfix(Member __instance)
        {
            if (!ReadyToDoJob(__instance))
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
