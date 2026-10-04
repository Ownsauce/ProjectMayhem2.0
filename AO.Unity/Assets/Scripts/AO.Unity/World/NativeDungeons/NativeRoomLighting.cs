using System.Collections.Generic;
using UnityEngine;
using WorldGen.Dungeons;

namespace AO.Unity.World
{
    /// <summary>Visible ceiling fixtures and their matching light sources, owned by the placed room.</summary>
    public sealed class NativeRoomLighting : MonoBehaviour
    {
        private Material housing, diffuser;
        public static void Build(Transform destination, NativeRoomTemplate template)
        {
            if (template.Lights.Length == 0) return;
            var owner = destination.gameObject.AddComponent<NativeRoomLighting>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            owner.housing = new Material(shader) { name = "NativeFixtureHousing", color = new Color(.12f, .14f, .16f) };
            owner.diffuser = new Material(shader) { name = "NativeFixtureDiffuser", color = Color.white };
            owner.diffuser.EnableKeyword("_EMISSION");
            owner.diffuser.SetColor("_EmissionColor", new Color(1, .93f, .8f) * 8);
            foreach (var record in template.Lights)
            {
                var fixture = new GameObject("CeilingFixture").transform;
                fixture.SetParent(destination, false);
                fixture.localPosition = new Vector3(template.OriginX + record.X, template.OriginY + record.Y, template.OriginZ + record.Z) * .001f;
                Part(fixture, "Housing", Vector3.zero, new Vector3(.7f, .12f, .4f), owner.housing);
                var surface = Part(fixture, "LuminousDiffuser", Vector3.down * .075f, new Vector3(.6f, .025f, .3f), owner.diffuser);
                var emission = new MaterialPropertyBlock(); emission.SetColor("_EmissionColor", new Color(1, .93f, .8f) * record.Emission);
                surface.GetComponent<MeshRenderer>().SetPropertyBlock(emission);
                var source = new GameObject("FixtureLight").transform; source.SetParent(fixture, false);
                source.localPosition = Vector3.down * .15f; source.localRotation = Quaternion.Euler(90, 0, 0);
                var light = source.gameObject.AddComponent<Light>(); light.type = LightType.Spot;
                light.color = new Color(1, .93f, .8f); light.intensity = record.Intensity; light.range = record.Range;
                light.spotAngle = 125; light.innerSpotAngle = 85; light.shadows = LightShadows.None;
                light.shadowBias = .03f; light.shadowNormalBias = .1f;
            }
        }
        private static GameObject Part(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.name = name;
            part.transform.SetParent(parent, false); part.transform.localPosition = position; part.transform.localScale = size;
            part.GetComponent<Collider>().enabled = false; part.GetComponent<MeshRenderer>().sharedMaterial = material; return part;
        }
        private void OnDestroy()
        {
            if (Application.isPlaying) { Destroy(housing); Destroy(diffuser); }
            else { DestroyImmediate(housing); DestroyImmediate(diffuser); }
        }
    }
}
