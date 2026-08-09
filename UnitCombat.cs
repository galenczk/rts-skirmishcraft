using Godot;

public partial class UnitCombat : Node
{
    private const float MinimumAttackCooldown = 0.01f;

    private SelectableUnit _unit = null!;
    private UnitDefinition _definition = null!;
    private float _attackCooldownRemaining;
    private bool _initialized;

    public override void _Ready()
    {
        SetPhysicsProcess(_initialized);
    }

    public override void _PhysicsProcess(double delta)
    {
        _attackCooldownRemaining = Mathf.Max(
            _attackCooldownRemaining - (float)delta,
            0.0f);
        SelectableUnit target = FindNearestTargetInRange();
        if (target is null || _attackCooldownRemaining > 0.0f)
        {
            return;
        }

        target.TakeDamage(Mathf.Max(_definition.AttackDamage, 0.0f));
        _attackCooldownRemaining = Mathf.Max(
            _definition.AttackCooldown,
            MinimumAttackCooldown);
    }

    public void Initialize(SelectableUnit unit, UnitDefinition definition)
    {
        _unit = unit;
        _definition = definition;
        _initialized = true;
        SetPhysicsProcess(true);
    }

    public void Stop()
    {
        SetPhysicsProcess(false);
    }

    private SelectableUnit FindNearestTargetInRange()
    {
        SelectableUnit nearestTarget = null!;
        float nearestDistanceSquared = float.MaxValue;
        float attackRange = Mathf.Max(_definition.AttackRange, 0.0f);
        float attackRangeSquared = attackRange * attackRange;
        StringName enemyGroup = SelectableUnit.GetEnemyCombatGroup(_unit.Team);

        foreach (Node node in GetTree().GetNodesInGroup(enemyGroup))
        {
            if (node is not SelectableUnit candidate ||
                !IsInstanceValid(candidate) ||
                !candidate.IsAlive)
            {
                continue;
            }

            float distanceSquared = _unit.GlobalPosition.DistanceSquaredTo(
                candidate.GlobalPosition);
            if (distanceSquared > attackRangeSquared)
            {
                continue;
            }

            bool isCloser = distanceSquared < nearestDistanceSquared;
            bool winsTie = Mathf.IsEqualApprox(distanceSquared, nearestDistanceSquared) &&
                (nearestTarget is null || candidate.GetInstanceId() < nearestTarget.GetInstanceId());
            if (isCloser || winsTie)
            {
                nearestTarget = candidate;
                nearestDistanceSquared = distanceSquared;
            }
        }

        return nearestTarget;
    }
}
