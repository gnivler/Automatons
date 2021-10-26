using System.Linq;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.AI;

namespace Automatons
{
    public static class Extensions
    {
        internal static float PathDistanceTo(this Vector3 startPos, Vector3 targetPos)
        {
            var path = new NavMeshPath();
            NavMesh.CalculatePath(startPos, targetPos, 1 << NavMesh.GetAreaFromName("Walkable"), path);
            var pathLength = path.corners.Select(v => v.magnitude).Sum();
            if (pathLength == 0)
            {
                pathLength = Vector3.Distance(startPos, targetPos);
            }

            return pathLength;
        }

        internal static bool IsUsable(this Object_Base objectBase)
        {
            return !objectBase.beingUsed && objectBase.IsConstructionCompleted && !objectBase.isBroken;
        }

        internal static bool HasActiveInteractionMembers(this Object_Base objectBase)
        {
            return objectBase.interactions.Any(i => i.interactionMembers.Any());
        }

        internal static bool HasEmptyQueues(this Member member)
        {
            return member.jobQueueCount + member.aiQueueCount == 0;
        }
    }
}
