using System;
using System.Reflection;
using DNF60Prototype;
using UnityEditor;
using UnityEngine;

public static class DungeonBossCoverChecks
{
    public static void RunBatch()
    {
        try
        {
            var method=typeof(GameBootstrap).GetMethod("DungeonBossCoverId",BindingFlags.Static|BindingFlags.NonPublic);
            string[] names={"幽暗密林","雷鸣废墟","格拉卡","烈焰格拉卡","冰霜幽暗密林","猛毒雷鸣废墟","暗黑雷鸣废墟"};
            string[] ids={"mirkwood","thunder_ruins","graka","blazing_graka","frost_mirkwood","poison_thunder","dark_thunder"};
            var textures=new System.Collections.Generic.HashSet<Texture2D>();
            // Reversed order ensures mapping does not depend on selection/list position.
            for(int i=6;i>=0;i--)
            {
                var id=(string)method.Invoke(null,new object[]{names[i]});
                if(id!=ids[i])throw new Exception("Wrong boss mapping: "+names[i]);
                var texture=Resources.Load<Texture2D>("DNF60/UI/DungeonCovers/"+id);
                if(texture==null||texture.width!=447||texture.height!=390||!textures.Add(texture))throw new Exception("Missing, reused or rescaled cover: "+id);
                Debug.Log("BOSS_COVER_OK "+names[i]+" -> "+texture.name);
            }
            foreach(string other in new[]{"龙人之塔","人偶玄关","石巨人塔",""})
                if(method.Invoke(null,new object[]{other})!=null)throw new Exception("Unrelated dungeon shows forest boss");
            Debug.Log("DUNGEON_BOSS_COVER_CHECKS_PASS");EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
