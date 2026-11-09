using System;
using UnityEngine;
using DNF60Prototype;

public static class CombatPlaneChecks
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        var feet = new Vector3(0, -2, 0);
        Require(CombatPlane.Velocity(Vector2.up, 5).y > 0, "Vertical movement blocked");
        Require(CombatPlane.Velocity(Vector2.one, 5).magnitude <= 5, "Diagonal speed boost");
        Require(CombatPlane.Clamp(new Vector3(99,99,4)).y == CombatPlane.Back, "Back boundary");
        Require(CombatPlane.Clamp(new Vector3(-99,-99,4)).y == CombatPlane.Front, "Front boundary");
        Require(CombatPlane.Order(-3) > CombatPlane.Order(-1), "Depth order reversed");
        Require(CombatPlane.InReach(feet, feet+Vector3.right,1,2,.45f), "Aligned hit rejected");
        Require(!CombatPlane.InReach(feet,feet+Vector3.right+Vector3.up,1,2,.45f), "Cross lane hit");
        Require(!CombatPlane.InReach(feet,feet-Vector3.right,1,2,.45f), "Rear hit");
        for(int i=0;i<45;i++)Require(Resources.Load<Texture2D>("DNF60/Sprites/Hero/hero_"+i.ToString("D2"))!=null,"Missing hero frame");
        Require(Resources.Load<Texture2D>("DNF60/Sprites/Dungeon/forest")!=null,"Missing forest");
        Debug.Log("COMBAT_PLANE_CHECKS_PASS: movement, bounds, depth sorting, hit lanes, 45 hero frames, forest.");
    }
}
