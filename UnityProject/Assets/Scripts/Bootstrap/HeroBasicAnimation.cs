using UnityEngine;

namespace DNF60Prototype
{
    // Original swordman attack1/2/3 ANI: 10/11/9 records, 50 ms each,
    // with a 150 ms final record. New drawings use six distinct key poses.
    public static class HeroBasicAnimation
    {
        public const int PoseCount = 6;
        static readonly int[][] Poses = {
            new[] { 0, 0, 1, 2, 3, 3, 4, 4, 4, 5 },
            new[] { 0, 0, 1, 2, 3, 4, 4, 4, 5, 5, 5 },
            new[] { 0, 1, 2, 3, 3, 4, 4, 4, 5 }
        };
        public static int StageIndex(int stage) => Mathf.Clamp(stage - 1, 0, 2);
        public static float Duration(int stage) => (Poses[StageIndex(stage)].Length - 1) * .05f + .15f;
        public static float Impact(int stage) => stage == 3 ? .15f : .20f;
        public static int PoseAt(int stage, float elapsed)
        {
            var poses = Poses[StageIndex(stage)];
            return poses[Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0, elapsed) / .05f), 0, poses.Length - 1)];
        }
        public static int Stage(HeroAction action)
        {
            return action == HeroAction.Basic ? 1 : action == HeroAction.Basic2 ? 2 : action == HeroAction.Basic3 ? 3 : 0;
        }
    }
}
