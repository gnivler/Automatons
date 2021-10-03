using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace Automatons
{
    public static class Extensions
    {
        internal static float PathDistanceTo(this Vector3 startPos, Vector3 targetPos)
        {
            var path = new NavMeshPath();
            NavMesh.CalculatePath(startPos, targetPos, NavMesh.AllAreas, path);
            var pathLength = path.corners.Select(v => v.magnitude).Sum();
            return pathLength;
        }
    }
}
