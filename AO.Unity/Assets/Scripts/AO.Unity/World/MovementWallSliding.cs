using System.Collections.Generic;
using UnityEngine;

namespace AO.Unity.World
{
    /// <summary>Preserves planar tangent/retreat input while rejecting only motion into contact planes.</summary>
    internal static class MovementWallSliding
    {
        public static Vector3 Project(Vector3 intent, IReadOnlyList<Vector3> outwardNormals, Vector3 blockedIntoDirection)
        {
            if (outwardNormals.Count == 0)
            {
                float inward = Vector3.Dot(intent, blockedIntoDirection);
                return inward > 0 ? intent - blockedIntoDirection * inward : intent;
            }
            for (int pass = 0; pass < 2; pass++)
                foreach (Vector3 normal in outwardNormals)
                {
                    float inward = Vector3.Dot(intent, normal);
                    if (inward < 0) intent -= normal * inward;
                }
            // Two nonparallel walls can leave no usable tangent. Stop in that
            // corner rather than feeding a residual inward vector back to prediction.
            foreach (Vector3 normal in outwardNormals)
                if (Vector3.Dot(intent, normal) < -.00001f) return Vector3.zero;
            return intent;
        }
    }
}
