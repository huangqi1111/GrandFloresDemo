using UnityEngine;

namespace DNF60Prototype
{
    public sealed partial class GameBootstrap
    {
        bool dungeonResultOpen;
        Texture2D dungeonResultTexture;
        void ShowDungeonResult()
        {
            if(mode!=GameMode.Dungeon||!bossRoom||!roomCleared)return;
            dungeonResultOpen=true;basicQueued=false;
            CloseSkillTree();inventoryOpen=questsOpen=false;
            if(jobPanel!=null)jobPanel.SetActive(false);
            art.Move(Vector2.zero);
        }
        void ResolveDungeonResult(int choice)
        {
            if(!dungeonResultOpen)return;
            string title=dungeonSelectionTitle;var options=dungeonSelectionOptions;
            int selection=selectedDungeonIndex;
            EnterTown();
            if(choice==0){selectedDungeonIndex=selection;EnterDungeon();}
            else if(choice==1){ShowDungeonSelection(title,options);selectedDungeonIndex=selection;}
        }
        void DrawDungeonResult()
        {
            if(dungeonResultTexture==null)dungeonResultTexture=Resources.Load<Texture2D>("DNF60/UI/dungeon_result");
            float size=Mathf.Min(Screen.width*.29f,Screen.height*.48f);
            var rect=new Rect(Screen.width-size-18,18,size,size);
            GUI.DrawTexture(rect,dungeonResultTexture);
            var style=new GUIStyle(GUIStyle.none){fontSize=Mathf.Max(12,Mathf.RoundToInt(size*.052f)),alignment=TextAnchor.MiddleCenter,fontStyle=FontStyle.Bold};
            style.normal.textColor=new Color(1,.88f,.57f);style.hover.textColor=Color.white;style.active.textColor=new Color(1,.73f,.25f);
            string[] labels={"再次挑战","挑战其他地下城","返回城镇"};
            for(int i=0;i<3;i++)
            {
                var button=new Rect(rect.x+size*.145f,rect.y+size*(.302f+i*.21f),size*.71f,size*.125f);
                if(GUI.Button(button,labels[i],style)){ResolveDungeonResult(i);return;}
            }
        }
    }
}
