using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using AODB.Common.RDBObjects;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class TerrainParser
{
    const int LodCount = 3;

    readonly ResourceDatabase _database;
    readonly RenderConfig _renderConfig;

    public TerrainParser(ResourceDatabase database, RenderConfig renderConfig)
    {
        _database = database;
        _renderConfig = renderConfig;
    }

    public IEnumerator BuildCoroutine(int playfieldId, Transform parent)
    {
        if (_renderConfig == null)
        {
            Debug.LogError("TerrainParser: RenderConfig is missing.");
            yield break;
        }

        var tilemap = _database.Get<Tilemap>(playfieldId);
        if (tilemap == null)
        {
            Debug.LogError($"TerrainParser: Tilemap {playfieldId} not found.");
            yield break;
        }

        if (tilemap.Heightmap == null || tilemap.Heightmap.Count == 0 || tilemap.ChunkSize <= 1)
        {
            Debug.LogError($"TerrainParser: No outdoor chunked ground for playfield {playfieldId}.");
            yield break;
        }

        int gridWidth = ResolveGridWidth(tilemap);
        if (gridWidth <= 0 || tilemap.Heightmap.Count % gridWidth != 0)
        {
            Debug.LogError(
                $"TerrainParser: Invalid chunk grid for playfield {playfieldId} " +
                $"(count={tilemap.Heightmap.Count}, gridWidth={gridWidth}).");
            yield break;
        }

        if (!TryCreateAtlas(tilemap.TextureIds, out Texture2D atlas, out Rect[] texBounds))
        {
            Debug.LogError($"TerrainParser: Failed to build atlas for playfield {playfieldId}.");
            yield break;
        }

        List<ChunkSource> chunks = CollectChunks(tilemap);
        if (chunks.Count == 0)
        {
            Debug.LogError($"TerrainParser: No usable chunks for playfield {playfieldId}.");
            yield break;
        }

        Material material = CreateAtlasMaterial(atlas);

        var root = new GameObject($"Playfield_{playfieldId}");
        root.transform.SetParent(parent, false);

        var chunkViews = new ChunkView[chunks.Count];
        for (int i = 0; i < chunks.Count; i++)
            chunkViews[i] = CreateChunkView(root.transform, chunks[i], material);

        yield return null;

        TerrainChunkMeshData[] lod0 = null;
        yield return RunParallel(() =>
        {
            lod0 = BuildLodLevel(chunks, texBounds, lod: 0);
        });

        for (int i = 0; i < chunkViews.Length; i++)
        {
            ApplyMesh(chunkViews[i], lod: 0, lod0[i]);
            chunkViews[i].LodGroup.SetLODs(new[]
            {
                new LOD(_renderConfig.GetTerrainLodScreenHeight(0), new Renderer[] { chunkViews[i].Renderers[0] })
            });
            chunkViews[i].LodGroup.RecalculateBounds();
        }

        yield return null;

        TerrainChunkMeshData[] lod1 = null;
        TerrainChunkMeshData[] lod2 = null;
        yield return RunParallel(() =>
        {
            lod1 = BuildLodLevel(chunks, texBounds, lod: 1);
            lod2 = BuildLodLevel(chunks, texBounds, lod: 2);
        });

        for (int i = 0; i < chunkViews.Length; i++)
        {
            ApplyMesh(chunkViews[i], lod: 1, lod1[i]);
            ApplyMesh(chunkViews[i], lod: 2, lod2[i]);

            for (int lod = 0; lod < LodCount; lod++)
                chunkViews[i].Renderers[lod].gameObject.SetActive(true);

            chunkViews[i].LodGroup.SetLODs(new[]
            {
                new LOD(_renderConfig.GetTerrainLodScreenHeight(0), new Renderer[] { chunkViews[i].Renderers[0] }),
                new LOD(_renderConfig.GetTerrainLodScreenHeight(1), new Renderer[] { chunkViews[i].Renderers[1] }),
                new LOD(_renderConfig.GetTerrainLodScreenHeight(2), new Renderer[] { chunkViews[i].Renderers[2] })
            });
            chunkViews[i].LodGroup.RecalculateBounds();
            chunkViews[i].Root.isStatic = true;
        }
    }

    static List<ChunkSource> CollectChunks(Tilemap tilemap)
    {
        int chunkSize = tilemap.ChunkSize;
        int gridWidth = ResolveGridWidth(tilemap);
        var chunks = new List<ChunkSource>(tilemap.Heightmap.Count);

        for (int i = 0; i < tilemap.Heightmap.Count; i++)
        {
            ushort[,] heightmap = tilemap.Heightmap[i];
            if (heightmap == null ||
                heightmap.GetLength(0) != chunkSize ||
                heightmap.GetLength(1) != chunkSize)
            {
                continue;
            }

            if (!tilemap.TileMapDatas.TryGetValue(i, out List<Tilemap.TileMapData> tileData) ||
                tileData == null ||
                tileData.Count == 0)
            {
                continue;
            }

            chunks.Add(new ChunkSource
            {
                ChunkX = i % gridWidth,
                ChunkY = i / gridWidth,
                ChunkSize = chunkSize,
                HeightMod = tilemap.HeightMod,
                MapScale = tilemap.MapScale,
                Heightmap = heightmap,
                TileData = tileData
            });
        }

        return chunks;
    }

    static int ResolveGridWidth(Tilemap tilemap)
    {
        if (tilemap == null || tilemap.Heightmap == null || tilemap.Heightmap.Count == 0)
            return 0;

        int extent = Mathf.Max(1, tilemap.ChunkSize - 1);
        int expected = Mathf.Max(1, Mathf.CeilToInt(tilemap.MapWidth / (float)extent));
        if (tilemap.Heightmap.Count % expected == 0)
            return expected;

        int root = Mathf.FloorToInt(Mathf.Sqrt(tilemap.Heightmap.Count));
        for (int width = root; width >= 1; width--)
        {
            if (tilemap.Heightmap.Count % width == 0)
                return width;
        }
        return 1;
    }

    static TerrainChunkMeshData[] BuildLodLevel(List<ChunkSource> chunks, Rect[] texBounds, int lod)
    {
        var result = new TerrainChunkMeshData[chunks.Count];
        Parallel.For(0, chunks.Count, i =>
        {
            ChunkSource c = chunks[i];
            result[i] = TerrainChunkBuilder.Build(
                c.ChunkX,
                c.ChunkY,
                c.ChunkSize,
                c.HeightMod,
                c.MapScale,
                c.Heightmap,
                c.TileData,
                texBounds,
                lod);
        });

        SmoothChunkBoundaries(chunks, result);
        return result;
    }

    static void SmoothChunkBoundaries(List<ChunkSource> chunks, TerrainChunkMeshData[] meshes)
    {
        var indexByCoord = new Dictionary<(int x, int y), int>(chunks.Count);
        for (int i = 0; i < chunks.Count; i++)
            indexByCoord[(chunks[i].ChunkX, chunks[i].ChunkY)] = i;

        for (int i = 0; i < chunks.Count; i++)
        {
            int cx = chunks[i].ChunkX;
            int cy = chunks[i].ChunkY;

            if (indexByCoord.TryGetValue((cx + 1, cy), out int right))
                TerrainChunkBuilder.SmoothBoundary(meshes[i], meshes[right]);

            if (indexByCoord.TryGetValue((cx, cy + 1), out int bottom))
                TerrainChunkBuilder.SmoothBoundary(meshes[i], meshes[bottom]);
        }
    }

    static ChunkView CreateChunkView(Transform parent, ChunkSource source, Material material)
    {
        var root = new GameObject($"Chunk_{source.ChunkX}_{source.ChunkY}");
        root.transform.SetParent(parent, false);

        var lodGroup = root.AddComponent<LODGroup>();
        var filters = new MeshFilter[LodCount];
        var renderers = new MeshRenderer[LodCount];

        for (int lod = 0; lod < LodCount; lod++)
        {
            var lodGo = new GameObject($"LOD{lod}");
            lodGo.transform.SetParent(root.transform, false);
            filters[lod] = lodGo.AddComponent<MeshFilter>();
            renderers[lod] = lodGo.AddComponent<MeshRenderer>();
            renderers[lod].sharedMaterial = material;
            lodGo.SetActive(lod == 0);
        }

        var collider = root.AddComponent<MeshCollider>();
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer >= 0)
        {
            root.layer = groundLayer;
            for (int i = 0; i < root.transform.childCount; i++)
                root.transform.GetChild(i).gameObject.layer = groundLayer;
        }

        return new ChunkView
        {
            Root = root,
            LodGroup = lodGroup,
            Filters = filters,
            Renderers = renderers,
            Collider = collider
        };
    }

    static void ApplyMesh(ChunkView view, int lod, TerrainChunkMeshData data)
    {
        if (data.Vertices == null || data.Vertices.Length == 0)
            return;

        var mesh = new Mesh
        {
            name = $"{view.Root.name}_LOD{lod}",
            indexFormat = data.Vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        mesh.SetVertices(data.Vertices);
        mesh.SetNormals(data.Normals);
        mesh.SetUVs(0, data.UVs);
        mesh.SetTriangles(data.Triangles, 0, calculateBounds: false);
        mesh.RecalculateBounds();

        view.Filters[lod].sharedMesh = mesh;
        view.Renderers[lod].gameObject.SetActive(true);

        if (lod == 0)
            view.Collider.sharedMesh = mesh;
        else
            mesh.UploadMeshData(markNoLongerReadable: true);
    }

    bool TryCreateAtlas(short[] textureIds, out Texture2D atlas, out Rect[] texBounds)
    {
        atlas = null;
        texBounds = Array.Empty<Rect>();

        if (textureIds == null || textureIds.Length == 0)
            return false;

        var textures = new Texture2D[textureIds.Length];
        for (int i = 0; i < textureIds.Length; i++)
        {
            var ground = _database.Get<GroundTexture>(textureIds[i]);
            if (ground?.JpgData == null || ground.JpgData.Length == 0)
            {
                Debug.LogError($"TerrainParser: Missing GroundTexture {textureIds[i]}.");
                DestroyTextures(textures);
                return false;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            if (!tex.LoadImage(ground.JpgData, markNonReadable: false))
            {
                Debug.LogError($"TerrainParser: Failed to decode GroundTexture {textureIds[i]}.");
                UnityEngine.Object.Destroy(tex);
                DestroyTextures(textures);
                return false;
            }

            textures[i] = tex;
        }

        int padding = Math.Max(
            16,
            Math.Max(
                _renderConfig.TerrainAtlasPadding,
                (1 << Mathf.Max(0, _renderConfig.TerrainAtlasFirstMipToSoften))
                    + _renderConfig.TerrainAtlasMipBlurPasses));

        atlas = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: true);
        texBounds = atlas.PackTextures(
            textures,
            padding,
            _renderConfig.TerrainAtlasMaxSize,
            makeNoLongerReadable: false);
        if (texBounds == null || texBounds.Length == 0)
        {
            DestroyTextures(textures);
            UnityEngine.Object.Destroy(atlas);
            atlas = null;
            return false;
        }

        atlas = RebuildAtlasWithSoftMips(
            atlas,
            texBounds,
            padding,
            _renderConfig.TerrainAtlasFirstMipToSoften,
            _renderConfig.TerrainAtlasMipBlurPasses,
            _renderConfig.TerrainAtlasAnisoLevel,
            _renderConfig.TerrainAtlasMipBias);

        DestroyTextures(textures);
        return true;
    }

    /// <summary>
    /// Preserve a sharp mip 0 while sealing every tile gutter at every mip level.
    /// </summary>
    static Texture2D RebuildAtlasWithSoftMips(
        Texture2D packed,
        Rect[] bounds,
        int gutterRadius,
        int firstMipToSoften,
        int blurPasses,
        int anisoLevel,
        float mipMapBias)
    {
        int width = packed.width;
        int height = packed.height;
        Color[] mip0 = packed.GetPixels(0);
        DilateAtlasTileBorders(mip0, width, height, bounds, Mathf.Max(1, gutterRadius));

        var rebuilt = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: true, linear: false)
        {
            name = "PlayfieldAtlas"
        };
        rebuilt.SetPixels(mip0);
        rebuilt.Apply(updateMipmaps: true, makeNoLongerReadable: false);

        int start = Mathf.Clamp(firstMipToSoften, 1, rebuilt.mipmapCount - 1);
        for (int mip = start; mip < rebuilt.mipmapCount; mip++)
        {
            int w = Mathf.Max(1, width >> mip);
            int h = Mathf.Max(1, height >> mip);
            Color[] pixels = rebuilt.GetPixels(mip);
            if (pixels == null || pixels.Length != w * h)
                continue;

            int passes = Mathf.Max(0, blurPasses) + (mip - start);
            for (int pass = 0; pass < passes; pass++)
                pixels = BoxBlur3x3(pixels, w, h);

            DilateAtlasTileBorders(pixels, w, h, bounds, Mathf.Max(1, gutterRadius >> mip));
            rebuilt.SetPixels(pixels, mip);
        }

        rebuilt.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        rebuilt.wrapMode = TextureWrapMode.Clamp;
        rebuilt.filterMode = FilterMode.Trilinear;
        rebuilt.anisoLevel = Mathf.Clamp(anisoLevel, 0, 16);
        rebuilt.mipMapBias = mipMapBias;
        UnityEngine.Object.Destroy(packed);
        return rebuilt;
    }

    static void DilateAtlasTileBorders(Color[] pixels, int width, int height, Rect[] bounds, int radius)
    {
        if (pixels == null || bounds == null || radius <= 0)
            return;

        for (int i = 0; i < bounds.Length; i++)
        {
            Rect rect = bounds[i];
            int x0 = Mathf.Clamp(Mathf.FloorToInt(rect.x * width), 0, width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(rect.y * height), 0, height - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((rect.x + rect.width) * width), x0 + 1, width);
            int y1 = Mathf.Clamp(Mathf.CeilToInt((rect.y + rect.height) * height), y0 + 1, height);
            for (int distance = 1; distance <= radius; distance++)
            {
                for (int y = y0; y < y1; y++)
                {
                    if (x0 - distance >= 0) pixels[y * width + x0 - distance] = pixels[y * width + x0];
                    if (x1 - 1 + distance < width) pixels[y * width + x1 - 1 + distance] = pixels[y * width + x1 - 1];
                }
                for (int x = Math.Max(0, x0 - distance); x < Math.Min(width, x1 + distance); x++)
                {
                    int sourceX = Mathf.Clamp(x, x0, x1 - 1);
                    if (y0 - distance >= 0) pixels[(y0 - distance) * width + x] = pixels[y0 * width + sourceX];
                    if (y1 - 1 + distance < height) pixels[(y1 - 1 + distance) * width + x] = pixels[(y1 - 1) * width + sourceX];
                }
            }
        }
    }

    static Color[] BoxBlur3x3(Color[] src, int width, int height)
    {
        var dst = new Color[src.Length];
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                Color sum = Color.clear;
                int count = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int sy = y + dy;
                    if ((uint)sy >= (uint)height)
                        continue;

                    int srow = sy * width;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int sx = x + dx;
                        if ((uint)sx >= (uint)width)
                            continue;

                        sum += src[srow + sx];
                        count++;
                    }
                }

                dst[row + x] = count > 0 ? sum / count : src[row + x];
            }
        }

        return dst;
    }

    static Material CreateAtlasMaterial(Texture2D atlas)
    {
        Material material = HdrpLitMaterialFactory.Create("PlayfieldTerrain");
        if (material.HasProperty("_BaseColorMap"))
            material.SetTexture("_BaseColorMap", atlas);
        else if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", atlas);
        else if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", atlas);

        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0f);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", 0f);

        return material;
    }

    static void DestroyTextures(Texture2D[] textures)
    {
        if (textures == null)
            return;

        for (int i = 0; i < textures.Length; i++)
        {
            if (textures[i] != null)
                UnityEngine.Object.Destroy(textures[i]);
        }
    }

    static IEnumerator RunParallel(Action work)
    {
        Task task = Task.Run(work);
        while (!task.IsCompleted)
            yield return null;

        if (task.IsFaulted)
            throw task.Exception?.InnerException ?? task.Exception;
    }

    sealed class ChunkSource
    {
        public int ChunkX;
        public int ChunkY;
        public int ChunkSize;
        public float HeightMod;
        public float MapScale;
        public ushort[,] Heightmap;
        public List<Tilemap.TileMapData> TileData;
    }

    sealed class ChunkView
    {
        public GameObject Root;
        public LODGroup LodGroup;
        public MeshFilter[] Filters;
        public MeshRenderer[] Renderers;
        public MeshCollider Collider;
    }
}
