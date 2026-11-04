using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using PaperSeizerStone.Presentation;

namespace PaperSeizerStone.Editor
{
    // Explicit asset authoring. No menu and no changes to the open scene.
    public static class GuaiwuCombatAuthoring
    {
        const string Folder = "Assets/Game/art/animation/Guaiwu/";
        const string Actor = "Assets/Game/art/act/Guaiwu/Prefabs/Guaiwu_Cloth.prefab";
        const string Model = "Assets/Game/art/act/Guaiwu/Models/Guaiwu_Rigged.fbx";
        const string Props = "Assets/Game/Prefabs/Props/GuaiwuShootingProps.prefab";
        const string Fire = Folder + "guaiwu_fire.fbx";
        const string Hit = Folder + "Sources/guaiwu_dead.fbx";
        const string Gun = Folder + "Sources/guaiwu_fire_gun.fbx";
        [Serializable] sealed class Record
        {
            public string characterSource = Fire, hitSource = Hit, gunSource = Gun;
            public string fireClip, hitClip, gunClip, actor = Actor, props = Props;
            public float characterSeconds, hitSeconds, gunSeconds, gunStartSeconds, fireMomentSeconds, headingCorrectionDegrees = 180;
            public float characterFrameRate, hitFrameRate, gunFrameRate, boneLengthRatio;
            public float gunMatchPositionRms, gunMatchAngleRmsDegrees, gunMatchScoreGap;
            public int characterSamples, hitSamples;
            public string timing, savedLocal;
            public bool playModeRun = false;
        }
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("请退出 Play 和 Animation Preview 后保存怪物动作。");
            string backup = "UserSettings/GuaiwuCombatImport/Backup";
            Directory.CreateDirectory(backup);
            foreach (string path in new[]{Actor, Props, Folder+"Clips/Guaiwu_Fire.anim", Folder+"Clips/Guaiwu_Hit.anim", Folder+"Clips/Guaiwu_Gun.anim"})
                if (File.Exists(path)) foreach(string p in new[]{path,path+".meta"})
                    if(File.Exists(p))File.Copy(p,Path.Combine(backup,Path.GetFileName(p)),true);
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject prop = null, actor = null;
            try
            {
                var fireSource = Take(Fire); var hitSource = Take(Hit); var gunSource = Take(Gun);
                // Establish timing before writing any destination asset. An unmoving
                // armature root carries no information about the beginning of a take.
                float gunStart = FindGunStart(fireSource, gunSource, out float matchPosition,
                    out float matchAngle, out float matchGap);
                var fire = PlayerAnimationSyncAuthoring.Bake(Fire, fireSource, Model, "Guaiwu_Rig");
                var hit = PlayerAnimationSyncAuthoring.Bake(Hit, hitSource, Model, "Guaiwu_Rig");
                CorrectHeading(fire, fireSource.length); CorrectHeading(hit, hitSource.length);
                prop = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Props/Revolver.prefab"), scene);
                PrefabUtility.UnpackPrefabInstance(prop, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                prop.name = "GuaiwuShootingProps";
                Identity(prop.transform);
                Animator gunAnimator = prop.GetComponentsInChildren<Animator>(true).Single();
                Identity(gunAnimator.transform); gunAnimator.runtimeAnimatorController = null;
                gunAnimator.applyRootMotion = false; gunAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Transform rig = Find(prop, "Revolver_Rig");
                var gunData = PlayerShootingAuthoring.BakeProp(scene, Gun, gunSource, "Revolver_Rig", rig, gunAnimator.transform, fire, true);
                var fireClip = PlayerShootingAuthoring.SaveClip(Folder+"Clips/Guaiwu_Fire.anim", fire.Bindings, fire.Curves, fireSource);
                var hitClip = PlayerShootingAuthoring.SaveClip(Folder+"Clips/Guaiwu_Hit.anim", hit.Bindings, hit.Curves, hitSource);
                var gunClip = PlayerShootingAuthoring.SaveClip(Folder+"Clips/Guaiwu_Gun.anim", gunData.Bindings, gunData.Curves, gunSource);
                gunClip.SampleAnimation(gunAnimator.gameObject, 0);
                Transform muzzle = Find(prop, "Socket_Muzzle"); GameObject flash = Find(prop, "MuzzleFlash").gameObject;
                Vector3 flashScale = flash.transform.localScale;
                flash.transform.SetParent(muzzle, false); Identity(flash.transform); flash.transform.localScale = flashScale; flash.SetActive(false);
                foreach(var collider in prop.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(collider);
                foreach(var body in prop.GetComponentsInChildren<Rigidbody>(true))UnityEngine.Object.DestroyImmediate(body);
                foreach(var node in prop.GetComponentsInChildren<Transform>(true))node.gameObject.layer = 0;
                foreach(var renderer in prop.GetComponentsInChildren<Renderer>(true))
                { renderer.shadowCastingMode = ShadowCastingMode.On; if(renderer is SkinnedMeshRenderer skin)skin.updateWhenOffscreen = true; }
                if(PrefabUtility.SaveAsPrefabAsset(prop, Props)==null)throw new Exception("怪物枪械预制体保存失败。");
                actor = PrefabUtility.LoadPrefabContents(Actor);
                var characterAnimator = actor.transform.Find("Guaiwu_Rigged").GetComponent<Animator>();
                var existing = characterAnimator.transform.Find("GuaiwuCombatProps");
                if(existing!=null)UnityEngine.Object.DestroyImmediate(existing.gameObject);
                var wrapper = new GameObject("GuaiwuCombatProps").transform;
                wrapper.SetParent(characterAnimator.transform, false); Identity(wrapper);
                var child = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Props), actor.scene);
                child.transform.SetParent(wrapper, false); Identity(child.transform);
                var sequence = actor.GetComponent<GuaiwuCombatSequence>();
                if(sequence==null)sequence=actor.AddComponent<GuaiwuCombatSequence>();
                sequence.Configure(characterAnimator, child.GetComponentsInChildren<Animator>(true).Single(), wrapper,
                    Find(child,"MuzzleFlash").gameObject, fireClip, hitClip, gunClip, gunStart);
                wrapper.gameObject.SetActive(false);
                if(PrefabUtility.SaveAsPrefabAsset(actor,Actor)==null)throw new Exception("怪物动作绑定保存失败。");
                var record = new Record { fireClip=AssetDatabase.GetAssetPath(fireClip),hitClip=AssetDatabase.GetAssetPath(hitClip),gunClip=AssetDatabase.GetAssetPath(gunClip),
                    characterSeconds=fireClip.length,hitSeconds=hitClip.length,gunSeconds=gunClip.length,gunStartSeconds=gunStart,fireMomentSeconds=fireClip.length,
                    characterFrameRate=fireSource.frameRate,hitFrameRate=hitSource.frameRate,gunFrameRate=gunSource.frameRate,boneLengthRatio=fire.BoneLengthRatio,
                    gunMatchPositionRms=matchPosition,gunMatchAngleRmsDegrees=matchAngle,gunMatchScoreGap=matchGap,
                    characterSamples=fire.Samples,hitSamples=hit.Samples,savedLocal=DateTimeOffset.Now.ToString("o"),
                    timing="Gun start matches animated GunRoot plus Guaiwu Right_Hand in the common source coordinate space; a static or ambiguous signal is rejected. Fire moment defaults to character last frame and remains adjustable. Hit is a reaction, not a rule-layer death event." };
                File.WriteAllText(Folder+"Guaiwu_Combat_ImportRecord.json",JsonUtility.ToJson(record,true));
                AssetDatabase.ImportAsset(Folder+"Guaiwu_Combat_ImportRecord.json"); AssetDatabase.SaveAssets();
            }
            finally
            {
                if(actor!=null)PrefabUtility.UnloadPrefabContents(actor);
                if(prop!=null)UnityEngine.Object.DestroyImmediate(prop);
                EditorSceneManager.ClosePreviewScene(scene); EditorUtility.ClearProgressBar();
            }
        }
        static float FindGunStart(AnimationClip character, AnimationClip gun,
            out float positionRms, out float angleRms, out float scoreGap)
        {
            // Revolver_Rig is stationary in both exports. Match the moving gun and
            // the same character's hand, in their original shared export frame.
            if (gun.length > character.length + .0001f || character.frameRate <= 0)
                throw new InvalidOperationException("枪轨超过人物轨范围，无法确定同步起点。");
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var a=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Fire),scene);
                var b=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Gun),scene);
                foreach(var animator in a.GetComponentsInChildren<Animator>(true).Concat(b.GetComponentsInChildren<Animator>(true)))animator.enabled=false;
                Transform[] from = { Find(a,"Revolver_Rig").Find("GunRoot"),
                    Find(a,"Guaiwu_Rig").GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Right_Hand") };
                Transform[] to = { Find(b,"Revolver_Rig").Find("GunRoot"),
                    Find(b,"Guaiwu_Rig").GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Right_Hand") };
                if (from.Concat(to).Any(t=>t==null))
                    throw new InvalidOperationException("来源缺少实际 GunRoot 或怪物右手，不能从静止根节点推断同步。");
                const int samples = 11;
                var positions = new Vector3[samples,2];
                var rotations = new Quaternion[samples,2];
                float positionSignal=0,angleSignal=0;
                for(int i=0;i<samples;i++)
                {
                    gun.SampleAnimation(b,gun.length*i/(samples-1));
                    for(int bone=0;bone<2;bone++)
                    {
                        positions[i,bone]=to[bone].position; rotations[i,bone]=to[bone].rotation;
                        positionSignal=Mathf.Max(positionSignal,Vector3.Distance(positions[i,bone],positions[0,bone]));
                        angleSignal=Mathf.Max(angleSignal,Quaternion.Angle(rotations[i,bone],rotations[0,bone]));
                    }
                }
                if(positionSignal<.001f && angleSignal<.5f)
                    throw new InvalidOperationException("GunRoot 和怪物右手均无显著运动，无法唯一确定枪轨起点；请保留明确帧范围。");
                float best=float.PositiveInfinity,second=float.PositiveInfinity,start=0;
                float bestPosition=0,bestAngle=0;
                int frames=Mathf.Max(0,Mathf.RoundToInt((character.length-gun.length)*character.frameRate));
                for(int frame=0;frame<=frames;frame++)
                {
                    float offset=frame/character.frameRate,positionError=0,angleError=0;
                    for(int i=0;i<samples;i++)
                    {
                        float time=gun.length*i/(samples-1);
                        character.SampleAnimation(a,Mathf.Min(character.length,offset+time));
                        for(int bone=0;bone<2;bone++)
                        {
                            positionError+=(from[bone].position-positions[i,bone]).sqrMagnitude;
                            angleError+=Mathf.Pow(Quaternion.Angle(from[bone].rotation,rotations[i,bone]),2);
                        }
                    }
                    positionError/=samples*2; angleError/=samples*2;
                    float error=positionError+angleError*Mathf.Pow(Mathf.Deg2Rad*.1f,2);
                    if(error<best){second=best;best=error;start=offset;bestPosition=positionError;bestAngle=angleError;}
                    else if(error<second)second=error;
                }
                positionRms=Mathf.Sqrt(bestPosition); angleRms=Mathf.Sqrt(bestAngle);
                scoreGap=frames>0?second-best:0;
                if(frames>0 && scoreGap<.0000000001f)
                    throw new InvalidOperationException("多个枪轨起点的匹配结果相同，无法唯一确定同步；未默认使用第0帧。");
                if(positionRms>.003f || angleRms>1f)
                    throw new InvalidOperationException("人物与枪轨没有同一段匹配动作。最小位置误差="+positionRms+" 米，角度误差="+angleRms+" 度；请核对导出范围。");
                return start;
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }
        internal static void CorrectHeading(PlayerAnimationSyncAuthoring.BakedPose data,float length)
        {
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Model),scene);
                Transform hip=Find(model,"Hips");string path=AnimationUtility.CalculateTransformPath(hip,model.transform);
                Quaternion correction=Quaternion.Inverse(hip.parent.rotation)*Quaternion.Euler(0,180,0)*hip.parent.rotation;
                var indexes=new[]{"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w"}
                    .Select(name=>Array.FindIndex(data.Bindings,x=>x.path==path&&x.propertyName==name)).ToArray();
                if(indexes.Any(x=>x<0))throw new Exception("怪物根骨曲线不完整。");
                var keys=indexes.Select(i=>data.Curves[i].keys).ToArray();
                Vector3 pivot=new Vector3(keys[0][0].value,keys[1][0].value,keys[2][0].value);
                Quaternion previous=Quaternion.identity;
                for(int f=0;f<keys[0].Length;f++)
                {
                    Vector3 p=pivot+correction*(new Vector3(keys[0][f].value,keys[1][f].value,keys[2][f].value)-pivot);
                    Quaternion q=correction*new Quaternion(keys[3][f].value,keys[4][f].value,keys[5][f].value,keys[6][f].value);
                    if(f>0&&Quaternion.Dot(q,previous)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous=q;
                    float[] values={p.x,p.y,p.z,q.x,q.y,q.z,q.w};for(int c=0;c<7;c++)keys[c][f].value=values[c];
                }
                for(int c=0;c<7;c++){var curve=new AnimationCurve(keys[c]);for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}data.Curves[indexes[c]]=curve;}
                data.Alignment=Quaternion.Euler(0,180,0)*data.Alignment;
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        static AnimationClip Take(string path)=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(x=>!x.name.StartsWith("__preview",StringComparison.OrdinalIgnoreCase));
        static Transform Find(GameObject root,string name)=>root.GetComponentsInChildren<Transform>(true).Single(x=>x.name==name);
        static void Identity(Transform t){t.localPosition=Vector3.zero;t.localRotation=Quaternion.identity;t.localScale=Vector3.one;}
    }
}

