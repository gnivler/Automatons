using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace Automatons
{
    public static class Helper
    {
        internal static bool SurvivorsInitialized;

        public class CompareVector3 : Comparer<Vector3>
        {
            public override int Compare(Vector3 x, Vector3 y)
            {
                if (x.magnitude > y.magnitude)
                {
                    return 1;
                }

                if (x.magnitude < y.magnitude)
                {
                    return -1;
                }

                return 0;
            }
        }

        internal static void DoFarmingJob<T>(Object_Base planter) where T : ObjectInteraction_Base
        {
            if (!SurvivorsInitialized
                && MemberManager.instance.GetAllShelteredMembers().All(m => m.name != "Sam Smith"))
            {
                SurvivorsInitialized = true;
            }

            if (!SurvivorsInitialized
                || planter.beingUsed)
            {
                return;
            }

            if (planter.IsSurfaceObject
                && WeatherManager.instance.IsRaining())
            {
                return;
            }

            var member = MemberManager.instance.GetAllShelteredMembers()
                .Where(m => IsAvailable(m.member))
                .OrderBy(m =>
                {
                    var point = planter.ChooseValidInteractionPoint();
                    return point.position.PathDistanceTo(m.transform.position);
                }).FirstOrDefault();

            if (member is null)
            {
                return;
            }

            Mod.Log($"Sending {member.name} to {planter.name} - {typeof(T)}");
            var interaction = planter.GetComponent<T>();
            var job = new Job(member, planter, interaction, planter.ChooseValidInteractionPoint());
            member.member.AddJob(job);
            member.member.currentjob = job;
            planter.beingUsed = true;
        }

        internal static void DoFirefightingJob(Object_FireExtinguisher extinguisher, Member member, Object_Base burningObject)
        {
            if (burningObject.beingUsed)
            {
                return;
            }

            Job job;
            if (extinguisher is not null)
            {
                Mod.Log($"Sending {member.name} to {burningObject.GetObjectName()} with extinguisher");
                extinguisher.beingUsed = true;
                job = new Job_UseFireExtinguisher(
                    member.memberRH,
                    burningObject.GetComponent<ObjectInteraction_UseFireExtinguisher>(),
                    burningObject,
                    extinguisher.GetInteractionTransform(0),
                    extinguisher);
            }
            else
            {
                Mod.Log($"Sending {member.name} to extinguish {burningObject.name}");
                var interaction = burningObject.GetComponent<ObjectInteraction_ExtinguishFire>();
                var butt = ObjectManager.instance.GetNearestObjectOfCategory(ObjectManager.ObjectCategory.WaterButt, member.transform.position);
                job = new Job_ExtinguishFire(member.memberRH, interaction, burningObject, burningObject.GetInteractionTransform(0), butt);
                member.ForceInterruptionJob(job);
            }

            burningObject.beingUsed = true;
            member.AddJob(job);
            //member.currentjob = job;
        }

        internal static Member GetNearestMemberToFireExtinguisher(Object_FireExtinguisher extinguisher, List<MemberReferenceHolder> members, Object_Base burningObject)
        {
            Member member;
            if (extinguisher is not null)
            {
                member = members.OrderBy(m =>
                    m.member.transform.position.PathDistanceTo(extinguisher.ChooseValidInteractionPoint().position)).FirstOrDefault()?.member;
            }
            else
            {
                //var waterSources = ObjectManager.instance.GetAllObjects().Where(o => o.objectType.ToString().Contains("Water"));

                var point = burningObject.ChooseValidInteractionPoint().position;
                member = members.OrderBy(m => point.PathDistanceTo(m.transform.position)).First().member;
            }

            return member;
        }

        internal static Object_FireExtinguisher GetNearestFireExtinguisherToFire(Object_Base burningObject)
        {
            // get the nearest fire extinguisher if any
            var extinguishers = ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.FireExtinguisher);
            Object_FireExtinguisher closestExtinguisher = default;
            var closest = float.MaxValue;
            foreach (var extinguisher in extinguishers)
            {
                var point = extinguisher.ChooseValidInteractionPoint().position;
                var extinguisherDistanceToFire = point.PathDistanceTo(burningObject.ChooseValidInteractionPoint().position);
                if (extinguisherDistanceToFire < closest)
                {
                    closest = extinguisherDistanceToFire;
                    closestExtinguisher = extinguisher as Object_FireExtinguisher;
                }
            }

            return closestExtinguisher;
        }

        internal static bool IsAvailable(Member memberToCheck)
        {
            return memberToCheck.currentjob is null
                   && memberToCheck.currentQueue.Count == 0
                   && memberToCheck.jobQueueCount == 0
                   && memberToCheck.aiQueueCount == 0
                   && !memberToCheck.m_breakdownController.isHavingBreakdown
                   && !memberToCheck.OutOnExpedition
                   || memberToCheck.currentjob?.jobInteractionType == InteractionTypes.InteractionType.GoHere
                   && !memberToCheck.m_breakdownController.isHavingBreakdown
                   && !memberToCheck.OutOnExpedition;
        }
    }
}
