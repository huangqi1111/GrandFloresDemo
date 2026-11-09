using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace DNF60Prototype
{
    public sealed partial class GameBootstrap
    {
        [Serializable] sealed class SkillTreeCatalog { public SkillRule[] Skills; public int[] SpGains; }
        [Serializable] sealed class SkillRule
        {
            public string Source, Description;
            public int Id, Cost, Required, Step, Max, Group, X, Y;
            public int[] Prerequisites, Mp;
            public float[][] Values;
        }
        SkillTreeCatalog skillRules;
        bool skillTreeOpen;
        int[] skillRanks, grantedSkillRanks, draftSkillRanks;
        int[] hotbarSkills;
        KeyCode[] hotbarKeys;
        Sprite[] treeIcons;
        Text[] skillKeyLabels;
        float[] baseSkillDamage;
        int selectedSkill, dragSkill=-1, dragSlot=-1, captureSlot=-1;
        Vector2 dragOrigin;
        bool draggingSkill;
        string skillMessage="";
        Texture2D skillTreeTexture;
        GUIStyle treeLabel, treeSmall, treeTitle, treeButton;
        static readonly KeyCode[] DefaultSkillKeys={KeyCode.Q,KeyCode.W,KeyCode.E,KeyCode.R,KeyCode.T,KeyCode.Y,KeyCode.A,KeyCode.S,KeyCode.D,KeyCode.F,KeyCode.G,KeyCode.H};

        Sprite LoadSkillIcon(HeroAction action)
        {
            var texture=Resources.Load<Texture2D>("DNF60/UI/Skills/"+action);
            if(texture==null)throw new InvalidOperationException("Missing redrawn skill icon: "+action);
            return Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),texture.width);
        }
        void InitializeSkillTree()
        {
            var data=Resources.Load<TextAsset>("DNF60/Data/SkillTree60");
            skillRules=JsonConvert.DeserializeObject<SkillTreeCatalog>(data.text);
            if(skillRules.Skills.Length!=skills.Count)throw new InvalidOperationException("Skill tree catalog does not match combat skills.");
            skillRanks=new int[skills.Count];grantedSkillRanks=new int[skills.Count];
            hotbarSkills=new int[]{5,6,7,8,9,10,1,2,3,4,-1,-1};hotbarKeys=(KeyCode[])DefaultSkillKeys.Clone();treeIcons=new Sprite[skills.Count];baseSkillDamage=new float[skills.Count];
            skillKeyLabels=new Text[12];
            for(int i=0;i<skills.Count;i++)
            {
                treeIcons[i]=LoadSkillIcon(skills[i].Action);baseSkillDamage[i]=skills[i].Damage;
                skills[i].Level=skillRules.Skills[i].Required;
            }
            for(int slot=0;slot<12;slot++)skillKeyLabels[slot]=skillIcons[slot].transform.parent.Find("KeyBinding").GetComponent<Text>();
            // Character/swordman/swordman.chr grants Ghost Slash (5) and Upper Slash (46).
            skillRanks[0]=skillRanks[1]=grantedSkillRanks[0]=grantedSkillRanks[1]=1;
            ApplySkillRanks();
        }
        int TotalSkillPoints
        {
            get { int sum=0;for(int n=1;n<=level&&n<skillRules.SpGains.Length;n++)sum+=skillRules.SpGains[n];return sum; }
        }
        int SpentSkillPoints(int[] ranks)
        {
            int sum=0;for(int i=0;i<ranks.Length;i++)sum+=Mathf.Max(0,ranks[i]-grantedSkillRanks[i])*skillRules.Skills[i].Cost;return sum;
        }
        int AvailableSkillPoints=>Mathf.Max(0,TotalSkillPoints-SpentSkillPoints(skillRanks));
        int RequiredSkillLevel(int index,int rank)=>skillRules.Skills[index].Required+Mathf.Max(0,rank-1)*skillRules.Skills[index].Step;
        string LearningBlock(int index,int[] ranks)
        {
            var rule=skillRules.Skills[index];int next=ranks[index]+1;
            if(next>rule.Max)return "已达到技能等级上限";
            if(level<RequiredSkillLevel(index,next))return "需要角色 Lv."+RequiredSkillLevel(index,next);
            for(int p=0;p+1<rule.Prerequisites.Length;p+=2)
            {
                int prerequisite=Array.FindIndex(skillRules.Skills,x=>x.Id==rule.Prerequisites[p]);
                if(prerequisite<0||ranks[prerequisite]<rule.Prerequisites[p+1])return "前置技能等级不足";
            }
            if(TotalSkillPoints-SpentSkillPoints(ranks)<rule.Cost)return "SP 不足";
            return "";
        }
        bool QueueSkillLevel(int index)
        {
            if(!skillTreeOpen||index<0||index>=skills.Count)return false;
            skillMessage=LearningBlock(index,draftSkillRanks);if(skillMessage!="")return false;
            draftSkillRanks[index]++;return true;
        }
        bool ConfirmSkillLearning()
        {
            if(!skillTreeOpen||draftSkillRanks==null)return false;
            var check=(int[])skillRanks.Clone();
            for(int i=0;i<skills.Count;i++)while(check[i]<draftSkillRanks[i])
            {skillMessage=LearningBlock(i,check);if(skillMessage!="")return false;check[i]++;}
            skillRanks=check;ApplySkillRanks();Save();Refresh();skillMessage="技能学习已保存";return true;
        }
        void ToggleSkillTree()
        {
            if(dungeonSelectionOpen||dungeonResultOpen)return;
            skillTreeOpen=!skillTreeOpen;captureSlot=-1;dragSkill=dragSlot=-1;draggingSkill=false;
            if(!skillTreeOpen){draftSkillRanks=null;return;}
            inventoryOpen=questsOpen=false;activeNpc="";if(jobPanel!=null)jobPanel.SetActive(false);
            draftSkillRanks=(int[])skillRanks.Clone();skillMessage="";
        }
        void CloseSkillTree(){skillTreeOpen=false;draftSkillRanks=null;captureSlot=-1;dragSkill=dragSlot=-1;draggingSkill=false;}
        void ApplySkillRanks()
        {
            for(int i=0;i<skills.Count;i++)
            {
                var rule=skillRules.Skills[i];int rank=Mathf.Max(1,skillRanks[i]);
                skills[i].Damage=SkillRankDamage(i,rank);
                skills[i].Mana=Mathf.CeilToInt(SkillRankMana(i,rank)*(i==0||i==4?1f:1.25f));
                skills[i].CommandBound=false;skills[i].Key=KeyCode.None;
            }
            for(int slot=0;slot<12;slot++)if(hotbarSkills[slot]>=0)
            {var s=skills[hotbarSkills[slot]];s.Key=hotbarKeys[slot];s.CommandBound=true;}
            skills[0].Key=KeyCode.Z;skills[0].CommandBound=true;
            UpdateResourceRecovery();
        }
        float SkillRankDamage(int i,int rank)
        {
            var rule=skillRules.Skills[i];rank=Mathf.Max(1,rank);float factor=1;
            // Original dungeon level rows govern growth; preserve the established project's damage scale.
            if(skills[i].DealsDamage&&rule.Values!=null&&rule.Values.Length>=rank&&rule.Values[0].Length>0&&rule.Values[0][0]>0)
                factor=rule.Values[rank-1][0]/rule.Values[0][0];
            return baseSkillDamage[i]*factor;
        }
        int SkillRankMana(int i,int rank)
        {
            var mpRange=skillRules.Skills[i].Mp;
            return mpRange!=null&&mpRange.Length>0?Mathf.RoundToInt(Mathf.Lerp(mpRange[0],mpRange[mpRange.Length-1],(Mathf.Max(1,rank)-1)/49f)):skills[i].Mana;
        }
        float SkillRankValue(int i,int column,float fallback)
        {
            int rank=skillRanks[i]-1;var rows=skillRules.Skills[i].Values;
            return rank>=0&&rows!=null&&rank<rows.Length&&column<rows[rank].Length?rows[rank][column]:fallback;
        }
        bool AssignSkillSlot(int index,int slot,int fromSlot=-1)
        {
            if(index<=0||index>=skills.Count||slot<0||slot>=12||skillRanks[index]==0)return false;
            int old=Array.IndexOf(hotbarSkills,index),replaced=hotbarSkills[slot];
            if(old==slot)return true;
            if(old>=0)hotbarSkills[old]=fromSlot>=0?replaced:-1;
            hotbarSkills[slot]=index;ApplySkillRanks();Save();Refresh();return true;
        }
        void ClearSkillSlot(int slot){hotbarSkills[slot]=-1;ApplySkillRanks();Save();Refresh();}
        bool RebindSkillSlot(int slot,KeyCode key)
        {
            if(key<KeyCode.A||key>KeyCode.Z||key==KeyCode.K||key==KeyCode.I||key==KeyCode.X||key==KeyCode.C||key==KeyCode.B||key==KeyCode.Z||key==KeyCode.P)return false;
            int old=Array.IndexOf(hotbarKeys,key);if(old>=0)hotbarKeys[old]=hotbarKeys[slot];
            hotbarKeys[slot]=key;ApplySkillRanks();Save();Refresh();return true;
        }
        void RefreshSkillSlots()
        {
            if(skillRanks==null)return;
            for(int slot=0;slot<12;slot++)
            {
                int i=hotbarSkills[slot];bool filled=i>=0&&skillRanks[i]>0;float remain=filled?Mathf.Max(0,skills[i].ReadyAt-Time.time):0;
                bool usable=filled&&skillRanks[i]>0&&level>=skills[i].Level;
                skillIcons[slot].enabled=filled;skillIcons[slot].sprite=filled?treeIcons[i]:null;
                skillIcons[slot].color=usable&&remain<=0?(mp>=skills[i].Mana?Color.white:new Color(.62f,.62f,.62f,1)):new Color(.42f,.42f,.42f,1);
                skillCooldownMasks[slot].fillAmount=0;
                skillCooldownLabels[slot].text=usable&&remain>0?Mathf.CeilToInt(remain).ToString():"";
                skillKeyLabels[slot].text=filled?hotbarKeys[slot].ToString():"";
            }
        }
        void RestoreSkillTree(SaveData data)
        {
            if(data.Version<3){hotbarSkills=new[]{5,6,7,8,9,10,1,2,3,4,-1,-1};hotbarKeys=(KeyCode[])DefaultSkillKeys.Clone();}
            if(data.SkillRanks==null)
            {
                // Old saves used automatic unlocks. Preserve their already usable skills as free grants.
                for(int i=0;i<skills.Count;i++)if(level>=skills[i].Level)skillRanks[i]=grantedSkillRanks[i]=1;
            }
            else
            {
                for(int i=0;i<skills.Count;i++)
                {
                    string id=skills[i].Action.ToString();int allowed=Mathf.Min(skillRules.Skills[i].Max,level<skills[i].Level?0:1+(level-skills[i].Level)/skillRules.Skills[i].Step);
                    skillRanks[i]=data.SkillRanks.TryGetValue(id,out int rank)?Mathf.Clamp(rank,0,allowed):0;
                    grantedSkillRanks[i]=data.SkillGrants!=null&&data.SkillGrants.TryGetValue(id,out int grant)?Mathf.Clamp(grant,0,skillRanks[i]):0;
                }
                skillRanks[0]=Mathf.Max(1,skillRanks[0]);skillRanks[1]=Mathf.Max(1,skillRanks[1]);grantedSkillRanks[0]=Mathf.Max(1,grantedSkillRanks[0]);grantedSkillRanks[1]=Mathf.Max(1,grantedSkillRanks[1]);
            }
            if(data.Version>=3&&data.SkillSlots!=null&&data.SkillSlots.Length==12)
            {
                var used=new HashSet<int>();for(int slot=0;slot<12;slot++)
                {int i=skills.FindIndex(x=>x.Action.ToString()==data.SkillSlots[slot]);hotbarSkills[slot]=i>0&&used.Add(i)?i:-1;}
            }
            if(data.Version>=3&&data.SkillKeys!=null&&data.SkillKeys.Length==12)
                for(int slot=0;slot<12;slot++)if(Enum.TryParse(data.SkillKeys[slot],out KeyCode key))RebindSkillSlotWithoutSave(slot,key);
            // Reject overspent/corrupt rank data without modifying the recovery copy or granting SP.
            if(SpentSkillPoints(skillRanks)>TotalSkillPoints)skillRanks=(int[])grantedSkillRanks.Clone();
            ApplySkillRanks();
        }
        void RebindSkillSlotWithoutSave(int slot,KeyCode key)
        {
            if(key<KeyCode.A||key>KeyCode.Z||key==KeyCode.K||key==KeyCode.I||key==KeyCode.X||key==KeyCode.C||key==KeyCode.B||key==KeyCode.Z||key==KeyCode.P)return;
            int old=Array.IndexOf(hotbarKeys,key);if(old>=0)hotbarKeys[old]=hotbarKeys[slot];hotbarKeys[slot]=key;
        }
        void WriteSkillTreeSave(SaveData data)
        {
            data.SkillRanks=new Dictionary<string,int>();data.SkillGrants=new Dictionary<string,int>();data.SkillSlots=new string[12];data.SkillKeys=new string[12];
            for(int i=0;i<skills.Count;i++){string id=skills[i].Action.ToString();data.SkillRanks[id]=skillRanks[i];data.SkillGrants[id]=grantedSkillRanks[i];}
            for(int slot=0;slot<12;slot++){data.SkillSlots[slot]=hotbarSkills[slot]<0?"":skills[hotbarSkills[slot]].Action.ToString();data.SkillKeys[slot]=hotbarKeys[slot].ToString();}
        }
        void TreeText(Rect rect,string text,GUIStyle style){GUI.Label(rect,text,style);}
        Rect TreeNodeRect(int i)
        {
            var rule=skillRules.Skills[i];int group=rule.Group;
            if(group==4)return new Rect(75,385,34,34);
            float groupStart=group==0?367:group==1?405:group==2?481:555;
            return new Rect(62+group*174+(rule.X-groupStart)*1.65f,136+(rule.Y+8)*1.7f,34,34);
        }
        static Rect TreeSlotRect(int slot)=>new Rect(350+slot%6*48,505+slot/6*48,34,34);
        void DrawSkillTree()
        {
            if(skillTreeTexture==null)skillTreeTexture=Resources.Load<Texture2D>("DNF60/UI/skill_tree_frame");
            if(treeLabel==null)
            {
                treeLabel=new GUIStyle(GUI.skin.label){fontSize=13,wordWrap=true};treeLabel.normal.textColor=new Color(.9f,.88f,.8f);
                treeSmall=new GUIStyle(treeLabel){fontSize=11};treeTitle=new GUIStyle(treeLabel){fontSize=18,fontStyle=FontStyle.Bold};treeTitle.normal.textColor=new Color(1,.85f,.5f);
                treeButton=new GUIStyle(GUI.skin.button){fontSize=13};treeButton.normal.textColor=new Color(1,.87f,.55f);
            }
            float scale=Mathf.Min(Screen.width/980f,Screen.height/660f);var origin=new Vector2((Screen.width-960*scale)/2,(Screen.height-640*scale)/2);
            var matrix=GUI.matrix;var mouse=(Event.current.mousePosition-origin)/scale;var ev=Event.current;
            GUI.matrix=Matrix4x4.TRS(origin,Quaternion.identity,new Vector3(scale,scale,1));
            GUI.DrawTexture(new Rect(0,0,960,640),skillTreeTexture);
            TreeText(new Rect(75,24,680,30),"鬼剑士 · 技能",treeTitle);
            if(GUI.Button(new Rect(913,19,29,31),"",GUIStyle.none)){CloseSkillTree();GUI.matrix=matrix;return;}
            string[] groups={"波动","剑术","血气","鬼神"};for(int g=0;g<4;g++)TreeText(new Rect(42+g*174,88,140,24),groups[g],treeTitle);
            TreeText(new Rect(48,350,130,22),"通用",treeLabel);
            for(int i=0;i<skills.Count;i++)
            {
                Rect r=TreeNodeRect(i);float remain=Mathf.Max(0,skills[i].ReadyAt-Time.time);bool learned=skillRanks[i]>0;
                GUI.color=learned&&remain<=0?Color.white:new Color(.42f,.42f,.42f);GUI.DrawTexture(r,treeIcons[i].texture);GUI.color=Color.white;
                if(i==selectedSkill){GUI.color=new Color(1,.85f,.45f);GUI.Box(new Rect(r.x-3,r.y-3,r.width+6,r.height+6),GUIContent.none);GUI.color=learned&&remain<=0?Color.white:new Color(.42f,.42f,.42f);GUI.DrawTexture(r,treeIcons[i].texture);GUI.color=Color.white;}
                if(i!=0&&remain>0&&learned)TreeText(r,Mathf.CeilToInt(remain).ToString(),treeSmall);
                TreeText(new Rect(r.x-22,r.y+35,90,18),skills[i].Name,treeSmall);
                TreeText(new Rect(r.x-6,r.y+51,75,18),"Lv."+draftSkillRanks[i]+" / "+skillRules.Skills[i].Max,treeSmall);
                if(ev.type==EventType.MouseDown&&ev.button==0&&r.Contains(mouse)){selectedSkill=i;dragSkill=i;dragSlot=-1;dragOrigin=mouse;draggingSkill=false;if(ev.clickCount==2)QueueSkillLevel(i);ev.Use();}
                if(GUI.Button(new Rect(r.x+36,r.y+12,20,20),"+",treeButton)){selectedSkill=i;QueueSkillLevel(i);}
            }
            int s=selectedSkill;var ruleSelected=skillRules.Skills[s];int shown=draftSkillRanks[s];
            TreeText(new Rect(739,86,192,28),skills[s].Name,treeTitle);
            TreeText(new Rect(739,121,190,140),ruleSelected.Description,treeLabel);
            TreeText(new Rect(739,264,186,125),"技能等级  "+shown+" / "+ruleSelected.Max+"\n消耗 MP  "+SkillRankMana(s,shown)+" → "+SkillRankMana(s,shown+1)+"\n"+(s==0?"独立按键 Z\n":"冷却时间  "+skills[s].Cooldown.ToString("0.#")+" 秒\n")+(skills[s].DealsDamage?"技能伤害  "+Mathf.RoundToInt(SkillRankDamage(s,shown))+" → "+Mathf.RoundToInt(SkillRankDamage(s,shown+1))+"\n":"")+"下一级需要 Lv."+RequiredSkillLevel(s,shown+1)+"\n学习消耗  "+ruleSelected.Cost+" SP",treeLabel);
            string block=LearningBlock(s,draftSkillRanks);GUI.enabled=block=="";
            if(GUI.Button(new Rect(738,396,88,28),shown==0?"学习 +":"升级 +",treeButton))QueueSkillLevel(s);
            GUI.enabled=shown>skillRanks[s];if(GUI.Button(new Rect(835,396,88,28),"撤销 -",treeButton))draftSkillRanks[s]--;
            GUI.enabled=true;TreeText(new Rect(739,430,188,36),block,treeSmall);
            TreeText(new Rect(45,495,285,26),"剩余 SP："+(TotalSkillPoints-SpentSkillPoints(draftSkillRanks))+" / "+TotalSkillPoints,treeTitle);
            TreeText(new Rect(45,528,285,66),"拖动已学技能到快捷栏\n右键槽位移除 · 点击键位修改\nK / Esc 关闭",treeLabel);
            for(int slot=0;slot<12;slot++)
            {
                Rect r=TreeSlotRect(slot);GUI.Box(new Rect(r.x-2,r.y-2,38,38),GUIContent.none);int i=hotbarSkills[slot];
                if(i>=0&&skillRanks[i]>0){GUI.color=skills[i].ReadyAt<=Time.time?Color.white:new Color(.42f,.42f,.42f);GUI.DrawTexture(r,treeIcons[i].texture);GUI.color=Color.white;float remain=skills[i].ReadyAt-Time.time;if(remain>0)TreeText(r,Mathf.CeilToInt(remain).ToString(),treeSmall);}
                var keyRect=new Rect(r.x,r.y+33,34,15);if(GUI.Button(keyRect,i>=0&&skillRanks[i]>0?hotbarKeys[slot].ToString():"",treeSmall)){captureSlot=slot;skillMessage="按下新的技能键（保留移动和系统按键）";}
                if(ev.type==EventType.MouseDown&&r.Contains(mouse))
                {if(ev.button==1){ClearSkillSlot(slot);ev.Use();}else if(ev.button==0&&i>=0){selectedSkill=i;dragSkill=i;dragSlot=slot;dragOrigin=mouse;draggingSkill=false;ev.Use();}}
            }
            if(GUI.Button(new Rect(685,502,113,30),"确认学习",treeButton))ConfirmSkillLearning();
            if(GUI.Button(new Rect(808,502,113,30),"撤销本次加点",treeButton)){draftSkillRanks=(int[])skillRanks.Clone();skillMessage="本次未确认的加点已撤销";}
            TreeText(new Rect(685,548,236,47),skillMessage,treeSmall);
            if(captureSlot>=0&&ev.type==EventType.KeyDown)
            {if(ev.keyCode==KeyCode.Escape){captureSlot=-1;ev.Use();}else if(RebindSkillSlot(captureSlot,ev.keyCode)){captureSlot=-1;skillMessage="快捷键已保存";ev.Use();}}
            if(dragSkill>=0&&ev.type==EventType.MouseDrag){draggingSkill|=(mouse-dragOrigin).sqrMagnitude>25;ev.Use();}
            if(dragSkill>=0&&ev.type==EventType.MouseUp)
            {if(draggingSkill)for(int slot=0;slot<12;slot++)if(TreeSlotRect(slot).Contains(mouse)){if(!AssignSkillSlot(dragSkill,slot,dragSlot))skillMessage="请先学习并确认这个技能";break;}dragSkill=dragSlot=-1;draggingSkill=false;ev.Use();}
            if(draggingSkill&&dragSkill>=0)GUI.DrawTexture(new Rect(mouse.x-17,mouse.y-17,34,34),treeIcons[dragSkill].texture);
            GUI.matrix=matrix;
        }
    }
}
