using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WorldGen.Dungeons;
using WorldGen.Spatial;

namespace AO.Unity.World
{
    /// <summary>Neighbor visibility and a shared shadow budget. Collision remains active.</summary>
    public sealed class NativeRoomVisibility : MonoBehaviour
    {
        private NativeRoomRecipe recipe;
        private sealed class Visual { public Renderer[] Renderers; public Light[] Lights; }
        private readonly Dictionary<int, Visual> visuals = new Dictionary<int, Visual>();
        private readonly Dictionary<int, List<int>> neighbors = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, Vector3[]> points = new Dictionary<int, Vector3[]>();
        private Light[] lights;
        private float nextUpdate;
        [SerializeField] private int lightBudget = 64, shadowBudget = 12;
        public void Initialize(NativeRoomRecipe value)
        {
            recipe = value;
            foreach (var room in recipe.Rooms)
            {
                neighbors[room.Index] = new List<int>();
                points[room.Index] = recipe.Template(room).WalkPoints.Where((p, i) => i % 8 == 0).Select(p => {
                    var position = NativeRoomDungeon.Transform(room, new WorldVector3(p.X, p.Y, p.Z));
                    return new Vector3(position.X, position.Y, position.Z) * .001f;
                }).ToArray();
            }
            foreach (var join in recipe.Joins) { neighbors[join.From].Add(join.To); neighbors[join.To].Add(join.From); }
        }
        public void Register(int room, GameObject visual) { visuals[room] = new Visual { Renderers = visual.GetComponentsInChildren<Renderer>(true), Lights = visual.GetComponentsInChildren<Light>(true) }; lights = null; }
        private void Update()
        {
            if (Time.unscaledTime < nextUpdate || Camera.main == null) return;
            nextUpdate = Time.unscaledTime + .25f; Refresh(Camera.main.transform.position, true, Camera.main);
        }
        public void Refresh(Vector3 observer, bool cull, Camera view = null)
        {
            int current = 0; float distance = float.MaxValue;
            foreach (var pair in points) foreach (var point in pair.Value)
            {
                float d = (point - observer).sqrMagnitude;
                if (d < distance) { distance = d; current = pair.Key; }
            }
            var visible = new HashSet<int> { current }; var frontier = new List<int> { current };
            for (int depth = 0; depth < 2; depth++)
            { var next = new List<int>(); foreach (int room in frontier) foreach (int neighbor in neighbors[room]) if (visible.Add(neighbor)) next.Add(neighbor); frontier = next; }
            // Keep neighboring rooms ready, then conservatively follow every doorway
            // intersecting the camera frustum. Long visible passages must not disappear
            // merely because they cross more than two graph edges.
            if (cull && view != null)
            {
                var planes = GeometryUtility.CalculateFrustumPlanes(view);
                var pending = new Queue<int>(visible);
                while (pending.Count > 0)
                {
                    int from = pending.Dequeue();
                    foreach (var join in recipe.Joins.Where(j => j.From == from || j.To == from))
                    {
                        int to = join.From == from ? join.To : join.From;
                        if (visible.Contains(to)) continue;
                        var placement = recipe.Rooms[from];
                        var socket = recipe.Template(placement).Sockets[join.From == from ? join.FromSocket : join.ToSocket];
                        var center = NativeRoomDungeon.Transform(placement, new WorldVector3(socket.X, socket.Y + socket.Clearance / 2, socket.Z));
                        int facing = (socket.Facing + placement.QuarterTurns) & 3;
                        var size = new Vector3((facing & 1) == 0 ? socket.Width * .001f : .4f, socket.Clearance * .001f, (facing & 1) == 0 ? .4f : socket.Width * .001f);
                        var bounds = new Bounds(new Vector3(center.X, center.Y, center.Z) * .001f, size + Vector3.one);
                        if (GeometryUtility.TestPlanesAABB(planes, bounds)) { visible.Add(to); pending.Enqueue(to); }
                    }
                }
            }
            foreach (var pair in visuals)
            {
                // Rendering and light visibility must never disable the floor-support colliders.
                bool active = !cull || visible.Contains(pair.Key);
                foreach (var renderer in pair.Value.Renderers) renderer.enabled = active;
                foreach (var light in pair.Value.Lights) light.enabled = active;
            }
            if (lights == null) lights = GetComponentsInChildren<Light>(true);
            var illuminating = lights.Where(l => l.enabled).OrderBy(l => (l.transform.position - observer).sqrMagnitude).Take(lightBudget).ToArray();
            foreach (var light in lights) light.enabled = illuminating.Contains(light);
            var shadowed = lights.Where(l => l.enabled && (l.transform.position - observer).sqrMagnitude < 900)
                .OrderBy(l => (l.transform.position - observer).sqrMagnitude).Take(shadowBudget).ToArray();
            foreach (var light in lights) light.shadows = shadowed.Contains(light) ? LightShadows.Soft : LightShadows.None;
        }
    }
}
