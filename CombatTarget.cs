using Godot;

public enum UnitTeam
{
    Friendly,
    Enemy,
}

public interface ICombatTarget
{
    UnitTeam Team { get; }
    bool IsAlive { get; }
    Vector3 TargetPosition { get; }
    float TargetRadius { get; }

    void TakeDamage(float damage);
}

public static class CombatTargetGroups
{
    private static readonly StringName FriendlyTargets = "targets_friendly";
    private static readonly StringName EnemyTargets = "targets_enemy";

    public static StringName ForTeam(UnitTeam team)
    {
        return team == UnitTeam.Friendly ? FriendlyTargets : EnemyTargets;
    }

    public static StringName ForEnemyOf(UnitTeam team)
    {
        return team == UnitTeam.Friendly ? EnemyTargets : FriendlyTargets;
    }

    public static bool IsValid(ICombatTarget target)
    {
        return target is GodotObject targetObject &&
            GodotObject.IsInstanceValid(targetObject) &&
            target.IsAlive;
    }
}
