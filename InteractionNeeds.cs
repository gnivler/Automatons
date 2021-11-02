using UnityEngine;

namespace Automatons
{
    public class InteractionNeeds : MonoBehaviour
    {
        internal Member Member;
        private float timer;
        private float idleTimer;

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
                Member.memberRH.memberAI.EvaluateNeeds();
                if (Member.memberRH.memberAI.currentPriorityNeed is not NeedsStat.NeedsStatType.Max)
                {
                    Mod.Log($"{Member.name} found needs job during interaction {Member.currentjob.jobInteractionType}");
                    Member.memberRH.memberAI.FindNeedsJob();
                    return;
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
            }

            idleTimer -= 10;
        }

        private static bool IsDoingInteraction(Member member)
        {
            return member.interactionType
                is InteractionTypes.InteractionType.Exercise
                or InteractionTypes.InteractionType.ReadCharismaBook
                or InteractionTypes.InteractionType.ReadIntelligenceBook
                or InteractionTypes.InteractionType.ReadPerceptionBook
                or InteractionTypes.InteractionType.ReadStoryBook
                or InteractionTypes.InteractionType.Cleaning
                or InteractionTypes.InteractionType.Broadcast_Trader
                or InteractionTypes.InteractionType.Broadcast_Recruit;
        }
    }
}
