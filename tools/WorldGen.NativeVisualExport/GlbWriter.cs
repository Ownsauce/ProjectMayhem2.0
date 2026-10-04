using System.Text.Json;
using AO.Assets.Decoders;

// Self-contained glTF 2.0 preview/export. AO is left-handed; mirror Z and reverse
// triangle winding for glTF. AO's Direct3D UVs already use glTF's top-origin V.
static class GlbWriter
{
    public static void Write(string path,string name,VisualRoom room,Dictionary<uint,ExportTexture> textures,AOIndoorRoom sourceRoom)
    {
        using var data=new MemoryStream();using var binary=new BinaryWriter(data);
        var views=new List<object>();var accessors=new List<object>();var images=new List<object>();var mats=new List<object>();var primitives=new List<object>();
        void Align(){while(data.Position%4!=0)binary.Write((byte)0);}
        int View(byte[] bytes,int? target=null){Align();int offset=(int)data.Position;binary.Write(bytes);int index=views.Count;views.Add(target.HasValue?new {buffer=0,byteOffset=offset,byteLength=bytes.Length,target=target.Value}:(object)new {buffer=0,byteOffset=offset,byteLength=bytes.Length});return index;}
        int Floats(float[] values,int components,bool bounds=false)
        {
            var bytes=new byte[values.Length*4];Buffer.BlockCopy(values,0,bytes,0,bytes.Length);int view=View(bytes,34962),id=accessors.Count;
            var accessor=new Dictionary<string,object>{{"bufferView",view},{"componentType",5126},{"count",values.Length/components},{"type",components==3?"VEC3":"VEC2"}};
            if(bounds){accessor["min"]=Enumerable.Range(0,components).Select(c=>values.Where((_,i)=>i%components==c).Min()).ToArray();accessor["max"]=Enumerable.Range(0,components).Select(c=>values.Where((_,i)=>i%components==c).Max()).ToArray();}
            accessors.Add(accessor);return id;
        }
        foreach(var (material,group) in room.Groups)
        {
            var texture=textures[material];int image=images.Count;images.Add(new {bufferView=View(texture.Bytes),mimeType="image/png",name=$"{texture.ResourceType}:{texture.ResourceId}"});
            int mat=mats.Count;mats.Add(new {name=$"AO_{texture.ResourceType}_{texture.ResourceId}",pbrMetallicRoughness=new {baseColorTexture=new {index=image},metallicFactor=0,roughnessFactor=1},doubleSided=false});
            float[] positions=group.Positions.ToArray(),normals=group.Normals.ToArray();for(int i=0;i<positions.Length;i+=3){positions[i]-=sourceRoom.X;positions[i+1]-=sourceRoom.Y;positions[i+2]=-(positions[i+2]-sourceRoom.Z);normals[i+2]=-normals[i+2];}
            uint[] indices=group.Indices.ToArray();for(int i=0;i<indices.Length;i+=3)(indices[i+1],indices[i+2])=(indices[i+2],indices[i+1]);
            var indexBytes=new byte[indices.Length*4];Buffer.BlockCopy(indices,0,indexBytes,0,indexBytes.Length);int indexView=View(indexBytes,34963),indexAccessor=accessors.Count;
            accessors.Add(new {bufferView=indexView,componentType=5125,count=indices.Length,type="SCALAR"});
            primitives.Add(new {attributes=new Dictionary<string,int>{{"POSITION",Floats(positions,3,true)},{"NORMAL",Floats(normals,3)},{"TEXCOORD_0",Floats(group.Uvs.ToArray(),2)}},indices=indexAccessor,material=mat,mode=4});
        }
        Align();var doc=new {asset=new {version="2.0",generator="ProjectMayhem NativeVisualExport"},scene=0,scenes=new[]{new {nodes=new[]{0}}},nodes=new[]{new {name,mesh=0}},meshes=new[]{new {name,primitives}},materials=mats,
            textures=images.Select((_,i)=>new {source=i,sampler=0}).ToArray(),samplers=new[]{new {magFilter=9729,minFilter=9987,wrapS=10497,wrapT=10497}},images,buffers=new[]{new {byteLength=(int)data.Length}},bufferViews=views,accessors};
        byte[] json=JsonSerializer.SerializeToUtf8Bytes(doc);int padded=(json.Length+3)&~3;
        using var output=new BinaryWriter(File.Create(path));output.Write(0x46546c67);output.Write(2);output.Write(12+8+padded+8+(int)data.Length);output.Write(padded);output.Write(0x4e4f534a);output.Write(json);for(int i=json.Length;i<padded;++i)output.Write((byte)32);
        output.Write((int)data.Length);output.Write(0x004e4942);output.Write(data.ToArray());
    }
}
