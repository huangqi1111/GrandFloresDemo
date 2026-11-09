using System.Collections.Generic;
using UnityEngine;

namespace DNF60Prototype
{
    public static class EightSkillRotation
    {
        // Excludes independent Z, Guard and the non-damaging Khazan buff.
        public static readonly int[] SkillIndices={1,3,5,6,7,8,9,10};
        public sealed class Result
        {
            public float ManaPerSecond;
            public readonly int[] Casts=new int[8];
        }
        public static Result Simulate(IReadOnlyList<SkillData> source)
        {
            var result=new Result();var skills=new SkillData[8];
            for(int i=0;i<8;i++)
            {
                var s=source[SkillIndices[i]];
                skills[i]=new SkillData(s.Name,s.Key,s.Level,s.Mana,s.Cooldown,s.Damage,s.Hits,s.HitInterval,s.HitOffset,s.Reach,s.Depth)
                {Recovery=s.Recovery,AirOnly=s.AirOnly,StartsJump=s.StartsJump,WaitForLanding=s.WaitForLanding};
            }
            const float step=1f/60f,warmup=30f,measured=150f;
            float now=0,pauseUntil=0;int mana=0;var jump=new JumpState();
            CombatController controller=null;
            controller=new CombatController(()=>Vector3.zero,()=>1,hit=>{
                controller.Pause(.09f);pauseUntil=now+.09f;
            },i=>skills[i]);
            for(int frame=0;frame<(warmup+measured)*60;frame++)
            {
                now=frame*step;
                if(now<pauseUntil)continue;
                jump.Step(step);controller.Tick(now,jump.Airborne);
                if(controller.Active||jump.Airborne)continue;
                int selected=-1;
                // Serve the longest-waiting ready skill, never wait with a usable skill idle.
                for(int i=0;i<8;i++)if(skills[i].ReadyAt<=now&&(selected<0||skills[i].ReadyAt<skills[selected].ReadyAt))selected=i;
                if(selected<0)continue;
                var skill=skills[selected];
                if(skill.AirOnly)jump.Start();
                else if(skill.StartsJump)jump.Start(4.8f);
                if(!controller.BeginSkill(selected,now,0))continue;
                skill.ReadyAt=now+skill.Cooldown;
                if(now>=warmup){mana+=skill.Mana;result.Casts[selected]++;}
            }
            result.ManaPerSecond=mana/measured;return result;
        }
    }
}
