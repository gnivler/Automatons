using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Automatons
{
    public static class Helper
    {
        private static bool survivorsInitialized;

        public class CompareVector3 : IComparer<Vector3>
        {
            public int Compare(Vector3 x, Vector3 y)
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
            if (!survivorsInitialized
                && MemberManager.instance.GetAllShelteredMembers().All(m => m.name != "Sam Smith"))
            {
                survivorsInitialized = true;
            }

            if (!survivorsInitialized
                || planter.beingUsed)
            {
                return;
            }

            var members = MemberManager.instance.GetAllShelteredMembers()
                .Where(m => !m.member.OutOnExpedition)
                .OrderBy(m => planter.ChooseValidInteractionPoint().position.PathDistanceTo(m.transform.position));

            foreach (var member in members)
            {
                if (member.member.currentjob is not null
                    || planter.IsSurfaceObject
                    && WeatherManager.instance.IsRaining())
                {
                    continue;
                }

                Mod.Log($"Sending {member.name} to {planter.name} - {typeof(T)}");
                var interaction = planter.GetComponent<T>();
                var job = new Job(member, planter, interaction, planter.ChooseValidInteractionPoint());
                member.member.AddJob(job);
                member.member.currentjob = job;
                planter.beingUsed = true;
                break;
            }
        }

        internal static void DoFirefightingJob(Object_FireExtinguisher extinguisher, Member member, Object_Base burningObject, Object_Base nearestButt)
        {
            Job job;
            if (extinguisher is not null)
            {
                Mod.Log($"Sending {member.name} to {burningObject.GetObjectName()} with extinguisher");
                extinguisher.beingUsed = true;
                job = new Job_UseFireExtinguisher(
                    member.memberRH,
                    burningObject.GetComponent<ObjectInteraction_UseFireExtinguisher>(),
                    burningObject,
                    extinguisher.ChooseValidInteractionPoint() ?? extinguisher.transform,
                    extinguisher);
            }
            else
            {
                Mod.Log($"Sending {member.name} to {burningObject.name}");
                Mod.Log(nearestButt);
                nearestButt.beingUsed = true;
                job = new Job_ExtinguishFire(
                    member.memberRH,
                    burningObject.GetComponent<ObjectInteraction_ExtinguishFire>(),
                    burningObject,
                    burningObject.ChooseValidInteractionPoint() ?? burningObject.transform,
                    nearestButt);
            }

            //burningObject.beingUsed = true;
            member.ForceInterruptionJob(job);
            member.currentjob = job;
        }

        internal static Member GetNearestMember(Object_FireExtinguisher extinguisher, List<MemberReferenceHolder> members, Object_Base burningObject, out Object_Base nearestButt)
        {
            nearestButt = default;
            Member member;
            if (extinguisher is not null)
            {
                member = members.OrderBy(m =>
                    m.member.transform.position.PathDistanceTo(extinguisher.ChooseValidInteractionPoint().position)).FirstOrDefault()?.member;
            }
            else
            {
                var waterSources = ObjectManager.instance.GetAllObjects().Where(o => o.objectType.ToString().Contains("Water"));
                var butt = waterSources.OrderBy(o => o.ChooseValidInteractionPoint().position.PathDistanceTo(burningObject.ChooseValidInteractionPoint().position)).First();
                member = members.OrderBy(m =>
                    m.member.transform.position.PathDistanceTo(butt.ChooseValidInteractionPoint().position)).FirstOrDefault()?.member;
                nearestButt = butt;
            }

            return member;
        }

        internal static Object_FireExtinguisher GetNearestFireExtinguisher(Object_Base burningObject)
        {
            // get the nearest fire extinguisher if any
            var extinguishers = ObjectManager.instance.GetObjectsOfType(ObjectManager.ObjectType.FireExtinguisher);
            Object_FireExtinguisher closestExtinguisher = default;
            var closest = float.MaxValue;
            foreach (var extinguisher in extinguishers)
            {
                var extinguisherDistanceToFire = extinguisher.ChooseValidInteractionPoint().position.PathDistanceTo(burningObject.ChooseValidInteractionPoint().position);
                if (extinguisherDistanceToFire < closest)
                {
                    closest = extinguisherDistanceToFire;
                    closestExtinguisher = extinguisher as Object_FireExtinguisher;
                }
            }

            return closestExtinguisher;
        }
    }
}
