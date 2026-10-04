using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AO.Assets.Decoders
{
    public sealed class AOIndoorVisualMesh
    {
        internal AOIndoorVisualMesh(float[] positions, float[] normals, float[] uvs, int[] triangles)
        { Positions=positions; Normals=normals; Uvs=uvs; Triangles=triangles; }
        public float[] Positions { get; }
        public float[] Normals { get; }
        public float[] Uvs { get; }
        public int[] Triangles { get; }
        public int VertexCount => Positions.Length / 3;
    }
    public sealed class AOIndoorVisualRoom
    {
        internal AOIndoorVisualRoom(int index, int cells, Dictionary<uint, AOIndoorVisualMesh> meshes)
        { Index=index; CellCount=cells; Meshes=meshes; }
        public int Index { get; }
        public int CellCount { get; }
        public IReadOnlyDictionary<uint, AOIndoorVisualMesh> Meshes { get; }
    }
    public sealed class AOIndoorVisualSet
    {
        internal AOIndoorVisualSet(int pf, int tilemap, List<AOIndoorVisualRoom> rooms, int[] dependencies)
        { PlayfieldId=pf; TilemapId=tilemap; Rooms=rooms; VisualResourceIds=dependencies; }
        public int PlayfieldId { get; }
        public int TilemapId { get; }
        public IReadOnlyList<AOIndoorVisualRoom> Rooms { get; }
        public int[] VisualResourceIds { get; }
    }
    /// <summary>Engine-independent AOVR decoder shared by the exporter and Unity renderer.</summary>
    public static class AOIndoorVisualStreamDecoder
    {
        private sealed class Builder
        {
            public readonly List<float> Positions=new List<float>(), Normals=new List<float>(), Uvs=new List<float>();
            public readonly List<int> Triangles=new List<int>();
        }
        public static AOIndoorVisualSet Decode(string path, int expectedPlayfield)
        { using (var input=File.OpenRead(path)) return Decode(input,expectedPlayfield); }
        public static AOIndoorVisualSet Decode(Stream input, int expectedPlayfield)
        {
            using (var reader=new BinaryReader(input,Encoding.ASCII,true))
            {
                try
                {
                    Magic(reader,"AOVR");
                    if (reader.ReadInt32()!=1 || reader.ReadInt32()!=expectedPlayfield) throw new InvalidDataException("Invalid native visual stream identity");
                    int tilemap=reader.ReadInt32(), count=Count(reader,65535); if(tilemap<=0 || count==0) throw new InvalidDataException("Invalid visual metadata");
                    var rooms=new List<AOIndoorVisualRoom>();var seenRooms=new HashSet<int>();
                    for(int r=0;r<count;++r)
                    {
                        int index=reader.ReadInt32(), cells=Count(reader,100000);
                        if(index<0 || !seenRooms.Add(index)) throw new InvalidDataException("Duplicate or invalid visual room");
                        var groups=new Dictionary<uint,Builder>();
                        for(int c=0;c<cells;++c)
                        {
                            int x=reader.ReadInt32(), y=reader.ReadInt32(), tile=reader.ReadInt32(), rotation=reader.ReadInt32(), vertices=Count(reader,32768);
                            if(x<0 || y<0 || tile<=0 || tile>=16384 || rotation<0 || rotation>3 || vertices==0) throw new InvalidDataException("Invalid visual cell");
                            Remaining(reader,checked(vertices*36));
                            var pos=new float[vertices*3];var normal=new float[vertices*3];var uv=new float[vertices*2];
                            for(int v=0;v<vertices;++v)
                            {
                                for(int k=0;k<3;++k)pos[v*3+k]=Finite(reader);
                                for(int k=0;k<3;++k)normal[v*3+k]=Finite(reader);
                                reader.ReadUInt32(); // native colour is not a recovered lightmap
                                uv[v*2]=Finite(reader);uv[v*2+1]=Finite(reader);
                            }
                            int materialGroups=Count(reader,64);
                            for(int g=0;g<materialGroups;++g)
                            {
                                uint material=reader.ReadUInt32();int indices=Count(reader,196608);
                                if(indices%3!=0)throw new InvalidDataException("Nontriangular visual buffer");Remaining(reader,checked(indices*2));
                                if(indices==0)continue;
                                if(!groups.TryGetValue(material,out var builder))groups.Add(material,builder=new Builder());
                                var remap=new Dictionary<ushort,int>();
                                for(int i=0;i<indices;++i)
                                {
                                    ushort original=reader.ReadUInt16();if(original>=vertices)throw new InvalidDataException("Visual index outside vertex buffer");
                                    if(!remap.TryGetValue(original,out int mapped))
                                    {
                                        mapped=builder.Positions.Count/3;remap.Add(original,mapped);
                                        for(int k=0;k<3;++k){builder.Positions.Add(pos[original*3+k]);builder.Normals.Add(normal[original*3+k]);}
                                        builder.Uvs.Add(uv[original*2]);builder.Uvs.Add(uv[original*2+1]);
                                    }
                                    builder.Triangles.Add(mapped);
                                }
                            }
                        }
                        if(groups.Count==0)throw new InvalidDataException("Empty visual room");
                        rooms.Add(new AOIndoorVisualRoom(index,cells,groups.ToDictionary(p=>p.Key,p=>new AOIndoorVisualMesh(p.Value.Positions.ToArray(),p.Value.Normals.ToArray(),p.Value.Uvs.ToArray(),p.Value.Triangles.ToArray()))));
                    }
                    Magic(reader,"AOVD");int dependencies=Count(reader,16384);Remaining(reader,checked(dependencies*4));
                    var ids=new int[dependencies];var seen=new HashSet<int>();
                    for(int i=0;i<ids.Length;++i){ids[i]=reader.ReadInt32();if(ids[i]<=0 || ids[i]>=16384 || !seen.Add(ids[i]))throw new InvalidDataException("Invalid visual dependency");}
                    if(ids.Length==0 || input.Position!=input.Length)throw new InvalidDataException("Incomplete or trailing visual data");
                    return new AOIndoorVisualSet(expectedPlayfield,tilemap,rooms,ids);
                }
                catch(EndOfStreamException error){throw new InvalidDataException("Truncated native visual stream",error);}
            }
        }
        private static void Magic(BinaryReader reader,string expected)
        { if(Encoding.ASCII.GetString(reader.ReadBytes(4))!=expected)throw new InvalidDataException("Invalid visual stream marker: "+expected); }
        private static int Count(BinaryReader reader,int limit)
        { int value=reader.ReadInt32();if(value<0 || value>limit)throw new InvalidDataException("Invalid visual count");return value; }
        private static float Finite(BinaryReader reader)
        { float value=reader.ReadSingle();if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidDataException("Nonfinite visual attribute");return value; }
        private static void Remaining(BinaryReader reader,int length)
        { if(length>reader.BaseStream.Length-reader.BaseStream.Position)throw new InvalidDataException("Truncated visual buffer"); }
    }
}
