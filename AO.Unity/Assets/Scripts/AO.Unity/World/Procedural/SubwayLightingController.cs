using System.Collections.Generic;
using UnityEngine;

namespace AO.Unity.World.Procedural
{
    public sealed class SubwayLightingController
    {
        private readonly Dictionary<Light, bool> subwaySunStates = new Dictionary<Light, bool>();
        private bool subwayEnvironmentActive;
        private UnityEngine.Rendering.AmbientMode savedAmbientMode;
        private Color savedAmbientLight;
        private float savedReflectionIntensity;
        private bool savedFog;

        public void Activate()
        {
            if (!Application.isPlaying || subwayEnvironmentActive) return;
            subwayEnvironmentActive = true;
            savedAmbientMode = RenderSettings.ambientMode;
            savedAmbientLight = RenderSettings.ambientLight;
            savedReflectionIntensity = RenderSettings.reflectionIntensity;
            savedFog = RenderSettings.fog;
            foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (light.type == LightType.Directional)
                { subwaySunStates[light] = light.enabled; light.enabled = false; }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.16f, .18f, .20f);
            RenderSettings.reflectionIntensity = .15f;
            RenderSettings.fog = false;
        }

        public void Restore()
        {
            if (!subwayEnvironmentActive) return;
            foreach (var pair in subwaySunStates) if (pair.Key != null) pair.Key.enabled = pair.Value;
            subwaySunStates.Clear();
            RenderSettings.ambientMode = savedAmbientMode;
            RenderSettings.ambientLight = savedAmbientLight;
            RenderSettings.reflectionIntensity = savedReflectionIntensity;
            RenderSettings.fog = savedFog;
            subwayEnvironmentActive = false;
        }

        private readonly List<Light> nearbyFixtureLights = new List<Light>();
        public void RefreshShadows(Transform generatedRoot, Vector3 position)
        {
            nearbyFixtureLights.Clear();
            foreach (SubwayFixtureLight fixture in generatedRoot.GetComponentsInChildren<SubwayFixtureLight>())
                {
                    Light light = fixture.Source;
                    light.shadows = LightShadows.None;
                    if (light.enabled && (light.transform.position - position).sqrMagnitude < 900f)
                        nearbyFixtureLights.Add(light);
                }
            nearbyFixtureLights.Sort((a, b) => (a.transform.position - position).sqrMagnitude.CompareTo(
                (b.transform.position - position).sqrMagnitude));
            for (int i = 0; i < Mathf.Min(16, nearbyFixtureLights.Count); i++)
                nearbyFixtureLights[i].shadows = LightShadows.Soft;
        }
    }
}
