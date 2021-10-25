using HarmonyLib;
using UnityEngine;

namespace Automatons
{
    public class InteractionNeeds : MonoBehaviour
    {
        internal Member Member;
        private float timer;
        private float idleTimer;

        private static readonly AccessTools.FieldRef<MemberAI, NeedsStat.NeedsStatType> currentPriorityNeed =
            AccessTools.FieldRefAccess<MemberAI, NeedsStat.NeedsStatType>("currentPriorityNeed");

        private void Update()
        {
            if (Member.OutOnExpedition
                || Member.m_outOnLoan)
            {
                return;
            }

            timer += Time.deltaTime;
            if (timer < 10)
            {
                return;
            }

            timer -= 10;
            if (IsDoingInteraction(Member))
            {
                AccessTools.Method(typeof(MemberAI), "EvaluateNeeds").Invoke(Member.memberRH.memberAI, new object[] { });
                if (currentPriorityNeed(Member.memberRH.memberAI) != NeedsStat.NeedsStatType.Max)
                {
                    Mod.Log($"{Member.name} found needs job during interaction {Member.currentjob?.jobInteractionType}");
                    Member.CancelJobsImmediately();
                    Member.CancelAIJobsImmediately();
                    Member.memberRH.memberAI.FindNeedsJob();
                }
            }

            // collect idle timer
            if (Member.currentjob is null)
            {
                idleTimer += Time.deltaTime;
            }

            if (idleTimer > 10)
            {
                Helper.DoReading(Member, true);
                idleTimer -= 10;
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
