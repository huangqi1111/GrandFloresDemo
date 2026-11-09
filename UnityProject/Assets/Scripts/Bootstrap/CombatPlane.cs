using UnityEngine;
using UnityEngine.Rendering;

namespace DNF60Prototype
{
    // Coordinates describe feet on the floor; sprite height does not affect depth.
    public static class CombatPlane
    {
        public const float Front = -3.5f, Back = -0.7f;
        public static Vector3 Clamp(Vector3 p) => new Vector3(Mathf.Clamp(p.x, -8.5f, 8.5f), Mathf.Clamp(p.y, Front, Back), 0);
        public static Vector3 Velocity(Vector2 input, float speed)
        {
            input = Vector2.ClampMagnitude(input, 1);
            return new Vector3(input.x * speed, input.y * speed * .65f, 0);
        }
        public static bool InReach(Vector3 attacker, Vector3 target, int facing, float reach, float depth)
        {
            var delta = target - attacker;
            return delta.x * facing >= -.25f && Mathf.Abs(delta.x) <= reach && Mathf.Abs(delta.y) <= depth;
        }
        public static int Order(float feetY) => 1000 - Mathf.RoundToInt(feetY * 100);
    }

    [RequireComponent(typeof(SortingGroup))]
    public sealed class GroundDepth : MonoBehaviour
    {
        SortingGroup group;
        void Awake() { group = GetComponent<SortingGroup>(); }
        void LateUpdate() { group.sortingOrder = CombatPlane.Order(transform.position.y); }
    }
}
