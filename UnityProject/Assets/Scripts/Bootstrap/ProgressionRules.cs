using System;
using System.Collections.Generic;

namespace DNF60Prototype
{
    public enum Job { GhostKnight, Fighter, Berserker, Asura, Striker, NenMaster }
    public enum ItemSlot { Weapon, Top, Bracelet, Shoulder, Bottom, Belt, Shoes, Necklace, Ring, Title }
    [Serializable]
    public class ItemData
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name, SourceId, Rarity="普通";
        public ItemSlot Slot;
        public int Level, Attack, Defense, Enhance;
        public bool BindOnEquip, Bound;
        public string[] Jobs = new string[0];
        public ItemData(string n, ItemSlot s, int l, int a, int d) { Name=n; Slot=s; Level=l; Attack=a; Defense=d; }
        public override string ToString() => $"[{Rarity}] {Name} (Lv.{Level})  攻{Attack} 防{Defense}";
    }
    [Serializable] public class EquipmentDefinition
    {
        public string Id, Name, Rarity="普通", SourceId;
        public ItemSlot Slot; public int Level, Attack, Defense; public string[] Jobs=new string[0];
        public ItemData Create(){return new ItemData(Name,Slot,Level,Attack,Defense){Id=Guid.NewGuid().ToString("N"),SourceId=SourceId??Id,Rarity=Rarity,Jobs=Jobs??new string[0]};}
    }
    public static class EquipmentCatalog
    {
        static readonly EquipmentDefinition[] entries={
            new EquipmentDefinition{Id="wood_sword",Name="生锈的短剑",Rarity="普通",SourceId="equipment.lv1.weapon",Slot=ItemSlot.Weapon,Level=1,Attack=3,Jobs=new[]{"GhostKnight"}},
            new EquipmentDefinition{Id="forest_blade",Name="森林短剑",Rarity="普通",SourceId="equipment.lv5.weapon",Slot=ItemSlot.Weapon,Level=5,Attack=8,Jobs=new[]{"GhostKnight"}},
            new EquipmentDefinition{Id="goblin_cloth",Name="哥布林皮甲",Rarity="普通",SourceId="equipment.lv3.top",Slot=ItemSlot.Top,Level=3,Defense=4},
            new EquipmentDefinition{Id="forest_boots",Name="森林皮靴",Rarity="普通",SourceId="equipment.lv7.shoes",Slot=ItemSlot.Shoes,Level=7,Defense=3},
            new EquipmentDefinition{Id="taurus_armor",Name="牛头护甲",Rarity="稀有",SourceId="equipment.lv10.top",Slot=ItemSlot.Top,Level=10,Defense=10},
            new EquipmentDefinition{Id="sauta_greatsword",Name="萨乌塔巨剑",Rarity="稀有",SourceId="equipment.lv15.weapon",Slot=ItemSlot.Weapon,Level=15,Attack=22,Jobs=new[]{"GhostKnight"}}
        };
        public static ItemData DropFor(string enemy,int level){
            EquipmentDefinition best=null;foreach(var e in entries)if(e.Level<=level&&((enemy.StartsWith("领主")&&e.Rarity=="稀有")||(!enemy.StartsWith("领主")&&e.Rarity=="普通"))&&(best==null||e.Level>best.Level))best=e;
            return best==null?null:best.Create();
        }
    }
    public static class EquipmentRules
    {
        public static bool CanEquip(ItemData item, int level, Job job)
        {
            if(item==null || item.Level<1 || item.Level>25 || item.Level>level || !Enum.IsDefined(typeof(ItemSlot),item.Slot)) return false;
            if(item.Jobs==null || item.Jobs.Length==0) return true;
            return Array.IndexOf(item.Jobs,job.ToString())>=0;
        }
        public static int Attack(IEnumerable<ItemData> items) { int total=0;foreach(var i in items)total+=Math.Max(0,i.Attack);return total; }
        public static int Defense(IEnumerable<ItemData> items) { int total=0;foreach(var i in items)total+=Math.Max(0,i.Defense);return total; }
        public static string SlotName(ItemSlot slot) => new[]{"武器","上衣","手镯","护肩","下装","腰带","鞋","项链","戒指","称号"}[(int)slot];
    }
    public enum ObjectiveKind { Kill, Talk, ClearDungeon }
    public enum QuestStatus { Accepted, Ready, Completed }
    [Serializable] public class QuestObjective { public ObjectiveKind Kind; public string Target; public int Count=1; }
    [Serializable] public class QuestDefinition
    {
        public string Id, Name, Description, StartNpc, EndNpc, Source;
        public bool Verified60;
        public int Level=1, Experience, Gold;
        public string[] Prerequisites=new string[0];
        public QuestObjective[] Objectives=new QuestObjective[0];
    }
    [Serializable] public class QuestProgress
    {
        public string Id; public QuestStatus Status; public int[] Counts;
    }
    public sealed class QuestJournal
    {
        public readonly List<QuestProgress> Entries=new List<QuestProgress>();
        public readonly Dictionary<string,QuestDefinition> Definitions=new Dictionary<string,QuestDefinition>();
        public QuestJournal(IEnumerable<QuestDefinition> definitions)
        {
            foreach(var d in definitions)
            {
                if(d==null||!d.Verified60||string.IsNullOrEmpty(d.Source)||string.IsNullOrEmpty(d.Id)||d.Level<1||d.Level>25||d.Experience<0||d.Gold<0||d.Objectives==null||d.Objectives.Length==0||string.IsNullOrEmpty(d.StartNpc)||string.IsNullOrEmpty(d.EndNpc))continue;
                bool valid=true;foreach(var o in d.Objectives)if(o==null||o.Count<=0||string.IsNullOrEmpty(o.Target)||!Enum.IsDefined(typeof(ObjectiveKind),o.Kind))valid=false;
                if(valid&&!Definitions.ContainsKey(d.Id))Definitions.Add(d.Id,d);
            }
        }
        public QuestProgress Find(string id) => string.IsNullOrEmpty(id)?null:Entries.Find(x=>x.Id==id);
        public bool CanAccept(string id,int level,string npc)
        {
            if(string.IsNullOrEmpty(id)||!Definitions.TryGetValue(id,out var d)||Find(id)!=null||level<d.Level||npc!=d.StartNpc)return false;
            foreach(var prerequisite in d.Prerequisites??new string[0])if(Find(prerequisite)?.Status!=QuestStatus.Completed)return false;
            return true;
        }
        public bool Accept(string id,int level,string npc)
        {
            if(!CanAccept(id,level,npc))return false;
            Entries.Add(new QuestProgress{Id=id,Status=QuestStatus.Accepted,Counts=new int[Definitions[id].Objectives.Length]});return true;
        }
        public void Record(ObjectiveKind kind,string target)
        {
            foreach(var p in Entries)
            {
                if(p.Status!=QuestStatus.Accepted||!Definitions.TryGetValue(p.Id,out var d))continue;
                for(int i=0;i<d.Objectives.Length;i++){var o=d.Objectives[i];if(o.Kind==kind&&o.Target==target)p.Counts[i]=Math.Min(o.Count,p.Counts[i]+1);}
                Refresh(p,d);
            }
        }
        static void Refresh(QuestProgress p,QuestDefinition d)
        {
            bool ready=true;for(int i=0;i<d.Objectives.Length;i++)if(p.Counts[i]<d.Objectives[i].Count)ready=false;
            p.Status=ready?QuestStatus.Ready:QuestStatus.Accepted;
        }
        public QuestDefinition Claim(string id,string npc)
        {
            var p=Find(id);if(p==null||p.Status!=QuestStatus.Ready||!Definitions.TryGetValue(id,out var d)||npc!=d.EndNpc)return null;
            p.Status=QuestStatus.Completed;return d;
        }
        public void Restore(IEnumerable<QuestProgress> entries)
        {
            Entries.Clear();foreach(var p in entries){if(p==null||string.IsNullOrEmpty(p.Id)||Find(p.Id)!=null||!Definitions.TryGetValue(p.Id,out var d)||p.Counts==null||p.Counts.Length!=d.Objectives.Length)continue;
                for(int i=0;i<p.Counts.Length;i++)p.Counts[i]=Math.Max(0,Math.Min(d.Objectives[i].Count,p.Counts[i]));
                if(p.Status!=QuestStatus.Completed)Refresh(p,d);Entries.Add(p);
            }
        }
    }
}
