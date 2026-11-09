using System;
using System.Collections.Generic;
using UnityEngine;

namespace DNF60Prototype
{
    [Serializable] public sealed class ForestCatalog { public ForestDungeon[] dungeons; }
    [Serializable] public sealed class ForestDungeon
    {
        public string id, name, source;
        public int minimumLevel, basisLevel, width, height, start, boss;
        public ForestRoom[] rooms;
    }
    [Serializable] public sealed class ForestRoom
    {
        public int x, y, mapId; public string source; public bool boss;
        // Right, up, left, down; decoded from the original A..P path bit masks.
        public int[] links; public ForestSpawn[] enemies;
    }
    [Serializable] public sealed class ForestSpawn
    {
        public int monsterId, hp, damage, warlike;
        public string name, asset, behavior, source;
        public float x, y, speed, attackDelay; public bool boss;
    }

    public sealed partial class GameBootstrap
    {
        ForestCatalog forestCatalog;
        ForestDungeon activeForest;
        int activeForestRoom;
        bool[] clearedForestRooms;
        bool[] visitedForestRooms;
        readonly GameObject[] forestGates = new GameObject[4];
        float forestTransitionAt, forestSlowUntil;
        int forestPoisonTicks, forestPoisonDamage;
        float forestPoisonTimer;
        string ActiveDungeonName => activeForest != null ? activeForest.name : "幽暗密林";
        static readonly Vector3[] ForestGatePositions = {
            new Vector3(8,-2.1f),new Vector3(0,CombatPlane.Back),
            new Vector3(-8,-2.1f),new Vector3(0,CombatPlane.Front)
        };
        void SelectForestContent()
        {
            if(forestCatalog==null)
            {
                var data=Resources.Load<TextAsset>("DNF60/Data/grand_flores");
                if(data!=null)forestCatalog=JsonUtility.FromJson<ForestCatalog>(data.text);
            }
            activeForest=null;
            string selected=selectedDungeonIndex>=0&&selectedDungeonIndex<dungeonSelectionOptions.Length?dungeonSelectionOptions[selectedDungeonIndex]:"幽暗密林";
            if(forestCatalog!=null)foreach(var definition in forestCatalog.dungeons)if(definition.name==selected){activeForest=definition;break;}
            clearedForestRooms=activeForest==null?null:new bool[activeForest.rooms.Length];
            visitedForestRooms=activeForest==null?null:new bool[activeForest.rooms.Length];
            activeForestRoom=activeForest==null?0:activeForest.start;
            if(activeForest!=null)player.transform.position=new Vector3(-6.5f,GateY,0);
            forestSlowUntil=0;
            ClearForestPoison();
            art.SetDungeonBackground(activeForest==null?"dark_forest":activeForest.id);
            HideForestGates();
        }
        void SpawnConfiguredForestRoom()
        {
            ClearEnemies(); ClearForestHazards();
            var room=activeForest.rooms[activeForestRoom];
            visitedForestRooms[activeForestRoom]=true;
            forestRoom=activeForestRoom; bossRoom=room.boss;
            roomCleared=clearedForestRooms[activeForestRoom];
            gameAudio?.SetDungeon(true,bossRoom);
            if(!roomCleared)foreach(var spawn in room.enemies)
            {
                var sprite=art.LoadForestEnemy(spawn.asset);
                if(sprite==null){Debug.LogError("Missing Grand Flores creature: "+spawn.asset);return;}
                var go=new GameObject(spawn.name); go.transform.position=new Vector3(spawn.x,spawn.y);
                // New assets use a common 640x400 canvas and 5% foot pivot.
                float scale=spawn.boss?spawn.asset=="shauta"?1.18f:.85f:.72f;
                go.transform.localScale=Vector3.one*scale;
                var renderer=go.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sortingOrder=4;
                var enemy=go.AddComponent<Enemy>();enemy.Setup(this,spawn.name,spawn.hp,spawn.damage);
                enemy.ConfigureForest(spawn);enemy.SetWalk(art.LoadForestEnemyWalk(spawn.asset));
                enemy.SetSingleAttack(art.LoadForestEnemyAttack(spawn.asset));enemies.Add(go);
            }
            UpdateForestGates();forestTransitionAt=Time.time+.3f;
            systemMessage=activeForest.name+(bossRoom?"·领主房":"")+"：清理怪物后通过打开的门。";
            Refresh();
        }
        void HideForestGates(){foreach(var gate in forestGates)if(gate!=null)gate.SetActive(false);}
        void UpdateForestGates()
        {
            if(roomGate!=null)roomGate.SetActive(false);
            var room=activeForest.rooms[activeForestRoom];
            for(int direction=0;direction<4;direction++)
            {
                bool exists=room.links[direction]>=0;
                if(forestGates[direction]==null&&exists)
                {
                    var go=new GameObject("ForestGate_"+direction);go.transform.SetParent(transform);
                    go.AddComponent<SpriteRenderer>();go.AddComponent<GroundDepth>();forestGates[direction]=go;
                }
                var gate=forestGates[direction];if(gate==null)continue;gate.SetActive(exists);
                gate.transform.position=ForestGatePositions[direction];
                var renderer=gate.GetComponent<SpriteRenderer>();renderer.sprite=art.LoadDungeonGate(roomCleared);
                renderer.flipX=direction==2;
                // Keep the foreground doorway readable above the bottom battle HUD.
                if(direction==3)gate.transform.position+=Vector3.up*.35f;
                float height=direction==1||direction==3?1.9f:2.6f;
                if(renderer.sprite!=null)gate.transform.localScale=Vector3.one*(height/renderer.sprite.bounds.size.y);
            }
        }
        Vector3 ClampDungeonPassage(Vector3 next)
        {
            if(mode!=GameMode.Dungeon)return next;
            if(activeForest==null)
            {
                if(next.x>GateBarrierX&&(!roomCleared||Mathf.Abs(next.y-GateY)>GateHalfLane))next.x=GateBarrierX;
                return next;
            }
            var links=activeForest.rooms[activeForestRoom].links;
            bool horizontal=roomCleared&&Mathf.Abs(next.y-GateY)<=GateHalfLane;
            bool vertical=roomCleared&&Mathf.Abs(next.x)<.65f;
            if(!horizontal||links[0]<0)next.x=Mathf.Min(next.x,GateBarrierX);
            if(!horizontal||links[2]<0)next.x=Mathf.Max(next.x,-GateBarrierX);
            if(!vertical||links[1]<0)next.y=Mathf.Min(next.y,CombatPlane.Back-.15f);
            if(!vertical||links[3]<0)next.y=Mathf.Max(next.y,CombatPlane.Front+.15f);
            return next;
        }
        void HandleForestRoomExit()
        {
            if(!roomCleared||jump.Airborne||Time.time<forestTransitionAt)return;
            if(bossRoom){ShowDungeonResult();return;}
            var p=player.transform.position;int direction=-1;
            if(p.x>GateX&&Mathf.Abs(p.y-GateY)<=GateHalfLane)direction=0;
            else if(p.x<-GateX&&Mathf.Abs(p.y-GateY)<=GateHalfLane)direction=2;
            else if(p.y>=CombatPlane.Back-.04f&&Mathf.Abs(p.x)<.65f)direction=1;
            else if(p.y<=CombatPlane.Front+.04f&&Mathf.Abs(p.x)<.65f)direction=3;
            if(direction<0)return;
            int target=activeForest.rooms[activeForestRoom].links[direction];if(target<0)return;
            clearedForestRooms[activeForestRoom]=true;
            ResetCombatAction();jump.Reset();activeForestRoom=target;SpawnConfiguredForestRoom();
            player.transform.position=direction==0?new Vector3(-7.1f,GateY):direction==2?new Vector3(7.1f,GateY):direction==1?new Vector3(0,CombatPlane.Front+.45f):new Vector3(0,CombatPlane.Back-.45f);
        }
        void ClearForestHazards(){foreach(var hazard in GetComponentsInChildren<ForestHazard>()){hazard.gameObject.SetActive(false);Destroy(hazard.gameObject);}}
        void ApplyForestPoison(int power){if(mode!=GameMode.Dungeon)return;forestPoisonTicks=3;forestPoisonDamage=Mathf.Max(1,power/4);forestPoisonTimer=1;}
        void ClearForestPoison(){forestPoisonTicks=0;forestPoisonTimer=0;}
        void UpdateForestStatus(float dt)
        {
            if(mode!=GameMode.Dungeon||forestPoisonTicks<=0)return;
            forestPoisonTimer-=dt;
            if(forestPoisonTimer<=0)
            {
                forestPoisonTimer+=1;forestPoisonTicks--;
                // Damage over time must not restart hurt poses or cancel the player's attack.
                if(hp<=forestPoisonDamage)TakePlayerDamage(forestPoisonDamage);
                else{hp-=forestPoisonDamage;Refresh();}
            }
        }
        void DrawForestMinimap()
        {
            if(mode!=GameMode.Dungeon||activeForest==null||SystemPanelOpen)return;
            float cell=Mathf.Clamp(Screen.height*.023f,12,20),size=cell-3;
            float left=Screen.width-activeForest.width*cell-24,top=26;
            Color previous=GUI.color;
            foreach(var room in activeForest.rooms)
            {
                int index=Array.IndexOf(activeForest.rooms,room);
                if(!visitedForestRooms[index]&&!room.boss&&index!=activeForest.start)continue;
                GUI.color=index==activeForestRoom?new Color(1,.85f,.35f,.95f):room.boss?new Color(.8f,.24f,.2f,.85f):clearedForestRooms[index]?new Color(.48f,.61f,.67f,.85f):new Color(.24f,.3f,.37f,.85f);
                GUI.DrawTexture(new Rect(left+room.x*cell,top+room.y*cell,size,size),Texture2D.whiteTexture);
                if(!visitedForestRooms[index])continue;
                foreach(int d in new[]{0,1})if(room.links[d]>=0)
                {
                    GUI.color=new Color(.65f,.69f,.66f,.8f);
                    GUI.DrawTexture(d==0?new Rect(left+room.x*cell+size,top+room.y*cell+size*.4f,3,size*.2f):new Rect(left+room.x*cell+size*.4f,top+room.y*cell-3,size*.2f,3),Texture2D.whiteTexture);
                }
            }
            GUI.color=previous;
        }
        void CreateForestHazard(string kind,Vector3 origin,int facing,int damage)
        {
            var go=new GameObject("ForestAttack_"+kind);go.transform.SetParent(transform);
            bool projectile=kind=="throw";
            go.transform.position=projectile?origin:CombatPlane.Clamp(player.transform.position);
            var renderer=go.AddComponent<SpriteRenderer>();renderer.sprite=art.LoadForestAttackEffect(kind);
            renderer.sortingOrder=CombatPlane.Order(go.transform.position.y)+1;
            go.AddComponent<ForestHazard>().Init(this,kind,facing,damage,projectile);
        }
        public sealed class ForestHazard:MonoBehaviour
        {
            GameBootstrap game;string kind;float elapsed;int facing,damage;bool projectile,hit;
            SpriteRenderer rendererCache;
            ForestLightning lightning;
            public void Init(GameBootstrap owner,string type,int direction,int power,bool travels)
            {
                game=owner;kind=type;facing=direction;damage=power;projectile=travels;
                rendererCache=GetComponent<SpriteRenderer>();
                if(kind=="lightning")
                {
                    rendererCache.enabled=false;
                    lightning=gameObject.AddComponent<ForestLightning>();lightning.Build(rendererCache.sortingOrder);
                    return;
                }
                if(rendererCache.sprite==null){Debug.LogError("Missing forest attack effect: "+type);Destroy(gameObject);return;}
                transform.localScale=Vector3.one*(projectile?.55f:1.6f)/Mathf.Max(.1f,rendererCache.sprite.bounds.size.x);
            }
            void Update()
            {
                if(game==null||game.mode!=GameMode.Dungeon){Destroy(gameObject);return;}
                if(Time.time<game.hitStopUntil||game.SystemPanelOpen)return;
                elapsed+=Time.deltaTime;
                if(lightning!=null)lightning.Show(elapsed);
                Color color=kind=="fire"?new Color(1,.35f,.12f):kind=="ice"||kind=="cold"?new Color(.4f,.8f,1):kind=="poison"?new Color(.55f,1,.25f):new Color(1,.9f,.45f);
                if(projectile)
                {
                    transform.position+=Vector3.right*facing*4.5f*Time.deltaTime;
                    rendererCache.color=color;
                    if(!hit&&Mathf.Abs(transform.position.x-game.player.transform.position.x)<.4f&&Mathf.Abs(transform.position.y-game.player.transform.position.y)<.4f&&game.jump.Height<1.1f){Deal();Destroy(gameObject);}
                    if(elapsed>2.8f)Destroy(gameObject);return;
                }
                // Target is fixed on cast; the player can leave the warning before impact.
                color.a=elapsed<.65f?.25f+.2f*Mathf.Sin(elapsed*24f):Mathf.Clamp01((1.1f-elapsed)/.45f);
                rendererCache.color=color;
                if(elapsed>=.65f&&!hit)
                {
                    hit=true;
                    if(Mathf.Abs(transform.position.x-game.player.transform.position.x)<.9f&&Mathf.Abs(transform.position.y-game.player.transform.position.y)<.45f&&game.jump.Height<1.1f)Deal();
                }
                if(elapsed>1.1f)Destroy(gameObject);
            }
            void Deal(){hit=true;game.TakePlayerDamage(game.MitigateMonsterDamage(damage));if(kind=="ice"||kind=="cold")game.forestSlowUntil=Time.time+1.5f;}
        }
    }
}
