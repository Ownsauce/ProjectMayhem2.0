using UnityEngine;
using WorldGen.Content;

namespace AO.Unity.World.Procedural
{
    public static class SubwayFixtureLighting
    {
        public static void Attach(Transform instance, Vector3 size, CatalogLighting definition, Material emitterMaterial)
        {
            // Keep the source below the mesh so it does not shadow its own fixture.
            var source = new GameObject("Subway Fixture Light");
            source.transform.SetParent(instance, false);
            source.transform.localPosition = new Vector3(0, -.5f - .12f / Mathf.Max(.001f, size.y), 0);
            source.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var diffuser = GameObject.CreatePrimitive(PrimitiveType.Quad);
            diffuser.name = "Subway Lamp Diffuser";
            diffuser.transform.SetParent(instance, false);
            diffuser.transform.localPosition = new Vector3(0, -.5f - .015f / Mathf.Max(.001f, size.y), 0);
            diffuser.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            diffuser.transform.localScale = new Vector3(.88f, .8f, 1);
            if (Application.isPlaying) UnityEngine.Object.Destroy(diffuser.GetComponent<Collider>());
            else UnityEngine.Object.DestroyImmediate(diffuser.GetComponent<Collider>());
            Renderer emitter = diffuser.GetComponent<Renderer>();
            emitter.sharedMaterial = emitterMaterial;
            emitter.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            emitter.receiveShadows = false;
            Light light = source.AddComponent<Light>();
            source.AddComponent<SubwayFixtureLight>();
            light.type = LightType.Spot;
            light.color = new Color(.86f, .94f, 1f);
            
            var emission = new MaterialPropertyBlock();
            Color glow = definition?.color != null ? new Color(definition.color.x, definition.color.y, definition.color.z) : light.color;
            emission.SetColor("_BaseColor", glow);
            emission.SetColor("_EmissionColor", glow * Mathf.Max(8f, definition?.emissionIntensity ?? 4));
            emitter.SetPropertyBlock(emission);
            if (definition?.socketMeters != null)
                source.transform.localPosition = new Vector3(definition.socketMeters.x / Mathf.Max(.001f, size.x),
                    definition.socketMeters.y / Mathf.Max(.001f, size.y), definition.socketMeters.z / Mathf.Max(.001f, size.z));
            light.color = definition?.color != null ? new Color(definition.color.x, definition.color.y, definition.color.z) : light.color;
            light.intensity = definition?.intensity ?? 4f;
            light.range = definition?.rangeMeters ?? 24f;
            light.spotAngle = definition?.coneDegrees ?? 110f;
            light.innerSpotAngle = light.spotAngle * .68f;
            light.shadows = LightShadows.Soft;
            light.shadowCustomResolution = 256;
            light.shadowBias = .015f;
            light.shadowNormalBias = .15f;
            light.renderMode = LightRenderMode.ForcePixel;
        }
    }
}
