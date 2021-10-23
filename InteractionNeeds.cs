using HarmonyLib;
using UnityEngine;

namespace Automatons
{
    public class InteractionNeeds : MonoBehaviour
    {
        internal Member Member;
        private float timer;

        private static readonly AccessTools.FieldRef<MemberAI, NeedsStat.NeedsStatType> currentPriorityNeed =
            AccessTools.FieldRefAccess<MemberAI, NeedsStat.NeedsStatType>("currentPriorityNeed");

        private void Update()
        {
            if (Member.OutOnExpedition)
            {
                return;
            }

            timer += Time.deltaTime;
            if (timer < 10)
            {
                return;
            }

            timer -= Time.deltaTime;
            if (IsDoingInteraction(Member))
            {
                AccessTools.Method(typeof(MemberAI), "EvaluateNeeds").Invoke(Member.memberRH.memberAI, new object[] { });
                if (currentPriorityNeed(Member.memberRH.memberAI) != NeedsStat.NeedsStatType.Max)
                {
                    Mod.Log($"{Member.name} found needs job during interaction {Member.currentjob?.jobInteractionType}");
                    Member.currentjob?.CancelJob();
                    Member.memberRH.memberAI.FindNeedsJob();
                }
            }
        }

        private static bool IsDoingInteraction(Member member)
        {
            return member.interactionType is InteractionTypes.InteractionType.Exercise
                or InteractionTypes.InteractionType.ReadCharismaBook
                or InteractionTypes.InteractionType.ReadIntelligenceBook
                or InteractionTypes.InteractionType.ReadPerceptionBook
                or InteractionTypes.InteractionType.ReadStoryBook
                or InteractionTypes.InteractionType.Cleaning;
        }
    }
}
