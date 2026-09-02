using UnityEngine;

public sealed class RenderConfig
{
    private readonly float[] _terrainLodScreenHeights = { 0.6f, 0.25f, 0.08f };

    public int TerrainAtlasMaxSize => 8192;
    public int TerrainAtlasPadding => 16;
    public int TerrainAtlasFirstMipToSoften => 2;
    public int TerrainAtlasMipBlurPasses => 2;
    public int TerrainAtlasAnisoLevel => 8;
    public float TerrainAtlasMipBias => 0.25f;

    public float GetTerrainLodScreenHeight(int lod)
    {
        int index = Mathf.Clamp(lod, 0, _terrainLodScreenHeights.Length - 1);
        return _terrainLodScreenHeights[index];
    }
}
