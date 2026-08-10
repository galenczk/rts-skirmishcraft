using Godot;

public partial class UnitEngagement : Node
{
    private SelectableUnit _unit = null!;
    private UnitDefinition _definition = null!;

    public void Initialize(SelectableUnit unit, UnitDefinition definition)
    {
        _unit = unit;
        _definition = definition;
    }

    public SelectableUnit FindNearestEnemyWithinRange()
    {
        if (!_definition.CanAttack)
        {
            return null!;
        }

        SelectableUnit nearestTarget = null!;
        float nearestDistanceSquared = float.MaxValue;
        float engagementRange = Mathf.Max(_definition.EngagementRange, 0.0f);
        float engagementRangeSquared = engagementRange * engagementRange;

        foreach (Node node in GetTree().GetNodesInGroup(
                     SelectableUnit.GetEnemyUnitGroup(_unit.Team)))
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
