using UnityEngine;

namespace DNF60Prototype
{
    public sealed partial class GameBootstrap
    {
        UnityEngine.UI.Image PaintedOrb(Transform frame,bool mana)
        {
            var go=new GameObject(mana?"MPOrbLiquid":"HPOrbLiquid");go.transform.SetParent(frame,false);
            var rect=go.AddComponent<RectTransform>();float x=mana?1399:137;
            rect.anchorMin=new Vector2((x-71)/1536f,(272-131-74)/272f);
            rect.anchorMax=new Vector2((x+71)/1536f,(272-131+74)/272f);
            rect.offsetMin=rect.offsetMax=Vector2.zero;
            var image=go.AddComponent<UnityEngine.UI.Image>();
            image.sprite=UiSprite(mana?"combat_mp_liquid":"combat_hp_liquid");
            image.type=UnityEngine.UI.Image.Type.Filled;
            image.fillMethod=UnityEngine.UI.Image.FillMethod.Vertical;image.fillOrigin=0;
            image.raycastTarget=false;
            return image;
        }
        // Monster power belongs to the dungeon, not the player's current level.
        // HP/XP, original attack delays and population remain independently authored.
        int BalancedMonsterDamage(string name,int sourceDamage)
        {
            int dungeonLevel=activeForest!=null?activeForest.basisLevel:3;
            bool boss=name.StartsWith("领主");
            float weight=boss?1.7f:name.Contains("牛头")?1.3f:name.Contains("猫")?1.05f:1f;
            // Retain stronger authored attacks while lifting legacy placeholder damage.
            return Mathf.Max(sourceDamage,Mathf.RoundToInt((12+2*dungeonLevel)*weight));
        }
        int MitigateMonsterDamage(int rawDamage)
        {
            if(rawDamage<=0)return 0;
            float defense=Mathf.Max(0,TotalDefense);
            float reduction=Mathf.Min(.65f,defense/(defense+40+3*level));
            return Mathf.Max(1,Mathf.RoundToInt(rawDamage*(1-reduction)));
        }
    }
}
