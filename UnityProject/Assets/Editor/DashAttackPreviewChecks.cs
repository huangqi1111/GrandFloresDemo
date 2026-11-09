using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DNF60Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Captures the game's camera and live AdventureArt, rather than assembling a sprite GIF.
[InitializeOnLoad]
public static class DashAttackPreviewChecks
{
    const string Session="DNF60.DashAttackCapture";
    static double timeout;
    static DashAttackPreviewChecks(){if(SessionState.GetBool(Session,false))Attach();}
    public static void RunBatch(){
        if(!Array.Exists(Environment.GetCommandLineArgs(),x=>x=="-dnfCombatVerification"))throw new Exception("Isolated-save flag required");
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity",OpenSceneMode.Single);
        SessionState.SetBool(Session,true);Attach();EditorApplication.EnterPlaymode();
    }
    static void Attach(){timeout=EditorApplication.timeSinceStartup+90;EditorApplication.update-=Wait;EditorApplication.update+=Wait;}
    static void Wait(){
        if(EditorApplication.timeSinceStartup>timeout){SessionState.SetBool(Session,false);EditorApplication.Exit(1);return;}
        if(!EditorApplication.isPlaying)return;
        var game=UnityEngine.Object.FindObjectOfType<GameBootstrap>();
        if(game==null||game.GetType().GetField("art",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(game)==null)return;
        EditorApplication.update-=Wait;SessionState.SetBool(Session,false);
        game.gameObject.AddComponent<DashAttackCameraCapture>().Begin(game);
    }
}

public sealed class DashAttackCameraCapture:MonoBehaviour
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    GameBootstrap game;
    public void Begin(GameBootstrap value){game=value;Application.logMessageReceived+=OnError;StartCoroutine(Capture());}
    void OnError(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)EditorApplication.Exit(1);}
    T Field<T>(object owner,string name)=> (T)owner.GetType().GetField(name,Flags).GetValue(owner);
    void Call(string name,params object[] args)=>game.GetType().GetMethod(name,Flags).Invoke(game,args);
    IEnumerator Capture(){
        yield return null;Call("EnterDungeon");
        foreach(var enemy in FindObjectsOfType<GameBootstrap.Enemy>())enemy.enabled=false;
        game.enabled=false;
        var art=Field<AdventureArt>(game,"art");
        var player=Field<GameObject>(game,"player");var controller=Field<CombatController>(game,"combat");var run=Field<DungeonRunInput>(game,"runInput");
        var visual=player.transform.Find("Visual").GetComponent<SpriteRenderer>();
        player.transform.position=new Vector3(-4,-2.1f,0);art.CancelEffects();art.SetRunning(false);art.Move(Vector2.zero);
        string folder=null;var args=Environment.GetCommandLineArgs();
        for(int i=0;i<args.Length-1;i++)if(args[i]=="-dashCaptureOutput")folder=args[i+1];
        if(string.IsNullOrEmpty(folder))throw new Exception("Missing ghost capture output folder");
        Directory.CreateDirectory(folder);var camera=Camera.main;var rt=new RenderTexture(1280,720,24);rt.Create();
        var overlays=new List<Canvas>();
        foreach(var canvas in FindObjectsOfType<Canvas>())if(canvas.renderMode==RenderMode.ScreenSpaceOverlay){overlays.Add(canvas);canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;}
        var image=new Texture2D(1280,720,TextureFormat.RGB24,false);var seen=new HashSet<string>();
        run.Reset();run.Step(0,1,1,true);run.Step(.05f,0,0,true);run.Step(.18f,1,1,true);art.SetRunning(true);Time.captureFramerate=30;
        for(int frame=0;frame<70;frame++){
            if(frame==40){controller.Reset();art.CancelEffects();player.transform.position=new Vector3(-1,-2.1f,0);}
            if(frame==5||frame==40){art.Move(frame==5?Vector2.right:Vector2.left);Call("BasicAttack");if(!controller.DashAttack)throw new Exception("Running X failed to start dash attack");}
            if(frame==12||frame==47){controller.Pause(.09f);art.SetHitPause(.09f);game.GetType().GetField("hitStopUntil",Flags).SetValue(game,Time.time+.09f);}
            yield return null;Call("MovePlayer",frame<40?Vector2.right:Vector2.left);controller.Tick(Time.time);typeof(AdventureArt).GetMethod("LateUpdate",Flags).Invoke(art,null);
            if(visual.sprite.texture.name.StartsWith("dash_attack_"))seen.Add(visual.sprite.texture.name);
            var previousTarget=camera.targetTexture;var previousActive=RenderTexture.active;
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(folder,"frame_"+frame.ToString("D3")+".png"),image.EncodeToPNG());
            camera.targetTexture=previousTarget;RenderTexture.active=previousActive;
        }
        if(seen.Count!=6)throw new Exception("Not all independent ghost poses played");
        Time.captureFramerate=0;rt.Release();Destroy(image);Destroy(rt);
        foreach(var canvas in overlays)if(canvas!=null){canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;}
        Debug.Log("DASH_ATTACK_CAMERA_PASS: running X routes through production combat, six independent poses, continuous travel in both directions and contact pause. Automated capture: "+folder);EditorApplication.Exit(0);
    }
}
