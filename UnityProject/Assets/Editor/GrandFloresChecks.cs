using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DNF60Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class GrandFloresChecks
{
    const string Session="DNF60.GrandFloresChecks";
    static double timeout;
    static GrandFloresChecks(){if(SessionState.GetBool(Session,false))Attach();}
    public static void RunBatch()
    {
        if(!Array.Exists(Environment.GetCommandLineArgs(),x=>x=="-dnfCombatVerification"))throw new Exception("Isolated save required");
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity",OpenSceneMode.Single);
        SessionState.SetBool(Session,true);Attach();EditorApplication.EnterPlaymode();
    }
    static void Attach(){timeout=EditorApplication.timeSinceStartup+180;EditorApplication.update-=Wait;EditorApplication.update+=Wait;}
    static void Wait()
    {
        if(EditorApplication.timeSinceStartup>timeout){Debug.LogError("GRAND_FLORES_TIMEOUT");EditorApplication.Exit(1);return;}
        if(!EditorApplication.isPlaying)return;
        var game=UnityEngine.Object.FindObjectOfType<GameBootstrap>();
        if(game==null||typeof(GameBootstrap).GetField("art",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(game)==null)return;
        EditorApplication.update-=Wait;SessionState.SetBool(Session,false);
        game.gameObject.AddComponent<GrandFloresPlayChecks>().Begin(game);
    }
}

public sealed class GrandFloresPlayChecks:MonoBehaviour
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    const string Output="C:/Users/Administrator/Documents/Codex/2026-09-29/new-chat/outputs/grand-flores-expansion/play";
    GameBootstrap game;int checkedRooms,checkedEdges;
    T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Flags).GetValue(owner);
    void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Flags).SetValue(owner,value);
    object Call(string name,params object[] args)=>typeof(GameBootstrap).GetMethod(name,Flags).Invoke(game,args);
    void Require(bool value,string detail){if(!value)throw new Exception("GRAND_FLORES_FAILED: "+detail);}
    public void Begin(GameBootstrap value){game=value;Application.logMessageReceived+=OnError;StartCoroutine(Check());}
    void OnError(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)EditorApplication.Exit(1);}
    List<GameObject> Enemies=>Field<List<GameObject>>(game,"enemies");
    void Freeze(){foreach(var go in Enemies)go.GetComponent<GameBootstrap.Enemy>().enabled=false;}
    void ClearByDamage(){foreach(var go in Enemies.ToArray())go.GetComponent<GameBootstrap.Enemy>().Take(1000000);}
    void Capture(string id)
    {
        var camera=Camera.main;var target=new RenderTexture(1280,720,24);target.Create();
        var canvases=new List<Canvas>();
        foreach(var canvas in FindObjectsOfType<Canvas>())if(canvas.renderMode==RenderMode.ScreenSpaceOverlay){canvases.Add(canvas);canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;}
        Canvas.ForceUpdateCanvases();var old=camera.targetTexture;var active=RenderTexture.active;
        camera.targetTexture=target;camera.Render();RenderTexture.active=target;
        var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
        File.WriteAllBytes(Path.Combine(Output,id+".png"),image.EncodeToPNG());camera.targetTexture=old;RenderTexture.active=active;
        foreach(var canvas in canvases){canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;}
        target.Release();Destroy(target);Destroy(image);
    }
    Vector3 Exit(int direction)=>direction==0?new Vector3(8.2f,-2.1f):direction==2?new Vector3(-8.2f,-2.1f):direction==1?new Vector3(0,CombatPlane.Back):new Vector3(0,CombatPlane.Front);
    IEnumerator Check()
    {
        yield return null;Directory.CreateDirectory(Output);game.enabled=false;
        var player=Field<GameObject>(game,"player");var art=Field<AdventureArt>(game,"art");
        Call("ShowDungeonSelection","格兰之森",new[]{"幽暗密林","雷鸣废墟","格拉卡","烈焰格拉卡","冰霜幽暗密林","猛毒雷鸣废墟","暗黑雷鸣废墟"});
        for(int selection=1;selection<=6;selection++)
        {
            Call("EnterTown");Set(game,"selectedDungeonIndex",selection);Call("EnterDungeon");Freeze();
            var dungeon=Field<ForestDungeon>(game,"activeForest");
            Require(dungeon!=null,"selection still shares Mirkwood "+selection);
            Require(Field<int>(game,"activeForestRoom")==dungeon.start,"wrong initial room");
            Require(Resources.Load<Texture2D>("DNF60/Sprites/Dungeon/"+dungeon.id)!=null,"background missing");
            Require(Resources.Load<Texture2D>("DNF60/UI/DungeonBoss/"+dungeon.id)!=null,"cover missing");
            var seen=new HashSet<int>{dungeon.start};var queue=new Queue<int>();queue.Enqueue(dungeon.start);
            while(queue.Count>0)foreach(int target in dungeon.rooms[queue.Dequeue()].links)if(target>=0&&seen.Add(target))queue.Enqueue(target);
            Require(seen.Count==dungeon.rooms.Length,"unreachable room");
            // Last real kill, rather than a debug clear flag, must open every start-room gate.
            var gates=Field<GameObject[]>(game,"forestGates");
            for(int direction=0;direction<4;direction++)
            {
                var clamped=(Vector3)Call("ClampDungeonPassage",Exit(direction));
                Require((clamped-Exit(direction)).sqrMagnitude>.001f,"uncleared boundary leaks "+direction);
                if(dungeon.rooms[dungeon.start].links[direction]>=0)Require(gates[direction].GetComponent<SpriteRenderer>().sprite.texture.name=="gate_closed","gate not closed");
            }
            ClearByDamage();Require(Field<bool>(game,"roomCleared"),"final kill did not clear start");
            yield return null;
            for(int roomIndex=0;roomIndex<dungeon.rooms.Length;roomIndex++)
            {
                Set(game,"activeForestRoom",roomIndex);Call("SpawnConfiguredForestRoom");Freeze();
                var room=dungeon.rooms[roomIndex];bool cleared=Field<bool[]>(game,"clearedForestRooms")[roomIndex];
                Require(Enemies.Count==(cleared?0:room.enemies.Length),"population mismatch "+dungeon.id+" "+roomIndex);
                foreach(var spawn in room.enemies)
                {
                    Require(art.LoadForestEnemy(spawn.asset)!=null,"missing idle "+spawn.asset);
                    var walk=art.LoadForestEnemyWalk(spawn.asset);var attack=art.LoadForestEnemyAttack(spawn.asset);
                    Require(walk!=null&&walk.Length==4&&attack!=null&&attack.Length==3,"missing action clips "+spawn.asset);
                }
                for(int direction=0;direction<4;direction++)
                {
                    int next=room.links[direction];
                    Require((gates[direction]!=null&&gates[direction].activeSelf)==(next>=0),"wrong door availability");
                    if(next>=0){Require(dungeon.rooms[next].links[(direction+2)%4]==roomIndex,"nonreciprocal link");checkedEdges++;}
                }
                checkedRooms++;
                if(room.boss)
                {
                    Require(Array.Exists(room.enemies,x=>x.boss),"boss missing");
                    var audio=Field<AdventureAudio>(game,"gameAudio");Require(Field<AudioSource>(audio,"music").clip.name=="boss_variation","boss BGM missing");
                    player.transform.position=new Vector3(-4,-2.1f);yield return null;Capture(dungeon.id);
                    game.enabled=true;ScreenCapture.CaptureScreenshot(Path.Combine(Output,dungeon.id+"-gameview.png"));
                    yield return new WaitForSeconds(.25f);game.enabled=false;
                }
                // Deferred old enemies must actually leave the scene before testing the next room.
                yield return null;
            }
            // Physically touching each direction uses the production room-exit path.
            var exercised=new HashSet<int>();
            for(int i=0;i<dungeon.rooms.Length;i++)if(!dungeon.rooms[i].boss)
            for(int d=0;d<4;d++)if(dungeon.rooms[i].links[d]>=0&&exercised.Add(d))
            {
                Set(game,"activeForestRoom",i);Call("SpawnConfiguredForestRoom");Freeze();
                ClearByDamage();Set(game,"forestTransitionAt",0f);player.transform.position=Exit(d);Call("HandleRoomExit");Freeze();
                int target=dungeon.rooms[i].links[d];Require(Field<int>(game,"activeForestRoom")==target,"wrong gate target");
                // Return to the cleared origin; no duplicate population is allowed.
                if(!dungeon.rooms[target].boss)
                {
                    ClearByDamage();Set(game,"forestTransitionAt",0f);player.transform.position=Exit((d+2)%4);Call("HandleRoomExit");Freeze();
                    Require(Field<int>(game,"activeForestRoom")==i&&Enemies.Count==0,"backtracking respawned enemies");
                }
                yield return null;
            }
            Set(game,"activeForestRoom",dungeon.boss);Set(game,"dungeonResultOpen",false);Call("SpawnConfiguredForestRoom");Freeze();
            ClearByDamage();Require(Field<bool>(game,"dungeonResultOpen"),"boss kill did not show result");
            // Observe the new boss's actual AI attack, including all three drawing poses.
            Set(game,"dungeonResultOpen",false);Field<bool[]>(game,"clearedForestRooms")[dungeon.boss]=false;
            Call("SpawnConfiguredForestRoom");Freeze();Set(game,"hp",999);Call("ClearForestPoison");
            GameBootstrap.Enemy naturalBoss=null;
            foreach(var go in Enemies)if(Field<ForestSpawn>(go.GetComponent<GameBootstrap.Enemy>(),"forestProfile").boss){naturalBoss=go.GetComponent<GameBootstrap.Enemy>();break;}
            Require(naturalBoss!=null,"natural boss missing");naturalBoss.transform.position=new Vector3(1.2f,-2.1f);player.transform.position=new Vector3(0,-2.1f);naturalBoss.enabled=true;
            var poses=new HashSet<Sprite>();float until=Time.time+7f;bool sawHazard=false;
            while(Time.time<until)
            {
                var shown=naturalBoss.GetComponent<SpriteRenderer>().sprite;
                if(shown.texture.name.Contains("_attack_"))poses.Add(shown);
                sawHazard|=FindObjectsOfType<GameBootstrap.ForestHazard>().Length>0;
                yield return null;
            }
            Require(poses.Count==3,"natural boss did not play single full attack "+dungeon.id);
            Require(Field<int>(game,"hp")<999,"natural boss never hit "+dungeon.id);
            if(selection==1||selection==3||selection==4)Require(sawHazard,"ranged boss did not cast "+dungeon.id);
            Call("ClearForestHazards");Call("ClearForestPoison");Set(game,"dungeonResultOpen",true);
            Call("ResolveDungeonResult",0);Freeze();
            Require(Field<ForestDungeon>(game,"activeForest").id==dungeon.id&&Field<int>(game,"activeForestRoom")==dungeon.start,"retry entered wrong dungeon");
            Require(Enemies.Count==dungeon.rooms[dungeon.start].enemies.Length&&!Field<bool>(game,"roomCleared"),"retry did not reset run");
            Debug.Log("GRAND_FLORES_DUNGEON_PASS: "+dungeon.id+" rooms="+dungeon.rooms.Length+" gateDirections="+exercised.Count);
            yield return null;
        }
        // Test the new balance at a representative level without changing saved progress.
        int savedLevel=Field<int>(game,"level"),savedDefense=Field<int>(game,"baseDefense");
        Set(game,"level",8);Set(game,"baseDefense",11);
        int ordinary=(int)Call("BalancedMonsterDamage","Goblin",10);
        int received=(int)Call("MitigateMonsterDamage",ordinary);
        Require(received>=20&&received<ordinary,"ordinary hit is negligible or armor ignored");
        Set(game,"baseDefense",10000);
        int armored=(int)Call("MitigateMonsterDamage",ordinary);
        Require(armored>=ordinary*.34f&&armored<received,"armor must reduce, never erase, monster damage");
        Set(game,"level",savedLevel);Set(game,"baseDefense",savedDefense);
        var feedbackAudio=Field<AdventureAudio>(game,"gameAudio");var voice=Field<AudioSource>(feedbackAudio,"voice");
        var grunt=Field<AudioClip>(feedbackAudio,"hurtSound");
        Require(grunt!=null&&grunt.length>.05f&&grunt.length<1.5f,"short damage voice failed to decode");
        Set(feedbackAudio,"nextHurtAt",0f);voice.Stop();Set(game,"hp",999);
        Call("TakePlayerDamage",0);Require(!voice.isPlaying,"zero damage played hurt voice");
        Call("TakePlayerDamage",10);Require(voice.isPlaying&&Field<int>(game,"hp")==989,"real damage has no hurt voice");
        float nextVoice=Field<float>(feedbackAudio,"nextHurtAt");
        Call("TakePlayerDamage",10);Require(Field<float>(feedbackAudio,"nextHurtAt")==nextVoice,"rapid damage retriggered hurt voice");
        var rotation=EightSkillRotation.Simulate(Field<List<SkillData>>(game,"skills"));
        float recovery=Field<float>(game,"manaRegenPerSecond");
        Require(Mathf.Abs(rotation.ManaPerSecond/recovery-1.3f)<.001f,"8-skill MP drain/recovery ratio incorrect");
        foreach(int count in rotation.Casts)Require(count>0,"rotation starved one of eight skills");
        Set(game,"mp",0);Set(game,"manaRegenFraction",0f);Call("RecoverMana",1f);
        int battleRecovery=Field<int>(game,"mp");Require(battleRecovery==Mathf.FloorToInt(recovery),"combat MP recovery incorrect");
        Call("EnterTown");Set(game,"mp",0);Set(game,"manaRegenFraction",0f);Call("RecoverMana",1f);
        Require(Field<int>(game,"mp")==battleRecovery,"town and battle MP recovery differ");
        int maxHealth=100+Field<int>(game,"level")*12;
        Set(game,"hp",maxHealth/2);Set(game,"healthRegenFraction",0f);Call("RecoverHealth",60f);
        Require(Field<int>(game,"hp")==maxHealth/2+Mathf.FloorToInt(maxHealth*.06f),"slow HP recovery budget incorrect");
        Call("EnterDungeon");Freeze();Set(game,"hp",maxHealth/2);Set(game,"healthRegenFraction",0f);Call("RecoverHealth",60f);
        Require(Field<int>(game,"hp")==maxHealth/2+Mathf.FloorToInt(maxHealth*.06f),"combat affected HP recovery");
        Debug.Log("BALANCE_FEEDBACK_PASS: eight-skill drain="+rotation.ManaPerSecond+" MP/s, recovery="+recovery+" MP/s, ratio=1.3, HP=6%/minute in town and combat; casts="+string.Join(",",rotation.Casts));
        // Spell windup fixes target; moving out avoids impact, remaining in it takes damage.
        Set(game,"hp",999);Set(game,"hitStopUntil",0f);Call("ClearEnemies");player.transform.position=new Vector3(0,-2.1f);
        Call("CreateForestHazard","lightning",Vector3.zero,1,30);
        var lightning=FindObjectOfType<ForestLightning>();Require(lightning!=null,"dedicated lightning absent");
        Require(!lightning.GetComponent<SpriteRenderer>().enabled,"lightning still displays generic crescent");
        var lines=lightning.GetComponentsInChildren<LineRenderer>();Require(lines.Length==4,"lightning geometry missing");
        Require(lines[0].enabled&&!lines[1].enabled,"bolt visible before warning finishes");
        player.transform.position=new Vector3(4,-2.1f);
        yield return new WaitForSeconds(.8f);Require(Field<int>(game,"hp")==999,"telegraph followed player");
        Call("ClearForestHazards");player.transform.position=new Vector3(0,-2.1f);
        Call("CreateForestHazard","lightning",Vector3.zero,1,30);
        yield return new WaitForSeconds(.72f);
        Require(Field<int>(game,"hp")<999,"standing in lightning took no damage");
        var landed=FindObjectOfType<ForestLightning>();Require(landed!=null&&landed.transform.Find("Lightning core").GetComponent<LineRenderer>().enabled,"no visible bolt at impact");
        Set(game,"hp",(100+Field<int>(game,"level")*12)/2);
        Set(game,"mp",(140+(Field<int>(game,"level")-1)*10)/2);Call("Refresh");
        var hpLiquid=Field<UnityEngine.UI.Image>(game,"hpBar");var mpLiquid=Field<UnityEngine.UI.Image>(game,"mpBar");
        Require(hpLiquid!=null&&mpLiquid!=null,"painted orb liquids are disconnected");
        Require(Mathf.Abs(hpLiquid.fillAmount-.5f)<.005f&&Mathf.Abs(mpLiquid.fillAmount-.5f)<.005f,"HP/MP orb fill does not reflect real resources");
        Require(hpLiquid.fillMethod==UnityEngine.UI.Image.FillMethod.Vertical&&mpLiquid.fillMethod==UnityEngine.UI.Image.FillMethod.Vertical,"orb liquid must lower vertically");
        Capture("lightning-impact");int lightningHp=Field<int>(game,"hp");
        yield return new WaitForSeconds(.13f);Require(Field<int>(game,"hp")==lightningHp,"one lightning hit more than once");
        Call("ClearForestHazards");
        Call("CreateForestHazard","ice",Vector3.zero,1,30);
        yield return new WaitForSeconds(.8f);Require(Field<int>(game,"hp")<999&&Field<float>(game,"forestSlowUntil")>Time.time,"ice impact or slow missing");
        Call("ApplyForestPoison",20);int poisonHp=Field<int>(game,"hp");
        var combat=Field<CombatController>(game,"combat");combat.Reset();combat.BeginBasic(Time.time,.7f,18);
        for(int i=0;i<3;i++)Call("UpdateForestStatus",1.01f);
        Require(Field<int>(game,"hp")==poisonHp-15&&Field<int>(game,"forestPoisonTicks")==0,"poison tick budget wrong");
        Require(combat.Active,"poison interrupted the player's attack");
        Call("EnterTown");yield return null;
        Require(FindObjectsOfType<GameBootstrap.ForestHazard>().Length==0,"hazard survived town");
        foreach(var gate in Field<GameObject[]>(game,"forestGates"))Require(gate==null||!gate.activeSelf,"gate survived town");
        Require(Field<float>(game,"forestSlowUntil")==0,"slow survived town");
        File.WriteAllText(Path.Combine(Output,"verification.txt"),"PASS: six dungeons, "+checkedRooms+" rooms, "+checkedEdges+" reciprocal exits; resource clips; actual kills, all four gate directions, backtracking, boss music/result/retry, fixed-target dodge, ice damage/slow, town cleanup.");
        Debug.Log("GRAND_FLORES_PLAY_CHECKS_PASS: rooms="+checkedRooms+", exits="+checkedEdges);EditorApplication.Exit(0);
    }
}
