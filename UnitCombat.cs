using Godot;

public partial class UnitCombat : Node
{
    private const float MinimumAttackCooldown = 0.01f;

    private SelectableUnit _unit = null!;
    private UnitDefinition _definition = null!;
    private float _attackCooldownRemaining;
    private bool _isStopped;

    public void Initialize(SelectableUnit unit, UnitDefinition definition)
    {
        _unit = unit;
        _definition = definition;
    }

    public void AdvanceCooldown(double delta)
    {
        if (_isStopped)
        {
            return;
        }

        _attackCooldownRemaining = Mathf.Max(
            _attackCooldownRemaining - (float)delta,
            0.0f);
    }

    public bool IsTargetInRange(SelectableUnit target)
    {
        float attackRange = Mathf.Max(_definition.AttackRange, 0.0f);
        return _unit.GlobalPosition.DistanceSquaredTo(target.GlobalPosition) <=
            attackRange * attackRange;
    }

    public void TryAttack(SelectableUnit target)
    {
        if (_isStopped ||
            !_definition.CanAttack ||
            _attackCooldownRemaining > 0.0f ||
            !IsInstanceValid(target) ||
            !target.IsAlive ||
            target.Team == _unit.Team ||
            !IsTargetInRange(target))
        {
            return;
        }

        target.TakeDamage(Mathf.Max(_definition.AttackDamage, 0.0f));
        _attackCooldownRemaining = Mathf.Max(
            _definition.AttackCooldown,
            MinimumAttackCooldown);
    }

    public void Stop()
    {
        _isStopped = true;
    }
}
