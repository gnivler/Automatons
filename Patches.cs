using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Management.Instrumentation;
using HarmonyLib;
using Unity.Jobs.LowLevel.Unsafe;
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

        // bugfix
        [HarmonyPatch(typeof(ObjectInteraction_Base), "RegisterForInteraction")]
        [HarmonyPrefix]
        public static bool ObjectInteraction_BaseRegisterForInteractionPrefix(
            ObjectInteraction_Base __instance,
            Dictionary<int, float> ___interactionTimers,
            MemberReferenceHolder memberRH,
            ref bool __runOriginal,
            ref bool __result)
        {
            if (___interactionTimers.ContainsKey(memberRH.member.GetID))
            {
                __instance.obj.beingUsed = true;
                __result = true;
                __runOriginal = false;
                return false;
            }

            __runOriginal = true;
            return true;
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
            objectsOfType.Sort((left, right) => CompareFloats(left.GetInteractionTransform(0).position.PathDistanceTo(pos), right.GetInteractionTransform(0).position.PathDistanceTo(pos)));
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
            if (MemberManager.instance.GetAllShelteredMembers().Contains(__instance.memberRH))
            {
                var go = __instance.gameObject.AddComponent<InteractionNeeds>();
                go.Member = __instance.memberRH.member;
            }
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
            Mod.Log("Load game");
            Mod.Log(new string('=', 80));
            ClearGlobals();
        }

        [HarmonyPatch(typeof(SlaveAI), "Wander")]
        [HarmonyPrefix]
        public static bool SlaveAIWanderPrefix() => false;

        // bugfix
        [HarmonyPatch(typeof(MemberManager), "IsTheLeaderDead")]
        [HarmonyPrefix]
        public static bool MemberManagerIsTheLeaderDeadPrefix(ref bool __runOriginal, ref bool __result)
        {
            __runOriginal = false;
            var leader = MemberManager.instance.GetMemberByID(MemberManager.instance.LeaderId);
            var leaderParty = ExplorationManager.Instance.Parties.FirstOrDefault(p => p.m_partyMembers.Any(m => m.memberRH.member == MemberManager.instance.GetMemberByID(MemberManager.instance.LeaderId)));
            __result = leader.isDead
                       || leader.IsUnconscious
                       && MemberManager.instance.GetAllShelteredMembers().All(m => m.member.isDead || m.member.IsUnconscious)
                       || leaderParty is not null
                       && leaderParty.m_partyMembers.All(m => m.memberRH.member.isDead || m.memberRH.member.IsUnconscious);

            return false;
        }

        //[HarmonyPatch(typeof(Job), MethodType.Constructor, typeof(MemberReferenceHolder), typeof(Object_Base), typeof(ObjectInteraction_Base), typeof(Transform), typeof(bool), typeof(float), typeof(float))]
        //[HarmonyPostfix]
        //public static void JobCtorPostfix(Action ___onCancelJob)
        //{
        //    Mod.Log(___onCancelJob?.Method.Name);
        //}
    }
}
