using System;
using System.Linq;
using System.Reflection;
using AO.Unity.World.Procedural;
using UnityEngine;
using WorldGen.Dungeons;

namespace AO.Unity.Editor.WorldGen
{
    public static class SubwayPresentationVerifier
    {
        public static string Verify(ProceduralDungeonDebugView view)
        {
            var layout = (DungeonLayout)typeof(ProceduralDungeonDebugView).GetField("activeLayout", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            var validation = DungeonValidator.Validate(layout);
            if (!validation.IsValid) throw new InvalidOperationException(string.Join("; ", validation.Errors));
            var transforms = view.GetComponentsInChildren<Transform>(true);
            if (transforms.Any(x => x.name.Contains("Stall Door") || x.name.Contains("/stall-door-")))
                throw new InvalidOperationException("Preview still contains a stall door.");
            int fixtures = 0;
            foreach (var element in layout.Presentation.Where(x => x.FixtureRole == DungeonFixtureRole.Toilet || x.FixtureRole == DungeonFixtureRole.Sink))
            {
                Transform fixture = transforms.SingleOrDefault(x => x.name == "Resolved Fixture " + element.Id + " [Subway GLB]");
                if (fixture == null) throw new InvalidOperationException("Missing GLB fixture: " + element.Id);
                var renderers = fixture.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) throw new InvalidOperationException("Missing fixture mesh: " + element.Id);
                Bounds visual = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) visual.Encapsulate(renderer.bounds);
                var b = element.Bounds;
                Vector3 expectedCenter = new Vector3((b.Minimum.X + b.Maximum.X) * .0005f,
                    (b.Minimum.Y + b.Maximum.Y) * .0005f, (b.Minimum.Z + b.Maximum.Z) * .0005f) + view.transform.position;
                Vector3 expectedSize = new Vector3(b.Maximum.X - b.Minimum.X, b.Maximum.Y - b.Minimum.Y, b.Maximum.Z - b.Minimum.Z) * .001f;
                if ((visual.center - expectedCenter).sqrMagnitude > .00001f || (visual.size - expectedSize).sqrMagnitude > .00001f)
                    throw new InvalidOperationException("Fixture visual differs from shared bounds: " + element.Id);
                Transform support = transforms.SingleOrDefault(x => x.name == "Core Collision presentation-" + element.Id);
                if (support == null || support.GetComponent<BoxCollider>() == null)
                    throw new InvalidOperationException("Missing shared fixture collider: " + element.Id);
                fixtures++;
            }
            var lights = view.GetComponentsInChildren<Light>(true).Where(x => x.name == "Subway Fixture Light").ToArray();
            if (lights.Length == 0) throw new InvalidOperationException("No connected fixture lights.");
            foreach (Light light in lights)
            {
                Transform diffuser = light.transform.parent.Find("Subway Lamp Diffuser");
                if (diffuser == null || !diffuser.GetComponent<Renderer>().sharedMaterial.IsKeywordEnabled("_EMISSION"))
                    throw new InvalidOperationException("Light has no visible emissive diffuser.");
                if (Vector3.Dot(light.transform.forward, Vector3.down) < .99f)
                    throw new InvalidOperationException("Fixture light is not directed into the room.");
            }
            string result = $"Verified generator {layout.Manifest.GeneratorVersion}: {fixtures} resolved fixtures, {lights.Length} emitting lights, no stall doors.";
            Debug.Log("[WorldGen] " + result);
            return result;
        }
    }
}
