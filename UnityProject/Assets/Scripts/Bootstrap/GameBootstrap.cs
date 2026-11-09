using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DNF60Prototype
{
    public enum GameMode { Town, Dungeon }
    public enum WorldRegion { HuttonMar, WestCoast, SeriaRoom, GrandFlores, SkyTower }
    public enum HeroAction { Basic, Basic2, Basic3, UpperSlash, GhostSlash, Khazan, WaveSword, Guard, Thrust=9, WaveWheel, TripleSlash, GoreCross, AshenFork, HopSmash, Hurt, AirBasic, DashAttack }
    [Serializable] public class SkillData {
        public string Name; public KeyCode Key; public int Level; public int Mana; public float Cooldown; public float Damage; public HeroAction Action;
        public bool AirOnly,StartsJump,WaitForLanding,DealsDamage=true,CommandBound; public float Travel,Recovery=.42f;
        public int Hits=1; public float HitInterval=.12f,HitOffset=.18f,Reach=2.8f,Depth=.65f; [NonSerialized] public float ReadyAt;
        public SkillData(string n,KeyCode k,int l,int m,float c,float d,int hits=1,float interval=.12f,float offset=.18f,float reach=2.8f,float depth=.65f){Name=n;Key=k;Level=l;Mana=m;Cooldown=c;Damage=d;Hits=hits;HitInterval=interval;HitOffset=offset;Reach=reach;Depth=depth;}
    }
    [DisallowMultipleComponent]
    public sealed partial class GameBootstrap : MonoBehaviour
    {
        AdventureAudio gameAudio;
        readonly DungeonRunInput runInput=new DungeonRunInput();
        SkillData queuedSkill;float queuedSkillUntil,manaRegenFraction;
        int MaxMana=>140+(level-1)*10;
        // Recovery is independent of town/dungeon, attacks, hit stop and recent casting.
        float healthRegenFraction,manaRegenPerSecond;
        void RecoverHealth(float seconds)
        {
            if(seconds<=0||hp<=0)return;
            int maxHp=100+level*12;
            if(hp>=maxHp){hp=maxHp;healthRegenFraction=0;return;}
            healthRegenFraction+=seconds*maxHp*.001f;
            int gained=Mathf.FloorToInt(healthRegenFraction);
            hp=Mathf.Min(maxHp,hp+gained);healthRegenFraction-=gained;
        }
        void RecoverMana(float seconds)
        {
            if(seconds<=0||hp<=0)return;
            if(mp>=MaxMana){mp=MaxMana;manaRegenFraction=0;return;}
            manaRegenFraction+=seconds*manaRegenPerSecond;
            int gained=Mathf.FloorToInt(manaRegenFraction);
            mp=Mathf.Min(MaxMana,mp+gained);manaRegenFraction-=gained;
        }
        void UpdateResourceRecovery(){manaRegenPerSecond=EightSkillRotation.Simulate(skills).ManaPerSecond/1.3f;}
        void ReadSkillInput(){foreach(var skill in skills)if(skill.CommandBound&&Input.GetKeyDown(skill.Key)){RequestSkill(skill);break;}}
        void RequestSkill(SkillData skill){if(mode!=GameMode.Dungeon||SystemPanelOpen||skill==null)return;if(Time.time<hitStopUntil||combat!=null&&combat.Active&&combat.RemainingRecovery(Time.time)<=.20f){queuedSkill=skill;queuedSkillUntil=Mathf.Max(Time.time,hitStopUntil)+.24f;return;}Cast(skill);}
        void ConsumeQueuedSkill(){if(queuedSkill==null)return;if(SystemPanelOpen||Time.time>queuedSkillUntil){queuedSkill=null;return;}if(Time.time<hitStopUntil||combat!=null&&combat.Active)return;var skill=queuedSkill;queuedSkill=null;Cast(skill);}
        int forestRoom;
        const float GateX=8f,GateY=-2.1f,GateHalfLane=.55f,GateBarrierX=7.65f;
        readonly JumpState jump=new JumpState(); AdventureArt art; CombatController combat; const int MaxLevel=25; GameObject player; Sprite[] goblinFrames; readonly List<GameObject> enemies=new List<GameObject>(); readonly List<NpcInteraction> npcs=new List<NpcInteraction>(); readonly List<ItemData> inventory=new List<ItemData>(); readonly Dictionary<ItemSlot,ItemData> equipped=new Dictionary<ItemSlot,ItemData>(); readonly List<SkillData> skills=new List<SkillData>(); int comboStep; float lastComboAt,hitStopUntil,basicQueuedUntil; bool basicQueued;
        Text status,quest,stats,inventoryText,skillText,jobText,modeText,hpValue,mpValue; Image hpBar,mpBar,expBar; Image[] skillIcons; Image[] skillCooldownMasks; Text[] skillCooldownLabels; GameObject inventoryPanel,jobPanel,roomGate,roomLabel,combatHud; int level=1,exp,gold=500,hp=100,mp=140,baseAttack=12,baseDefense=4,questKills,potions=3,dungeonStage; int bagPage; ItemData selectedItem; bool bossRoom,roomCleared; float guardUntil,khazanUntil; Job job=Job.GhostKnight; GameMode mode=GameMode.Town; WorldRegion region=WorldRegion.HuttonMar; readonly Color panelColor=new Color(.04f,.06f,.10f,.95f);
        void Start(){
#if UNITY_EDITOR
            persistenceEnabled = !Array.Exists(Environment.GetCommandLineArgs(), value => value == "-dnfCombatVerification");
#endif
            var all=FindObjectsOfType<GameBootstrap>();
            if(all.Length>1&&all[0]!=this){Destroy(gameObject);return;}
            CleanupGeneratedObjects(); art=gameObject.AddComponent<AdventureArt>(); Debug.Log("DNF60 prototype started");Application.targetFrameRate=60;BuildScene();gameAudio=gameObject.AddComponent<AdventureAudio>();gameAudio.SetDungeon(false);ConfigureSkillTiming();combat=new CombatController(()=>player.transform.position,()=>art.Facing,HitNearest,(index)=>index>=0&&index<skills.Count?skills[index]:null);BuildTownNpcs();InitializeSystems();InitializeSkillTree();Load();EnsureStarterLoadout();Refresh();
        }
        void ConfigureSkillTiming(){ }
        void Update()
        {
            if (player == null) return;
            UpdateRunInput();
            RecoverHealth(Time.deltaTime);RecoverMana(Time.deltaTime);
            if (Time.time < hitStopUntil) { if(Input.GetKeyDown(KeyCode.X)&&!SystemPanelOpen)BasicAttack();ReadSkillInput();RefreshSkillSlots();return; }
            if (skillText != null && skillText.gameObject.activeSelf) skillText.gameObject.SetActive(false);
            MoveDashAttack();
            jump.Step(Time.deltaTime);
            art.SetJumpHeight(jump.Height);
            combat?.Tick(Time.time, jump.Airborne);
            if(Time.time<hitStopUntil){if(Input.GetKeyDown(KeyCode.X)&&!SystemPanelOpen)BasicAttack();ReadSkillInput();return;}
            HandlePanels();
            if (SystemPanelOpen)
            {
                basicQueued = false;
                queuedSkill=null;
                runInput.Reset();art.SetRunning(false);
                art.Move(Vector2.zero);
                Refresh();
                return;
            }
            ConsumeQueuedSkill();
            UpdateForestStatus(Time.deltaTime);
            if(basicQueued && combat != null && !combat.Active){bool valid=Time.time<=basicQueuedUntil;basicQueued=false;if(valid)BasicAttack();}
            var input = ReadMovementInput();
            MovePlayer(input);
            HandleRoomExit();
            HandleJumpAndCombat(input);
            HandleUtilityKeys();
            Refresh();
        }

        void HandlePanels()
        {
            if(dungeonResultOpen||dungeonSelectionOpen||captureSlot>=0)return;
            if(Input.GetKeyDown(KeyCode.K))ToggleSkillTree();
            if (Input.GetKeyDown(KeyCode.I)) { CloseSkillTree();inventoryOpen = !inventoryOpen; questsOpen = false; }
            if (Input.GetKeyDown(KeyCode.F1)) { CloseSkillTree();questsOpen = !questsOpen; inventoryOpen = false; activeNpc = ""; }
            if (Input.GetKeyDown(KeyCode.Escape)) { CloseSkillTree();inventoryOpen = false; questsOpen = false; jobPanel.SetActive(false); activeNpc = ""; }
            if (Input.GetKeyDown(KeyCode.Space)&&!SystemPanelOpen) TryInteract();
        }

        void UpdateRunInput()
        {
            int held=0,pressed=0;
            KeyCode[] keys={KeyCode.RightArrow,KeyCode.LeftArrow,KeyCode.UpArrow,KeyCode.DownArrow};
            for(int i=0;i<keys.Length;i++){
                if(Input.GetKey(keys[i]))held|=1<<i;
                if(Input.GetKeyDown(keys[i]))pressed|=1<<i;
            }
            runInput.Step(Time.unscaledTime,held,pressed,mode==GameMode.Dungeon&&!SystemPanelOpen);
            art.SetRunning(mode==GameMode.Dungeon&&runInput.Running);
        }

        Vector2 ReadMovementInput()
        {
            float x=(Input.GetKey(KeyCode.RightArrow)?1:0)-(Input.GetKey(KeyCode.LeftArrow)?1:0);
            float y=(Input.GetKey(KeyCode.UpArrow)?1:0)-(Input.GetKey(KeyCode.DownArrow)?1:0);
            return new Vector2(x,y);
        }

        void MoveDashAttack(){
            if(combat==null||!combat.Active||!combat.DashAttack||Time.time<hitStopUntil||SystemPanelOpen)return;
            float travel=combat.ConsumeDashTravel(Time.time);
            var next=CombatPlane.Clamp(player.transform.position+Vector3.right*(combat.DashFacing*travel));
            next=ClampDungeonPassage(next);
            player.transform.position=next;
        }
        void MovePlayer(Vector2 input)
        {
            if (combat != null && combat.Active) { MoveDashAttack();art.Move(Vector2.zero); return; }
            art.Move(input);
            if(mode==GameMode.Town && region==WorldRegion.HuttonMar && input.x<0 && player.transform.position.x<=-8.25f){player.transform.position=new Vector3(-8.1f,player.transform.position.y,0);ShowDungeonSelection("格兰之森",new[]{"幽暗密林","雷鸣废墟","格拉卡","烈焰格拉卡","冰霜幽暗密林","猛毒雷鸣废墟","暗黑雷鸣废墟"});return;}
            if(mode==GameMode.Town && region==WorldRegion.HuttonMar && input.x>0 && player.transform.position.x>=8.25f){region=WorldRegion.WestCoast;player.transform.position=new Vector3(-8.2f,player.transform.position.y,0);art.SetTown(false);art.SetWestCoast(true);systemMessage="进入西海岸。";return;}
            if(mode==GameMode.Town && region==WorldRegion.WestCoast && input.x<0 && player.transform.position.x<=-8.25f){region=WorldRegion.HuttonMar;player.transform.position=new Vector3(8.2f,player.transform.position.y,0);art.SetWestCoast(false);art.SetTown(true);systemMessage="返回赫顿玛尔。";return;}
            var strideInput = input.sqrMagnitude > .01f ? input.normalized : Vector2.zero;
            var rawNext = player.transform.position + CombatPlane.Velocity(strideInput, mode==GameMode.Dungeon?runInput.Speed*(Time.time<forestSlowUntil?.65f:1f):5f) * Time.deltaTime;
            var next = CombatPlane.Clamp(rawNext);
            // CombatPlane clamps town positions to +/-8.5, so test the transition
            // at that actual playable edge before applying the clamp.
            if(mode==GameMode.Town && region==WorldRegion.HuttonMar && rawNext.x<=-8.35f){player.transform.position=new Vector3(-8.1f,player.transform.position.y,0);ShowDungeonSelection("格兰之森",new[]{"幽暗密林","雷鸣废墟","格拉卡","烈焰格拉卡","冰霜幽暗密林","猛毒雷鸣废墟","暗黑雷鸣废墟"});return;}
            if(mode==GameMode.Town && region==WorldRegion.HuttonMar && rawNext.x>=8.35f){region=WorldRegion.WestCoast;next.x=-8.2f;art.SetTown(false);art.SetWestCoast(true);systemMessage="进入西海岸。";}
            else if(mode==GameMode.Town && region==WorldRegion.WestCoast && rawNext.x<=-8.35f){region=WorldRegion.HuttonMar;next.x=8.2f;art.SetWestCoast(false);art.SetTown(true);systemMessage="返回赫顿玛尔。";}
            next=ClampDungeonPassage(next);
            player.transform.position = next;
        }

        void HandleRoomExit()
        {
            if(mode==GameMode.Dungeon&&activeForest!=null){HandleForestRoomExit();return;}
            if (mode != GameMode.Dungeon || !roomCleared || jump.Airborne ||
                player.transform.position.x <= GateX || Mathf.Abs(player.transform.position.y-GateY)>GateHalfLane) return;
            if (bossRoom) ShowDungeonResult();
            else EnterBossRoom();
        }

        void HandleJumpAndCombat(Vector2 input)
        {
            if (Input.GetKeyDown(KeyCode.C) && (combat == null || !combat.Active)) jump.Start();
            if (Input.GetKeyDown(KeyCode.X)) BasicAttack();
            ReadSkillInput();
        }

        void HandleUtilityKeys()
        {
            if (Input.GetKeyDown(KeyCode.P)) jobPanel.SetActive(!jobPanel.activeSelf);
            if (Input.GetKeyDown(KeyCode.B)) BuyPotion();
            if (Input.GetKeyDown(KeyCode.Alpha1)) UsePotion();
            if (Input.GetKeyDown(KeyCode.F5)) Save();
        }
        void CleanupGeneratedObjects(){var roots=FindObjectsOfType<GameObject>();foreach(var go in roots){if(go==gameObject||go.name=="Main Camera")continue;if(go.name.StartsWith("Label_")||go.name=="TownArt"||go.name=="SkillEffect"||go.name=="Ground"||go.name=="Skyline"||go.name=="DungeonGate"||go.name=="DungeonGateLabel"||go.name=="Player"||go.name=="哥布林"||go.name=="牛头兵"||go.name=="猫妖"||go.name=="领主·牛头巨兽"||go.name=="领主·牛头王"||go.name=="UI")Destroy(go);}}
        void BuildTownNpcs(){npcs.Clear();}
        void CreateNpc(string id,Vector2 position,string label){var go=new GameObject("NPC_"+id);go.transform.position=position;var sr=go.AddComponent<SpriteRenderer>();string artId=id=="wind"?"gsd":id=="linus"?"kiri":id;sr.sprite=art.LoadNpc(artId);if(sr.sprite!=null){sr.sortingOrder=5;go.transform.localScale=Vector3.one*1.15f;}var npc=go.AddComponent<NpcInteraction>();npc.Setup(this,id,label);npcs.Add(npc);}
        bool TryInteract(){if(mode!=GameMode.Town)return false;NpcInteraction nearest=null;float distance=.95f;foreach(var npc in npcs){if(npc==null||!npc.gameObject.activeSelf)continue;var d=Vector2.Distance(player.transform.position,npc.transform.position);if(d<distance){distance=d;nearest=npc;}}if(nearest==null){if(region==WorldRegion.SeriaRoom&&player.transform.position.y< -1.4f)UseWorldPortal("portal_seria_room");return false;}OpenNpcQuests(nearest.Id);return true;}
        void BuildScene(){
            art.BuildWestCoast();
            var camera=Camera.main;
            if(camera==null){var cameraObject=new GameObject("Main Camera");camera=cameraObject.AddComponent<Camera>();cameraObject.tag="MainCamera";cameraObject.transform.position=new Vector3(0,0,-10);}
            camera.transform.position=new Vector3(0f,0f,-10f);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.10f,.16f);camera.orthographic=true;camera.orthographicSize=5.5f;
            if(FindObjectOfType<AudioListener>()==null)camera.gameObject.AddComponent<AudioListener>();
            art.BuildTown();art.BuildDungeon();player=BuildPlayer(new Vector2(-5,-2.1f));player.AddComponent<GroundDepth>();
            skills.Add(new SkillData("上挑",KeyCode.Z,1,6,2f,28){Action=HeroAction.UpperSlash,CommandBound=true,HitOffset=.18f,Reach=2.6f});
            skills.Add(new SkillData("鬼斩",KeyCode.A,1,24,6f,58){Action=HeroAction.GhostSlash,CommandBound=true,Recovery=.58f,HitOffset=.30f,Reach=3.1f});
            skills.Add(new SkillData("刀魂之卡赞",KeyCode.S,5,25,10f,0){Action=HeroAction.Khazan,CommandBound=true,DealsDamage=false,Recovery=.5f});
            skills.Add(new SkillData("地裂·波动剑",KeyCode.D,5,8,2f,34){Action=HeroAction.WaveSword,CommandBound=true,Recovery=.48f,HitOffset=.22f,Reach=4.4f,Depth=.8f});
            skills.Add(new SkillData("格挡",KeyCode.F,5,3,2f,0){Action=HeroAction.Guard,CommandBound=true,DealsDamage=false,Recovery=.32f});
            skills.Add(new SkillData("连突刺",KeyCode.Q,5,6,1f,24){Action=HeroAction.Thrust,CommandBound=true,Travel=.7f,Recovery=.34f,HitOffset=.14f,Reach=2.9f});
            skills.Add(new SkillData("裂波斩",KeyCode.W,10,24,8f,76,3,.11f,.20f,2.5f,.7f){Action=HeroAction.WaveWheel,CommandBound=true,Recovery=.68f});
            skills.Add(new SkillData("三段斩",KeyCode.E,10,7,6f,66,3,.13f,.12f,3.1f,.65f){Action=HeroAction.TripleSlash,CommandBound=true,Travel=1.8f,Recovery=.62f});
            skills.Add(new SkillData("十字斩",KeyCode.R,10,17,3f,62,2,.12f,.18f,3f,.72f){Action=HeroAction.GoreCross,CommandBound=true,Recovery=.52f});
            skills.Add(new SkillData("银光落刃",KeyCode.T,10,12,4f,72){Action=HeroAction.AshenFork,CommandBound=true,AirOnly=true,WaitForLanding=true,Recovery=.35f,Reach=2.8f,Depth=1f});
            skills.Add(new SkillData("崩山击",KeyCode.Y,15,16,4f,90,3,.10f,.05f,3.2f,1f){Action=HeroAction.HopSmash,CommandBound=true,StartsJump=true,WaitForLanding=true,Travel=1.1f,Recovery=.5f});
            BuildUI();}
        GameObject MakeRect(string n,Vector2 p,Vector2 s,Color c){var go=new GameObject(n);go.transform.position=p;var sr=go.AddComponent<SpriteRenderer>();sr.sprite=Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,1,1),new Vector2(.5f,.5f),1f);sr.color=c;sr.sortingOrder=(n=="Ground"||n=="Skyline")?-3:1;go.transform.localScale=s;return go;}
        GameObject BuildPlayer(Vector2 p){var drawn=art.CreateHero(p);if(drawn!=null)return drawn;var root=MakeRect("Player",p,new Vector2(.7f,1.0f),new Color(.20f,.24f,.32f));Part(root,"Head",new Vector2(0,.55f),new Vector2(.56f,.5f),new Color(.95f,.72f,.52f));Part(root,"Hair",new Vector2(0,.76f),new Vector2(.62f,.22f),new Color(.08f,.06f,.07f));Part(root,"Coat",new Vector2(0,.05f),new Vector2(.72f,.62f),new Color(.35f,.08f,.10f));Part(root,"Scarf",new Vector2(.34f,.28f),new Vector2(.34f,.12f),new Color(.85f,.68f,.18f));Part(root,"Blade",new Vector2(.62f,.05f),new Vector2(.12f,1.05f),new Color(.72f,.80f,.90f));return root;}
        GameObject Part(GameObject parent,string n,Vector2 offset,Vector2 size,Color color){var part=MakeRect(n,parent.transform.position+(Vector3)offset,size,color);part.transform.SetParent(parent.transform,true);return part;}
        void SpawnEnemy(string n,Vector2 p,int h,int d){
            var forestId=n=="哥布林"?"goblin":n=="猫妖"?"cat":n=="牛头兵"?"taurus":n.StartsWith("领主")?"dark_forest_boss":null;var forestSprite=forestId==null?null:art.LoadForestEnemy(forestId);
            if(forestSprite!=null){var spriteGo=new GameObject(n);spriteGo.transform.position=p;spriteGo.transform.localScale=n.StartsWith("领主")?Vector3.one*1.2f:Vector3.one*.72f;var sr=spriteGo.AddComponent<SpriteRenderer>();sr.sprite=forestSprite;sr.sortingOrder=4;var unit=spriteGo.AddComponent<Enemy>();unit.Setup(this,n,h,d);unit.SetSingleAttack(art.LoadForestEnemyAttack(forestId));unit.SetWalk(art.LoadForestEnemyWalk(forestId));enemies.Add(spriteGo);return;}
            GameObject go;
            if(n=="牛头兵"||n=="猫妖"||n=="领主·牛头王"){
                bool tau=n!="猫妖";var frames=art.EnemyFrames(tau);
                if(frames!=null){go=new GameObject(n);go.transform.position=p;if(n.StartsWith("领主"))go.transform.localScale=Vector3.one*1.2f;var sr=go.AddComponent<SpriteRenderer>();sr.sprite=frames[0];sr.sortingOrder=4;var unit=go.AddComponent<Enemy>();unit.Setup(this,n,h,d);unit.SetAnimation(sr,frames);enemies.Add(go);return;}
            }
            if(n=="哥布林"&&LoadGoblinFrames()){
                go=new GameObject(n);go.transform.position=p;go.transform.localScale=Vector3.one*1.5f;
                var renderer=go.AddComponent<SpriteRenderer>();renderer.sprite=goblinFrames[0];renderer.sortingOrder=2;
                var animatedEnemy=go.AddComponent<Enemy>();animatedEnemy.Setup(this,n,h,d);animatedEnemy.SetAnimation(renderer,goblinFrames);enemies.Add(go);return;
            }
            go=MakeRect(n,p,new Vector2(.72f,.9f),new Color(.22f,.58f,.25f));Part(go,"Head",new Vector2(0,.55f),new Vector2(.62f,.5f),new Color(.65f,.45f,.28f));Part(go,"Belly",new Vector2(0,.04f),new Vector2(.80f,.55f),n=="猫妖"?new Color(.70f,.38f,.58f):new Color(.28f,.42f,.24f));if(n=="牛头兵")Part(go,"Horn",new Vector2(-.34f,.80f),new Vector2(.18f,.35f),new Color(.82f,.72f,.45f));if(n=="猫妖")Part(go,"Tail",new Vector2(.45f,.10f),new Vector2(.50f,.12f),new Color(.55f,.25f,.50f));var e=go.AddComponent<Enemy>();e.Setup(this,n,h,d);enemies.Add(go);
        }
        bool LoadGoblinFrames(){
            if(goblinFrames!=null)return goblinFrames.Length>0;
            goblinFrames=new Sprite[7];
            for(int i=0;i<7;i++)goblinFrames[i]=LoadGoblinFrame($"Goblin/goblin_{i:D2}",new Vector2(.5f,0));
            foreach(var frame in goblinFrames)if(frame==null){goblinFrames=Array.Empty<Sprite>();return false;}
            return true;
        }
        Sprite LoadGoblinFrame(string path,Vector2 pivot){var sprite=Resources.Load<Sprite>("DNF60/Sprites/"+path);if(sprite!=null)return sprite;var texture=Resources.Load<Texture2D>("DNF60/Sprites/"+path);return texture==null?null:Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),pivot,64);}
        void BuildUI()
        {
            var canvas=new GameObject("UI");var c=canvas.AddComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;canvas.AddComponent<CanvasScaler>();canvas.AddComponent<GraphicRaycaster>();
            combatHud=new GameObject("CombatHUD");combatHud.transform.SetParent(canvas.transform,false);var hud=combatHud.AddComponent<RectTransform>();hud.anchorMin=Vector2.zero;hud.anchorMax=Vector2.one;hud.offsetMin=hud.offsetMax=Vector2.zero;
            var hudFrame=new GameObject("DNF60HudFrame");hudFrame.transform.SetParent(combatHud.transform,false);var hudFrameRt=hudFrame.AddComponent<RectTransform>();hudFrameRt.anchorMin=new Vector2(0,0);hudFrameRt.anchorMax=new Vector2(1,0);hudFrameRt.pivot=new Vector2(.5f,0);hudFrameRt.sizeDelta=new Vector2(0,210);var hudFrameImage=hudFrame.AddComponent<Image>();hudFrameImage.sprite=UiSprite("combat_hud_frame_live");hudFrameImage.color=Color.white;hudFrameImage.raycastTarget=false;
            var left=Panel(combatHud.transform,new Vector2(122,112),new Vector2(230,130));left.anchorMin=left.anchorMax=Vector2.zero;left.pivot=new Vector2(.5f,.5f);left.GetComponent<Image>().color=Color.clear;
            var right=Panel(combatHud.transform,new Vector2(-122,112),new Vector2(230,130));right.anchorMin=right.anchorMax=new Vector2(1,0);right.pivot=new Vector2(.5f,.5f);right.GetComponent<Image>().color=Color.clear;
            hpBar=PaintedOrb(hudFrame.transform,false);mpBar=PaintedOrb(hudFrame.transform,true);hpValue=Label(left,"",Vector2.zero,12,TextAnchor.MiddleCenter);mpValue=Label(right,"",Vector2.zero,12,TextAnchor.MiddleCenter);hpValue.gameObject.SetActive(false);mpValue.gameObject.SetActive(false);
            status=Label(left,"Lv.1 鬼剑士",new Vector2(8,52),15,TextAnchor.MiddleLeft);status.rectTransform.anchorMin=status.rectTransform.anchorMax=new Vector2(0,1);status.rectTransform.pivot=new Vector2(0,1);
            stats=Label(right,"",new Vector2(-8,52),13,TextAnchor.MiddleRight);stats.gameObject.SetActive(false);
            // These normalized bounds follow the twelve sockets painted in
            // combat_hud_frame_expanded (1536 x 272), at any screen width.
            var slots=Panel(combatHud.transform,Vector2.zero,new Vector2(0,210));
            slots.name="SkillSlots";slots.anchorMin=new Vector2(899f/1536f,0);slots.anchorMax=new Vector2(1217f/1536f,0);slots.pivot=new Vector2(.5f,0);
            slots.GetComponent<Image>().color=Color.clear;slots.GetComponent<Image>().raycastTarget=false;
            skillIcons=new Image[12];skillCooldownMasks=new Image[12];skillCooldownLabels=new Text[12];
            for(int i=0;i<12;i++)
            {
                int column=i%6,row=i/6;
                var slot=Panel(slots,new Vector2(0,(272f-(row==0?153f:198f))*210f/272f),new Vector2(-10,30));
                slot.name="SkillSlot_"+i;
                slot.anchorMin=new Vector2(column/6f,0);slot.anchorMax=new Vector2((column+1)/6f,0);slot.pivot=new Vector2(.5f,.5f);
                var slotFrame=slot.GetComponent<Image>();slotFrame.color=Color.clear;slotFrame.raycastTarget=false;
                var icon=new GameObject("InkSkillIcon_"+i);icon.transform.SetParent(slot,false);
                var iconRt=icon.AddComponent<RectTransform>();iconRt.anchorMin=new Vector2(.08f,.06f);iconRt.anchorMax=new Vector2(.92f,.94f);iconRt.offsetMin=iconRt.offsetMax=Vector2.zero;
                var iconImage=icon.AddComponent<Image>();iconImage.sprite=null;iconImage.color=Color.white;iconImage.raycastTarget=false;skillIcons[i]=iconImage;
                var mask=new GameObject("CooldownMask");mask.transform.SetParent(slot,false);
                var maskRt=mask.AddComponent<RectTransform>();maskRt.anchorMin=iconRt.anchorMin;maskRt.anchorMax=iconRt.anchorMax;maskRt.offsetMin=maskRt.offsetMax=Vector2.zero;
                var maskImage=mask.AddComponent<Image>();maskImage.sprite=CooldownMaskSprite();maskImage.color=new Color(.02f,.025f,.03f,.72f);maskImage.type=Image.Type.Filled;maskImage.fillMethod=Image.FillMethod.Radial360;maskImage.fillOrigin=2;maskImage.fillClockwise=false;maskImage.fillAmount=0;maskImage.raycastTarget=false;skillCooldownMasks[i]=maskImage;
                var cd=Label(slot,"",Vector2.zero,11,TextAnchor.MiddleCenter);cd.rectTransform.anchorMin=Vector2.zero;cd.rectTransform.anchorMax=Vector2.one;cd.rectTransform.offsetMin=cd.rectTransform.offsetMax=Vector2.zero;cd.color=Color.white;cd.raycastTarget=false;skillCooldownLabels[i]=cd;
                var key=Label(slot,DefaultSkillKeys[i].ToString(),new Vector2(-2,0),9,TextAnchor.LowerRight);key.name="KeyBinding";key.rectTransform.anchorMin=key.rectTransform.anchorMax=new Vector2(1,0);key.rectTransform.pivot=new Vector2(1,0);key.rectTransform.sizeDelta=new Vector2(16,12);key.color=new Color(.95f,.82f,.48f,1);key.gameObject.AddComponent<Shadow>().effectColor=Color.black;key.raycastTarget=false;
            }
            var expBackground=Panel(combatHud.transform,new Vector2(0,4),new Vector2(0,8));expBackground.anchorMin=new Vector2(0,0);expBackground.anchorMax=new Vector2(1,0);expBackground.pivot=new Vector2(.5f,.5f);expBackground.offsetMin=new Vector2(0,0);expBackground.offsetMax=new Vector2(0,8);expBackground.GetComponent<Image>().color=new Color(.03f,.035f,.05f,.96f);
            var expFill=new GameObject("CombatExperience");expFill.transform.SetParent(expBackground,false);var expRt=expFill.AddComponent<RectTransform>();expRt.anchorMin=Vector2.zero;expRt.anchorMax=Vector2.one;expRt.offsetMin=new Vector2(2,2);expRt.offsetMax=new Vector2(-2,-2);expBar=expFill.AddComponent<Image>();expBar.color=new Color(.93f,.66f,.16f,1);expBar.type=Image.Type.Filled;expBar.fillMethod=Image.FillMethod.Horizontal;expBar.fillOrigin=0;expBar.fillAmount=0;
            skillText=Label(combatHud.transform,"",Vector2.zero,13,TextAnchor.MiddleCenter);skillText.gameObject.SetActive(false);
            quest=Label(canvas.transform,"",new Vector2(18,-18),15,TextAnchor.UpperLeft);quest.rectTransform.anchorMin=quest.rectTransform.anchorMax=new Vector2(0,1);quest.rectTransform.pivot=new Vector2(0,1);
            modeText=Label(canvas.transform,"",new Vector2(18,-40),15,TextAnchor.UpperLeft);modeText.rectTransform.anchorMin=modeText.rectTransform.anchorMax=new Vector2(0,1);modeText.rectTransform.pivot=new Vector2(0,1);
            quest.gameObject.SetActive(false);modeText.gameObject.SetActive(false);
            var inv=Panel(canvas.transform,new Vector2(-190,0),new Vector2(360,390));inventoryPanel=inv.gameObject;inventoryText=Label(inv,"背包",new Vector2(14,-14),16,TextAnchor.UpperLeft);inventoryPanel.SetActive(false);
            var jobp=Panel(canvas.transform,new Vector2(190,0),new Vector2(360,390));jobPanel=jobp.gameObject;jobText=Label(jobp,"转职",new Vector2(14,-14),16,TextAnchor.UpperLeft);jobPanel.SetActive(false);Refresh();
        }
        Sprite CooldownMaskSprite(){const int size=64;var tex=new Texture2D(size,size,TextureFormat.RGBA32,false);for(int y=0;y<size;y++)for(int x=0;x<size;x++)tex.SetPixel(x,y,Color.white);tex.Apply();return Sprite.Create(tex,new Rect(0,0,size,size),new Vector2(.5f,.5f),size);}
        Sprite UiSprite(string name){var texture=Resources.Load<Texture2D>("DNF60/UI/"+name);return texture==null?null:Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);}
        Image Bar(Transform parent,Vector2 pos,Vector2 size,Color color){var rt=Panel(parent,pos,size);var image=rt.GetComponent<Image>();image.color=color;image.type=Image.Type.Filled;image.fillMethod=Image.FillMethod.Horizontal;image.fillOrigin=0;image.fillAmount=1;return image;}
        Image Orb(Transform parent,Vector2 pos,Color color,Color rim){
            var holder=Panel(parent,pos,new Vector2(94,94));
            holder.GetComponent<Image>().color=Color.clear;
            var frame=new GameObject("OrbMetalFrame");frame.transform.SetParent(holder,false);var frameRt=frame.AddComponent<RectTransform>();frameRt.anchorMin=frameRt.anchorMax=new Vector2(.5f,.5f);frameRt.pivot=new Vector2(.5f,.5f);frameRt.sizeDelta=new Vector2(94,94);var frameImage=frame.AddComponent<Image>();frameImage.sprite=OrbSprite(rim,new Color(.12f,.08f,.04f),true,false);frameImage.color=Color.white;
            var liquid=new GameObject("OrbLiquid");liquid.transform.SetParent(holder,false);var liquidRt=liquid.AddComponent<RectTransform>();liquidRt.anchorMin=liquidRt.anchorMax=new Vector2(.5f,.5f);liquidRt.pivot=new Vector2(.5f,.5f);liquidRt.sizeDelta=new Vector2(78,78);var image=liquid.AddComponent<Image>();image.sprite=OrbSprite(color,Color.clear,false,false);image.color=Color.white;image.type=Image.Type.Filled;image.fillMethod=Image.FillMethod.Vertical;image.fillOrigin=0;image.fillAmount=1;
            var shine=new GameObject("OrbGemHighlight");shine.transform.SetParent(holder,false);var shineRt=shine.AddComponent<RectTransform>();shineRt.anchorMin=shineRt.anchorMax=new Vector2(.5f,.5f);shineRt.pivot=new Vector2(.5f,.5f);shineRt.sizeDelta=new Vector2(78,78);var shineImage=shine.AddComponent<Image>();shineImage.sprite=OrbSprite(Color.white,Color.clear,false,true);shineImage.color=new Color(1f,1f,1f,.72f);shineImage.raycastTarget=false;
            return image;
        }
        Sprite OrbSprite(Color baseColor,Color rimColor,bool ring,bool shine){
            const int size=96;var tex=new Texture2D(size,size,TextureFormat.RGBA32,false);tex.filterMode=FilterMode.Bilinear;tex.wrapMode=TextureWrapMode.Clamp;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++){float d=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(size*.5f,size*.5f))/(size*.5f);float edge=Mathf.Clamp01((1f-d)*18f);if(d>1f){tex.SetPixel(x,y,Color.clear);continue;}Color c=baseColor;if(ring&&d>.80f)c=Color.Lerp(baseColor,rimColor,Mathf.Clamp01((d-.80f)/.20f));float shade=.78f+.22f*Mathf.Clamp01(1f-d);c=new Color(c.r*shade,c.g*shade,c.b*shade,edge);if(shine){float spot=Mathf.Clamp01(1f-Mathf.Sqrt(Mathf.Pow((x-size*.33f)/(size*.22f),2)+Mathf.Pow((y-size*.30f)/(size*.16f),2)));c=new Color(1f,1f,1f,spot*.72f*edge);}tex.SetPixel(x,y,c);}tex.Apply();return Sprite.Create(tex,new Rect(0,0,size,size),new Vector2(.5f,.5f),size);
        }
        RectTransform Panel(Transform p,Vector2 pos,Vector2 size){var go=new GameObject("Panel");go.transform.SetParent(p,false);var rt=go.AddComponent<RectTransform>();rt.anchorMin=rt.anchorMax=new Vector2(.5f,.5f);rt.anchoredPosition=pos;rt.sizeDelta=size;var img=go.AddComponent<Image>();img.color=panelColor;return rt;}
        Text Label(Transform p,string text,Vector2 pos,int font,TextAnchor anchor){var go=new GameObject("Text");go.transform.SetParent(p,false);var rt=go.AddComponent<RectTransform>();rt.anchorMin=rt.anchorMax=new Vector2(0,1);rt.pivot=new Vector2(0,1);rt.anchoredPosition=pos;rt.sizeDelta=new Vector2(600,360);var t=go.AddComponent<Text>();t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.fontSize=font;t.color=Color.white;t.alignment=anchor;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.text=text;return t;}
        void Refresh(){
            if(status==null||stats==null||skillText==null||inventoryText==null||jobText==null){BuildUI();if(status==null)return;}
            int maxHp=100+level*12,maxMp=MaxMana;if(hpBar!=null)hpBar.fillAmount=maxHp<=0?0:(float)hp/maxHp;if(mpBar!=null)mpBar.fillAmount=maxMp<=0?0:(float)mp/maxMp;if(hpValue!=null)hpValue.text=$"HP {hp}/{maxHp}";if(mpValue!=null)mpValue.text=$"MP {mp}/{maxMp}";if(combatHud!=null)combatHud.SetActive(mode==GameMode.Dungeon);if(expBar!=null)expBar.fillAmount=level>=MaxLevel?1f:Mathf.Clamp01((float)exp/Mathf.Max(1,ExpNext()));status.text=$"Lv.{level}  {JobName()}";stats.text="";quest.text="";modeText.text="";RefreshSkillSlots();var cooldown="";skillText.text="方向键 移动    X 普攻    C 跳跃    Z 上挑\n技能 "+cooldown+"\nI 背包 / 装备    F1 任务日志    空格 NPC交互";inventoryText.text="背包 / 装备\n\n"+string.Join("\n",inventory.ConvertAll(x=>x.ToString()))+"\n\n穿戴：\n"+string.Join("\n",equipped.Values);jobText.text=$"当前职业：{JobName()}\n\n转职需完成对应任务。\n\n地下城第 {dungeonStage} 区";
        }
        string JobName(){return job==Job.GhostKnight?"鬼剑士":job==Job.Fighter?"格斗家":job.ToString();}int ExpNext()=>level*100;
        void BasicAttack()
        {
            if (mode != GameMode.Dungeon || SystemPanelOpen || combat == null) return;
            if(jump.Airborne){if(combat.BeginAirBasic(Time.time,TotalAttack+level*2)){basicQueued=false;comboStep=0;art.Move(Vector2.zero);art.PlayAction(HeroAction.AirBasic,player.transform.position,jump.Height,.42f);}return;}
            // A tap near the end of a normal attack queues one following swing.
            // Skills and very early taps do not create a delayed attack.
            if(combat.Active){if(combat.BasicStage>0&&combat.RemainingRecovery(Time.time)<=.20f){basicQueued=true;basicQueuedUntil=Time.time+.25f;}return;}
            if(runInput.Running){
                if(combat.BeginDashAttack(Time.time,TotalAttack+level*2)){basicQueued=false;comboStep=0;lastComboAt=0;art.Move(Vector2.zero);art.PlayAction(HeroAction.DashAttack,player.transform.position,0,HeroDashAttack.Duration);}
                return;
            }
            if (Time.time-lastComboAt>.85f) comboStep=0;
            int nextStep=comboStep%3+1;
            if (combat.BeginBasic(Time.time, HeroBasicAnimation.Duration(nextStep), TotalAttack + level * 2,nextStep))
            { basicQueued=false;comboStep=nextStep; lastComboAt=Time.time; art.Move(Vector2.zero); art.PlayBasic(comboStep, player.transform.position, jump.Height); }
        }

        void Cast(SkillData skill)
        {
            if (mode != GameMode.Dungeon || SystemPanelOpen || skill == null || !skill.CommandBound) return;
            if (combat != null)
            {
                int index = skills.IndexOf(skill);
                if (index < 0 || skillRanks[index]<=0 || skill.AirOnly && !jump.Airborne || !skill.AirOnly && jump.Airborne || Time.time < skill.ReadyAt || level < skill.Level || mp < skill.Mana) return;
                if (skill.StartsJump && !jump.Start(4.8f)) return;
                if (!combat.BeginSkill(index, Time.time, TotalAttack + level * 2)) return;
                basicQueued=false;comboStep=0;
                mp -= skill.Mana; skill.ReadyAt = Time.time + skill.Cooldown;
                art.Move(Vector2.zero);
                if(skill.Travel!=0){var moved=player.transform.position+Vector3.right*(art.Facing*skill.Travel);player.transform.position=ClampDungeonPassage(CombatPlane.Clamp(moved));}
                if(skill.Action==HeroAction.Guard)guardUntil=Time.time+1.1f;
                if(skill.Action==HeroAction.Khazan)khazanUntil=Time.time+SkillRankValue(2,1,20000)/1000f;
                art.PlayAction(skill.Action, player.transform.position, jump.Height, skill.Recovery); Refresh(); return;
            }
            return;
        }

        void HitNearest(CombatController.Hit hit)
        {
            if (mode != GameMode.Dungeon || jump.Height > (combat.AirBasic ? 2.1f : 1.1f)) return;
            var origin = hit.Origin;
            Enemy best = null;
            float nearest = float.MaxValue;
            foreach (var go in enemies)
            {
                if (go == null) continue;
                var distance = Mathf.Abs(go.transform.position.x - origin.x);
                if (distance >= nearest || !CombatPlane.InReach(origin, go.transform.position, hit.Facing, hit.Reach, hit.Depth)) continue;
                nearest = distance;
                best = go.GetComponent<Enemy>();
            }
            if (best != null && best.Take((int)hit.Damage)) { gameAudio?.PlayHit();float pause=combat.BasicStage>0?.07f+combat.BasicStage*.01f:.09f;float until=Time.time+pause;float extra=Mathf.Max(0,until-Mathf.Max(Time.time,hitStopUntil));hitStopUntil=Mathf.Max(hitStopUntil,until);combat?.Pause(extra);lastComboAt+=extra;if(basicQueued)basicQueuedUntil+=extra;art.SetHitPause(pause);foreach(var go in enemies)if(go!=null)go.GetComponent<Enemy>()?.Pause(extra); }
        }
        public void Defeat(Enemy e){if(level<MaxLevel)exp+=e.Xp;gold+=e.Xp*2;questKills++;RecordQuest(ObjectiveKind.Kill,e.DisplayName);var drop=EquipmentCatalog.DropFor(e.DisplayName,level);if(drop!=null&&ReceiveEquipment(drop))systemMessage="获得装备："+drop.Name+"。按 I 查看背包。";if(mode==GameMode.Dungeon&&enemies.Count<=1){roomCleared=true;if(activeForest!=null)clearedForestRooms[activeForestRoom]=true;OpenDungeonGate();if(bossRoom){RecordQuest(ObjectiveKind.ClearDungeon,ActiveDungeonName);dungeonStage++;systemMessage=(drop==null?"领主已击败，副本通关。":"领主已击败，获得奖励："+drop.Name+"。")+" 请选择下一步。";ShowDungeonResult();}else systemMessage="房间已清理，铁门打开。通过打开的门继续探索。";}CheckLevel();Save();Refresh();}
        void CheckLevel(){bool leveled=false;while(level<MaxLevel&&exp>=ExpNext()){exp-=ExpNext();level++;hp=100+level*12;mp=MaxMana;baseAttack+=3;baseDefense++;leveled=true;}if(level>=MaxLevel)exp=0;if(leveled)systemMessage="升级了！当前等级："+level+"。";}
        void UseWorldPortal(string id)
        {
            if(id=="portal_seria_room"){if(region==WorldRegion.SeriaRoom){region=WorldRegion.HuttonMar;foreach(var npc in npcs)if(npc!=null)npc.gameObject.SetActive(true);player.transform.position=new Vector3(-3.2f,-2.1f,0);systemMessage="返回赫顿玛尔。";}else{region=WorldRegion.SeriaRoom;player.transform.position=new Vector3(-3.2f,-2.1f,0);foreach(var npc in npcs)if(npc!=null&&npc.Id!="portal_seria_room")npc.gameObject.SetActive(false);art.SetTown(true);systemMessage="赛丽亚房间：靠近树屋出口返回赫顿玛尔。";}Refresh();return;}
            if(id=="portal_forest"){ShowDungeonSelection("格兰之森",new[]{"幽暗密林","雷鸣废墟","格拉卡","烈焰格拉卡","冰霜幽暗密林","猛毒雷鸣废墟","暗黑雷鸣废墟"});return;}
            if(id=="portal_sky"){ShowDungeonSelection("天空之城",new[]{"龙人之塔","人偶玄关","石巨人塔"});return;}
        }
        void ShowDungeonSelection(string title,string[] dungeons)
        {
            dungeonSelectionTitle=title;dungeonSelectionOptions=dungeons??new string[0];selectedDungeonIndex=0;dungeonSelectionOpen=true;systemMessage=title+"副本选择已打开。";
            dungeonStage=Mathf.Max(1,dungeonStage);Refresh();
        }
        string RegionName(){switch(region){case WorldRegion.WestCoast:return "西海岸";case WorldRegion.SeriaRoom:return "赛丽亚房间";case WorldRegion.GrandFlores:return "格兰之森";case WorldRegion.SkyTower:return "天空之城";default:return "赫顿玛尔";}}
        void EnterDungeon(){if(mode==GameMode.Dungeon)return;dungeonResultOpen=false;dungeonSelectionOpen=false;ResetCombatAction();activeNpc="";questsOpen=false;jump.Reset();mode=GameMode.Dungeon;gameAudio?.SetDungeon(true);bossRoom=false;roomCleared=false;forestRoom=0;SelectForestContent();art.SetTown(false);dungeonStage=Mathf.Max(1,dungeonStage);ClearEnemies();SetDungeonGate(false,"铁门关闭");SpawnForestRoom();systemMessage=dungeonSelectionTitle+"·"+(dungeonSelectionOptions.Length>selectedDungeonIndex?dungeonSelectionOptions[selectedDungeonIndex]:"幽暗密林")+"：清理怪物后铁门会打开。";Refresh();}
        void SpawnForestRoom(){if(activeForest!=null){SpawnConfiguredForestRoom();return;}ClearEnemies();if(forestRoom==0){SpawnEnemy("哥布林",new Vector2(2,-1.2f),45+level*4,8+level/2);SpawnEnemy("猫妖",new Vector2(5,-2.2f),60+level*5,10+level/2);}else if(forestRoom==1){SpawnEnemy("哥布林",new Vector2(2,-1.2f),50+level*4,9+level/2);SpawnEnemy("牛头兵",new Vector2(5,-2.2f),80+level*6,12+level/2);SpawnEnemy("猫妖",new Vector2(7,-3.1f),65+level*5,11+level/2);}else{SpawnEnemy("牛头兵",new Vector2(3,-1.6f),90+level*6,13+level/2);SpawnEnemy("猫妖",new Vector2(6,-2.5f),70+level*5,11+level/2);}}
        void EnterBossRoom(){if(mode!=GameMode.Dungeon||!roomCleared)return;if(activeForest!=null){HandleForestRoomExit();return;}ResetCombatAction();jump.Reset();if(!bossRoom&&forestRoom<2){forestRoom++;roomCleared=false;SetDungeonGate(false,"铁门关闭");SpawnForestRoom();systemMessage="幽暗密林·第"+(forestRoom+1)+"房间：清理怪物后继续前进。";Refresh();return;}if(bossRoom)return;bossRoom=true;gameAudio?.SetDungeon(true,true);roomCleared=false;ClearEnemies();SetDungeonGate(false,"领主房关闭");SpawnEnemy("领主·牛头巨兽",new Vector2(5,-1.2f),150+level*15,18+level);systemMessage="幽暗密林·领主房：击败领主后走到右侧出口。";Refresh();}
        void EnterTown(){dungeonResultOpen=false;dungeonSelectionOpen=false;ResetCombatAction();jump.Reset();mode=GameMode.Town;HideForestGates();ClearForestHazards();activeForest=null;forestSlowUntil=0;ClearForestPoison();gameAudio?.SetDungeon(false);bossRoom=false;roomCleared=false;art.SetTown(true);foreach(var npc in npcs)if(npc!=null)npc.gameObject.SetActive(true);ClearEnemies();SetDungeonGate(false,"");systemMessage="已返回赫顿玛尔。靠近 NPC 后按空格交互。";Refresh();}
        void SetDungeonGate(bool open,string label)
        {
            if(mode==GameMode.Dungeon&&activeForest!=null){UpdateForestGates();return;}
            if(roomLabel!=null)Destroy(roomLabel);roomLabel=null;
            if(mode!=GameMode.Dungeon){if(roomGate!=null)roomGate.SetActive(false);return;}
            if(roomGate==null){roomGate=new GameObject("DungeonGate");roomGate.transform.SetParent(transform);roomGate.AddComponent<SpriteRenderer>();}
            roomGate.SetActive(true);
            roomGate.transform.position=new Vector3(GateX,GateY,0);
            var renderer=roomGate.GetComponent<SpriteRenderer>();
            renderer.sprite=art.LoadDungeonGate(open);
            renderer.sortingOrder=CombatPlane.Order(GateY);
            if(renderer.sprite!=null)roomGate.transform.localScale=Vector3.one*(3.1f/renderer.sprite.bounds.size.y);
            else Debug.LogError("Missing redrawn dungeon gate: "+(open?"open":"closed"));
        }
        void OpenDungeonGate(){SetDungeonGate(true,bossRoom?"出口":"铁门已开");}
        void ClearEnemies(){foreach(var e in enemies)if(e!=null)Destroy(e);enemies.Clear();}
        void BuyPotion(){if(gold>=50){gold-=50;potions++;Refresh();}}
        void UsePotion(){if(potions>0&&hp<100+level*12){potions--;hp=Mathf.Min(100+level*12,hp+50+level*3);Refresh();}}
        void EnsureStarterLoadout(){
            if(inventory.Count>0)return;
            var starter=new ItemData("新手短剑",ItemSlot.Weapon,1,3,0){SourceId="starter.ghostknight.lv1",BindOnEquip=true,Jobs=new[]{Job.GhostKnight.ToString()}};
            inventory.Add(starter);equipped[starter.Slot]=starter;starter.Bound=true;systemMessage="装备了新手短剑。靠近赛丽亚后按空格开始任务。";Save();
        }
        void TakePlayerDamage(int value){if(mode!=GameMode.Dungeon||value<=0||hp<=0)return;if(Time.time<guardUntil)value=Mathf.Max(1,Mathf.CeilToInt(value*(1f-Mathf.Clamp01(SkillRankValue(4,2,40)/100f))));hp=Mathf.Max(0,hp-value);gameAudio?.PlayHurt();if(hp>0){combat?.Reset();art.Hurt();Refresh();return;}hp=100+level*12;mp=MaxMana;EnterTown();systemMessage="角色倒下，地下城挑战失败。返回城镇。";Refresh();Save();}
        void ResetCombatAction(){runInput.Reset();art?.SetRunning(false);combat?.Reset();art?.CancelEffects();basicQueued=false;queuedSkill=null;comboStep=0;lastComboAt=hitStopUntil=0;player.transform.position=new Vector3(-5,-2.1f,0);foreach(var npc in npcs)if(npc!=null)npc.gameObject.SetActive(false);}
        
        bool persistenceEnabled = true;
        void Save(){if(persistenceEnabled)SaveProgression();}
        void Load(){
            if(!persistenceEnabled)return;
            if(LoadProgression()){Refresh();return;}
            if(PlayerPrefs.HasKey("level")){
                level=Mathf.Clamp(PlayerPrefs.GetInt("level"),1,MaxLevel);exp=level==MaxLevel?0:Mathf.Max(0,PlayerPrefs.GetInt("exp"));
                gold=Mathf.Max(0,PlayerPrefs.GetInt("gold"));var oldJob=PlayerPrefs.GetInt("job");job=Enum.IsDefined(typeof(Job),oldJob)?(Job)oldJob:Job.GhostKnight;
                potions=Mathf.Max(0,PlayerPrefs.GetInt("potions",3));dungeonStage=Mathf.Max(0,PlayerPrefs.GetInt("dungeonStage",0));
                baseAttack=12+(level-1)*3;baseDefense=4+level-1;hp=100+level*12;mp=MaxMana;
            }
            RestoreSkillTree(new SaveData());Refresh();
        }
        public sealed class Enemy:MonoBehaviour {
            GameBootstrap game;int hp,maxHp,damage,frameIndex,attackFacing=1,facing=1;float nextFrame,attackAt,impactAt=-1,flashUntil,knock;bool dead;EnemyState state=EnemyState.Chase;float recoverAt;const float AttackWindup=.32f,AttackRecover=.48f;
            enum EnemyState { Observe, Chase, Reposition, Windup, Recover, Stunned, Dead }
            float decisionAt;Vector2 movement;
            // PVF: goblin attack delay 1500ms / warlike 60; lugaru 2000ms / 60;
            // tauarmy 2000ms / 100. Decision windows and world sight scale are adaptations.
            ForestSpawn forestProfile;
            public void ConfigureForest(ForestSpawn profile){forestProfile=profile;}
            float AttackDelay=>forestProfile!=null?Mathf.Max(.8f,forestProfile.attackDelay):DisplayName=="哥布林"?1.5f:2f;
            float Windup=>forestProfile!=null&&forestProfile.boss?.55f:AttackWindup;
            bool Ranged=>forestProfile!=null&&(forestProfile.behavior=="throw"||forestProfile.behavior=="lightning"||forestProfile.behavior=="fire"||forestProfile.behavior=="ice");
            bool Cautious=>forestProfile!=null?forestProfile.warlike<=60:DisplayName=="哥布林"||DisplayName=="猫妖";
            void Observe(float now){state=EnemyState.Observe;movement=Vector2.zero;decisionAt=now+UnityEngine.Random.Range(.3f,.65f);}
            void Decide(float now,float dx,float dy){
                if(Mathf.Abs(dx)>7f||Mathf.Abs(dy)>2.5f){Observe(now);return;}
                bool reachable=Mathf.Abs(dx)<=AttackReach()&&Mathf.Abs(dy)<=.3f;
                if(reachable&&now>=attackAt){
                    if(Cautious&&UnityEngine.Random.value>=.6f){Observe(now);return;}
                    state=EnemyState.Windup;attackFacing=dx<0?-1:1;impactAt=now+Windup;attackAt=now+AttackDelay;return;
                }
                if(reachable){
                    if(Cautious&&UnityEngine.Random.value<.35f){state=EnemyState.Reposition;movement=new Vector2(-Mathf.Sign(dx),0);decisionAt=now+.25f;}
                    else Observe(now);
                    return;
                }
                state=EnemyState.Chase;decisionAt=now+UnityEngine.Random.Range(.45f,.75f);
                movement=new Vector2(Mathf.Abs(dx)>AttackReach()*.85f?Mathf.Sign(dx):0,Mathf.Abs(dy)>.15f?Mathf.Sign(dy):0);
            }
            SpriteRenderer animatedRenderer;Sprite[] animationFrames,singleAttackFrames,walkFrames;float walkDistance;Sprite idleSprite;Material flashMaterial;
            public int Xp=>Mathf.Max(10,maxHp/2);public string DisplayName{get;private set;}
            public void Setup(GameBootstrap g,string n,int h,int d){game=g;DisplayName=n;hp=maxHp=h;damage=g.BalancedMonsterDamage(n,d);attackAt=Time.time+1;Observe(Time.time);gameObject.AddComponent<GroundDepth>();var renderer=GetComponent<SpriteRenderer>();if(renderer!=null){SetAnimation(renderer,new[]{renderer.sprite});idleSprite=renderer.sprite;UpdateFacing(g.player.transform.position.x-transform.position.x);}}
            public void SetAnimation(SpriteRenderer renderer,Sprite[] frames){animatedRenderer=renderer;animationFrames=frames;var shader=Resources.Load<Shader>("DNF60/HitFlash");if(shader!=null){if(flashMaterial==null)flashMaterial=new Material(shader);renderer.sharedMaterial=flashMaterial;}}
            public void SetSingleAttack(Sprite[] frames){if(frames!=null&&frames.Length==3)singleAttackFrames=frames;}
            public void SetWalk(Sprite[] frames){if(frames!=null&&frames.Length==4)walkFrames=frames;}
            void UpdateFacing(float dx){if(state==EnemyState.Windup||state==EnemyState.Recover)facing=attackFacing;else if(state!=EnemyState.Stunned&&Mathf.Abs(dx)>.01f)facing=dx<0?-1:1;if(animatedRenderer!=null)animatedRenderer.flipX=facing<0;}
            public bool Take(int value){if(dead||value<=0)return false;hp-=value;flashUntil=Time.time+.12f;state=EnemyState.Stunned;impactAt=-1;recoverAt=Time.time+.22f;knock=(transform.position.x>=game.player.transform.position.x?1:-1)*2.8f;
                movement=Vector2.zero;if(animatedRenderer!=null){animatedRenderer.sprite=animationFrames.Length==32?animationFrames[8]:animationFrames.Length==26?animationFrames[15]:idleSprite;}if(flashMaterial!=null)flashMaterial.SetFloat("_Flash",1);
                var popup=new GameObject("DamageNumber");popup.transform.position=transform.position+Vector3.up*(DisplayName=="猫妖"?1.4f:2.1f);popup.transform.SetParent(game.transform);popup.AddComponent<DamageNumber>().Init(value);
                if(hp<=0){dead=true;state=EnemyState.Dead;game.systemMessage="击败了 "+DisplayName+"，获得经验和金币。";game.Defeat(this);game.enemies.Remove(gameObject);Destroy(gameObject);}return true;}
            public void Pause(float seconds){attackAt+=seconds;decisionAt+=seconds;recoverAt+=seconds;nextFrame+=seconds;flashUntil+=seconds;if(impactAt>=0)impactAt+=seconds;}
            void Update(){if(dead||game==null||game.player==null||Time.time<game.hitStopUntil||game.SystemPanelOpen)return;float now=Time.time;float dx=game.player.transform.position.x-transform.position.x;float dy=game.player.transform.position.y-transform.position.y;bool moving=false;var beforeMove=transform.position;bool fight=game.mode==GameMode.Dungeon;
                if(flashMaterial!=null)flashMaterial.SetFloat("_Flash",now<flashUntil?1:0);
                if(Mathf.Abs(knock)>.01f){transform.position+=Vector3.right*knock*Time.deltaTime;knock=Mathf.MoveTowards(knock,0,12*Time.deltaTime);}
  if(fight){
                    if(state==EnemyState.Stunned){if(now>=recoverAt){Observe(now);attackAt=Mathf.Max(attackAt,now+.35f);} }
                    else if(state==EnemyState.Windup){if(forestProfile!=null&&(forestProfile.monsterId==15||forestProfile.monsterId==5004)&&now>=impactAt-.18f)transform.position+=Vector3.right*attackFacing*3.5f*Time.deltaTime;if(now>=impactAt){if(Ranged)game.CreateForestHazard(forestProfile.behavior,transform.position,attackFacing,damage);else if(game.jump.Height<=1.1f&&CombatPlane.InReach(transform.position,game.player.transform.position,attackFacing,AttackReach(),.45f)){game.TakePlayerDamage(game.MitigateMonsterDamage(damage));if(forestProfile!=null&&forestProfile.behavior=="cold")game.forestSlowUntil=now+1.5f;if(forestProfile!=null&&forestProfile.behavior=="poison")game.ApplyForestPoison(damage);}impactAt=-1;state=EnemyState.Recover;recoverAt=now+AttackRecover;}}
                    else if(state==EnemyState.Recover){if(now>=recoverAt)Observe(now);}
                    else if(state==EnemyState.Observe){if(now>=decisionAt)Decide(now,dx,dy);}
                    else if(state==EnemyState.Chase||state==EnemyState.Reposition){
                        if(now>=decisionAt||(state==EnemyState.Chase&&Mathf.Abs(dx)<=AttackReach()&&Mathf.Abs(dy)<=.3f))Observe(now);
                        else{moving=true;transform.position+=CombatPlane.Velocity(movement,forestProfile!=null?forestProfile.speed:DisplayName=="猫妖"?1.7f:.95f)*Time.deltaTime;}
                    }}
                transform.position=CombatPlane.Clamp(transform.position);
                if(animatedRenderer!=null){UpdateFacing(dx);
                    if(singleAttackFrames!=null){
                        bool walking=moving&&Vector3.Distance(beforeMove,transform.position)>.00001f;
                        if(walking)walkDistance+=Vector3.Distance(beforeMove,transform.position);else walkDistance=0;
                        // Advance only with actual locomotion, never knockback or hit stop.
                        int step=Mathf.FloorToInt(walkDistance/(DisplayName=="猫妖"?.16f:DisplayName.StartsWith("领主")?.20f:.14f))%4;
                        animatedRenderer.sprite=state==EnemyState.Windup?singleAttackFrames[0]:state==EnemyState.Recover?singleAttackFrames[now<recoverAt-AttackRecover+.12f?1:2]:walking&&walkFrames!=null?walkFrames[step]:idleSprite;return;
                    }
                    if(now>=nextFrame){nextFrame=now+.12f;frameIndex++;int index=0;
                    if(animationFrames.Length==32)index=state==EnemyState.Stunned?8:state==EnemyState.Windup?2:state==EnemyState.Recover?4:moving?13+frameIndex%3:frameIndex%2;
                    else if(animationFrames.Length==26)index=state==EnemyState.Stunned?15:state==EnemyState.Windup?7:state==EnemyState.Recover?8:moving?10+frameIndex%2:frameIndex%2;
                    else index=moving?frameIndex%animationFrames.Length:0;
                    animatedRenderer.sprite=animationFrames[index];}}
            }
            float AttackReach(){return Ranged?6f:forestProfile!=null&&forestProfile.monsterId==15?2.4f:DisplayName=="猫妖"?1.55f:DisplayName.StartsWith("领主")?2.0f:1.7f;}
            void OnDestroy(){if(flashMaterial!=null)Destroy(flashMaterial);}
        }
    }
}









