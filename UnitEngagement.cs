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

    public ICombatTarget FindNearestEnemyWithinRange()
    {
        if (!_definition.CanAttack)
        {
            return null!;
        }

        ICombatTarget nearestTarget = null!;
        float nearestSurfaceDistance = float.MaxValue;
        float engagementRange = Mathf.Max(_definition.EngagementRange, 0.0f);

        foreach (Node node in GetTree().GetNodesInGroup(
                     CombatTargetGroups.ForEnemyOf(_unit.Team)))
        {
            if (node is not ICombatTarget candidate ||
                !CombatTargetGroups.IsValid(candidate) ||
                candidate.Team == _unit.Team)
            {
                continue;
            }

            float centerDistance = _unit.GlobalPosition.DistanceTo(
                candidate.TargetPosition);
            float surfaceDistance = Mathf.Max(
                centerDistance - candidate.TargetRadius,
                0.0f);
            if (surfaceDistance > engagementRange)
            {
                continue;
            }

            bool isCloser = surfaceDistance < nearestSurfaceDistance;
            bool winsTie = Mathf.IsEqualApprox(
                    surfaceDistance,
                    nearestSurfaceDistance) &&
                (nearestTarget is null || node.GetInstanceId() <
                    ((Node)nearestTarget).GetInstanceId());
            if (isCloser || winsTie)
            {
                nearestTarget = candidate;
                nearestSurfaceDistance = surfaceDistance;
            }
        }

        return nearestTarget;
    }
}
