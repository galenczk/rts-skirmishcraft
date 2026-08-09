using Godot;

public partial class SelectableUnit : MeshInstance3D
{
    public enum UnitTeam
    {
        Friendly,
        Enemy,
    }

    public static readonly StringName FriendlySelectionGroup = "friendly_selectable_units";
    private static readonly StringName FriendlyCombatGroup = "combat_units_friendly";
    private static readonly StringName EnemyCombatGroup = "combat_units_enemy";

    [Export]
    public UnitTeam Team { get; set; } = UnitTeam.Friendly;

    [Export]
    public UnitDefinition Definition { get; set; } = null!;

    private UnitMovement _movement = null!;
    private UnitCombat _combat = null!;
    private EnemyEngagement _enemyEngagement = null!;
    private UnitPresentation _presentation = null!;
    private bool _isDead;

    public float Health { get; private set; }
    public bool IsAlive => !_isDead;
    public bool IsSelected { get; private set; }

    public override void _Ready()
    {
        if (Definition is null)
        {
            GD.PushWarning($"{Name} has no UnitDefinition; using temporary defaults.");
            Definition = new UnitDefinition();
        }

        Health = Mathf.Max(Definition.MaxHealth, 1.0f);
        AddToGroup(GetCombatGroup(Team));

        _presentation = new UnitPresentation { Name = "Presentation" };
        AddChild(_presentation);
        _presentation.Initialize(this, Team == UnitTeam.Friendly);

        if (Team == UnitTeam.Friendly)
        {
            AddToGroup(FriendlySelectionGroup);
        }

        _movement = new UnitMovement { Name = "Movement" };
        AddChild(_movement);
        _movement.Initialize(this, Definition);

        _combat = new UnitCombat { Name = "Combat" };
        AddChild(_combat);
        _combat.Initialize(this, Definition);

        if (Team == UnitTeam.Enemy)
        {
            _enemyEngagement = new EnemyEngagement { Name = "EnemyEngagement" };
            AddChild(_enemyEngagement);
            _enemyEngagement.Initialize(this, Definition);
        }
    }

    public void SetSelected(bool selected)
    {
        if (Team != UnitTeam.Friendly || _isDead)
        {
            return;
        }

        IsSelected = selected;
        _presentation.SetSelected(selected);
    }

    public void SetMoveTarget(Vector3 worldTarget)
    {
        if (Team != UnitTeam.Friendly || _isDead)
        {
            return;
        }

        _combat.ClearOrderedTarget();
        _movement.SetMoveTarget(worldTarget);
    }

    public void SetAttackTarget(SelectableUnit target)
    {
        if (Team != UnitTeam.Friendly || _isDead)
        {
            return;
        }

        AssignAttackTarget(target);
    }

    internal bool HasOrderedAttackTarget => _combat.HasOrderedTarget;

    internal void SetAutonomousAttackTarget(SelectableUnit target)
    {
        if (Team != UnitTeam.Enemy || _isDead)
        {
            return;
        }

        AssignAttackTarget(target);
    }

    private void AssignAttackTarget(SelectableUnit target)
    {
        if (!IsInstanceValid(target) ||
            !target.IsAlive ||
            target.Team == Team)
        {
            return;
        }

        _movement.CancelMoveOrder();
        _combat.SetOrderedTarget(target);
    }

    public void TakeDamage(float damage)
    {
        if (_isDead || damage <= 0.0f)
        {
            return;
        }

        Health = Mathf.Max(Health - damage, 0.0f);
        _presentation.ShowDamageFlash();

        if (Health <= 0.0f)
        {
            Die();
        }
    }

    internal static StringName GetCombatGroup(UnitTeam team)
    {
        return team == UnitTeam.Friendly ? FriendlyCombatGroup : EnemyCombatGroup;
    }

    internal static StringName GetEnemyCombatGroup(UnitTeam team)
    {
        return team == UnitTeam.Friendly ? EnemyCombatGroup : FriendlyCombatGroup;
    }

    internal void SetCombatPursuitDestination(Vector3 worldTarget)
    {
        if (!_isDead)
        {
            _movement.SetMoveTarget(worldTarget);
        }
    }

    internal void CancelCombatPursuit()
    {
        if (!_isDead)
        {
            _movement.CancelMoveOrder();
        }
    }

    private void Die()
    {
        _isDead = true;
        IsSelected = false;
        _combat.Stop();
        _movement.Stop();
        if (Team == UnitTeam.Enemy)
        {
            _enemyEngagement.Stop();
        }

        _presentation.HideUnit();
        RemoveFromGroup(FriendlySelectionGroup);
        RemoveFromGroup(GetCombatGroup(Team));
        QueueFree();
    }
}
