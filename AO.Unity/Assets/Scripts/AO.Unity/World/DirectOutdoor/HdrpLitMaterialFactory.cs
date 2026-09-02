using UnityEngine;

// Kept under the established type name so the adapted renderers remain close to
// upstream. Materials resolve against Project Mayhem's active render pipeline.
public static class HdrpLitMaterialFactory
{
    public static Material Create(string name = "AO Outdoor Lit")
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard")
            ?? Shader.Find("Unlit/Texture");
        if (shader == null)
            throw new System.InvalidOperationException("No compatible outdoor playfield shader is available.");
        return new Material(shader) { name = name };
    }

    public static Material CreateAlphaClip(string name = "AO Outdoor Alpha Clip", float cutoff = 0.5f)
    {
        Material material = Create(name);
        if (material.HasProperty("_AlphaClip"))
            material.SetFloat("_AlphaClip", 1f);
        if (material.HasProperty("_Cutoff"))
            material.SetFloat("_Cutoff", cutoff);
        material.EnableKeyword("_ALPHATEST_ON");
        material.renderQueue = 2450;
        return material;
    }
}
