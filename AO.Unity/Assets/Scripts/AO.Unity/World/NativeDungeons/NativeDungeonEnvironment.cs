using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AO.Unity.World
{
    /// <summary>Scoped indoor environment; restores the outdoor settings when the dungeon closes.</summary>
    public sealed class NativeDungeonEnvironment : MonoBehaviour
    {
        private static NativeDungeonEnvironment active;
        private static AmbientMode ambientMode;
        private static Color ambientLight, background;
        private static float reflection;
        private static Material skybox;
        private static Light sun;
        private static bool sunEnabled;
        private static readonly Dictionary<Light, bool> directionalLights = new Dictionary<Light, bool>();
        private static Camera camera;
        private static CameraClearFlags clearFlags;
        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (active == null)
            {
                ambientMode = RenderSettings.ambientMode; ambientLight = RenderSettings.ambientLight;
                skybox = RenderSettings.skybox; reflection = RenderSettings.reflectionIntensity;
                sun = RenderSettings.sun; sunEnabled = sun != null && sun.enabled;
                camera = Camera.main;
                if (camera != null) { clearFlags = camera.clearFlags; background = camera.backgroundColor; }
            }
            active = this;
            foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (light.type == LightType.Directional)
                { if (!directionalLights.ContainsKey(light)) directionalLights.Add(light, light.enabled); light.enabled = false; }
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.035f, .04f, .045f);
            RenderSettings.skybox = null; RenderSettings.reflectionIntensity = .15f;
            if (sun != null) sun.enabled = false;
            if (camera != null) { camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; }
        }
        private void OnDestroy()
        {
            if (active != this) return;
            RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambientLight;
            RenderSettings.skybox = skybox; RenderSettings.reflectionIntensity = reflection;
            if (sun != null) sun.enabled = sunEnabled;
            if (camera != null) { camera.clearFlags = clearFlags; camera.backgroundColor = background; }
            foreach (var pair in directionalLights) if (pair.Key != null) pair.Key.enabled = pair.Value;
            directionalLights.Clear();
            active = null;
        }
    }
}
