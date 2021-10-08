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
        private static bool survivorsInitialized;

        [HarmonyPatch(typeof(BurnableObject), "Awake")]
        public static void Postfix(Object_Base ___obj)
        {
            BurnableObjects.Add(___obj);
        }

        [HarmonyPatch(typeof(ObjectInteraction_HarvestTrap), "Awake")]
        public static void Postfix(ObjectInteraction_HarvestTrap __instance)
        {
            Traps.Add(__instance);
        }

        [HarmonyPatch(typeof(SaveManager), "LoadFromCurrentSlot")]
        public static void LoadPostfix()
        {
            ClearGlobals();
        }

        [HarmonyPatch(typeof(OptionsPanel), "OnQuitToMainMenuConfirm")]
        [HarmonyPostfix]
        public static void QuitPostfix()
        {
            ClearGlobals();
        }

        [HarmonyPatch(typeof(Member), "UpdateJobs")]
        //[HarmonyPatch(typeof(MemberAI), "Wander")]
        //public static void Postfix(MemberAI __instance)
        public static void Postfix(Member __instance)
        {
            if (BreachManager.instance.inProgress)
            {
                return;
            }

            var member = __instance.memberRH.member;
            if (IsAvailable(member))
            {
                // Sam Smith is an uninitialized survivor so if any exist, it's not done initializing everyone
                if (!survivorsInitialized
                    && MemberManager.instance.GetAllShelteredMembers().All(m => m.name != "Sam Smith"))
                {
                    survivorsInitialized = true;
                }

                // have to force it to invoke again to populate all survivors
                if (!survivorsInitialized)
                {
                    return;
                }

                ProcessBurningObjects(member);
                if (!IsAvailable(member))
                {
                    return;
                }

                ProcessFarming();
                if (!IsAvailable(member))
                {
                    return;
                }

                HarvestTraps(member);
                if (!IsAvailable(member))
                {
                    return;
                }

                RepairObjects(member);
            }
        }

        [HarmonyPatch(typeof(Object_Planter), "Update")]
        public static void Postfix(Object_Planter __instance)
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
        public static bool GetNearestObjectsOfTypePostfix(ref bool __runOriginal, ref List<Object_Base> __result, ObjectManager.ObjectType type, Vector3 pos)
        {
            var objectsOfType = ObjectManager.instance.GetObjectsOfType(type);
            __result = objectsOfType.OrderBy(o => o.transform.position, new CompareVector3()).ToList();
            __runOriginal = false;
            return false;
        }

        [HarmonyPatch(typeof(ObjectManager), "GetNearestObjectOfType")]
        [HarmonyPrefix]
        public static bool GetNearestObjectOfTypePostfix(ref bool __runOriginal, ref Object_Base __result, ObjectManager.ObjectType type, Vector3 pos)
        {
            __result = ObjectManager.instance.GetNearestObjectsOfType(type, pos).FirstOrDefault();
            __runOriginal = false;
            return false;
        }


        //[HarmonyPatch(typeof(CraftingPanel), "OnRecipeSlotPress")]
        //public static void Prefix(RecipeSlot recipeSlot)
        //{
        //    foreach (var stack in recipeSlot.recipe.ingredients)
        //    {
        //        ShelterInventoryManager.instance.inventory.AddItems(stack);
        //    }
        //}
    }
}
