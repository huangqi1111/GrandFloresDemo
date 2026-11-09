using System;
using UnityEngine;

namespace DNF60Prototype
{
    // One action owner. Skill timings are populated from the original PVF data.
    public sealed class CombatController
    {
        public readonly struct Hit
        {
            public readonly float Damage, Reach, Depth;
            public readonly Vector3 Origin;
            public readonly int Facing;
            public Hit(float damage, float reach, float depth, Vector3 origin, int facing)
            { Damage = damage; Reach = reach; Depth = depth; Origin = origin; Facing = facing; }
        }
        readonly Func<Vector3> origin;
        readonly Func<int> facing;
        readonly Action<Hit> hit;
        readonly Func<int, SkillData> skillAt;
        Hit pending;
        int hitsLeft, generation;
        float nextHit, interval, recoveryAt, basicReadyAt;
        bool waitForLanding;
        float dashStarted,dashTravelConsumed;
        public bool DashAttack {get;private set;}
        public int DashFacing=>pending.Facing;
        public float ConsumeDashTravel(float now){if(!Active||!DashAttack)return 0;float distance=HeroDashAttack.DistanceAt(now-dashStarted);float delta=Mathf.Max(0,distance-dashTravelConsumed);dashTravelConsumed=distance;return delta;}
        public bool Active { get; private set; }
        public int BasicStage { get; private set; }
        public bool AirBasic { get; private set; }
        public float RemainingRecovery(float now) => Active ? Mathf.Max(0, recoveryAt - now) : 0;

        public CombatController(Func<Vector3> origin, Func<int> facing, Action<Hit> hit, Func<int, SkillData> skillAt)
        {
            this.origin = origin ?? throw new ArgumentNullException(nameof(origin));
            this.facing = facing ?? throw new ArgumentNullException(nameof(facing));
            this.hit = hit ?? throw new ArgumentNullException(nameof(hit));
            this.skillAt = skillAt ?? throw new ArgumentNullException(nameof(skillAt));
        }
        public bool BeginBasic(float now, float cooldown, float damage, int stage = 1)
        {
            if (Active || now < basicReadyAt) return false;
            Begin(now, damage, 1, HeroBasicAnimation.Impact(stage), .01f, 2.1f, .45f, HeroBasicAnimation.Duration(stage), false);
            BasicStage = Mathf.Clamp(stage, 1, 3);
            basicReadyAt = now + Mathf.Max(.20f, cooldown);
            return true;
        }
        public bool BeginDashAttack(float now,float damage){
            if(Active||now<basicReadyAt)return false;
            Begin(now,damage,1,HeroDashAttack.Impact,.01f,2.8f,.45f,HeroDashAttack.Duration,false);
            DashAttack=true;dashStarted=now;dashTravelConsumed=0;basicReadyAt=now+HeroDashAttack.Duration;return true;
        }
        public bool BeginAirBasic(float now,float damage)
        {
            if(Active||now<basicReadyAt)return false;
            Begin(now,damage,1,.16f,.01f,2.5f,.6f,.42f,false);
            AirBasic=true;basicReadyAt=now+.42f;return true;
        }
        public bool BeginSkill(int index, float now, float attackDamage)
        {
            if (Active) return false;
            var skill = skillAt(index);
            if (skill == null || now < skill.ReadyAt) return false;
            // Damage is the total action budget; pulses must not multiply the attack bonus.
            float damage = skill.DealsDamage ? skill.Damage + attackDamage : 0;
            Begin(now, damage, Mathf.Max(1, skill.Hits), skill.HitOffset,
                skill.HitInterval, skill.Reach, skill.Depth, skill.Recovery, skill.WaitForLanding);
            return true;
        }
        void Begin(float now, float damage, int hits, float offset, float spacing, float reach, float depth, float recovery, bool landing)
        {
            generation++;
            BasicStage = 0;AirBasic=false;DashAttack=false;dashTravelConsumed=0;
            Active = true;
            hitsLeft = hits;
            interval = Mathf.Max(.01f, spacing);
            nextHit = now + Mathf.Max(.01f, offset);
            recoveryAt = Mathf.Max(now + recovery, nextHit + (hits - 1) * interval + .12f);
            waitForLanding = landing;
            pending = new Hit(Mathf.Max(0, damage) / hits, reach, depth, origin(), facing() < 0 ? -1 : 1);
        }
        public void Tick(float now, bool airborne = false)
        {
            if (!Active) return;
            if (waitForLanding)
            {
                if (airborne) return;
                waitForLanding = false;
                pending = new Hit(pending.Damage, pending.Reach, pending.Depth, origin(), pending.Facing);
                nextHit = now;
                recoveryAt = now + (hitsLeft - 1) * interval + .12f;
            }
            int action = generation;
            while (hitsLeft > 0 && now >= nextHit)
            {
                // Advance first: a callback can cancel combat or change rooms.
                hitsLeft--;
                nextHit += interval;
                if (pending.Damage > 0) hit(DashAttack?new Hit(pending.Damage,pending.Reach,pending.Depth,origin(),pending.Facing):pending);
                if (generation != action) return;
            }
            if (hitsLeft == 0 && now >= recoveryAt) Active = false;
        }
        public void Reset()
        {
            generation++;
            Active = false;
            BasicStage = 0;AirBasic=false;DashAttack=false;dashTravelConsumed=0;
            hitsLeft = 0;
            nextHit = recoveryAt = basicReadyAt = 0;
            waitForLanding = false;
            pending = default;
        }
        public void Pause(float seconds)
        {
            if (!Active || seconds <= 0) return;
            if(DashAttack)dashStarted+=seconds;
            nextHit += seconds;
            recoveryAt += seconds;
            basicReadyAt += seconds;
        }
    }
}
