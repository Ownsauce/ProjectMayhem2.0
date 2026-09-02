using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Applies AO playfield tweak data using URP-compatible Unity settings.</summary>
public sealed class UrpAoEnvironmentApplicator : MonoBehaviour
{
    bool _captured;
    bool _fog;
    FogMode _fogMode;
    Color _fogColor;
    float _fogDensity;
    Color _ambient;
    Light _sun;
    Quaternion _sunRotation;
    Color _sunColor;

    public bool Apply(string aoRoot, int playfieldId, ResourceDatabase database, AbiffMaterialFactory materials)
    {
        if (!AoTweakIncludeLoader.TryLoadPlayfieldFlattened(aoRoot, playfieldId, out string source))
            return false;

        Dictionary<string, AoTweakObjectParser.AoObject> objects = AoTweakObjectParser.Parse(source);
        PlayfieldEnvironmentTweak tweak = AoTweakEnvironmentBuilder.Build(objects);
        Capture();

        if (tweak.HasLighting)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = tweak.AmbientLightColor;
            Light sun = EnsureSun();
            sun.transform.rotation = tweak.SunRotation;
            sun.color = tweak.GroundLightColor.maxColorComponent > .01f ? tweak.GroundLightColor : Color.white;
            sun.intensity = Mathf.Clamp(1.1f + tweak.DayTimeFactor, .25f, 2.2f);
            RenderSettings.sun = sun;
        }

        if (tweak.HasFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = tweak.FogTint;
            RenderSettings.fogDensity = Mathf.Clamp(tweak.FogDensityHint * .01f, .00005f, .015f);
        }

        SpawnSkyMeshes(tweak, database, materials);
        Debug.Log($"[AoTweak/URP] Applied playfield {playfieldId}: lighting={tweak.HasLighting}, fog={tweak.HasFog}, skyMeshes={tweak.SkyMeshes.Count}.");
        return true;
    }

    void Capture()
    {
        if (_captured) return;
        _captured = true;
        _fog = RenderSettings.fog;
        _fogMode = RenderSettings.fogMode;
        _fogColor = RenderSettings.fogColor;
        _fogDensity = RenderSettings.fogDensity;
        _ambient = RenderSettings.ambientLight;
        _sun = RenderSettings.sun;
        if (_sun != null)
        {
            _sunRotation = _sun.transform.rotation;
            _sunColor = _sun.color;
        }
    }

    Light EnsureSun()
    {
        if (RenderSettings.sun != null) return RenderSettings.sun;
        var go = new GameObject("AO Primary Sun (URP)");
        go.transform.SetParent(transform, false);
        Light light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.shadows = LightShadows.Soft;
        return light;
    }

    void SpawnSkyMeshes(PlayfieldEnvironmentTweak tweak, ResourceDatabase database, AbiffMaterialFactory materials)
    {
        if (tweak?.SkyMeshes == null || tweak.SkyMeshes.Count == 0) return;
        var names = new AoTweakMeshNames(database);
        var loader = new AbiffLoader(database, materials);
        for (int i = 0; i < tweak.SkyMeshes.Count; i++)
        {
            AoSkyMeshPlacement placement = tweak.SkyMeshes[i];
            if (!placement.Enabled || !names.TryResolve(placement.MeshName, out int meshId)) continue;
            if (!loader.TryCreateSkyVisual(meshId, transform, out GameObject visual)) continue;
            visual.name = $"AO Sky {placement.ObjectName}";
            visual.transform.localPosition = placement.PositionOffset;
            visual.transform.localRotation = placement.LocalRotation;
            visual.transform.localScale = Vector3.one * Mathf.Max(.001f, placement.Scale);
            var follower = visual.AddComponent<UrpAoSkyFollower>();
            follower.PositionOffset = placement.PositionOffset;
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                renderers[r].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers[r].receiveShadows = false;
                Material material = renderers[r].material;
                float intensity = Mathf.Max(0f, placement.Intensity);
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", material.GetColor("_BaseColor") * intensity);
                else if (material.HasProperty("_Color"))
                    material.SetColor("_Color", material.GetColor("_Color") * intensity);
            }
        }
    }

    void OnDestroy()
    {
        if (!_captured) return;
        RenderSettings.fog = _fog;
        RenderSettings.fogMode = _fogMode;
        RenderSettings.fogColor = _fogColor;
        RenderSettings.fogDensity = _fogDensity;
        RenderSettings.ambientLight = _ambient;
        RenderSettings.sun = _sun;
        if (_sun != null)
        {
            _sun.transform.rotation = _sunRotation;
            _sun.color = _sunColor;
        }
    }
}

public sealed class UrpAoSkyFollower : MonoBehaviour
{
    public Vector3 PositionOffset;
    void LateUpdate()
    {
        Camera camera = Camera.main;
        if (camera != null) transform.position = camera.transform.position + PositionOffset;
    }
}
