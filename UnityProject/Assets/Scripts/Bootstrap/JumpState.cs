using System;

namespace DNF60Prototype
{
    // Temporary motion tuning, not a claim about original-client numeric values.
    public sealed class JumpState
    {
        public float Height { get; private set; }
        public float Velocity { get; private set; }
        public bool Airborne { get; private set; }
        public bool Start(float impulse = 6f)
        {
            if (Airborne || impulse <= 0) return false;
            Airborne = true;
            Velocity = impulse;
            return true;
        }
        public void Step(float seconds, float gravity = 18f)
        {
            if (!Airborne || seconds <= 0) return;
            if (gravity <= 0) throw new ArgumentOutOfRangeException(nameof(gravity));
            Height += Velocity * seconds - .5f * gravity * seconds * seconds;
            Velocity -= gravity * seconds;
            if (Height <= 0 && Velocity <= 0) Reset();
        }
        public void Reset() { Height = 0; Velocity = 0; Airborne = false; }
    }
}
