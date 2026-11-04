using System;using System.Linq;using System.Collections.Generic;using Unity.Collections;using UnityEngine;
namespace PaperSeizerStone.Editor {
 public static class GuaiwuShoulderCutAuthoring {
  public sealed class Result {public Mesh Mesh;public ClothSkinningCoefficient[] Coefficients;public int affectedTriangles;public Vector3 shoulderBindPoint;}
  sealed class Vertex {public Vector3 P,N;public Vector4 T;public Color Color;public Vector4[] Uv;public Dictionary<int,float> W;public ClothSkinningCoefficient C;public float Side;}
  public static Result Build(SkinnedMeshRenderer r,Transform root,GuaiwuSleeveClothAuthoring.Snapshot before) {
   Mesh src=r.sharedMesh;int upper=Array.FindIndex(r.bones,b=>b.name=="Left_UpperArm");if(upper<0)throw new Exception("缺少画面右侧手臂骨骼。");
   Vector3 shoulder=root.InverseTransformPoint(r.transform.TransformPoint(src.bindposes[upper].inverse.MultiplyPoint3x4(Vector3.zero)));
   var p=src.vertices;var n=src.normals;var t=src.tangents;var colors=src.colors;var uv=new List<Vector4>[8];for(int c=0;c<8;c++){uv[c]=new List<Vector4>();src.GetUVs(c,uv[c]);}
   var all=new List<Vertex>();using(var counts=src.GetBonesPerVertex())using(var weights=src.GetAllBoneWeights()){int cursor=0;for(int i=0;i<p.Length;i++){Vector3 bind=root.InverseTransformPoint(r.transform.TransformPoint(p[i]));
    // Outboard of the shoulder joint, limited to the upper-arm/shoulder band.
    // The torso below the armpit and the hood above the shoulder are retained.
    float side=Mathf.Min(shoulder.x+.008f-bind.x,Mathf.Min(bind.y-(shoulder.y-.18f),shoulder.y+.075f-bind.y));
    var w=new Dictionary<int,float>();for(int j=0;j<counts[i];j++){var b=weights[cursor++];w[b.boneIndex]=b.weight;}
    all.Add(new Vertex{P=p[i],N=n.Length==p.Length?n[i]:Vector3.up,T=t.Length==p.Length?t[i]:new Vector4(1,0,0,1),Color=colors.Length==p.Length?colors[i]:Color.white,Uv=uv.Select(u=>u.Count==p.Length?u[i]:Vector4.zero).ToArray(),W=w,C=before.Coefficients[before.ParticleOfVertex[i]],Side=side});}}
   var intersections=new Dictionary<long,int>();
   int Intersect(int a,int b){long key=((long)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);if(intersections.TryGetValue(key,out int old))return old;var x=all[a];var y=all[b];float f=x.Side/(x.Side-y.Side);var w=new Dictionary<int,float>();foreach(int bone in x.W.Keys.Union(y.W.Keys)){x.W.TryGetValue(bone,out float wx);y.W.TryGetValue(bone,out float wy);w[bone]=Mathf.Lerp(wx,wy,f);}Vector4 tangent=Vector4.Lerp(x.T,y.T,f);Vector3 tn=new Vector3(tangent.x,tangent.y,tangent.z).normalized;tangent=new Vector4(tn.x,tn.y,tn.z,f<.5f?x.T.w:y.T.w);int index=all.Count;all.Add(new Vertex{P=Vector3.Lerp(x.P,y.P,f),N=Vector3.Slerp(x.N,y.N,f).normalized,T=tangent,Color=Color.Lerp(x.Color,y.Color,f),Uv=x.Uv.Select((u,c)=>Vector4.Lerp(u,y.Uv[c],f)).ToArray(),W=w,C=new ClothSkinningCoefficient{maxDistance=Mathf.Lerp(x.C.maxDistance,y.C.maxDistance,f),collisionSphereDistance=Mathf.Lerp(x.C.collisionSphereDistance,y.C.collisionSphereDistance,f)},Side=0});intersections[key]=index;return index;}
   var submeshes=new List<int[]>();var used=new HashSet<int>();int affected=0;
   for(int s=0;s<src.subMeshCount;s++){var output=new List<int>();int[] tris=src.GetTriangles(s);for(int i=0;i<tris.Length;i+=3){int[] tri={tris[i],tris[i+1],tris[i+2]};if(tri.All(a=>all[a].Side<=0)){output.AddRange(tri);foreach(int a in tri)used.Add(a);continue;}affected++;var polygon=new List<int>();for(int j=0;j<3;j++){int a=tri[j],b=tri[(j+1)%3];bool ia=all[a].Side<=0,ib=all[b].Side<=0;if(ia)polygon.Add(a);if(ia!=ib)polygon.Add(Intersect(a,b));}for(int j=1;j+1<polygon.Count;j++){int a=polygon[0],b=polygon[j],c=polygon[j+1];if(Vector3.Cross(all[b].P-all[a].P,all[c].P-all[a].P).sqrMagnitude<1e-16f)continue;output.Add(a);output.Add(b);output.Add(c);used.Add(a);used.Add(b);used.Add(c);}}submeshes.Add(output.ToArray());}
   if(affected<20||affected>3000)throw new Exception("肩部裁剪范围异常，未生成资源："+affected);
   int[] ids=used.OrderBy(i=>i).ToArray();var remap=new int[all.Count];for(int i=0;i<ids.Length;i++)remap[ids[i]]=i;
   var mesh=new Mesh{name="Guaiwu_Cloak_LeftShoulderCut",indexFormat=src.indexFormat};mesh.vertices=ids.Select(i=>all[i].P).ToArray();mesh.normals=ids.Select(i=>all[i].N).ToArray();if(t.Length==p.Length)mesh.tangents=ids.Select(i=>all[i].T).ToArray();if(colors.Length==p.Length)mesh.colors=ids.Select(i=>all[i].Color).ToArray();for(int c=0;c<8;c++)if(uv[c].Count==p.Length)mesh.SetUVs(c,ids.Select(i=>all[i].Uv[c]).ToList());mesh.bindposes=src.bindposes;
   var newCounts=new NativeArray<byte>(ids.Length,Allocator.Temp);try{var weights=new List<BoneWeight1>();for(int i=0;i<ids.Length;i++){var w=all[ids[i]].W.Where(x=>x.Value>1e-8f).OrderByDescending(x=>x.Value).ToArray();float sum=w.Sum(x=>x.Value);newCounts[i]=(byte)w.Length;foreach(var b in w)weights.Add(new BoneWeight1{boneIndex=b.Key,weight=b.Value/sum});}using(var ws=new NativeArray<BoneWeight1>(weights.ToArray(),Allocator.Temp))mesh.SetBoneWeights(newCounts,ws);}finally{newCounts.Dispose();}
   mesh.subMeshCount=submeshes.Count;for(int s=0;s<submeshes.Count;s++)mesh.SetTriangles(submeshes[s].Select(i=>remap[i]).ToArray(),s);mesh.RecalculateBounds();return new Result{Mesh=mesh,Coefficients=ids.Select(i=>all[i].C).ToArray(),affectedTriangles=affected,shoulderBindPoint=shoulder};
  }
 }
}
