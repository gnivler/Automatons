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
        private static bool initialized;

        [HarmonyPatch(typeof(ObjectInteraction_HarvestTrap), "Awake")]
        [HarmonyPostfix]
        public static void ObjectInteraction_HarvestTrapAwakePostfix(ObjectInteraction_HarvestTrap __instance)
        {
            Traps.Add(__instance);
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

        [HarmonyPatch(typeof(BaseCharacter), "SetUpSpawn")]
        [HarmonyPostfix]
        public static void BaseCharacterSetUpSpawnPostfix(BaseCharacter __instance)
        {
            if (__instance.isShelterMember)
            {
                var component = __instance.gameObject.AddComponent<InteractionNeeds>();
                component.Member = __instance.baseRH.memberNavigation.member;
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
    }
}
