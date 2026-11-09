using UnityEngine;
namespace DNF60Prototype {
    // Original dashattack.ani records113..122: 100,80,50,100,60,60,60,60,30,30 ms.
    public static class HeroDashAttack {
        public const float Duration=.63f, Impact=.23f, Travel=1.6f;
        static readonly float[] Ends={.10f,.18f,.23f,.33f,.39f,.45f,.51f,.57f,.60f,.63f};
        static readonly int[] Poses={0,0,1,2,3,3,4,4,5,5};
        public static int PoseAt(float elapsed){for(int i=0;i<Ends.Length;i++)if(elapsed<Ends[i])return Poses[i];return 5;}
        public static float DistanceAt(float elapsed){float t=Mathf.Clamp01((elapsed-.18f)/.33f);return Travel*(1f-(1f-t)*(1f-t));}
    }
}
