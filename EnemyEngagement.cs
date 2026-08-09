using Godot;

public partial class EnemyEngagement : Node
{
    private SelectableUnit _unit = null!;
    private UnitDefinition _definition = null!;
    private bool _initialized;

    public override void _Ready()
    {
        SetPhysicsProcess(_initialized);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_unit.IsAlive || _unit.HasOrderedAttackTarget)
        {
            return;
        }

        SelectableUnit target = FindNearestBlueUnitWithinEngagementRange();
        if (target is not null)
        {
            _unit.SetAutonomousAttackTarget(target);
        }
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

    private SelectableUnit FindNearestBlueUnitWithinEngagementRange()
    {
        SelectableUnit nearestTarget = null!;
        float nearestDistanceSquared = float.MaxValue;
        float engagementRange = Mathf.Max(_definition.EngagementRange, 0.0f);
        float engagementRangeSquared = engagementRange * engagementRange;

        foreach (Node node in GetTree().GetNodesInGroup(
                     SelectableUnit.GetCombatGroup(SelectableUnit.UnitTeam.Friendly)))
        {
            if (node is not SelectableUnit candidate ||
                !IsInstanceValid(candidate) ||
                !candidate.IsAlive)
            {
                continue;
            }

            float distanceSquared = _unit.GlobalPosition.DistanceSquaredTo(
                candidate.GlobalPosition);
            if (distanceSquared > engagementRangeSquared)
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
