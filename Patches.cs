using System.Collections.Generic;
using System.Linq;
using System.Management.Instrumentation;
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
        private static bool initialized;

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
            if (!initialized
                && MemberManager.instance.GetAllShelteredMembers().All(m => m.name != "Sam Smith"))
            {
                initialized = true;
            }

            if (!initialized
                || DisabledAutomatonSurvivors.Contains(__instance)
                || __instance.OutOnExpedition
                || __instance.OutOnLoan)
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

        [HarmonyPatch(typeof(SlaveAI), "Wander")]
        [HarmonyPrefix]
        public static bool SlaveAIWanderPrefix() => false;
    }
}

