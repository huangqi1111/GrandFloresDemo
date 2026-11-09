using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

namespace DNF60Prototype
{
    public sealed class NpcInteraction : MonoBehaviour
    {
        GameBootstrap owner; SpriteRenderer rendererCache; Color normalColor; public string Id { get; private set; }
        public void Setup(GameBootstrap game,string id,string label){owner=game;Id=id;gameObject.name="NPC_"+id;rendererCache=GetComponent<SpriteRenderer>();if(rendererCache!=null)normalColor=rendererCache.color;}
        void OnMouseDown(){if(owner!=null)owner.OpenNpcQuests(Id);}
        void OnMouseEnter(){if(rendererCache!=null)rendererCache.color=new Color(1f,.82f,.35f);}
        void OnMouseExit(){if(rendererCache!=null)rendererCache.color=normalColor;}
    }
    public sealed partial class GameBootstrap
    {
        [Serializable] class QuestCatalog { public QuestDefinition[] Quests=new QuestDefinition[0]; }
        [Serializable] class SaveData
        {
            public int Version=3,Level,Exp,Gold,Hp,Mp,Potions,Stage;
            public Job Job;
            public Dictionary<string,int> SkillRanks,SkillGrants;
            public string[] SkillSlots,SkillKeys;
            public List<ItemData> Items=new List<ItemData>();
            public List<string> Equipped=new List<string>();
            public List<QuestProgress> Quests=new List<QuestProgress>();
        }
        const string SaveKey="DNF60.Progression.v1";
        QuestJournal journal=new QuestJournal(new QuestDefinition[0]);
        bool inventoryOpen,questsOpen;
        Vector2 bagScroll,questScroll;
        string systemMessage="",activeNpc=""; bool dungeonSelectionOpen; string dungeonSelectionTitle=""; string[] dungeonSelectionOptions=new string[0]; int selectedDungeonIndex; Texture2D dungeonSelectionTexture; readonly Dictionary<string,Texture2D> dungeonBossCovers=new Dictionary<string,Texture2D>();
        int TotalAttack=>baseAttack+EquipmentRules.Attack(equipped.Values)+(Time.time<khazanUntil?Mathf.RoundToInt(SkillRankValue(2,0,15)):0);
        int TotalDefense=>baseDefense+EquipmentRules.Defense(equipped.Values);
        bool SystemPanelOpen=>dungeonResultOpen||dungeonSelectionOpen||skillTreeOpen||inventoryOpen||questsOpen||jobPanel!=null&&jobPanel.activeSelf;
        void InitializeSystems()
        {
            var asset=Resources.Load<TextAsset>("DNF60/Data/Quests60");
            if(asset!=null){try{var catalog=JsonConvert.DeserializeObject<QuestCatalog>(asset.text);journal=new QuestJournal(catalog?.Quests??new QuestDefinition[0]);}catch(Exception e){Debug.LogError("Quest catalog: "+e.Message);}}
        }
        bool Equip(ItemData item)
        {
            if(!inventory.Contains(item)||!EquipmentRules.CanEquip(item,level,job)){systemMessage="等级或职业不符合装备要求。";return false;}
            equipped[item.Slot]=item;if(item.BindOnEquip)item.Bound=true;Save();Refresh();return true;
        }
        public bool ReceiveEquipment(ItemData item)
        {
            if(item==null||string.IsNullOrEmpty(item.SourceId)||string.IsNullOrEmpty(item.Id)||item.Level<1||item.Level>MaxLevel||!Enum.IsDefined(typeof(ItemSlot),item.Slot)||inventory.Exists(x=>x.Id==item.Id))return false;
            inventory.Add(item);Save();Refresh();return true;
        }
        void Unequip(ItemSlot slot){equipped.Remove(slot);Save();Refresh();}
        void RecordQuest(ObjectiveKind kind,string target){journal.Record(kind,target);}
        // NPC interaction must call this with the actual NPC's stable identifier.
        public void OpenNpcQuests(string npcId)
        {
            if(mode!=GameMode.Town||string.IsNullOrEmpty(npcId))return;
            if(npcId.StartsWith("portal_")){UseWorldPortal(npcId);return;}
            activeNpc=npcId;questsOpen=true;journal.Record(ObjectiveKind.Talk,npcId);Save();
        }
        void ClaimQuest(string id)
        {
            if(mode!=GameMode.Town||string.IsNullOrEmpty(activeNpc))return;
            var reward=journal.Claim(id,activeNpc);if(reward==null)return;
            gold+=reward.Gold;if(level<MaxLevel)exp+=reward.Experience;CheckLevel();Save();Refresh();
        }
        void OnGUI()
        {
            DrawForestMinimap();
            if(dungeonResultOpen)DrawDungeonResult();
            else if(skillTreeOpen)DrawSkillTree();
            else if(dungeonSelectionOpen)DrawDungeonSelection(603);
            else if(inventoryOpen)GUI.ModalWindow(601,new Rect(20,170,Mathf.Min(620,Screen.width-40),Mathf.Max(200,Screen.height-190)),DrawBag,"背包 / 装备");
            else if(questsOpen)GUI.ModalWindow(602,new Rect(20,170,Mathf.Min(620,Screen.width-40),Mathf.Max(200,Screen.height-190)),DrawQuests,"任务日志");
        }
        static string DungeonBossCoverId(string dungeon)
        {
            switch(dungeon)
            {
                case "幽暗密林":return "mirkwood";
                case "雷鸣废墟":return "thunder_ruins";
                case "格拉卡":return "graka";
                case "烈焰格拉卡":return "blazing_graka";
                case "冰霜幽暗密林":return "frost_mirkwood";
                case "猛毒雷鸣废墟":return "poison_thunder";
                case "暗黑雷鸣废墟":return "dark_thunder";
                default:return null;
            }
        }
        Texture2D LoadDungeonBossCover(string dungeon)
        {
            string id=DungeonBossCoverId(dungeon);
            if(id==null)return null;
            if(!dungeonBossCovers.TryGetValue(id,out var cover))
            {
                cover=Resources.Load<Texture2D>("DNF60/UI/DungeonCovers/"+id);
                if(cover!=null)dungeonBossCovers.Add(id,cover);
                else Debug.LogError("Missing dungeon boss cover: "+id);
            }
            return cover;
        }
        void DrawDungeonSelection(int window)
        {
            if(dungeonSelectionTexture==null)dungeonSelectionTexture=Resources.Load<Texture2D>("DNF60/UI/dungeon_select_map");
            if(dungeonSelectionTexture!=null)GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),dungeonSelectionTexture,ScaleMode.StretchToFill);
            var title=new GUIStyle(GUI.skin.label){fontSize=Mathf.RoundToInt(Screen.height*.032f),alignment=TextAnchor.MiddleCenter,fontStyle=FontStyle.Bold};title.normal.textColor=new Color(1f,.86f,.52f);
            var body=new GUIStyle(GUI.skin.label){fontSize=Mathf.RoundToInt(Screen.height*.020f),alignment=TextAnchor.MiddleCenter,wordWrap=true};body.normal.textColor=new Color(.95f,.91f,.78f);
            var button=new GUIStyle(GUI.skin.button){fontSize=Mathf.RoundToInt(Screen.height*.019f),fontStyle=FontStyle.Bold};button.normal.textColor=new Color(1f,.88f,.50f);button.hover.textColor=Color.white;
            int count=dungeonSelectionOptions==null?0:dungeonSelectionOptions.Length;if(count==0)return;selectedDungeonIndex=Mathf.Clamp(selectedDungeonIndex,0,count-1);
            var selected=dungeonSelectionOptions[selectedDungeonIndex];int[] levels={5,8,13,15,11,10,17};
            var previewRect=new Rect(Screen.width*(25f/1536f),Screen.height*(575f/1024f),Screen.width*(447f/1536f),Screen.height*(390f/1024f));
            var selectedCover=LoadDungeonBossCover(selected);
            // Opaque scene covers replace the baked forest completely, within the existing frame.
            GUI.DrawTexture(previewRect,selectedCover!=null?selectedCover:Texture2D.blackTexture,ScaleMode.StretchToFill);
            GUI.Label(new Rect(Screen.width*.32f,Screen.height*.045f,Screen.width*.36f,Screen.height*.055f),dungeonSelectionTitle+" · "+selected,title);
            GUI.Label(new Rect(Screen.width*.33f,Screen.height*.095f,Screen.width*.34f,Screen.height*.085f),"推荐等级 Lv."+levels[Mathf.Min(selectedDungeonIndex,levels.Length-1)]+"\n选择地下城后进入冒险区域。",body);
            Vector2[] nodes={new Vector2(.18f,.32f),new Vector2(.47f,.34f),new Vector2(.80f,.31f),new Vector2(.31f,.51f),new Vector2(.51f,.55f),new Vector2(.72f,.60f),new Vector2(.89f,.49f)};
            for(int i=0;i<count&&i<nodes.Length;i++){float w=Screen.width*.13f,h=Screen.height*.055f;var r=new Rect(Screen.width*nodes[i].x-w*.5f,Screen.height*nodes[i].y-h*.5f,w,h);GUI.color=i==selectedDungeonIndex?new Color(1f,.78f,.25f):Color.white;if(GUI.Button(r,dungeonSelectionOptions[i],button))selectedDungeonIndex=i;GUI.color=Color.white;}
            var action=new GUIStyle(GUIStyle.none){alignment=TextAnchor.MiddleCenter,fontSize=Mathf.RoundToInt(Screen.height*.019f),fontStyle=FontStyle.Bold};action.normal.background=null;action.hover.background=null;action.normal.textColor=new Color(1f,.88f,.50f);action.hover.textColor=Color.white;
            if(GUI.Button(new Rect(Screen.width*(994f/1536f),Screen.height*(892f/1024f),Screen.width*(243f/1536f),Screen.height*(89f/1024f)),"进入地下城",action)){dungeonSelectionOpen=false;EnterDungeon();}
            if(GUI.Button(new Rect(Screen.width*(1247f/1536f),Screen.height*(892f/1024f),Screen.width*(233f/1536f),Screen.height*(89f/1024f)),"返回城镇",action)){dungeonSelectionOpen=false;systemMessage="";Refresh();}
        }
        void DrawBag(int window)
        {
            if(GUILayout.Button("关闭")){inventoryOpen=false;return;}
            GUILayout.Label(systemMessage);bagScroll=GUILayout.BeginScrollView(bagScroll);
            foreach(ItemSlot slot in Enum.GetValues(typeof(ItemSlot)))
            {
                GUILayout.BeginHorizontal();GUILayout.Label(EquipmentRules.SlotName(slot),GUILayout.Width(55));
                if(equipped.TryGetValue(slot,out var item)){GUILayout.Label(item.Name);if(GUILayout.Button("卸下",GUILayout.Width(60)))Unequip(slot);}
                else GUILayout.Label("空");GUILayout.EndHorizontal();
            }
            GUILayout.Label("背包装备");int pageSize=6;int pageCount=Mathf.Max(1,(inventory.Count+pageSize-1)/pageSize);bagPage=Mathf.Clamp(bagPage,0,pageCount-1);GUILayout.BeginHorizontal();if(GUILayout.Button("上一页",GUILayout.Width(75)))bagPage=Mathf.Max(0,bagPage-1);GUILayout.Label($"第 {bagPage+1}/{pageCount} 页");if(GUILayout.Button("下一页",GUILayout.Width(75)))bagPage=Mathf.Min(pageCount-1,bagPage+1);GUILayout.EndHorizontal();
            int start=bagPage*pageSize;int end=Mathf.Min(inventory.Count,start+pageSize);for(int index=start;index<end;index++)
            {
                var item=inventory[index];
                bool worn=equipped.TryGetValue(item.Slot,out var current)&&current==item;
                GUILayout.BeginHorizontal();if(GUILayout.Button(item.Name,GUILayout.Width(150)))selectedItem=item;GUILayout.Label(item+ (item.Bound?" [已绑定]":""));
                GUI.enabled=!worn&&EquipmentRules.CanEquip(item,level,job);
                if(GUILayout.Button(worn?"已穿戴":"穿戴",GUILayout.Width(60)))Equip(item);
                GUI.enabled=true;GUILayout.EndHorizontal();
                GUILayout.Label("来源："+(string.IsNullOrEmpty(item.SourceId)?"未知":item.SourceId),GUILayout.Width(420));
            }
            if(selectedItem!=null){GUILayout.Space(8);GUILayout.Label("物品详情");GUILayout.Label($"{selectedItem.Name}  [{selectedItem.Rarity}]\n部位：{EquipmentRules.SlotName(selectedItem.Slot)}  等级：{selectedItem.Level}\n攻击：{selectedItem.Attack}  防御：{selectedItem.Defense}\n{(EquipmentRules.CanEquip(selectedItem,level,job)?"当前角色可以装备":"当前等级或职业不符合")}");}
            GUILayout.EndScrollView();
        }
        void DrawQuests(int window)
        {
            if(GUILayout.Button("关闭")){questsOpen=false;activeNpc="";return;}
            questScroll=GUILayout.BeginScrollView(questScroll);
            if(journal.Definitions.Count==0)GUILayout.Label("暂无可接任务。");
            foreach(var definition in journal.Definitions.Values)
            {
                var progress=journal.Find(definition.Id);
                if(progress==null&&!journal.CanAccept(definition.Id,level,activeNpc))continue;
                GUILayout.Label(definition.Name+"  Lv."+definition.Level);GUILayout.Label(definition.Description);
                if(progress==null){if(GUILayout.Button("接受任务")){journal.Accept(definition.Id,level,activeNpc);Save();}continue;}
                for(int i=0;i<definition.Objectives.Length;i++)GUILayout.Label(definition.Objectives[i].Target+"  "+progress.Counts[i]+"/"+definition.Objectives[i].Count);
                if(progress.Status==QuestStatus.Completed)GUILayout.Label("已完成");
                else if(progress.Status==QuestStatus.Ready){GUILayout.Label("返回交付："+definition.EndNpc);GUI.enabled=mode==GameMode.Town&&activeNpc==definition.EndNpc;if(GUILayout.Button("完成任务，领取奖励"))ClaimQuest(definition.Id);GUI.enabled=true;}
            }
            GUILayout.EndScrollView();
        }
        void SaveProgression()
        {
            var data=new SaveData{Level=level,Exp=exp,Gold=gold,Hp=hp,Mp=mp,Potions=potions,Stage=dungeonStage,Job=job,Items=new List<ItemData>(inventory),Quests=new List<QuestProgress>(journal.Entries)};
            WriteSkillTreeSave(data);
            foreach(var item in equipped.Values)data.Equipped.Add(item.Id);
            PlayerPrefs.SetString(SaveKey,JsonConvert.SerializeObject(data));PlayerPrefs.Save();
        }
        bool LoadProgression()
        {
            if(!PlayerPrefs.HasKey(SaveKey))return false;
            string original=PlayerPrefs.GetString(SaveKey);
            SaveData data;try{data=JsonConvert.DeserializeObject<SaveData>(original);}catch(Exception e){PlayerPrefs.SetString(SaveKey+".recovery",original);PlayerPrefs.Save();Debug.LogError("Save not loaded; preserved recovery copy: "+e.Message);return false;}
            if(data==null||(data.Version!=1&&data.Version!=2&&data.Version!=3)){PlayerPrefs.SetString(SaveKey+".recovery",original);PlayerPrefs.Save();return false;}
            level=Mathf.Clamp(data.Level,1,MaxLevel);exp=level==MaxLevel?0:Mathf.Max(0,data.Exp);gold=Mathf.Max(0,data.Gold);job=Enum.IsDefined(typeof(Job),data.Job)?data.Job:Job.GhostKnight;
            baseAttack=12+(level-1)*3;baseDefense=4+level-1;
            hp=Mathf.Clamp(data.Hp,1,100+level*12);mp=Mathf.Clamp(data.Mp,0,MaxMana);potions=Mathf.Max(0,data.Potions);dungeonStage=Mathf.Max(0,data.Stage);
            inventory.Clear();equipped.Clear();var ids=new HashSet<string>();
            foreach(var item in data.Items??new List<ItemData>())if(item!=null&&!string.IsNullOrEmpty(item.Id)&&ids.Add(item.Id)&&item.Level>=1&&item.Level<=25&&Enum.IsDefined(typeof(ItemSlot),item.Slot)){item.Attack=Mathf.Max(0,item.Attack);item.Defense=Mathf.Max(0,item.Defense);inventory.Add(item);}
            foreach(var id in data.Equipped??new List<string>()){var item=inventory.Find(x=>x.Id==id);if(EquipmentRules.CanEquip(item,level,job))equipped[item.Slot]=item;}
            journal.Restore(data.Quests??new List<QuestProgress>());RestoreSkillTree(data);return true;
        }
        void OnApplicationPause(bool paused){if(paused&&player!=null&&persistenceEnabled)SaveProgression();}
        void OnApplicationQuit(){if(player!=null&&persistenceEnabled)SaveProgression();}
    }
}
