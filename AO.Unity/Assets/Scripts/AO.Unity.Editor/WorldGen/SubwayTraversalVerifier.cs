using System;
using System.Linq;
using System.Reflection;
using AO.Unity.World;
using AO.Unity.World.Procedural;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AO.Unity.Editor.WorldGen
{
    public static class SubwayTraversalVerifier
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static void Verify()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var dungeon = new GameObject("Traversal regression dungeon").AddComponent<ProceduralDungeonDebugView>();
            var settings = new SerializedObject(dungeon);
            settings.FindProperty("seed").longValue = 90602;
            settings.FindProperty("roomCount").intValue = 30;
            settings.ApplyModifiedPropertiesWithoutUndo();
            dungeon.RebuildSubway();
            Physics.SyncTransforms();
            var player = new GameObject("Traversal regression capsule");
            try
            {
                var walker = player.AddComponent<PrototypeWalkerController>();
                var controller = player.GetComponent<CharacterController>();
                typeof(PrototypeWalkerController).GetMethod("Awake", Private).Invoke(walker, null);
                walker.enabled = false;
                var blocked = typeof(PrototypeWalkerController).GetField("_movementBlockedByCollision", Private);
                var direction = typeof(PrototypeWalkerController).GetField("_blockedMovementDirection", Private);
                var velocity = typeof(PrototypeWalkerController).GetField("_planarVelocity", Private);
                var filter = typeof(PrototypeWalkerController).GetMethod("FilterCollisionBlockedIntent", Private);
                var compute = typeof(PrototypeWalkerController).GetMethod("ComputeHorizontalMotion", Private);
                foreach (Vector3 obstacle in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                {
                    blocked.SetValue(walker, true);
                    direction.SetValue(walker, obstacle);
                    if (((Vector3)filter.Invoke(walker, new object[] { obstacle })).sqrMagnitude != 0)
                        throw new Exception("Movement into rejected direction was not suppressed.");
                    foreach (Vector3 escape in new[] { -obstacle, Vector3.Cross(obstacle, Vector3.up) })
                        if (((Vector3)filter.Invoke(walker, new object[] { escape }) - escape).sqrMagnitude > .000001f)
                            throw new Exception("Retreat/strafe intent was suppressed.");
                }
                blocked.SetValue(walker, true);
                direction.SetValue(walker, Vector3.forward);
                velocity.SetValue(walker, Vector3.forward * 8f);
                Vector3 retreat = (Vector3)compute.Invoke(walker, new object[] { -1f, 0f });
                if ((bool)blocked.GetValue(walker) || retreat.z > .000001f)
                    throw new Exception("Retreat retained velocity into the obstacle.");

                // Exercise the real generated entrance's solid wall, door jambs,
                // threshold and nearby floor with the production capsule dimensions.
                BoxCollider[] walls = dungeon.GetComponentsInChildren<BoxCollider>(true)
                    .Where(x => x.enabled && !x.isTrigger && x.name.StartsWith("Core Collision room-wall-")
                        && x.transform.parent.name.StartsWith("Room 0 ")).ToArray();
                if (walls.Length == 0) throw new Exception("No entrance wall collision found.");
                int contacts = 0;
                foreach (BoxCollider wall in walls)
                {
                    Bounds b = wall.bounds;
                    bool thinX = b.size.x < b.size.z;
                    Vector3 normal = thinX ? Vector3.right : Vector3.forward;
                    float half = (thinX ? b.size.x : b.size.z) * .5f;
                    foreach (float sign in new[] { -1f, 1f })
                    {
                        Vector3 away = normal * sign;
                        Vector3 start = b.center + away * (half + controller.radius + .3f);
                        start.y = b.min.y + .05f;
                        controller.enabled = false;
                        player.transform.position = start;
                        controller.enabled = true;
                        Physics.SyncTransforms();
                        var flags = controller.Move(-away * .6f);
                        if ((flags & CollisionFlags.Sides) == 0) continue;
                        contacts++;
                        Vector3 before = player.transform.position;
                        controller.Move(away * .25f);
                        if (Vector3.Dot(player.transform.position - before, away) < .15f)
                            throw new Exception("Capsule could not retreat from entrance wall " + wall.name);
                    }
                }
                if (contacts == 0) throw new Exception("Entrance regression did not establish wall contacts.");
                Debug.Log("SUBWAY_TRAVERSAL_VERIFIED entrance_contacts=" + contacts
                    + " directional_network_intent=passed immediate_retreat=passed");
            }
            finally { UnityEngine.Object.DestroyImmediate(player); }
        }
    }
}
