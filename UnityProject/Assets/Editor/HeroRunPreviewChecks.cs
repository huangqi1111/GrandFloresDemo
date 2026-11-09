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
public static class HeroRunPreviewChecks
{
    const string Session="DNF60.HeroRunCapture";
    static double timeout;
    static HeroRunPreviewChecks(){if(SessionState.GetBool(Session,false))Attach();}
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
        game.gameObject.AddComponent<HeroRunCameraCapture>().Begin(game);
    }
}

public sealed class HeroRunCameraCapture:MonoBehaviour
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    GameBootstrap game;
    public void Begin(GameBootstrap value){game=value;Application.logMessageReceived+=OnError;StartCoroutine(Capture());}
    void OnError(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)EditorApplication.Exit(1);}
    T Field<T>(object owner,string name)=> (T)owner.GetType().GetField(name,Flags).GetValue(owner);
    void Call(string name,params object[] args)=>game.GetType().GetMethod(name,Flags).Invoke(game,args);
    IEnumerator Capture(){
        yield return null;
        Call("EnterDungeon");
        foreach(var enemy in FindObjectsOfType<GameBootstrap.Enemy>())enemy.enabled=false;
        // Automated movement through the production MovePlayer, without keyboard interference.
        game.enabled=false;
        var art=Field<AdventureArt>(game,"art");
        var input=Field<DungeonRunInput>(game,"runInput");
        var player=Field<GameObject>(game,"player");
        var visual=player.transform.Find("Visual").GetComponent<SpriteRenderer>();
        input.Reset();input.Step(0,1,1,true);input.Step(.05f,0,0,true);input.Step(.18f,1,1,true);
        if(!input.Running)throw new Exception("Double tap didn't enable run");
        player.transform.position=new Vector3(-5,-2.1f,0);art.CancelEffects();art.SetRunning(true);
        string folder=null;
        var args=Environment.GetCommandLineArgs();for(int i=0;i<args.Length-1;i++)if(args[i]=="-runCaptureOutput")folder=args[i+1];
        if(string.IsNullOrEmpty(folder))throw new Exception("Missing run capture output folder");
        Directory.CreateDirectory(folder);
        var camera=Camera.main;var rt=new RenderTexture(1280,720,24);rt.Create();
        // Camera.Render omits overlay canvases. Include production HUD in this
        // capture by rendering those same canvases through the capture camera.
        var overlays=new List<Canvas>();
        foreach(var canvas in FindObjectsOfType<Canvas>())if(canvas.renderMode==RenderMode.ScreenSpaceOverlay){
            overlays.Add(canvas);canvas.renderMode=RenderMode.ScreenSpaceCamera;
            canvas.worldCamera=camera;canvas.planeDistance=1;
        }
        var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
        var seen=new HashSet<string>();
        Time.captureFramerate=30;
        for(int frame=0;frame<90;frame++){
            Vector2 direction=frame<45?Vector2.right:Vector2.left;
            if(frame==45)input.Step(.3f,2,2,true);
            float previous=player.transform.position.x;
            Call("MovePlayer",direction);art.SetRunning(input.Running);
            float expected=direction.x*DungeonRunInput.RunSpeed*Time.deltaTime;
            if(Mathf.Abs(player.transform.position.x-previous-expected)>.001f)throw new Exception("Capture didn't use real run speed");
            yield return null;
            typeof(AdventureArt).GetMethod("LateUpdate",Flags).Invoke(art,null);
            if(visual.sprite==null||!visual.sprite.texture.name.StartsWith("run_"))throw new Exception("Run rendered old walking art");
            if(visual.flipX!=(direction.x<0))throw new Exception("Run facing mismatch");
            seen.Add(visual.sprite.texture.name);
            var previousTarget=camera.targetTexture;var previousActive=RenderTexture.active;
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(folder,"frame_"+frame.ToString("D3")+".png"),image.EncodeToPNG());
            camera.targetTexture=previousTarget;RenderTexture.active=previousActive;
        }
        if(seen.Count!=8)throw new Exception("Not all independent run frames played");
        input.Step(1,0,0,true);input.Step(1.2f,0,0,true);art.SetRunning(input.Running);Call("MovePlayer",Vector2.zero);
        yield return null;
        typeof(AdventureArt).GetMethod("LateUpdate",Flags).Invoke(art,null);
        if(input.Running||!visual.sprite.texture.name.StartsWith("hero_0"))throw new Exception("Release failed to restore idle");
        art.Move(Vector2.right);yield return null;
        typeof(AdventureArt).GetMethod("LateUpdate",Flags).Invoke(art,null);
        if(!visual.sprite.texture.name.StartsWith("hero_"))throw new Exception("Walk was replaced by run art");
        art.SetRunning(true);art.PlayBasic(2,player.transform.position,0);yield return null;
        if(!visual.sprite.texture.name.StartsWith("basic_2_"))throw new Exception("Running overrode attack art");
        Time.captureFramerate=0;rt.Release();Destroy(image);Destroy(rt);
        foreach(var canvas in overlays)if(canvas!=null){canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;}
        Debug.Log("HERO_RUN_CAMERA_PASS: eight independent poses, real movement speed, reversal, release to idle, walk preservation and attack priority. Camera captures: "+folder);
        EditorApplication.Exit(0);
    }
}
