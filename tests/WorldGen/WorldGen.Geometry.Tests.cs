using System;
using WorldGen.Geometry;

internal static class Program
{
    private static void Main()
    {
        CalibratedProjectionMovesWithPrincipalPoint();
        ProjectionUsesUnityScreenRight();
        LodPreservesClosedSurfaceAndBudget();
        AuthoredMeshRejectsDegenerateFaces();
        ConcavePolygonExtrudesClosedFaces();
        UvAtlasPreservesGeometryAndBounds();
        Console.WriteLine("WorldGen.Geometry projection and LOD tests passed.");
    }

    private static void CalibratedProjectionMovesWithPrincipalPoint()
    {
        GeometryMeshData box = AssetPrimitiveMeshBuilder.Build(new AssetPrimitiveRecipe
        { Shape = AssetPrimitiveShape.Box, Width = 1, Height = 1, Depth = 1 });
        var camera = new MeshProjectionCamera
        { Position = new GeometryVector3(0, 0, -3), Target = new GeometryVector3(0, 0, 0) };
        MeshProjectionResult centered = MeshProjectionBuilder.Render(box, camera, 64, 64);
        int left = Count(centered, 0, 32), right = Count(centered, 32, 64);
        if (Math.Abs(left - right) > 40) throw new Exception("Centered projection is asymmetric.");
        camera.PrincipalPointX = .7f;
        MeshProjectionResult shifted = MeshProjectionBuilder.Render(box, camera, 64, 64);
        if (Count(shifted, 32, 64) <= right) throw new Exception("Principal point did not shift the render.");
    }

    private static void ProjectionUsesUnityScreenRight()
    {
        GeometryMeshData box = AssetPrimitiveMeshBuilder.Build(new AssetPrimitiveRecipe
        { Shape = AssetPrimitiveShape.Box, Width = .4f, Height = .4f, Depth = .4f });
        var translated = new GeometryVector3[box.Vertices.Length];
        for (int i = 0; i < translated.Length; i++)
            translated[i] = new GeometryVector3(box.Vertices[i].X + .5f,
                box.Vertices[i].Y, box.Vertices[i].Z + .5f);
        var mesh = new GeometryMeshData(translated, box.Normals, box.UV, box.Indices);
        var front = new MeshProjectionCamera
        { Position = new GeometryVector3(0,0,3), Target = new GeometryVector3(),
            Perspective = false, OrthographicHeight = 2 };
        MeshProjectionResult image = MeshProjectionBuilder.Render(mesh, front, 64, 64);
        if (Count(image,0,32) <= Count(image,32,64))
            throw new Exception("+X geometry should appear left in Unity's +Z front view.");
        var side = new MeshProjectionCamera
        { Position = new GeometryVector3(3,0,0), Target = new GeometryVector3(),
            Perspective = false, OrthographicHeight = 2 };
        image = MeshProjectionBuilder.Render(mesh, side, 64, 64);
        if (Count(image,32,64) <= Count(image,0,32))
            throw new Exception("+Z geometry should appear right in Unity's +X side view.");
    }

    private static void LodPreservesClosedSurfaceAndBudget()
    {
        GeometryMeshData sphere = AssetPrimitiveMeshBuilder.Build(new AssetPrimitiveRecipe
        { Shape = AssetPrimitiveShape.Ellipsoid, Width = 2, Height = 2, Depth = 2, Segments = 64 });
        GeometryMeshData lod = MeshLodBuilder.Build(sphere, 800);
        if (lod.Indices.Length / 3 > 800 || lod.Indices.Length / 3 >= sphere.Indices.Length / 3)
            throw new Exception("LOD did not meet the triangle budget.");
        MeshProjectionCamera camera = new MeshProjectionCamera
        { Position = new GeometryVector3(0, 0, -4), Target = new GeometryVector3(0, 0, 0) };
        MeshProjectionResult source = MeshProjectionBuilder.Render(sphere, camera, 128, 128);
        MeshProjectionResult reduced = MeshProjectionBuilder.Render(lod, camera, 128, 128);
        int intersection = 0, union = 0;
        for (int i = 0; i < source.Mask.Length; i++)
        { if (source.Mask[i] && reduced.Mask[i]) intersection++; if (source.Mask[i] || reduced.Mask[i]) union++; }
        if (intersection / (double)union < .92) throw new Exception("LOD changed the silhouette too much.");
    }

    private static int Count(MeshProjectionResult image, int firstX, int lastX)
    { int count = 0; for (int y = 0; y < image.Height; y++) for (int x = firstX; x < lastX; x++)
        if (image.Mask[y * image.Width + x]) count++; return count; }

    private static void AuthoredMeshRejectsDegenerateFaces()
    {
        var recipe = new AuthoredMeshRecipe
        { Positions = new float[] { 0, 0, 0, 1, 0, 0, 2, 0, 0 }, Indices = new[] { 0, 1, 2 } };
        try { AuthoredMeshBuilder.Build(recipe); throw new Exception("Degenerate face was accepted."); }
        catch (ArgumentException) { }
    }





    private static void ConcavePolygonExtrudesClosedFaces()
    {
        var recipe = new AssetPrimitiveRecipe
        {
            Shape = AssetPrimitiveShape.ExtrudedPolygon, Width = 2, Height = 2, Depth = .2f,
            Axis = AssetPrimitiveAxis.Z,
            PolygonOutline = new[]
            {
                new AssetPolygonPoint(-1,-1), new AssetPolygonPoint(1,-1),
                new AssetPolygonPoint(1,0), new AssetPolygonPoint(.2f,0),
                new AssetPolygonPoint(.2f,1), new AssetPolygonPoint(-1,1)
            }
        };
        GeometryMeshData mesh = AssetPrimitiveMeshBuilder.Build(recipe);
        if (mesh.Indices.Length != (6 * 2 + (6 - 2) * 2) * 3)
            throw new Exception("Concave extrusion emitted the wrong number of faces.");
        foreach (GeometryVector3 vertex in mesh.Vertices)
            if (Math.Abs(vertex.X) > 1.001f || Math.Abs(vertex.Y) > 1.001f ||
                Math.Abs(vertex.Z) > .101f)
                throw new Exception("Concave extrusion exceeded its bounds.");
        recipe.PolygonOutline = new[]
        {
            new AssetPolygonPoint(-1,-1), new AssetPolygonPoint(1,1),
            new AssetPolygonPoint(-1,1), new AssetPolygonPoint(1,-1)
        };
        try { AssetPrimitiveMeshBuilder.Build(recipe); throw new Exception("Crossed outline was accepted."); }
        catch (ArgumentException) { }
    }

    private static void UvAtlasPreservesGeometryAndBounds()
    {
        GeometryMeshData source = AssetPrimitiveMeshBuilder.Build(new AssetPrimitiveRecipe
        { Shape = AssetPrimitiveShape.Box, Width = 2, Height = 2, Depth = 2 });
        GeometryMeshData atlas = MeshUvAtlasBuilder.Build(source,
            new GeometryVector3(-1, -1, -1), new GeometryVector3(1, 1, 1));
        if (atlas.Indices.Length != source.Indices.Length || atlas.UV.Length != atlas.Vertices.Length)
            throw new Exception("UV atlas changed face count or omitted texture coordinates.");
        AuthoredMeshRecipe serialized = AuthoredMeshBuilder.FromMesh(atlas, atlas.Indices.Length / 3);
        if (serialized.Normals.Length != atlas.Vertices.Length * 3 ||
            AuthoredMeshBuilder.Build(serialized).Normals.Length != atlas.Vertices.Length)
            throw new Exception("UV atlas lost normals during mesh serialization.");
        foreach (GeometryVector2 uv in atlas.UV)
            if (uv.X <= 0 || uv.X >= 1 || uv.Y <= 0 || uv.Y >= 1)
                throw new Exception("UV atlas coordinate is outside its padded tile.");
        var camera = new MeshProjectionCamera
        { Position = new GeometryVector3(0, 0, 4), Target = new GeometryVector3() };
        MeshProjectionResult a = MeshProjectionBuilder.Render(source, camera, 32, 32);
        MeshProjectionResult b = MeshProjectionBuilder.Render(atlas, camera, 32, 32);
        for (int i = 0; i < a.Mask.Length; i++)
            if (a.Mask[i] != b.Mask[i]) throw new Exception("UV atlas changed the silhouette.");
    }


}
