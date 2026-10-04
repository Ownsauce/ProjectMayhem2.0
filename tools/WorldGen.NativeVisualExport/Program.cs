using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using AO.Assets.Decoders;
using AO.Assets.ResourceDatabase;
using AODB.Common.RDBObjects;
using StbImageSharp;
using StbImageWriteSharp;

if (args.Length < 4 || args.Length > 5)
{
    Console.Error.WriteLine("Usage: WorldGen.NativeVisualExport <AO-install> <PF-id> <output-directory> <helper.exe> [room-index]");
    return 2;
}
string install=Path.GetFullPath(args[0]), output=Path.GetFullPath(args[2]);
int pfId=int.Parse(args[1]); int? onlyRoom=args.Length==5?int.Parse(args[4]):null;
Directory.CreateDirectory(output);
using var db=new AOResourceDatabase(install);
byte[] Record(int type,int id) => db.TryReadRaw(type,id,out var bytes)?bytes:throw new InvalidDataException($"Missing resource {type}:{id}");
string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
var source=Record(1000001,pfId);var pf=AOPlayfieldDefinitionDecoder.Decode(source,pfId);
if(!pf.IsIndoor||pf.RoomCount==0)throw new InvalidDataException("An indoor playfield is required.");
if(onlyRoom.HasValue&&(onlyRoom<0||onlyRoom>=pf.RoomCount))throw new ArgumentOutOfRangeException("room-index");
var requested=Enumerable.Range(0,pf.RoomCount).Where(i=>!onlyRoom.HasValue||i==onlyRoom).ToArray();
string geometry=$"pf{pfId}.visual.aovr",manifest=$"pf{pfId}.visual.json";
string temporary=Path.Combine(output,$".extract-{Guid.NewGuid():N}");Directory.CreateDirectory(temporary);
try
{
    string plan=Path.Combine(temporary,"rooms.plan"),rawPath=Path.Combine(temporary,geometry);
    using(var writer=new BinaryWriter(File.Create(plan)))
    {
        writer.Write("AORP"u8);writer.Write(1);writer.Write(pfId);writer.Write(pf.TilemapId);writer.Write(requested.Length);
        foreach(int i in requested){var r=pf.Rooms[i];writer.Write(i);writer.Write(r.RotationQuarterTurns);writer.Write((int)r.TileX1);writer.Write((int)r.TileY1);writer.Write((int)r.TileX2);writer.Write((int)r.TileY2);writer.Write(r.X);writer.Write(r.Y);writer.Write(r.Z);writer.Write(r.CenterX);writer.Write(r.CenterZ);}
    }
    bool wine=!OperatingSystem.IsWindows();
    string NativePath(string path)=>wine?"Z:"+Path.GetFullPath(path).Replace('/','\\'):Path.GetFullPath(path);
    var start=new ProcessStartInfo(wine?"wine":Path.GetFullPath(args[3])){UseShellExecute=false};
    if(wine)start.ArgumentList.Add(Path.GetFullPath(args[3]));
    start.ArgumentList.Add(NativePath(install));start.ArgumentList.Add(NativePath(plan));start.ArgumentList.Add(NativePath(rawPath));start.Environment["WINEDEBUG"]="-all";
    using(var process=Process.Start(start)??throw new InvalidOperationException("Could not launch extraction helper"))
    {
        if(!process.WaitForExit(300000)){process.Kill(true);throw new TimeoutException("Native visual extraction exceeded five minutes");}
        if(process.ExitCode!=0)throw new InvalidDataException($"Native helper failed ({process.ExitCode}); no package was installed.");
    }
    var decoded=AOIndoorVisualStreamDecoder.Decode(rawPath,pfId);
    if(decoded.TilemapId!=pf.TilemapId||!decoded.Rooms.Select(r=>r.Index).SequenceEqual(requested))throw new InvalidDataException("Incomplete or mismatched visual rooms");
    var visualIds=decoded.VisualResourceIds;
    var rooms=decoded.Rooms.Select(r=>{
        var room=new VisualRoom(r.Index){CellCount=r.CellCount};
        foreach(var pair in r.Meshes){var mesh=pair.Value;var group=new VisualGroup();group.Positions.AddRange(mesh.Positions);group.Normals.AddRange(mesh.Normals);group.Uvs.AddRange(mesh.Uvs);group.Indices.AddRange(mesh.Triangles.Select(i=>(uint)i));room.Groups.Add(pair.Key,group);}return room;
    }).ToList();
    var materials=rooms.SelectMany(r=>r.Groups.Keys).Distinct().Order().ToArray();
    string textureDirectory=Path.Combine(temporary,"textures");Directory.CreateDirectory(textureDirectory);
    var textures=new Dictionary<uint,ExportTexture>();
    foreach(uint material in materials)
    {
        int type=(material&0x80000000)!=0?1010004:1010009,id=(int)(material&0x7fffffff);
        var record=Record(type,id);byte[] image;
        using(var reader=new BinaryReader(new MemoryStream(record,12,record.Length-12,false)))
        {
            if(type==1010004){var texture=new AOTexture();texture.Deserialize(reader);image=texture.JpgData;}
            else {var texture=new WallTexture();texture.Deserialize(reader);image=texture.JpgData;}
        }
        if(image==null||image.Length==0)throw new InvalidDataException($"Empty texture {type}:{id}");
        var decodedImage=ImageResult.FromMemory(image,StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        using var png=new MemoryStream();new ImageWriter().WritePng(decodedImage.Data,decodedImage.Width,decodedImage.Height,StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha,png);
        byte[] bytes=png.ToArray();string path=$"textures/{type}-{id}.png";File.WriteAllBytes(Path.Combine(temporary,path),bytes);
        textures.Add(material,new ExportTexture(material,type,id,path,Hash(record),Hash(bytes),decodedImage.Width,decodedImage.Height,bytes));
    }
    var roomReports=new List<object>();
    foreach(var room in rooms)
    {
        string path=$"room-{room.Index:D3}.glb";
        GlbWriter.Write(Path.Combine(temporary,path),pf.Rooms[room.Index].Name,room,textures,pf.Rooms[room.Index]);
        roomReports.Add(new {room.Index,Name=pf.Rooms[room.Index].Name,room.CellCount,OriginAo=new[]{pf.Rooms[room.Index].X,pf.Rooms[room.Index].Y,pf.Rooms[room.Index].Z},SourceRotationQuarterTurns=pf.Rooms[room.Index].RotationQuarterTurns,VertexCount=room.Groups.Values.Sum(g=>g.Positions.Count/3),TriangleCount=room.Groups.Values.Sum(g=>g.Indices.Count/3),Glb=path,Bounds=room.Bounds()});
    }
    var report=new {SchemaVersion=1,SourcePlayfield=pfId,SourceSha256=Hash(source),TilemapId=pf.TilemapId,TilemapSha256=Hash(Record(1000009,pf.TilemapId)),
        NativeRuntimeSha256=Hash(File.ReadAllBytes(Path.Combine(install,"N3.dll"))),GeometryFile=geometry,GeometrySha256=Hash(File.ReadAllBytes(rawPath)),
        Dependencies=visualIds.Select(id=>new {ResourceType=1010013,ResourceId=id,RecordSha256=Hash(Record(1010013,id))}),
        CompleteRoomSet=!onlyRoom.HasValue,RoomCount=rooms.Count,SourceRoomCount=pf.RoomCount,Rooms=roomReports,
        Textures=textures.Values.Select(t=>new {t.PackedMaterial,t.ResourceType,t.ResourceId,t.Path,t.RecordSha256,t.ImageSha256,t.Width,t.Height}),
        Limitations=new[]{"Static visual tile architecture only; placed dynel props, animated doors, water and original baked lighting are separate dependencies.","Native extraction supports the explicitly verified N3.dll build only; offsets are never called for a different hash.","GLB uses right-handed coordinates (AO Z negated) relative to OriginAo; source room rotation is baked. The runtime AOVR stream retains AO world coordinates."}};
    File.WriteAllText(Path.Combine(temporary,manifest),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    // The manifest is the commit marker: only publish it after complete, validated geometry/images.
    foreach(string file in Directory.EnumerateFiles(temporary,"*",SearchOption.AllDirectories))
    {
        if(file==plan||Path.GetFileName(file)==manifest)continue;
        string target=Path.Combine(output,Path.GetRelativePath(temporary,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Move(file,target,true);
    }
    File.Move(Path.Combine(temporary,manifest),Path.Combine(output,manifest),true);
    Console.WriteLine($"Exported PF {pfId}: {rooms.Count} rooms, {rooms.Sum(r=>r.Groups.Values.Sum(g=>g.Indices.Count/3))} triangles, {textures.Count} original textures to {output}");
}
finally {Directory.Delete(temporary,true);}
return 0;

record ExportTexture(uint PackedMaterial,int ResourceType,int ResourceId,string Path,string RecordSha256,string ImageSha256,int Width,int Height,byte[] Bytes);
sealed class VisualGroup
{
    public List<float> Positions {get;}=[];public List<float> Normals {get;}=[];public List<float> Uvs {get;}=[];public List<uint> Indices {get;}=[];
}
sealed class VisualRoom(int index)
{
    public int Index {get;}=index;public int CellCount {get;set;}
    public SortedDictionary<uint,VisualGroup> Groups {get;}=[];
    public object Bounds() {var p=Groups.Values.SelectMany(g=>g.Positions).ToArray();return new {Minimum=Enumerable.Range(0,3).Select(k=>p.Where((_,i)=>i%3==k).Min()).ToArray(),Maximum=Enumerable.Range(0,3).Select(k=>p.Where((_,i)=>i%3==k).Max()).ToArray()};}
}
