using System.Text.Json;
using WorldGen.Dungeons;
using WorldGen.Spatial;
using AO.Unity.World;
using UnityEngine;

void Check(bool value,string message) { if(!value) throw new Exception(message); }
var n = new[] { Vector3.left };
Check(MovementWallSliding.Project(new Vector3(1,0,.3f),n,Vector3.right)==new Vector3(0,0,.3f),"Right jamb discarded forward tangent.");
Check(MovementWallSliding.Project(new Vector3(1,0,-.3f),n,Vector3.right)==new Vector3(0,0,-.3f),"Left tangent changed side.");
Check(MovementWallSliding.Project(Vector3.right,n,Vector3.right)==Vector3.zero,"Head-on input entered the wall.");
Check(MovementWallSliding.Project(new Vector3(-1,0,.2f),n,Vector3.right)==new Vector3(-1,0,.2f),"Retreat was suppressed.");
Check(MovementWallSliding.Project(new Vector3(1,0,1),new[]{Vector3.left,Vector3.back},Vector3.right)==Vector3.zero,"Closed corner retained inward input.");
Check(MovementWallSliding.Project(new Vector3(1,0,.3f),Array.Empty<Vector3>(),Vector3.right)==new Vector3(0,0,.3f),"Server contact fallback discarded the tangent.");

if(args.Length!=2)throw new ArgumentException("Supply corrected PF127/PF1931 preparation directories.");
foreach((int pf,string directory) in new[]{(127,args[0]),(1931,args[1])})
{
 var cat=JsonSerializer.Deserialize<NativeRoomCatalog>(File.ReadAllText(Path.Combine(directory,$"pf{pf}.rooms.json")),new JsonSerializerOptions{IncludeFields=true})!;
 Check(cat.CollisionGeometryVersion==1,"Corrected catalog lost its collision policy.");
 var source=NativeRoomCatalogStore.ReadCollision(Path.Combine(directory,$"pf{pf}.rooms.collision"),cat);
 foreach(var room in cat.Rooms)for(int i=0;i<source[room.SourceIndex].Length;i+=3)
 {
  var points=source[room.SourceIndex];float x=points[i]-room.OriginX*.001f,z=points[i+2]-room.OriginZ*.001f;
  Check(x>=room.MinX*.001f-.002f&&x<=room.MaxX*.001f+.002f&&z>=room.MinZ*.001f-.002f&&z<=room.MaxZ*.001f+.002f,"Source collision escaped its room footprint.");
 }
 if(pf!=1931)continue;
 var recipe=NativeRoomDungeon.Generate(cat,90602,24,"mixed");
 var join=recipe.Joins.Single(j=>j.From==14&&j.To==15);
 Check(recipe.Template(recipe.Rooms[14]).Name=="Hallway"&&recipe.Template(recipe.Rooms[15]).Name=="Connector_shallow","Reported regression no longer identifies the expected pair.");
 var from=recipe.Rooms[join.From];var socket=recipe.Template(from).Sockets[join.FromSocket];
 var raw=NativeRoomDungeon.Transform(from,new WorldVector3(socket.X,socket.Y,socket.Z));var center=new Vector3(raw.X,raw.Y,raw.Z)*.001f;
 int facing=(socket.Facing+from.QuarterTurns)&3;
 var normal=facing switch{0=>Vector3.forward,1=>Vector3.right,2=>Vector3.back,_=>Vector3.left};var side=new Vector3(normal.z,0,-normal.x);
 var triangles=new List<(Vector3 A,Vector3 B,Vector3 C)>();
 foreach(var placement in new[]{recipe.Rooms[14],recipe.Rooms[15]})
 {
  var mesh=source[placement.SourceIndex];for(int i=0;i<mesh.Length;i+=9)
  {
   Vector3 Point(int k){NativeRoomTransform.SourcePoint(recipe.Template(placement),placement,mesh[k],mesh[k+1],mesh[k+2],out float x,out float y,out float z);return new(x,y,z);}
   triangles.Add((Point(i),Point(i+3),Point(i+6)));
  }
 }
 foreach(float lateral in new[]{-.4f,0,.4f})
 {
  // The foreign ramp occupied the approach while the room's real floor stayed level.
  foreach(float depth in new[]{-1.1f,-.75f,-.35f,-.05f,.05f,.35f,.75f,1.1f})
  {
   var p=center+normal*depth+side*lateral;var top=p+Vector3.up;
   var heights=triangles.Where(t=>Vector3.Cross(t.B-t.A,t.C-t.A).y>0).Select(t=>Intersect(top,top-Vector3.up*2,t)).Where(h=>h.HasValue).Select(h=>1-h.Value*2).ToArray();
   Check(heights.Length>0&&Math.Abs(heights.Max())<.03f,"Hallway/shallow connector approach has an unsupported or raised floor.");
  }
  foreach(float height in new[]{.05f,.2f,.55f,1.05f,1.4f,1.75f})
  {
   var start=center-normal*1.1f+side*lateral+Vector3.up*height;
   Check(!triangles.Any(t=>Intersect(start,start+normal*2.2f,t).HasValue),"A collision face still crosses the open doorway.");
  }
 }
}
Console.WriteLine("PASS: glancing wall tangents, retreat, head-on/corner stops, source footprints and Hallway14/ConnectorShallow15 floor/body clearance.");
float? Intersect(Vector3 start,Vector3 end,(Vector3 A,Vector3 B,Vector3 C) t)
{
 var d=end-start;var e=t.B-t.A;var f=t.C-t.A;var p=Vector3.Cross(d,f);float det=Vector3.Dot(e,p);if(Math.Abs(det)<1e-7)return null;
 float inv=1/det;var a=start-t.A;float u=Vector3.Dot(a,p)*inv;if(u<-.00001||u>1.00001)return null;
 var q=Vector3.Cross(a,e);float v=Vector3.Dot(d,q)*inv;if(v<-.00001||u+v>1.00001)return null;
 float hit=Vector3.Dot(f,q)*inv;return hit>=0&&hit<=1?hit:null;
}
