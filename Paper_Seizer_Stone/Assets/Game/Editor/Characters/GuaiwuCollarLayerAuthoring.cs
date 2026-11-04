using System;
using System.Linq;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
namespace PaperSeizerStone.Editor
{
    // Local saved-mesh authoring. No animation sampling, menu or runtime deformation.
    public static class GuaiwuCollarLayerAuthoring
    {
        public sealed class Result { public Mesh Mesh; public int nearbyLayerPairs, coordinatedGroups, smoothedGroups, movedGroups; public float maximumDisplacement; }
        sealed class Group { public Vector3 P, Normal; public List<int> Vertices=new List<int>(); public HashSet<int> Neighbors=new HashSet<int>(); public Dictionary<int,float> Weights=new Dictionary<int,float>(); }
        sealed class Pair { public int A,B; }
        public static Result Build(SkinnedMeshRenderer renderer, Transform model)
        {
            Mesh source=renderer.sharedMesh;
            var vertices=source.vertices; var groups=new List<Group>(); var lookup=new Dictionary<Vector3Int,int>();var groupOf=new int[vertices.Length];
            Vector3 ToModel(Vector3 p)=>model.InverseTransformPoint(renderer.transform.TransformPoint(p));
            Vector3 ToMesh(Vector3 p)=>renderer.transform.InverseTransformPoint(model.TransformPoint(p));
            for(int i=0;i<vertices.Length;i++)
            {
                Vector3 p=ToModel(vertices[i]);var k=new Vector3Int(Mathf.RoundToInt(p.x*1000000),Mathf.RoundToInt(p.y*1000000),Mathf.RoundToInt(p.z*1000000));
                if(!lookup.TryGetValue(k,out int g)){g=groups.Count;lookup[k]=g;groups.Add(new Group{P=p});}groups[g].Vertices.Add(i);groupOf[i]=g;
            }
            int[] triangles=source.triangles;
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=groupOf[triangles[i]],b=groupOf[triangles[i+1]],c=groupOf[triangles[i+2]];Vector3 n=Vector3.Cross(groups[b].P-groups[a].P,groups[c].P-groups[a].P);
                foreach(int g in new[]{a,b,c}){groups[g].Normal+=n;foreach(int h in new[]{a,b,c})if(h!=g)groups[g].Neighbors.Add(h);}
            }
            foreach(var g in groups)g.Normal.Normalize();
            using(var counts=source.GetBonesPerVertex())using(var weights=source.GetAllBoneWeights())
            {int cursor=0;for(int i=0;i<vertices.Length;i++)for(int j=0;j<counts[i];j++){var w=weights[cursor++];var g=groups[groupOf[i]];g.Weights.TryGetValue(w.boneIndex,out float p);g.Weights[w.boneIndex]=p+w.weight/g.Vertices.Count;}}
            bool Local(Group g)=>g.P.y>.69f&&g.P.y<.94f&&Mathf.Abs(g.P.x)<.27f;
            var candidates=Enumerable.Range(0,groups.Count).Where(i=>Local(groups[i])).ToArray();var pairs=new List<Pair>();
            // A common local 3D weight field also regularizes the space between
            // the sampled layer pairs. Never assign a joint merely from XY.
            var supportNames=new HashSet<string>{"Chest","UpperChest","Neck","Head","Left_Shoulder","Right_Shoulder"};
            bool Supported(Group g)=>g.Weights.Where(w=>supportNames.Contains(renderer.bones[w.Key].name)).Sum(w=>w.Value)>.5f;
            int[] supported=candidates.Where(i=>Supported(groups[i])).ToArray();
            var regularized=new Dictionary<int,Dictionary<int,float>>();
            foreach(int g in supported)
            {
                var mean=new Dictionary<int,float>();float total=0;
                foreach(int h in supported)
                {
                    float d=(groups[g].P-groups[h].P).sqrMagnitude;if(d>.000196f)continue;
                    float kernel=Mathf.Exp(-d/.000072f);total+=kernel;
                    foreach(var w in groups[h].Weights){mean.TryGetValue(w.Key,out float p);mean[w.Key]=p+kernel*w.Value;}
                }
                var merged=new Dictionary<int,float>();
                foreach(int bone in mean.Keys.Union(groups[g].Weights.Keys))
                {mean.TryGetValue(bone,out float average);groups[g].Weights.TryGetValue(bone,out float original);merged[bone]=.2f*original+.8f*average/total;}
                regularized[g]=merged;
            }
            foreach(var entry in regularized)groups[entry.Key].Weights=entry.Value;
            foreach(int a in candidates)
            {
                // Exclude nearby vertices on the same surface/crease: retain four rings.
                var near=new HashSet<int>{a};var ring=new HashSet<int>{a};for(int level=0;level<4;level++){var next=new HashSet<int>();foreach(int g in ring)foreach(int h in groups[g].Neighbors)if(near.Add(h))next.Add(h);ring=next;}
                foreach(int b in candidates)
                {
                    if(b>=a||near.Contains(b))continue;Vector3 delta=groups[a].P-groups[b].P;float d=delta.magnitude;if(d<.00001f||d>=.004f)continue;
                    Vector3 axis=delta/d;float alignment=Mathf.Abs(Vector3.Dot(groups[a].Normal,groups[b].Normal));
                    if(alignment<.65f||Mathf.Abs(Vector3.Dot(axis,groups[a].Normal))<.35f||Mathf.Abs(Vector3.Dot(axis,groups[b].Normal))<.35f)continue;
                    pairs.Add(new Pair{A=a,B=b});
                }
            }
            if(pairs.Count==0)throw new InvalidOperationException("当前局部没有符合间距条件的非相邻衣层，未改网格。");
            var parent=Enumerable.Range(0,groups.Count).ToArray();var lo=groups.Select(g=>g.P).ToArray();var hi=(Vector3[])lo.Clone();
            int Root(int g){while(parent[g]!=g){parent[g]=parent[parent[g]];g=parent[g];}return g;}
            foreach(var pair in pairs)
            {
                int a=Root(pair.A),b=Root(pair.B);if(a==b)continue;var min=Vector3.Min(lo[a],lo[b]);var max=Vector3.Max(hi[a],hi[b]);if((max-min).magnitude>.02f)continue;
                parent[b]=a;lo[a]=min;hi[a]=max;
            }
            var components=Enumerable.Range(0,groups.Count).GroupBy(Root).Where(g=>g.Count()>1).ToArray();int coordinated=0;
            foreach(var component in components)
            {
                var mean=new Dictionary<int,float>();foreach(int g in component)foreach(var w in groups[g].Weights){mean.TryGetValue(w.Key,out float v);mean[w.Key]=v+w.Value/component.Count();}
                foreach(int g in component){groups[g].Weights=new Dictionary<int,float>(mean);coordinated++;}
            }
            // Separate only identified local sheets; cap total displacement to 2 mm
            // in model bind space, so UVs, tattered outlines and topology stay intact.
            var positions=groups.Select(g=>g.P).ToArray();
            for(int iteration=0;iteration<8;iteration++)
            {
                var push=new Vector3[groups.Count];var hits=new int[groups.Count];
                foreach(var pair in pairs){Vector3 delta=positions[pair.A]-positions[pair.B];float d=delta.magnitude;if(d>=.004f||d<.000001f)continue;Vector3 correction=delta/d*((.004f-d)*.5f);push[pair.A]+=correction;push[pair.B]-=correction;hits[pair.A]++;hits[pair.B]++;}
                for(int g=0;g<groups.Count;g++)if(hits[g]>0)positions[g]=groups[g].P+Vector3.ClampMagnitude(positions[g]-groups[g].P+push[g]/hits[g],.002f);
            }
            Mesh mesh=UnityEngine.Object.Instantiate(source);mesh.name="Guaiwu_Cloak_CollarLayers";int moved=0;float maxMove=0;
            for(int g=0;g<groups.Count;g++){float distance=(positions[g]-groups[g].P).magnitude;if(distance>.000001f)moved++;maxMove=Mathf.Max(maxMove,distance);foreach(int i in groups[g].Vertices)vertices[i]=ToMesh(positions[g]);}
            mesh.vertices=vertices;
            var newCounts=new NativeArray<byte>(vertices.Length,Allocator.Temp);var all=new List<BoneWeight1>();
            try{for(int i=0;i<vertices.Length;i++){var sorted=groups[groupOf[i]].Weights.Where(w=>w.Value>.0000001f).OrderByDescending(w=>w.Value).ToArray();float sum=sorted.Sum(w=>w.Value);newCounts[i]=(byte)sorted.Length;foreach(var w in sorted)all.Add(new BoneWeight1{boneIndex=w.Key,weight=w.Value/sum});}using(var native=new NativeArray<BoneWeight1>(all.ToArray(),Allocator.Temp))mesh.SetBoneWeights(newCounts,native);}finally{newCounts.Dispose();}
            mesh.RecalculateBounds();return new Result{Mesh=mesh,nearbyLayerPairs=pairs.Count,coordinatedGroups=coordinated,smoothedGroups=supported.Length,movedGroups=moved,maximumDisplacement=maxMove};
        }
    }
}
