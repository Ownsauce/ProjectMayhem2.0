using System.Text.Json;
using AO.Assets.Decoders;
using AO.Assets.Navigation;
using AO.Assets.ResourceDatabase;
using WorldGen.Dungeons;
using WorldGen.Spatial;

if (args.Length < 3 || args.Length > 5) throw new ArgumentException("<AO-install> <source.aois> <output-directory> [authoring-overrides.json|-] [source-definition.json]");
var sourceDefinition = NativeRoomSourceDefinition.Load(args.Length == 5 ? args[4] : Path.Combine(AppContext.BaseDirectory, "pf127.source.json"));
int sourcePlayfield = sourceDefinition.SourcePlayfield;
int entranceIndex = sourceDefinition.EntranceSourceRoom;
using var db=new AOResourceDatabase(args[0]);
if(!db.TryReadRaw(AOResourceTypes.Playfield,sourcePlayfield,out var record))throw new InvalidDataException("Missing source playfield " + sourcePlayfield);
var definition=AOPlayfieldDefinitionDecoder.Decode(record,sourcePlayfield);
var surfaces=AOIndoorSurfaceStreamDecoder.Decode(args[1],sourcePlayfield);
if(surfaces.Rooms.Count!=definition.RoomCount)throw new InvalidDataException("Source room count mismatch");
sourceDefinition.ValidateRoomCount(definition.RoomCount);
int Mm(float value)=>(int)Math.Round(value*1000,MidpointRounding.AwayFromZero);
var templates=new List<NativeRoomTemplate>();var geometry=new Dictionary<int,List<AOIndoorSurfaceMesh>>();
for(int index=0;index<definition.RoomCount;index++)
{
 var room=definition.Rooms[index];int width=room.TileX2-room.TileX1,depth=room.TileY2-room.TileY1;
 var meshes=surfaces.Rooms.Single(r=>r.Instance==index).Meshes
  .Select(mesh=>AOIndoorRoomCollisionClipper.Clip(mesh,room,surfaces.Tilemap.TileSize)).Where(mesh=>mesh.TriangleCount>0).ToList();
 var terrain=AOIndoorDungeonTerrainBuilder.Build(room,surfaces.Tilemap,true);if(terrain!=null)meshes.Add(terrain);
 geometry[index]=meshes;
 float baseline=float.MaxValue;
 for(int z=room.TileY1;z<room.TileY2;z++)for(int x=room.TileX1;x<room.TileX2;x++)
  if((surfaces.Tilemap.GetCollision(x,z)&127)!=0)baseline=Math.Min(baseline,surfaces.Tilemap.GetHeight(x,z)*surfaces.Tilemap.HeightmapScale);
 var cells=room.DoorConnections.Where(d=>d.ZoneLink!=index).Select(d=>{
  int packed=(ushort)d.PosRot,cell=packed>>2,side=packed&3,x=cell%width,z=cell/width;
  if(x>=width||z>=depth)throw new InvalidDataException("Portal cell outside room "+index);
  float lx=(x-room.CenterX)*2+(side==1?1:side==3?-1:0),lz=(z-room.CenterZ)*2+(side==0?1:side==2?-1:0);
  var offset=NativeRoomDungeon.Rotate(new WorldVector3(Mm(lx),0,Mm(lz)),room.RotationQuarterTurns);
  float guess=room.Y+surfaces.Tilemap.GetHeight(room.TileX1+x,room.TileY1+z)*surfaces.Tilemap.HeightmapScale-baseline;
  var normal=NativeRoomDungeon.Rotate(new WorldVector3(side==1?1000:side==3?-1000:0,0,side==0?1000:side==2?-1000:0),room.RotationQuarterTurns);
  float y=FindHeight(meshes,room.X+offset.X*.001f-normal.X*.00035f,room.Z+offset.Z*.001f-normal.Z*.00035f,guess)??guess;
  return new{d.ZoneLink,Side=side,Axis=side%2==0?x:z,Fixed=side%2==0?z:x,Offset=offset,Y=Mm(y-room.Y)};
 }).ToArray();
 var sockets=new List<NativeRoomSocket>();
 foreach(var group in cells.GroupBy(c=>(c.ZoneLink,c.Side,c.Fixed)).OrderBy(g=>g.Key.ZoneLink).ThenBy(g=>g.Key.Side).ThenBy(g=>g.Key.Fixed))
 {
  var sorted=group.OrderBy(c=>c.Axis).ToArray();int start=0;
  while(start<sorted.Length){int end=start+1;
   while(end<sorted.Length&&sorted[end].Axis==sorted[end-1].Axis+1&&Math.Abs(sorted[end].Y-sorted[start].Y)<250)end++;
   var run=sorted[start..end];sockets.Add(new NativeRoomSocket {X=(int)run.Average(c=>c.Offset.X),Z=(int)run.Average(c=>c.Offset.Z),
    Y=(int)run.Average(c=>c.Y),Facing=(run[0].Side+room.RotationQuarterTurns)&3,Width=run.Length*2000,Height=10000,Exterior=run[0].ZoneLink<0});start=end;
  }
 }
 foreach (var socket in sockets.Where(s => !s.Exterior))
 {
  float x=(Mm(room.X)+socket.X)*.001f, z=(Mm(room.Z)+socket.Z)*.001f;
  if (NativeDoorwayAnalyzer.TryResolve(meshes,x,z,socket.Facing,out float floor,out float clearance))
  {
   int old=socket.Y; socket.Y=Mm(floor-room.Y); socket.Clearance=Mm(clearance);
   if(Math.Abs(socket.Y-old)>50)Console.WriteLine($"Door floor corrected: room={index} socket={sockets.IndexOf(socket)} old={old} new={socket.Y} clearance={socket.Clearance}");
  }
  else { socket.Blocked=true; Console.WriteLine($"Door rejected: room={index} socket={sockets.IndexOf(socket)} no supported clear passage."); }
 }
 if(sockets.Count==0)continue;
 float minX=-room.CenterX*2-1,minZ=-room.CenterZ*2-1,maxX=(width-room.CenterX)*2-1,maxZ=(depth-room.CenterZ)*2-1;
 var corners=new[]{new WorldVector3(Mm(minX),0,Mm(minZ)),new WorldVector3(Mm(maxX),0,Mm(minZ)),new WorldVector3(Mm(minX),0,Mm(maxZ)),new WorldVector3(Mm(maxX),0,Mm(maxZ))}
  .Select(c=>NativeRoomDungeon.Rotate(c,room.RotationQuarterTurns)).ToArray();
 templates.Add(new NativeRoomTemplate {SourceIndex=index,Name=room.Name,Pool=sourceDefinition.PoolFor(index),OriginX=Mm(room.X),OriginY=Mm(room.Y),OriginZ=Mm(room.Z),
  MinX=corners.Min(c=>c.X),MinZ=corners.Min(c=>c.Z),MaxX=corners.Max(c=>c.X),MaxZ=corners.Max(c=>c.Z),
  MinY=Mm(meshes.Min(m=>Enumerable.Range(0,m.VertexCount).Min(v=>m.Vertices[v*3+1]))-room.Y),
  MaxY=Mm(meshes.Max(m=>Enumerable.Range(0,m.VertexCount).Max(v=>m.Vertices[v*3+1]))-room.Y),
  SpawnX=index==entranceIndex?sourceDefinition.EntranceSpawnX:0,SpawnY=80,SpawnZ=index==entranceIndex?sourceDefinition.EntranceSpawnZ:0,Sockets=sockets.ToArray()});
}
// Keep the authored entrance X/Z, but derive Y from a floor with room for the capsule.
// The same point may also have a buried terrain plane and a roof above it.
var entrance = templates.Single(t => t.SourceIndex == entranceIndex);
float spawnFloor = FindSpawnFloor(geometry[entrance.SourceIndex],
 (entrance.OriginX + entrance.SpawnX) * .001f, (entrance.OriginZ + entrance.SpawnZ) * .001f);
entrance.SpawnY = Mm(spawnFloor - entrance.OriginY * .001f) + 80;
Console.WriteLine($"Validated entrance floor: y={spawnFloor:F5}, spawnY={(entrance.OriginY + entrance.SpawnY) * .001f:F5}, capsuleRadius=0.5, height=1.8.");
foreach (var template in templates) NativeTraversalBuilder.Build(template, geometry[template.SourceIndex], template.SourceIndex == entrance.SourceIndex);
var catalog=new NativeRoomCatalog{Version=4,CollisionGeometryVersion=1,SourcePlayfield=sourcePlayfield,EntranceSourceRoom=entranceIndex,Rooms=templates.ToArray(),SourceSha256=NativeRoomDungeon.Sha(record),SurfaceSha256=NativeRoomDungeon.Sha(File.ReadAllBytes(args[1]))};
if (args.Length >= 4 && args[3] != "-")
{
 var overrides = JsonSerializer.Deserialize<NativeRoomOverrides>(File.ReadAllText(args[3]), new JsonSerializerOptions { IncludeFields = true }) ?? throw new InvalidDataException("Empty authoring overrides");
 overrides.Apply(catalog);
}
NativeRoomDungeon.Validate(catalog);
var pools = catalog.Rooms.Select(r => r.Pool).Distinct().Prepend("mixed").ToArray();
foreach(string pool in pools)
{
 int probeCount = Math.Max(12, catalog.PinnedRecipe?.Rooms.Length ?? 0);
 var hashes=new HashSet<string>();
 for(ulong seed=1;seed<=100;seed++)
 {
  var recipe=NativeRoomDungeon.Generate(catalog,seed,probeCount,pool);
  AuditJoins(recipe);
  foreach(var join in recipe.Joins){var a=recipe.Rooms[join.From];var b=recipe.Rooms[join.To];var sa=recipe.Template(a).Sockets[join.FromSocket];var sb=recipe.Template(b).Sockets[join.ToSocket];
   if(!NativeRoomDungeon.Transform(a,new(sa.X,sa.Y,sa.Z)).Equals(NativeRoomDungeon.Transform(b,new(sb.X,sb.Y,sb.Z))))throw new Exception("Portal mismatch");
   if(((sa.Facing+a.QuarterTurns+2)&3)!=((sb.Facing+b.QuarterTurns)&3))throw new Exception("Portal facing mismatch");
  }
  hashes.Add(string.Join(";",recipe.Rooms.Select(r=>$"{r.SourceIndex}:{r.X},{r.Y},{r.Z}:{r.QuarterTurns}")));
 }
 if(hashes.Count<2 && (catalog.PinnedRecipe?.Rooms.Length ?? 0) < probeCount)throw new Exception("Seeds did not vary the layout");
 Console.WriteLine($"Checked pool={pool}, seeds=100, rooms={probeCount}, distinctLayouts={hashes.Count}");
}
foreach (string pool in pools)
foreach (int count in new[] { 4, 12, 24 })
foreach (ulong seed in new ulong[] { 1, 2, 90602, 90603 })
{
 if (count < (catalog.PinnedRecipe?.Rooms.Length ?? 0)) continue;
 Console.WriteLine($"Checking pool={pool} count={count} seed={seed}");
 var recipe = NativeRoomDungeon.Generate(catalog, seed, count, pool);
 AuditJoins(recipe);
 var repeat = NativeRoomDungeon.Generate(catalog, seed, count, pool);
 var snapshot = JsonSerializer.Serialize(NativeRoomRecipeData.Capture(recipe), new JsonSerializerOptions { IncludeFields = true });
 var restored = JsonSerializer.Deserialize<NativeRoomRecipeData>(snapshot, new JsonSerializerOptions { IncludeFields = true })!.Restore(catalog);
 var manifest = new WorldGen.Contracts.GenerationManifest(NativeRoomDungeon.GeneratorId, NativeRoomDungeon.GeneratorVersion, 1, seed, "probe", "catalog-probe");
 var layout = NativeRoomDungeon.Layout(manifest, recipe);
 if (layout.Rooms.Count != count || layout.Connections.Count != count - 1 || NativeRoomDungeon.Hash(manifest, recipe) != NativeRoomDungeon.Hash(manifest, repeat) || NativeRoomDungeon.Hash(manifest, recipe) != NativeRoomDungeon.Hash(manifest, restored))
  throw new Exception("Native layout/determinism mismatch");
}
Console.WriteLine("Checked 4/12/24-room layouts and repeated-recipe hashes for all pools, including seeds 90602/90603.");
int missingSupport = 0;
foreach (var template in templates)
foreach (var socket in template.Sockets.Where(s => !s.Exterior && !s.Blocked))
{
 var normal = NativeRoomDungeon.Rotate(new WorldVector3(0, 0, 1000), socket.Facing);
 float x = (template.OriginX + socket.X) * .001f - normal.X * .00035f;
 float z = (template.OriginZ + socket.Z) * .001f - normal.Z * .00035f;
 float height = (template.OriginY + socket.Y) * .001f;
 if (!FindHeight(geometry[template.SourceIndex], x, z, height).HasValue)
 { missingSupport++; Console.WriteLine($"Missing portal floor: room={template.SourceIndex}, facing={socket.Facing}, x={x}, z={z}, y={height}"); }
}
Console.WriteLine($"Portal inward floor probes: unsupported={missingSupport}.");
Console.WriteLine("Native collision normalized only for new room recipes; original copy unchanged.");
float? FindHeight(List<AOIndoorSurfaceMesh> meshes,float x,float z,float expected)
{
 float? best=null;float error=float.MaxValue;
 foreach(var mesh in meshes)for(int i=0;i<mesh.Triangles.Length;i+=3)
 {
  int a=mesh.Triangles[i]*3,b=mesh.Triangles[i+1]*3,c=mesh.Triangles[i+2]*3;var p=mesh.Vertices;
  float ux=p[b]-p[a],uz=p[b+2]-p[a+2],vx=p[c]-p[a],vz=p[c+2]-p[a+2],det=ux*vz-uz*vx;
  if(Math.Abs(det)<1e-6)continue;float s=((x-p[a])*vz-(z-p[a+2])*vx)/det,t=(ux*(z-p[a+2])-uz*(x-p[a]))/det;
  if(s<-.001||t<-.001||s+t>1.001)continue;float y=p[a+1]+s*(p[b+1]-p[a+1])+t*(p[c+1]-p[a+1]);
  float delta=Math.Abs(y-expected);if(delta<error&&delta<.6f){best=y;error=delta;}
 }
 return best;
}

float FindSpawnFloor(List<AOIndoorSurfaceMesh> meshes, float x, float z)
{
 var offsets = new[] { (0f, 0f), (-.5f, 0f), (.5f, 0f), (0f, -.5f), (0f, .5f),
  (-.35f, -.35f), (-.35f, .35f), (.35f, -.35f), (.35f, .35f) };
 var probes = offsets.Select(offset => SurfaceHits(meshes, x + offset.Item1, z + offset.Item2)).ToArray();
 foreach (float floor in probes[0].Where(hit => hit.NormalY >= .65f).Select(hit => hit.Height).Distinct().OrderByDescending(y => y))
 {
  bool supported = true;
  foreach (var probe in probes)
  {
   // Every point under the capsule must have the same landing and an interior ceiling.
   bool floorSupport = probe.Any(hit => hit.NormalY >= .65f && Math.Abs(hit.Height - floor) < .15f);
   var roofs = probe.Where(hit => hit.NormalY <= -.65f && hit.Height > floor + .05f).Select(hit => hit.Height).ToArray();
   if (!floorSupport || roofs.Length == 0 || roofs.Min() - floor < 1.8f + .1f) { supported = false; break; }
  }
  if (supported) return floor;
 }
 throw new InvalidDataException("The authored entrance has no supported interior spawn with capsule clearance.");
}
List<(float Height, float NormalY)> SurfaceHits(List<AOIndoorSurfaceMesh> meshes, float x, float z)
{
 var hits = new List<(float, float)>();
 foreach (var mesh in meshes) for (int i = 0; i < mesh.Triangles.Length; i += 3)
 {
  var p = mesh.Vertices; int a = mesh.Triangles[i] * 3, b = mesh.Triangles[i+1] * 3, c = mesh.Triangles[i+2] * 3;
  float ux=p[b]-p[a], uy=p[b+1]-p[a+1], uz=p[b+2]-p[a+2];
  float vx=p[c]-p[a], vy=p[c+1]-p[a+1], vz=p[c+2]-p[a+2];
  float nx=uy*vz-uz*vy, ny=uz*vx-ux*vz, nz=ux*vy-uy*vx;
  float length=(float)Math.Sqrt(nx*nx+ny*ny+nz*nz), det=ux*vz-uz*vx;
  if (length < 1e-6f || Math.Abs(det) < 1e-6f) continue;
  float u=((x-p[a])*vz-(z-p[a+2])*vx)/det, v=(ux*(z-p[a+2])-uz*(x-p[a]))/det;
  if(u<-.001f || v<-.001f || u+v>1.001f) continue;
  hits.Add((p[a+1]+u*uy+v*vy, ny/length));
 }
 return hits;
}

void AuditJoins(NativeRoomRecipe recipe)
{
 foreach (var join in recipe.Joins)
 {
  var a=recipe.Rooms[join.From];var b=recipe.Rooms[join.To];
  var ta=recipe.Template(a);var tb=recipe.Template(b);
  var sa=ta.Sockets[join.FromSocket];var sb=tb.Sockets[join.ToSocket];
  if(sa.Blocked || sb.Blocked || sa.Clearance<1900 || sb.Clearance<1900)throw new Exception("Recipe used an unapproved doorway.");
  float WorldFloor(NativeRoomTemplate template,NativeRoomPlacement placement,NativeRoomSocket socket,float depth)
  {
   var normal=NativeRoomDungeon.Rotate(new WorldVector3(0,0,1000),socket.Facing);
   float x=(template.OriginX+socket.X)*.001f-normal.X*.001f*depth;
   float z=(template.OriginZ+socket.Z)*.001f-normal.Z*.001f*depth;
   float expected=(template.OriginY+socket.Y)*.001f;
   float? source=NativeDoorwayAnalyzer.FloorNear(geometry[template.SourceIndex],x,z,expected);
   if(!source.HasValue)throw new Exception($"Doorway floor missing: source={template.SourceIndex}, depth={depth}");
   return placement.Y*.001f+source.Value-template.OriginY*.001f;
  }
  float left=WorldFloor(ta,a,sa,.05f),right=WorldFloor(tb,b,sb,.05f);
  if(Math.Abs(left-right)>.025f)throw new Exception($"Physical doorway step: {a.Index}/{ta.Name} -> {b.Index}/{tb.Name}, delta={left-right}");
 }
}
// Source-specific regression checks are authored data; every pair is checked in all rotations.
foreach (var check in sourceDefinition.DoorwayChecks)
{
 var first=templates.Single(t=>t.SourceIndex==check.FirstRoom);var second=templates.Single(t=>t.SourceIndex==check.SecondRoom);
 if(check.FirstSocket>=first.Sockets.Length || check.SecondSocket>=second.Sockets.Length)
  throw new InvalidDataException("Source doorway check references an unknown socket.");
 if(Math.Abs(first.Sockets[check.FirstSocket].Y-check.ExpectedFirstY)>check.FloorToleranceMm || first.Sockets[check.FirstSocket].Blocked)
  throw new Exception("Authored doorway floor expectation was not recovered.");
 for(int turn=0;turn<4;turn++)
 {
  var a=new NativeRoomPlacement{Index=0,SourceIndex=check.FirstRoom,X=40000,Y=107440,Z=80000,QuarterTurns=turn};
  var sa=first.Sockets[check.FirstSocket];var sb=second.Sockets[check.SecondSocket];
  int turns=(sa.Facing+turn+2-sb.Facing)&3;
  var point=NativeRoomDungeon.Transform(a,new(sa.X,sa.Y,sa.Z));var offset=NativeRoomDungeon.Rotate(new(sb.X,sb.Y,sb.Z),turns);
  var b=new NativeRoomPlacement{Index=1,SourceIndex=check.SecondRoom,X=point.X-offset.X,Y=point.Y-offset.Y,Z=point.Z-offset.Z,QuarterTurns=turns};
  var probe=new NativeRoomRecipe{Catalog=catalog};probe.Rooms.Add(a);probe.Rooms.Add(b);
  probe.Joins.Add(new NativeRoomJoin{From=0,FromSocket=check.FirstSocket,To=1,ToSocket=check.SecondSocket});AuditJoins(probe);
  Console.WriteLine($"Authored doorway floor match: source={check.FirstRoom}/{check.SecondRoom}, turn={turn}, jointY={point.Y*.001f:F3}.");
 }
}
Console.WriteLine("All generated joins have matching captured floor heights at the threshold and approved passage clearance.");

// Publish only after all source and recipe validation succeeds.
Directory.CreateDirectory(args[2]);
string json=JsonSerializer.Serialize(catalog,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true});
File.WriteAllText(Path.Combine(args[2],$"pf{sourcePlayfield}.rooms.json.tmp"),json);
using(var output=new BinaryWriter(File.Create(Path.Combine(args[2],$"pf{sourcePlayfield}.rooms.collision.tmp"))))
{
 output.Write("AONR"u8);output.Write(1);output.Write(sourcePlayfield);output.Write(geometry.Count);
 foreach(var pair in geometry){output.Write(pair.Key);output.Write(pair.Value.Sum(m=>m.TriangleCount));
  foreach(var mesh in pair.Value)foreach(int vertex in mesh.Triangles)for(int k=0;k<3;k++)output.Write(mesh.Vertices[vertex*3+k]);
 }
}
Console.WriteLine($"Catalog: templates={templates.Count}, sockets={templates.Sum(t=>t.Sockets.Length)}, sha={NativeRoomDungeon.Sha(System.Text.Encoding.UTF8.GetBytes(json))}");

File.Move(Path.Combine(args[2],$"pf{sourcePlayfield}.rooms.collision.tmp"),Path.Combine(args[2],$"pf{sourcePlayfield}.rooms.collision"),true);
File.Move(Path.Combine(args[2],$"pf{sourcePlayfield}.rooms.json.tmp"),Path.Combine(args[2],$"pf{sourcePlayfield}.rooms.json"),true);
Console.WriteLine("Published validated native room metadata and local collision package.");
