using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DNF60Prototype;

[InitializeOnLoad]
public static class CombatControllerChecks
{
    const string Running = "DNF60.CombatVerification.Running";
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static GameBootstrap game;
    static int phase, originalHp;
    static float deadline;
    static double timeout;
    static GameBootstrap.Enemy target;
    static bool finished;
    static int repeatMana;
    static readonly Dictionary<GameBootstrap.Enemy,HashSet<Sprite>> observedAttacks=new Dictionary<GameBootstrap.Enemy,HashSet<Sprite>>();

    static CombatControllerChecks()
    {
        if (SessionState.GetBool(Running, false)) Attach();
    }
    static void Require(bool condition, string message)
    { if (!condition) throw new Exception("COMBAT_CHECK_FAILED: " + message); }
    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, Private).GetValue(owner); }
    static void Set(object owner, string name, object value)
    { owner.GetType().GetField(name, Private).SetValue(owner, value); }
    static void Call(string name, params object[] args)
    { typeof(GameBootstrap).GetMethod(name, Private).Invoke(game, args); }

    [MenuItem("DNF60/Checks/Combat Controller")]
    public static void Run()
    {
        var position = new Vector3(0, -2, 0);
        int facing = 1;
        var hits = new List<CombatController.Hit>();
        var skill = new SkillData("test", KeyCode.Z, 1, 5, 1, 90, 3, .1f, .1f);
        var controller = new CombatController(() => position, () => facing, hits.Add, i => i == 0 ? skill : null);
        Require(controller.BeginBasic(0, .7f, 18), "basic attack rejected");
        facing = -1;
        position = Vector3.one;
        controller.Tick(.19f);
        Require(hits.Count == 0, "damage before impact");
        controller.Tick(.20f);
        controller.Tick(.20f);
        Require(hits.Count == 1 && hits[0].Damage == 18, "basic hit duplicated or wrong damage");
        Require(hits[0].Facing == 1 && hits[0].Origin == new Vector3(0,-2,0), "attack snapshot changed");
        Require(controller.Active && !controller.BeginSkill(0,.21f,30), "recovery interrupted");
        controller.Tick(.43f);
        Require(controller.Active, "normal animation recovery ended before its source duration");
        controller.Tick(.61f);
        Require(!controller.Active && !controller.BeginBasic(.62f,.7f,18), "basic cooldown ignored");
        Require(controller.BeginBasic(.71f,.7f,18), "basic cooldown never released");
        controller.Reset(); controller.Tick(10);
        Require(hits.Count == 1 && !controller.Active, "cancelled hit survived reset");
        for(int stage=1;stage<=3;stage++)
        {
            controller.Reset();hits.Clear();
            float impact=HeroBasicAnimation.Impact(stage),duration=HeroBasicAnimation.Duration(stage);
            Require(controller.BeginBasic(0,duration,18,stage),"combo stage rejected");
            controller.Tick(impact-.01f);Require(hits.Count==0,"combo stage hit too early");
            controller.Tick(impact);Require(hits.Count==1,"combo impact missing");
            controller.Pause(.06f);
            controller.Tick(duration+.01f);Require(controller.Active,"hit pause consumed recovery frames");
            controller.Tick(duration+.07f);Require(!controller.Active&&hits.Count==1,"hit pause duplicated damage or never released");
        }
        controller.Reset();hits.Clear();position=Vector3.zero;facing=1;
        Require(controller.BeginDashAttack(0,18)&&controller.DashAttack&&controller.BasicStage==0,"dash attack rejected or counted as combo");
        Require(controller.ConsumeDashTravel(.17f)==0,"dash moved before thrust transition");
        position+=Vector3.right*controller.ConsumeDashTravel(.23f);
        Require(position.x>0&&position.x<HeroDashAttack.Travel,"dash travel teleported or stalled");
        facing=-1;controller.Tick(.23f);
        Require(hits.Count==1&&hits[0].Origin==position&&hits[0].Facing==1,"dash hit didn't use moving origin and locked direction");
        controller.Pause(.09f);Require(controller.ConsumeDashTravel(.32f)<.001f,"dash travel advanced during hit pause");
        position+=Vector3.right*controller.ConsumeDashTravel(.61f);
        Require(Mathf.Abs(position.x-HeroDashAttack.Travel)<.001f,"dash travel budget wrong");
        controller.Tick(.73f);Require(!controller.Active&&hits.Count==1,"dash duplicated damage or failed recovery");
        controller.Reset();Require(!controller.DashAttack&&controller.ConsumeDashTravel(10)==0,"dash survived reset");
        hits.Clear();
        Require(controller.BeginSkill(0, 20, 30), "skill rejected");
        controller.Tick(21); controller.Tick(22);
        float total = 0; foreach(var h in hits) total += h.Damage;
        Require(hits.Count == 3 && Mathf.Abs(total-120)<.001f, "multi hit damage or catchup incorrect");
        skill.ReadyAt = 40;
        Require(!controller.BeginSkill(0,30,30), "skill cooldown ignored");
        Require(!controller.BeginSkill(-1,50,30), "invalid skill accepted");
        controller.Reset(); hits.Clear(); skill.ReadyAt=0; skill.Hits=1; skill.AirOnly=true; skill.WaitForLanding=true;
        Require(controller.BeginSkill(0,60,30), "air skill rejected");
        controller.Tick(61,true);
        Require(hits.Count==0 && controller.Active, "air skill hit before landing");
        position=new Vector3(3,-2,0); controller.Tick(62,false); controller.Tick(63,false);
        Require(hits.Count==1 && hits[0].Origin==position && !controller.Active, "landing hit incorrect");
        skill.AirOnly=false; skill.Hits=3;
        int callbacks=0;
        CombatController cancel=null;
        cancel=new CombatController(()=>position,()=>1,h=>{callbacks++;cancel.Reset();},i=>skill);
        cancel.BeginSkill(0,0,30); cancel.Tick(10);
        Require(callbacks==1 && !cancel.Active, "reset inside hit callback leaked extra hits");
        Debug.Log("COMBAT_CONTROLLER_CHECKS_PASS: timing, facing/origin snapshot, recovery, cooldown, damage budget, low-FPS catchup, landing, cancellation.");
    }

    public static void RunBatch()
    {
        Require(Array.Exists(Environment.GetCommandLineArgs(), x=>x=="-dnfCombatVerification"), "missing isolated-save flag");
        Run();
        CombatPlaneChecks.Run();
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
        SessionState.SetBool(Running,true);
        Attach();
        EditorApplication.EnterPlaymode();
    }
    static void Attach()
    {
        timeout=EditorApplication.timeSinceStartup+90;
        Application.logMessageReceived-=OnLog;
        Application.logMessageReceived+=OnLog;
        EditorApplication.update-=Step;
        EditorApplication.update+=Step;
    }
    static void OnLog(string message,string stack,LogType type)
    {
        if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)
            Finish(false,message);
    }
    static void FreezeEnemies()
    {
        foreach(var enemy in UnityEngine.Object.FindObjectsOfType<GameBootstrap.Enemy>()) enemy.enabled=false;
    }
    static void VerifyDungeonRun()
    {
        var run=new DungeonRunInput();
        // Every arrow supports a same-key double tap; holding maintains the latch.
        for(int key=0;key<4;key++){
            run.Reset();int bit=1<<key;
            run.Step(0,bit,bit,true);Require(!run.Running,"single press started running");
            run.Step(.07f,0,0,true);run.Step(.19f,bit,bit,true);
            Require(run.Running,"same-key double tap failed");
            run.Step(10,bit,0,true);Require(run.Running,"long hold lost running");
        }
        run.Reset();run.Step(0,1,1,true);run.Step(.08f,0,0,true);run.Step(.30f,1,1,true);
        Require(!run.Running,"late second press started running");
        run.Reset();run.Step(0,1,1,true);run.Step(.06f,0,0,true);run.Step(.18f,2,2,true);
        Require(!run.Running,"different arrow presses counted as double tap");
        run.Reset();run.Step(0,1,1,true);run.Step(.06f,0,0,true);run.Step(.18f,1,1,true);
        run.Step(.25f,3,2,true);Require(run.Running,"overlapping opposed keys stopped running");
        run.Step(.30f,2,0,true);Require(run.Running,"right-to-left hold lost running");
        run.Step(.35f,0,0,true);run.Step(.43f,4,4,true);
        Require(run.Running,"short direction-change gap lost running");
        run.Step(.50f,0,0,true);run.Step(.63f,0,0,true);
        Require(!run.Running,"release all keys failed to stop running");
        run.Step(.70f,8,8,true);Require(!run.Running,"new direction retained expired run latch");
        run.Reset();run.Step(0,1,1,true);run.Step(.05f,0,0,true);run.Step(.16f,1,1,true);
        run.Step(.17f,1,0,false);Require(!run.Running,"disabled controls retained running");
        run.Step(.18f,1,1,true);Require(!run.Running,"menu reset retained old tap");
        Call("EnterDungeon");FreezeEnemies();
        var live=Field<DungeonRunInput>(game,"runInput");live.Reset();
        var player=Field<GameObject>(game,"player");player.transform.position=new Vector3(0,-2,0);
        Call("MovePlayer",Vector2.right);float walked=player.transform.position.x;
        Require(Mathf.Abs(walked-DungeonRunInput.WalkSpeed*Time.deltaTime)<.001f,"dungeon walk speed not applied");
        live.Step(0,1,1,true);live.Step(.05f,0,0,true);live.Step(.18f,1,1,true);
        player.transform.position=new Vector3(0,-2,0);Call("MovePlayer",Vector2.right);
        float ran=player.transform.position.x;
        Require(ran>walked&&Mathf.Abs(ran-DungeonRunInput.RunSpeed*Time.deltaTime)<.001f,"run speed not applied");
        Call("EnterTown");Require(!live.Running,"town transition retained run latch");
        player.transform.position=new Vector3(0,-2,0);Call("MovePlayer",Vector2.right);
        Require(Mathf.Abs(player.transform.position.x-5f*Time.deltaTime)<.001f,"town speed changed");
        Call("EnterDungeon");FreezeEnemies();
        Require(!live.Running,"dungeon entry started running");
        Debug.Log("DUNGEON_RUN_CHECKS_PASS: four same-key double taps, timed window, long hold, opposed keys, reversal grace, full release, menu/transition reset, actual walk/run/town displacement.");
    }
    static void VerifyDashAttack(){
        var art=Field<AdventureArt>(game,"art");var controller=Field<CombatController>(game,"combat");var run=Field<DungeonRunInput>(game,"runInput");
        var player=Field<GameObject>(game,"player");var visual=player.transform.Find("Visual").GetComponent<SpriteRenderer>();
        foreach(int direction in new[]{1,-1}){
            controller.Reset();art.CancelEffects();Set(game,"hitStopUntil",0f);run.Reset();run.Step(0,1,1,true);run.Step(.05f,0,0,true);run.Step(.18f,1,1,true);
            art.Move(new Vector2(direction,0));player.transform.position=new Vector3(0,-2.1f,0);
            Call("BasicAttack");Require(controller.DashAttack&&controller.BasicStage==0,"running X didn't select dash thrust");
            Require(visual.sprite.texture.name=="dash_attack_00"&&visual.flipX==(direction<0),"dash began with wrong art or facing");
            Set(controller,"dashStarted",Time.time-.30f);Set(art,"actionStarted",Time.time-.30f);
            Call("MovePlayer",new Vector2(-direction,0));typeof(AdventureArt).GetMethod("LateUpdate",Private).Invoke(art,null);
            Require(Mathf.Abs(player.transform.position.x-direction*HeroDashAttack.DistanceAt(.30f))<.001f,"dash didn't move continuously in locked direction");
            Require(visual.sprite.texture.name=="dash_attack_02"&&visual.flipX==(direction<0),"dash contact reused old attack or reversed mid-thrust");
            var position=player.transform.position;Set(game,"hitStopUntil",Time.time+.09f);controller.Pause(.09f);art.SetHitPause(.09f);Call("MovePlayer",Vector2.right);
            Require(player.transform.position==position,"dash moved during hit stop");
            Set(game,"hitStopUntil",0f);controller.Reset();art.CancelEffects();
        }
        run.Reset();Call("BasicAttack");Require(controller.BasicStage==1&&!controller.DashAttack,"walking X no longer starts normal combo");controller.Reset();art.CancelEffects();
        // Dash obeys the same closed-room gate barrier as ordinary movement.
        run.Step(0,1,1,true);run.Step(.05f,0,0,true);run.Step(.18f,1,1,true);art.Move(Vector2.right);player.transform.position=new Vector3(7.6f,-2.1f,0);Call("BasicAttack");Set(controller,"dashStarted",Time.time-.51f);Call("MovePlayer",Vector2.right);
        Require(player.transform.position.x<=7.65f,"dash passed through closed gate");controller.Reset();art.CancelEffects();run.Reset();player.transform.position=new Vector3(0,-2.1f,0);
        Debug.Log("DASH_ATTACK_CHECKS_PASS: running X selects dedicated thrust in both directions, continuous locked travel, contact pose, hit pause, walking combo preserved and closed gates block dash.");
    }
    static void VerifyHeroClips()
    {
        var art=Field<AdventureArt>(game,"art");
        var renderer=Field<GameObject>(game,"player").transform.Find("Visual").GetComponent<SpriteRenderer>();
        for(int stage=1;stage<=3;stage++)
        {
            for(int pose=0;pose<HeroBasicAnimation.PoseCount;pose++)
                Require(Resources.Load<Texture2D>("DNF60/Sprites/HeroCombat/basic_"+stage+"_"+pose.ToString("D2"))!=null,"missing redrawn normal attack pose");
            art.PlayBasic(stage,Vector3.zero,0);
            float[] samples={0,HeroBasicAnimation.Impact(stage),HeroBasicAnimation.Duration(stage)-.01f};
            foreach(float elapsed in samples)
            {
                Set(art,"actionStarted",Time.time-elapsed);
                typeof(AdventureArt).GetMethod("LateUpdate",Private).Invoke(art,null);
                Require(renderer.sprite.texture.name=="basic_"+stage+"_"+HeroBasicAnimation.PoseAt(stage,elapsed).ToString("D2"),"old or wrong normal attack frame rendered");
            }
            float ending=Field<float>(art,"actionUntil");
            art.SetHitPause(.06f);
            Require(Mathf.Abs(Field<float>(art,"actionUntil")-ending-.06f)<.001f,"visual hit stop did not preserve action duration");
            art.CancelEffects();
        }
        art.CancelEffects();art.PlayBasic(1,Vector3.zero,0);
        Set(art,"actionStarted",Time.time-HeroBasicAnimation.Duration(1)+.01f);
        typeof(AdventureArt).GetMethod("LateUpdate",Private).Invoke(art,null);
        var finish=renderer.sprite;art.CancelEffects();art.PlayBasic(2,Vector3.zero,0);
        Require(Mathf.Abs(renderer.sprite.bounds.min.y-finish.bounds.min.y)<.001f&&renderer.sprite.texture.width==288,"second attack entry shifted feet anchor");
        art.CancelEffects();art.PlayBasic(2,Vector3.zero,0);
        Set(art,"actionStarted",Time.time-HeroBasicAnimation.Duration(2)+.01f);
        typeof(AdventureArt).GetMethod("LateUpdate",Private).Invoke(art,null);
        Require(renderer.sprite.texture.name=="basic_2_05","second attack finish missing");
        art.CancelEffects();
        art.PlayAction(HeroAction.Thrust,Vector3.zero,0,1f);
        Set(art,"actionStarted",Time.time-.5f);Set(art,"actionUntil",Time.time+.5f);
        art.SetHitPause(.08f);
        Require(renderer.sprite.texture.name=="hero_27","skill hit stop froze previous pose instead of contact pose");
        var held=renderer.sprite;float before=Field<float>(art,"actionUntil");
        art.SetHitPause(.1f);
        Require(renderer.sprite==held&&Mathf.Abs(Field<float>(art,"actionUntil")-before-.02f)<.001f,"overlapping hit stop rewound pose or counted delay twice");
        art.CancelEffects();
        art.PlayAction(HeroAction.AirBasic,Vector3.zero,1,.42f);
        for(int pose=0;pose<6;pose++){
            Set(art,"actionStarted",Time.time-(pose+.1f)*.42f/6);Set(art,"actionUntil",Time.time+1);
            typeof(AdventureArt).GetMethod("LateUpdate",Private).Invoke(art,null);
            Require(renderer.sprite!=null&&renderer.sprite.texture.name=="air_basic_"+pose.ToString("D2"),"air attack reused ground pose");
        }
        art.CancelEffects();
        var controller=Field<CombatController>(game,"combat");controller.Reset();
        Call("EnterDungeon");FreezeEnemies();Field<JumpState>(game,"jump").Start();Call("BasicAttack");
        Require(controller.AirBasic&&controller.BasicStage==0,"air X began ground combo");
        Call("BasicAttack");Require(!Field<bool>(game,"basicQueued"),"air X queued ground combo");
        controller.Reset();Field<JumpState>(game,"jump").Reset();art.CancelEffects();Call("EnterTown");
        Debug.Log("AIR_ATTACK_CHECKS_PASS: six independent airborne poses; airborne X cannot queue ground combo.");
        Debug.Log("HERO_COMBO_ASSETS_PASS: 18 textures loaded; all three clips render new poses; basic/skill hit stop preserves contact pose and duration.");
    }
    static void VerifyAudio()
    {
        var audio=Field<AdventureAudio>(game,"gameAudio");Require(audio!=null,"BGM system missing");
        var music=Field<AudioSource>(audio,"music");
        Require(music.clip!=null&&music.clip.name=="town_variation"&&music.loop&&music.volume<=.15f&&music.spatialBlend==0,"quiet looping town music missing");
        Require(UnityEngine.Object.FindObjectsOfType<AudioListener>().Length==1,"audio requires exactly one listener");
        Require(audio.GetComponents<AudioSource>().Length==3,"expected separate music, impact and hurt voice sources");
        var impact=Field<AudioSource>(audio,"impact");
        var hit=Field<AudioClip>(audio,"hitSound");
        Require(hit!=null&&hit.name=="hero_hit"&&hit.length>.10f&&hit.length<.13f,"approved hit sound missing or wrong duration");
        Require(!impact.loop&&impact.spatialBlend==0&&impact.volume==AdventureAudio.HitVolume,"impact source configuration incorrect");
        var clips=Resources.LoadAll<AudioClip>("DNF60/Audio");Require(clips.Length==7,"expected town/battle/boss variations, preserved originals and hit/hurt assets");
        foreach(var clip in clips)Require(clip.length>0&&clip.samples>0,"BGM failed to decode: "+clip.name);
        audio.SetDungeon(true);Require(music.clip.name=="battle_variation"&&music.loop&&music.volume<=.15f,"quiet battle music missing");
        audio.SetDungeon(true,true);Require(music.clip.name=="boss_variation"&&music.loop&&music.volume<=.15f,"boss music missing");
        audio.SetDungeon(false);Require(music.clip.name=="town_variation","town music did not restore");
        Debug.Log("ORIGINAL_AUDIO_CHECKS_PASS: approved BGM and impact, short hurt voice, independent sources, one listener.");
    }
    static void VerifyForestEnemyAttack()
    {
        Call("SpawnEnemy","牛头兵",new Vector2(7,-2),100,10);FreezeEnemies();
        Call("SpawnEnemy","领主·牛头巨兽",new Vector2(7,-2),200,10);FreezeEnemies();
        var player=Field<GameObject>(game,"player");var saved=player.transform.position;
        foreach(var go in Field<List<GameObject>>(game,"enemies"))
        {
            var enemy=go.GetComponent<GameBootstrap.Enemy>();var renderer=go.GetComponent<SpriteRenderer>();
            Require(Field<SpriteRenderer>(enemy,"animatedRenderer")==renderer,"forest renderer not registered");
            var clip=Field<Sprite[]>(enemy,"singleAttackFrames");Require(clip!=null&&clip.Length==3,"small monster must have exactly one three-pose attack");
            var stateField=enemy.GetType().GetField("state",Private);
            Action<string> state=value=>stateField.SetValue(enemy,Enum.Parse(stateField.FieldType,value));
            Action tick=()=>enemy.GetType().GetMethod("Update",Private).Invoke(enemy,null);
            Set(enemy,"attackAt",Time.time+10);go.transform.position=saved+Vector3.right*4;tick();Require(renderer.flipX,"enemy on right must face left toward hero");
            go.transform.position=saved-Vector3.right*4;tick();Require(!renderer.flipX,"enemy on left must face right toward hero");
            var walk=Field<Sprite[]>(enemy,"walkFrames");Require(walk!=null&&walk.Length==4,"four redrawn walking poses missing");
            float stride=enemy.DisplayName=="猫妖"?.16f:enemy.DisplayName.StartsWith("领主")?.20f:.14f;
            for(int pose=0;pose<4;pose++){
                state("Chase");Set(enemy,"movement",Vector2.right);Set(enemy,"decisionAt",Time.time+10);Set(enemy,"knock",0f);
                go.transform.position=saved+Vector3.right*4;Set(enemy,"walkDistance",stride*(pose+.2f));tick();
                int actualPose=Mathf.FloorToInt(Field<float>(enemy,"walkDistance")/stride)%4;
                Require(renderer.sprite==walk[actualPose],"moving enemy retained idle/incorrect walk pose: "+enemy.DisplayName);
                var held=renderer.sprite;float distance=Field<float>(enemy,"walkDistance");
                Set(game,"hitStopUntil",Time.time+1);tick();Set(game,"hitStopUntil",0f);
                Require(renderer.sprite==held&&Field<float>(enemy,"walkDistance")==distance,"hit stop advanced monster feet");
            }
            state("Observe");Set(enemy,"decisionAt",Time.time+1);tick();
            Require(renderer.sprite==Field<Sprite>(enemy,"idleSprite"),"stationary enemy kept walking");
            state("Windup");Set(enemy,"attackFacing",-1);Set(enemy,"impactAt",Time.time+10);tick();Require(renderer.flipX&&renderer.sprite==clip[0],"windup flips toward crossing hero or wrong pose");
            state("Recover");Set(enemy,"recoverAt",Time.time+.48f);tick();Require(renderer.sprite==clip[1]&&renderer.flipX,"contact pose/facing mismatch");
            Set(enemy,"recoverAt",Time.time+.2f);tick();Require(renderer.sprite==clip[2],"single attack followthrough missing");
            enemy.Take(1);tick();Require(Field<float>(enemy,"impactAt")<0&&renderer.sprite==Field<Sprite>(enemy,"idleSprite"),"hurt did not cancel attack visuals/damage");
            state("Observe");Set(enemy,"decisionAt",Time.time+1);Set(enemy,"knock",0f);
            var standing=go.transform.position;tick();Require(go.transform.position==standing,"observing enemy kept chasing");
            float decision=Field<float>(enemy,"decisionAt"),attack=Field<float>(enemy,"attackAt");enemy.Pause(.1f);
            Require(Mathf.Abs(Field<float>(enemy,"decisionAt")-decision-.1f)<.001f&&Mathf.Abs(Field<float>(enemy,"attackAt")-attack-.1f)<.001f,"hit stop did not pause AI/cooldown clocks");
            if(enemy.DisplayName=="牛头兵"){
                var decide=enemy.GetType().GetMethod("Decide",Private);
                go.transform.position=player.transform.position+Vector3.right;Set(enemy,"attackAt",Time.time-1);
                decide.Invoke(enemy,new object[]{Time.time,-1f,0f});
                Require(Field<float>(enemy,"attackAt")>=Time.time+1.99f&&Field<float>(enemy,"impactAt")>Time.time,"tau ignored original 2000ms attack delay");
                state("Recover");Set(enemy,"recoverAt",Time.time-1);tick();
                Require(stateField.GetValue(enemy).ToString()=="Observe","attack recovery immediately restarted pursuit");
                decide.Invoke(enemy,new object[]{Time.time,-1f,0f});
                Require(stateField.GetValue(enemy).ToString()=="Observe","enemy attacked again during original cooldown");
                decide.Invoke(enemy,new object[]{Time.time,4f,1f});
                var heading=Field<Vector2>(enemy,"movement");player.transform.position=go.transform.position+new Vector3(-4,-1,0);tick();
                Require(Field<Vector2>(enemy,"movement")==heading,"approach retargeted every frame instead of completing decision segment");
                Set(enemy,"decisionAt",Time.time-1);tick();Require(stateField.GetValue(enemy).ToString()=="Observe","approach segment did not stop to observe");
                decide.Invoke(enemy,new object[]{Time.time,10f,0f});Require(stateField.GetValue(enemy).ToString()=="Observe","enemy chased outside sight");
                player.transform.position=saved;
            }
        }
        player.transform.position=saved;
        var art=Field<AdventureArt>(game,"art");var combat=Field<CombatController>(game,"combat");
        var victim=Field<List<GameObject>>(game,"enemies")[0];
        var skills=Field<List<SkillData>>(game,"skills");
        var impact=Field<AudioSource>(Field<AdventureAudio>(game,"gameAudio"),"impact");
        for(int stage=1;stage<=3;stage++){
            combat.Reset();impact.Stop();
            Require(combat.BeginBasic(Time.time,1,1,stage),"basic audio setup rejected");
            Call("HitNearest",new CombatController.Hit(1,2,.5f,victim.transform.position+Vector3.right*30,1));
            Require(!impact.isPlaying,"basic miss played hit sound");
            Call("HitNearest",new CombatController.Hit(1,2,.5f,victim.transform.position-Vector3.right,1));
            Require(impact.isPlaying,"basic contact missing hit sound: "+stage);
        }
        for(int i=0;i<skills.Count;i++){
            var skill=skills[i];if(!skill.DealsDamage)continue;
            combat.Reset();art.CancelEffects();Set(game,"hitStopUntil",0f);impact.Stop();
            Require(combat.BeginSkill(i,Time.time,12),"skill hit pause setup rejected: "+skill.Name);
            art.PlayAction(skill.Action,player.transform.position,0,skill.Recovery);
            float ending=Field<float>(art,"actionUntil"),recovery=combat.RemainingRecovery(Time.time);
            Call("HitNearest",new CombatController.Hit(1,2,.5f,victim.transform.position+Vector3.right*30,1));
            Require(!impact.isPlaying,"missed skill played hit sound: "+skill.Name);
            Require(Field<float>(game,"hitStopUntil")==0&&Field<float>(art,"actionUntil")==ending,"missed skill caused hit stop: "+skill.Name);
            Call("HitNearest",new CombatController.Hit(1,2,.5f,victim.transform.position-Vector3.right,1));
            Require(impact.isPlaying,"skill contact missing hit sound: "+skill.Name);
            Require(Mathf.Abs(Field<float>(game,"hitStopUntil")-Time.time-.09f)<.001f&&Mathf.Abs(Field<float>(art,"actionUntil")-ending-.09f)<.001f,"skill impact did not extend pose 90ms: "+skill.Name);
            Require(Mathf.Abs(combat.RemainingRecovery(Time.time)-recovery-.09f)<.001f,"skill combat clock diverged from animation: "+skill.Name);
        }
        combat.Reset();art.CancelEffects();Set(game,"hitStopUntil",0f);
        impact.Stop();
        Call("HitNearest",new CombatController.Hit(0,2,.5f,victim.transform.position-Vector3.right,1));
        Require(!impact.isPlaying,"zero damage played hit sound");
        Debug.Log("HIT_AUDIO_CHECKS_PASS: all three basic stages and every damaging skill play on contact; misses and zero damage are silent.");
        Debug.Log("SKILL_HIT_PAUSE_CHECKS_PASS: all damaging skills hold on contact for 90ms, miss without pause, extend combat and visual clocks together.");
        Call("SpawnForestRoom");FreezeEnemies();
        Debug.Log("MONSTER_WALK_CHECKS_PASS: four poses per species render on movement, idle on stop, attacks override walk, hit stop freezes feet.");
        Debug.Log("FOREST_ENEMY_CHECKS_PASS: all 3 small monsters face hero from either side, lock attack direction, render one windup/contact/recovery clip, cancel on hurt.");
        Debug.Log("FOREST_AI_CHECKS_PASS: observe pauses, finite approach direction, sight range, original tau cooldown, post-attack observation, paused AI clocks.");
    }
    static void VerifyGhostSlash()
    {
        var art=Field<AdventureArt>(game,"art");art.CancelEffects();
        art.PlayAction(HeroAction.GhostSlash,Vector3.zero,0,.58f);
        ArtEffect effect=null;
        foreach(var candidate in art.GetComponentsInChildren<ArtEffect>()){
            var clip=Field<Sprite[]>(candidate,"frames");if(clip!=null&&clip[0].texture.name=="GhostSlash_0")effect=candidate;
        }
        Require(effect!=null,"ghost slash reused aura or failed to spawn");
        var frames=Field<Sprite[]>(effect,"frames");Require(frames.Length==6,"ghost slash requires six independent frames");
        for(int i=0;i<6;i++)Require(frames[i].texture.name=="GhostSlash_"+i,"old ghost slash frame loaded");
        var renderer=effect.GetComponent<SpriteRenderer>();Require(Mathf.Abs(renderer.sprite.bounds.size.x*effect.transform.localScale.x-4.2f)<.001f,"ghost slash visual width not enlarged");Require(Mathf.Abs(effect.transform.position.x-1.55f*art.Facing)<.001f,"ghost slash visual center wrong");Require(!renderer.enabled,"ghost slash appeared before swing");
        Require(Mathf.Abs(Field<float>(effect,"started")-Time.time-.16f)<.001f,"ghost effect delay wrong");
        Set(effect,"started",Time.time-.141f);effect.GetType().GetMethod("Update",Private).Invoke(effect,null);
        Require(renderer.enabled&&renderer.sprite==frames[2],"ghost slash peak not synchronized with .30s hit");
        float start=Field<float>(effect,"started");effect.Pause(.09f);
        Require(Mathf.Abs(Field<float>(effect,"started")-start-.09f)<.001f,"ghost slash ignored hit pause");
        var held=renderer.sprite;effect.GetType().GetMethod("Update",Private).Invoke(effect,null);
        Require(renderer.sprite==held,"paused ghost slash advanced");art.CancelEffects();
        Debug.Log("GHOST_SLASH_VFX_CHECKS_PASS: dedicated six-frame crescent, delayed swing, contact peak, hit stop.");
    }
    static void VerifySwordSkills()
    {
        var art=Field<AdventureArt>(game,"art");
        var visual=Field<GameObject>(game,"player").transform.Find("Visual").GetComponent<SpriteRenderer>();
        foreach(var action in new[]{HeroAction.GhostSlash,HeroAction.UpperSlash})foreach(int facing in new[]{-1,1}){
            art.CancelEffects();art.Move(new Vector2(facing,0));
            bool upper=action==HeroAction.UpperSlash;float duration=upper?.42f:.58f;float impact=upper?.18f:.30f;
            art.PlayAction(action,Vector3.zero,0,duration);
            Require(visual.sprite.texture.name==(upper?"basic_3_00":"ghost_slash_00")&&visual.flipX==(facing<0),"skill began with old pose or wrong facing");
            Set(art,"actionStarted",Time.time-impact);Set(art,"actionUntil",Time.time+duration-impact);
            art.SetHitPause(.08f);
            Require(visual.sprite.texture.name==(upper?"basic_3_03":"ghost_slash_02"),"skill contact uses malformed early weapon frame");
            if(!upper){var clip=Field<Sprite[]>(art,"ghostSlashClip");Require(clip.Length==6,"ghost slash missing independent clip");for(int i=0;i<6;i++)Require(clip[i]!=null&&clip[i].texture.name=="ghost_slash_"+i.ToString("D2"),"ghost slash mixed with basic poses");}
            var held=visual.sprite;typeof(AdventureArt).GetMethod("LateUpdate",Private).Invoke(art,null);
            Require(visual.sprite==held,"skill contact pose advanced during hit stop");
            if(upper){
                ArtEffect effect=null;foreach(var e in art.GetComponentsInChildren<ArtEffect>())if(Field<Sprite[]>(e,"frames")[0].texture.name=="UpperSlash_0")effect=e;
                Require(effect!=null&&Field<Sprite[]>(effect,"frames").Length==6,"upper slash uses old Rise effect");
                Set(effect,"pausedUntil",0f);Set(effect,"started",Time.time-.12f);
                effect.GetType().GetMethod("Update",Private).Invoke(effect,null);
                Require(effect.GetComponent<SpriteRenderer>().sprite.texture.name=="UpperSlash_2","upper slash peak not at contact");
            }
        }
        art.CancelEffects();
        Debug.Log("SWORD_SKILL_CHECKS_PASS: consistent weapon poses in both directions; contact hit pause; independent UpperSlash trail.");
    }
    static void VerifyDungeonGates()
    {
        var saved=new Dictionary<string,int>();
        foreach(var name in new[]{"level","exp","gold","hp","mp","baseAttack","baseDefense","questKills","dungeonStage"})saved[name]=Field<int>(game,name);
        var player=Field<GameObject>(game,"player");
        for(int room=0;room<4;room++){
            var roomMusic=Field<AudioSource>(Field<AdventureAudio>(game,"gameAudio"),"music");Require(roomMusic.clip.name==(room==3?"boss_variation":"battle_variation"),"wrong BGM for dungeon room");
            FreezeEnemies();
            var gate=Field<GameObject>(game,"roomGate");
            Require(gate!=null&&gate.activeSelf&&gate.GetComponent<SpriteRenderer>().sprite.texture.name=="gate_closed","room must begin with closed gate");
            player.transform.position=new Vector3(8.3f,-2.1f,0);
            Call("MovePlayer",Vector2.zero);
            Require(player.transform.position.x<=7.65f,"closed gate allows passage");
            var enemies=Field<List<GameObject>>(game,"enemies");
            var victims=enemies.ToArray();
            for(int i=0;i<victims.Length;i++){
                victims[i].GetComponent<GameBootstrap.Enemy>().Take(99999);
                Require(Field<bool>(game,"roomCleared")== (i==victims.Length-1),"gate opened before final enemy or failed to open");
            }
            Require(Field<GameObject>(game,"roomGate")==gate&&gate.GetComponent<SpriteRenderer>().sprite.texture.name=="gate_open","gate state duplicated or open sprite missing");
            int index=Field<int>(game,"forestRoom");
            player.transform.position=new Vector3(8.3f,CombatPlane.Front,0);
            Call("MovePlayer",Vector2.zero);Call("HandleRoomExit");
            Require(Field<GameMode>(game,"mode")==GameMode.Dungeon&&Field<int>(game,"forestRoom")==index,"transition happened outside gate lane");
            player.transform.position=new Vector3(8.1f,-2.1f,0);
            Call("HandleRoomExit");
            if(room<3){
                Require(Field<GameMode>(game,"mode")==GameMode.Dungeon&&!Field<bool>(game,"roomCleared"),"next room not reset");
                Require(player.transform.position.x<0,"next room starts inside exit");
            }else {
                Require(Field<bool>(game,"dungeonResultOpen"),"boss clear menu missing");
                Call("ResolveDungeonResult",0);Require(Field<GameMode>(game,"mode")==GameMode.Dungeon&&!Field<bool>(game,"bossRoom")&&Field<int>(game,"forestRoom")==0&&!Field<bool>(game,"dungeonResultOpen"),"retry failed to reset dungeon");Require(roomMusic.clip.name=="battle_variation","retry retained boss BGM");
                Set(game,"bossRoom",true);Set(game,"roomCleared",true);Call("ShowDungeonResult");Call("ResolveDungeonResult",1);
                Require(Field<GameMode>(game,"mode")==GameMode.Town&&Field<bool>(game,"dungeonSelectionOpen"),"other dungeon selection missing");
                Set(game,"dungeonSelectionOpen",false);Call("EnterDungeon");Set(game,"bossRoom",true);Set(game,"roomCleared",true);Call("ShowDungeonResult");Call("ResolveDungeonResult",2);
                Require(Field<GameMode>(game,"mode")==GameMode.Town&&!gate.activeSelf,"return town failed to hide gate");Require(roomMusic.clip.name=="town_variation","return retained boss BGM");
            }
        }
        foreach(var entry in saved)Set(game,entry.Key,entry.Value);
        Call("EnterDungeon");FreezeEnemies();
        Debug.Log("DUNGEON_GATE_CHECKS_PASS: closed passage blocks, final kill opens same gate, aligned walking enters next room, all four rooms reset, town hides gate.");
    }
    static void VerifySkillTree()
    {
        var ranks=(int[])Field<int[]>(game,"skillRanks").Clone();var grants=(int[])Field<int[]>(game,"grantedSkillRanks").Clone();
        var slots=(int[])Field<int[]>(game,"hotbarSkills").Clone();var keys=(KeyCode[])Field<KeyCode[]>(game,"hotbarKeys").Clone();
        int oldLevel=Field<int>(game,"level");var skills=Field<List<SkillData>>(game,"skills");
        Require(ranks[0]==1&&ranks[1]==1&&ranks[2]==0,"original starter skills / unlearned skill state missing");
        Require(Resources.Load<Texture2D>("DNF60/UI/skill_tree_frame")!=null,"redrawn skill tree window missing");
        Call("ToggleSkillTree");Require(Field<bool>(game,"skillTreeOpen"),"K action failed to open skill tree");
        Require(!(bool)typeof(GameBootstrap).GetMethod("QueueSkillLevel",Private).Invoke(game,new object[]{0}),"level gate ignored");
        Set(game,"level",5);
        Require((bool)typeof(GameBootstrap).GetMethod("QueueSkillLevel",Private).Invoke(game,new object[]{2}),"eligible Khazan learning rejected");
        Require(Field<int[]>(game,"skillRanks")[2]==0,"unconfirmed learning already took effect");
        Require((bool)typeof(GameBootstrap).GetMethod("ConfirmSkillLearning",Private).Invoke(game,null),"learning confirmation rejected");
        Require(Field<int[]>(game,"skillRanks")[2]==1,"learned rank missing");
        Require((int)typeof(GameBootstrap).GetProperty("AvailableSkillPoints",Private).GetValue(game)==175,"SP cost or original SP gains incorrect");
        Require(!(bool)typeof(GameBootstrap).GetMethod("QueueSkillLevel",Private).Invoke(game,new object[]{2}),"next rank level gate ignored");
        Call("EnterDungeon");FreezeEnemies();int mana=Field<int>(game,"mp");Call("Cast",skills[1]);Require(Field<int>(game,"mp")==mana,"skill cast while tree open");
        Call("CloseSkillTree");Call("Cast",skills[3]);Require(Field<int>(game,"mp")==mana,"unlearned wave sword cast");
        Require(!(bool)typeof(GameBootstrap).GetMethod("AssignSkillSlot",Private).Invoke(game,new object[]{3,0,-1}),"unlearned skill assigned");
        Require((bool)typeof(GameBootstrap).GetMethod("AssignSkillSlot",Private).Invoke(game,new object[]{2,6,7}),"skill slot swap rejected");
        Require(Field<int[]>(game,"hotbarSkills")[6]==2&&Field<int[]>(game,"hotbarSkills")[7]==1&&skills[2].Key==KeyCode.A,"slot swap not reflected in combat binding");
        Require(!(bool)typeof(GameBootstrap).GetMethod("RebindSkillSlot",Private).Invoke(game,new object[]{1,KeyCode.K}),"system K key overwritten");
        Call("ClearSkillSlot",6);Require(!skills[2].CommandBound&&!Field<UnityEngine.UI.Image[]>(game,"skillIcons")[6].enabled,"cleared slot still casts or displays icon");
        Require(Field<UnityEngine.UI.Image[]>(game,"skillIcons")[6].sprite==null&&Field<UnityEngine.UI.Text[]>(game,"skillKeyLabels")[6].text==""&&Field<UnityEngine.UI.Text[]>(game,"skillCooldownLabels")[6].text=="","cleared slot retains icon/key/countdown");
        var saveType=typeof(GameBootstrap).GetNestedType("SaveData",BindingFlags.NonPublic);var save=Activator.CreateInstance(saveType,true);
        Call("WriteSkillTreeSave",save);string serialized=Newtonsoft.Json.JsonConvert.SerializeObject(save);
        Field<int[]>(game,"skillRanks")[2]=0;Call("RestoreSkillTree",Newtonsoft.Json.JsonConvert.DeserializeObject(serialized,saveType));
        Require(Field<int[]>(game,"skillRanks")[2]==1&&Field<int[]>(game,"hotbarSkills")[6]==-1,"skill ranks/bindings failed save roundtrip");
        saveType.GetField("Version").SetValue(save,2);
        saveType.GetField("SkillSlots").SetValue(save,new[]{"UpperSlash","GhostSlash","Khazan","WaveSword","Guard","Backstep","Thrust","WaveWheel","TripleSlash","GoreCross","AshenFork","HopSmash"});
        Call("RestoreSkillTree",save);
        Require(Array.IndexOf(Field<int[]>(game,"hotbarSkills"),0)<0&&Field<int[]>(game,"hotbarSkills")[6]==1&&skills[0].Key==KeyCode.Z&&skills[0].CommandBound,"legacy layout restored upper/backstep or wrong rows");
        Require(Field<KeyCode[]>(game,"hotbarKeys")[11]==KeyCode.H,"legacy layout lost H skill slot");
        Call("ToggleSkillTree");Set(game,"level",1);Require(!(bool)typeof(GameBootstrap).GetMethod("QueueSkillLevel",Private).Invoke(game,new object[]{1}),"level requirement ignored");Call("CloseSkillTree");
        Set(game,"level",oldLevel);Set(game,"skillRanks",ranks);Set(game,"grantedSkillRanks",grants);Set(game,"hotbarSkills",slots);Set(game,"hotbarKeys",keys);Call("ApplySkillRanks");Call("EnterTown");
        Debug.Log("SKILL_TREE_CHECKS_PASS: original starter grants, SP table/costs, level gates, confirmation, blocked menu/unlearned casts, slot swaps/removal, reserved K key, save roundtrip.");
    }
    static void VerifySkillBar()
    {
        var icons=Field<UnityEngine.UI.Image[]>(game,"skillIcons");
        var masks=Field<UnityEngine.UI.Image[]>(game,"skillCooldownMasks");
        var skills=Field<List<SkillData>>(game,"skills");
        Require(icons.Length==12&&masks.Length==12,"two by six skill bar incomplete");
        Canvas.ForceUpdateCanvases();
        for(int i=0;i<12;i++){
            int mapped=Field<int[]>(game,"hotbarSkills")[i];
            Require(mapped!=0,"Z occupies hotbar");
            bool filled=mapped>=0&&Field<int[]>(game,"skillRanks")[mapped]>0;
            Require(icons[i].enabled==filled,"empty/filled hotbar state wrong");
            if(filled)Require(icons[i].sprite!=null&&icons[i].sprite.texture.name==skills[mapped].Action.ToString(),"wrong original-inspired skill icon");
            else Require(icons[i].sprite==null&&Field<UnityEngine.UI.Text[]>(game,"skillCooldownLabels")[i].text=="","empty or unlearned slot retains icon/countdown");
            var slot=(RectTransform)icons[i].transform.parent;
            Require(Mathf.Abs(slot.anchorMin.x-(i%6)/6f)<.001f&&Mathf.Abs(slot.anchorMax.x-(i%6+1)/6f)<.001f,"skill column not aligned to painted frame");
            Require(Mathf.Abs(slot.anchoredPosition.y-(272f-(i<6?153f:198f))*210f/272f)<.01f,"skill row not aligned to painted frame");
            string key=new[]{"Q","W","E","R","T","Y","A","S","D","F","G","H"}[i];
            Require(Field<KeyCode[]>(game,"hotbarKeys")[i].ToString()==key,"underlying row key layout wrong");
            Require(Field<UnityEngine.UI.Text[]>(game,"skillKeyLabels")[i].text==(filled?key:""),"empty slot key label visible or filled key wrong");
            Require(masks[i].fillAmount==0,"unused legacy cooldown cover visible");
        }
        Require(skills[1].Action==HeroAction.GhostSlash&&skills[1].Key==KeyCode.A&&Field<int[]>(game,"hotbarSkills")[6]==1,"A ghost slash not bound to displayed slot");
        Require(skills[0].Key==KeyCode.Z&&skills[0].CommandBound&&!skills.Exists(x=>x.Name=="后跳"),"independent upper / backstep removal wrong");
        Require(!(bool)typeof(GameBootstrap).GetMethod("AssignSkillSlot",Private).Invoke(game,new object[]{0,10,-1}),"upper assigned into hotbar");
        Require(!(bool)typeof(GameBootstrap).GetMethod("RebindSkillSlot",Private).Invoke(game,new object[]{10,KeyCode.Z}),"Z binding overwritten");
        int mana=Field<int>(game,"mp");
        Call("Cast",skills[1]);Call("Refresh");
        Require(icons[6].color.r<.5f&&masks[6].fillAmount==0&&Field<UnityEngine.UI.Text[]>(game,"skillCooldownLabels")[6].text!="","ghost slash gray icon / small countdown missing from A slot");
        Field<CombatController>(game,"combat").Reset();Field<AdventureArt>(game,"art").CancelEffects();
        skills[1].ReadyAt=0;Set(game,"mp",mana);Call("Refresh");
        Require(masks[1].fillAmount==0,"ready skill retains cooldown cover");
        Debug.Log("SKILL_BAR_CHECKS_PASS: twelve painted slots and independent Z, two rows aligned to painted sockets, unlearned/empty slots blank, A ghost slash and cooldown use same slot.");
    }
    static void Step()
    {
        if(finished)return;
        try
        {
            if(EditorApplication.timeSinceStartup>timeout)throw new Exception("Play verification timed out");
            if(!EditorApplication.isPlaying || Time.time < .2f)return;
            if(phase==16)foreach(var entry in observedAttacks){var sprite=entry.Key.GetComponent<SpriteRenderer>().sprite;if(sprite!=null&&sprite.texture.name.Contains("_attack_"))entry.Value.Add(sprite);}
            if(Time.time<deadline)return;
            switch(phase)
            {
                case 0:
                    game=UnityEngine.Object.FindObjectOfType<GameBootstrap>();
                    Require(game!=null,"Main scene has no bootstrap");
                    Require(!Field<bool>(game,"persistenceEnabled"),"test would write player save");
                    Require(Field<GameMode>(game,"mode")==GameMode.Town,"startup mode is not town");
                    Require(Field<List<GameObject>>(game,"enemies").Count==0,"town contains enemies");
                    Require(!Field<GameObject>(game,"combatHud").activeSelf,"town combat HUD visible");
                    VerifySkillTree();
                    VerifyDungeonRun();VerifyDashAttack();Call("EnterTown");VerifyHeroClips();
                    VerifyAudio();
                    Call("BasicAttack");
                    Require(!Field<CombatController>(game,"combat").Active,"town attack accepted");
                    Call("EnterDungeon"); FreezeEnemies();VerifyForestEnemyAttack();VerifyGhostSlash();VerifySwordSkills();VerifyDungeonGates();VerifySkillBar();
                    Require(Field<AudioSource>(Field<AdventureAudio>(game,"gameAudio"),"music").clip.name=="battle_variation","dungeon entry did not change BGM");
                    Require(Field<List<GameObject>>(game,"enemies").Count==2,"dungeon enemy count");
                    Require(Field<GameObject>(game,"combatHud").activeSelf,"dungeon HUD missing");
                    var player=Field<GameObject>(game,"player");
                    target=Field<List<GameObject>>(game,"enemies")[0].GetComponent<GameBootstrap.Enemy>();
                    target.transform.position=player.transform.position+Vector3.right;
                    originalHp=Field<int>(target,"hp");
                    Call("BasicAttack"); Call("BasicAttack");
                    Require(Field<CombatController>(game,"combat").Active,"dungeon attack rejected");
                    phase=1;deadline=Time.time+.28f;break;
                case 1:
                    Require(originalHp-Field<int>(target,"hp")==17,"basic attack should deal 17 exactly once with starter weapon");
                    phase=2;deadline=Time.time+.3f;break;
                case 2:
                    Require(originalHp-Field<int>(target,"hp")==17,"basic dealt delayed duplicate damage");
                    Call("EnterTown");
                    Require(Field<AudioSource>(Field<AdventureAudio>(game,"gameAudio"),"music").clip.name=="town_variation","town return did not change BGM");
                    Call("EnterDungeon"); FreezeEnemies();
                    Set(game,"level",10); Set(game,"mp",100);
                    var skills=Field<List<SkillData>>(game,"skills");
                    Require(skills.Count==11,"level-15 combat skill bar is incomplete");
                    Field<int[]>(game,"skillRanks")[9]=1;Call("ApplySkillRanks");
                    var falling=skills.Find(x=>x.Name=="银光落刃");
                    Require(falling!=null&&falling.AirOnly&&falling.WaitForLanding,"silver falling blade rules missing");
                    Call("Cast",falling);
                    Require(Field<int>(game,"mp")==100,"grounded air skill charged mana");
                    Field<JumpState>(game,"jump").Start();
                    Call("Cast",falling);
                    Require(Field<int>(game,"mp")==85,"air skill mana charge");
                    Call("Cast",falling);
                    Require(Field<int>(game,"mp")==85,"rejected cast charged mana twice");
                    phase=3;deadline=Time.time+.15f;break;
                case 3:
                    var jump=Field<JumpState>(game,"jump");
                    var visual=Field<GameObject>(game,"player").transform.Find("Visual");
                    Require(jump.Airborne && visual!=null && visual.localPosition.y>.1f,"jump sprite height overwritten");
                    Require(Field<CombatController>(game,"combat").Active,"air skill lost pending landing hit");
                    Call("EnterTown");
                    Require(!Field<CombatController>(game,"combat").Active,"town transition did not reset combat");
                    foreach(var effect in UnityEngine.Object.FindObjectsOfType<ArtEffect>()) Require(!effect.gameObject.activeInHierarchy,"stale effect after transition");
                    Call("EnterDungeon");FreezeEnemies();
                    var fresh=Field<List<GameObject>>(game,"enemies")[0].GetComponent<GameBootstrap.Enemy>();
                    target=fresh;originalHp=Field<int>(target,"hp");
                    phase=4;deadline=Time.time+.8f;break;
                case 4:
                    Require(Field<int>(target,"hp")==originalHp,"cancelled hit damaged new room");
                    Set(game,"forestRoom",2);Set(game,"roomCleared",true);Call("EnterBossRoom");FreezeEnemies();
                    Require(Field<GameObject>(game,"player").transform.position.x<0,"boss room entered at exit gate");
                    Require(Field<List<GameObject>>(game,"enemies").Count==1,"boss spawn count");
                    Call("TakePlayerDamage",99999);
                    Require(Field<GameMode>(game,"mode")==GameMode.Town,"death failed to return to town");
                    Require(Field<string>(game,"systemMessage").Contains("挑战失败"),"death message overwritten");
                    phase=5;deadline=Time.time+.3f;break;
                case 5:
                    Require(UnityEngine.Object.FindObjectsOfType<GameBootstrap.Enemy>().Length==0,"enemy survived town transition");
                    Require(UnityEngine.Object.FindObjectsOfType<ArtEffect>().Length==0,"effect survived town transition");
                    Call("EnterDungeon");FreezeEnemies();
                    foreach(var enemy in Field<List<GameObject>>(game,"enemies"))enemy.transform.position=Vector3.right*8;
                    Call("BasicAttack");phase=6;deadline=Time.time+.45f;break;
                case 6:
                    Call("BasicAttack");
                    Require(Field<bool>(game,"basicQueued"),"late X input was discarded");
                    phase=7;deadline=Time.time+.20f;break;
                case 7:
                    Require(Field<int>(game,"comboStep")==2,"buffered X did not chain second swing");
                    Require(Field<GameObject>(game,"player").transform.Find("Visual").GetComponent<SpriteRenderer>().sprite.texture.name.StartsWith("basic_2_"),"second swing uses stale art");
                    phase=8;deadline=Time.time+.45f;break;
                case 8:
                    Call("BasicAttack");Require(Field<bool>(game,"basicQueued"),"second swing did not queue third");
                    phase=9;deadline=Time.time+.20f;break;
                case 9:
                    Require(Field<int>(game,"comboStep")==3,"buffered X did not chain uppercut");
                    Require(Field<GameObject>(game,"player").transform.Find("Visual").GetComponent<SpriteRenderer>().sprite.texture.name.StartsWith("basic_3_"),"third swing uses stale art");
                    Call("EnterTown");Require(!Field<bool>(game,"basicQueued"),"queued X survived room transition");
                    Call("ToggleSkillTree");
                    phase=10;deadline=Time.time+.3f;break;
                case 10:
                    ScreenCapture.CaptureScreenshot("C:/Users/Administrator/Documents/Codex/2026-09-29/new-chat/outputs/skill-tree/play-preview.png");phase=11;deadline=Time.time+.5f;break;
                case 11:
                    Call("CloseSkillTree");Call("EnterDungeon");FreezeEnemies();
                    Set(game,"mp",0);Set(game,"manaRegenFraction",0f);Call("RecoverMana",60f);
                    Require(Field<int>(game,"mp")>0,"MP regeneration missing");
                    Set(game,"mp",100);repeatMana=100;
                    var repeatSkill=Field<List<SkillData>>(game,"skills")[1];
                    Require(Time.time>=repeatSkill.ReadyAt,"repeat cast setup still cooling down");
                    Call("Cast",repeatSkill);
                    Require(Field<int>(game,"mp")==repeatMana-repeatSkill.Mana,"first repeat cast rejected");
                    phase=12;deadline=Time.time+repeatSkill.Cooldown+.15f;break;
                case 12:
                    var repeat=Field<List<SkillData>>(game,"skills")[1];repeatMana=Field<int>(game,"mp");
                    Require(!Field<CombatController>(game,"combat").Active,"skill stayed active after cooldown");
                    Call("Cast",repeat);Require(Field<int>(game,"mp")==repeatMana-repeat.Mana,"second natural-cooldown cast rejected");
                    phase=13;deadline=Time.time+repeat.Cooldown+.15f;break;
                case 13:
                    var third=Field<List<SkillData>>(game,"skills")[1];repeatMana=Field<int>(game,"mp");
                    Call("Cast",third);Require(Field<int>(game,"mp")==repeatMana-third.Mana,"third natural-cooldown cast rejected");
                    phase=14;deadline=Time.time+.48f;break;
                case 14:
                    Set(game,"hitStopUntil",Time.time+.09f);
                    Call("RequestSkill",Field<List<SkillData>>(game,"skills")[0]);
                    Require(Field<SkillData>(game,"queuedSkill")!=null,"hit stop discarded skill input");
                    phase=15;deadline=Time.time+.24f;break;
                case 15:
                    Require(Field<List<SkillData>>(game,"skills")[0].ReadyAt>Time.time,"buffered upper slash did not fire after recovery");
                    Call("EnterTown");Require(Field<SkillData>(game,"queuedSkill")==null,"queued skill survived transition");
                    Debug.Log("REPEAT_SKILL_CHECKS_PASS: ghost slash casts three times across real cooldowns; MP regenerates; hit-stop input survives recovery.");
                    Call("EnterDungeon");Call("ClearEnemies");Set(game,"level",25);Set(game,"baseDefense",10000);Set(game,"hp",400);originalHp=400;
                    UnityEngine.Random.InitState(1310);observedAttacks.Clear();
                    foreach(var name in new[]{"哥布林","猫妖","牛头兵","领主·牛头巨兽"}){
                        Call("SpawnEnemy",name,(Vector2)Field<GameObject>(game,"player").transform.position+Vector2.right*.8f,500,12);
                        var spawned=Field<List<GameObject>>(game,"enemies");observedAttacks[spawned[spawned.Count-1].GetComponent<GameBootstrap.Enemy>()]=new HashSet<Sprite>();
                    }
                    phase=16;deadline=Time.time+8f;break;
                case 16:
                    foreach(var entry in observedAttacks)Require(entry.Value.Count==3,"natural AI failed to show all attack poses: "+entry.Key.DisplayName);
                    Require(Field<int>(game,"hp")<originalHp,"natural enemy attacks never hit");
                    Debug.Log("NATURAL_ENEMY_ATTACK_CHECKS_PASS: goblin, cat, taurus and boss independently enter windup/contact/followthrough and deal damage in live Play.");
                    Finish(true,"skill tree, SP learning, save migration, key assignment, repeated skills, MP regeneration, cooldown gray/countdown, startup, combo, hit stop, air skills, transitions, gates, death return");break;
            }
        }
        catch(Exception e){Finish(false,e.ToString());}
    }
    static void Finish(bool success,string detail)
    {
        if(finished)return;
        finished=true;
        SessionState.SetBool(Running,false);
        EditorApplication.update-=Step;
        Application.logMessageReceived-=OnLog;
        if(success)Debug.Log("COMBAT_PLAY_CHECKS_PASS: "+detail);
        else Debug.LogError("COMBAT_PLAY_CHECKS_FAIL: "+detail);
        EditorApplication.Exit(success?0:1);
    }
}

