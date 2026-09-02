using System.Collections;
using System.Collections.Generic;
using AODB;
using UnityEngine;
using UnityEngine.Rendering;
using AoVector3 = AODB.Common.Structs.Vector3;

public sealed class DirectOutdoorWaterBuilder
{
    private readonly ResourceDatabase _database;

    public DirectOutdoorWaterBuilder(ResourceDatabase database)
    {
        _database = database;
    }

    public IEnumerator BuildCoroutine(int playfieldId, Transform parent)
    {
        List<PfWaterMeshData> waterMeshes;
        try
        {
            waterMeshes = new WaterParser(_database.Rdb).Get(playfieldId);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"Direct outdoor water PF {playfieldId} could not be decoded: {exception.Message}");
            yield break;
        }

        if (waterMeshes == null || waterMeshes.Count == 0)
            yield break;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard")
            ?? Shader.Find("Unlit/Color");
        Material material = shader != null ? new Material(shader) : null;
        if (material != null)
        {
            material.name = $"PF_{playfieldId}_Water";
            Color color = new Color(0.08f, 0.38f, 0.48f, 0.72f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            material.renderQueue = 3000;
        }

        var root = new GameObject($"Water_{playfieldId}");
        root.transform.SetParent(parent, false);
        for (int i = 0; i < waterMeshes.Count; i++)
        {
            PfWaterMeshData source = waterMeshes[i];
            if (source?.Vertices == null || source.Vertices.Length == 0
                || source.Triangles == null || source.Triangles.Length < 3)
                continue;

            var vertices = new Vector3[source.Vertices.Length];
            for (int v = 0; v < vertices.Length; v++)
            {
                AoVector3 sourceVertex = source.Vertices[v];
                vertices[v] = new Vector3(sourceVertex.X, sourceVertex.Y, sourceVertex.Z);
            }
            var mesh = new Mesh
            {
                name = $"WaterMesh_{playfieldId}_{i}",
                indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(source.Triangles, 0, false);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var body = new GameObject($"WaterBody_{i}");
            body.transform.SetParent(root.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = body.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if ((i + 1) % 8 == 0)
                yield return null;
        }
    }
}
