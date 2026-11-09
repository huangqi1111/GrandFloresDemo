using UnityEngine;

namespace DNF60Prototype
{
    // Bits represent physical right/left/up/down keys, including opposed keys.
    // A brief release gap allows a direction change without losing the run latch.
    public sealed class DungeonRunInput
    {
        public const float DoubleTapWindow = .28f;
        public const float DirectionChangeWindow = .12f;
        public const float WalkSpeed = 3.2f, RunSpeed = 5.2f;
        readonly float[] lastPress = { float.NegativeInfinity, float.NegativeInfinity,
                                      float.NegativeInfinity, float.NegativeInfinity };
        float releasedAt = float.NegativeInfinity;
        public bool Running { get; private set; }
        public float Speed => Running ? RunSpeed : WalkSpeed;

        public void Step(float now, int held, int pressed, bool enabled)
        {
            if (!enabled) { Reset(); return; }
            // Expire before processing a new press so a late reversal is a walk.
            if (Running && now - releasedAt >= DirectionChangeWindow) Running = false;
            for (int key = 0; key < 4; key++)
            {
                if ((pressed & (1 << key)) == 0) continue;
                if (now - lastPress[key] <= DoubleTapWindow) Running = true;
                lastPress[key] = now;
            }
            if (held != 0) releasedAt = float.PositiveInfinity;
            else if (float.IsPositiveInfinity(releasedAt)) releasedAt = now;
        }

        public void Reset()
        {
            Running = false; releasedAt = float.NegativeInfinity;
            for (int i = 0; i < lastPress.Length; i++) lastPress[i] = float.NegativeInfinity;
        }
    }
}
