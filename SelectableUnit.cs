using Godot;

public partial class SelectableUnit : MeshInstance3D
{
    public enum UnitTeam
    {
        Friendly,
        Enemy,
    }

    public enum UnitActivity
    {
        Idle,
        Moving,
        Pursuing,
        Attacking,
        Dead,
    }

    public static readonly StringName FriendlySelectionGroup = "friendly_selectable_units";
    private static readonly StringName FriendlyCombatGroup = "combat_units_friendly";
    private static readonly StringName EnemyCombatGroup = "combat_units_enemy";
    private const float AttackApproachMargin = 0.2f;

    [Export]
    public UnitTeam Team { get; set; } = UnitTeam.Friendly;

    [Export]
    public UnitDefinition Definition { get; set; } = null!;

    private UnitMovement _movement = null!;
    private UnitCombat _combat = null!;
    private UnitEngagement _engagement = null!;
    private UnitPresentation _presentation = null!;
    private SelectableUnit _combatTarget = null!;
    private bool _isGameplayStopped;

    public float Health { get; private set; }
    public bool IsAlive => Activity != UnitActivity.Dead;
    public bool IsSelected { get; private set; }
    public UnitActivity Activity { get; private set; } = UnitActivity.Idle;

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

        _engagement = new UnitEngagement { Name = "Engagement" };
        AddChild(_engagement);
        _engagement.Initialize(this, Definition);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_isGameplayStopped || Activity == UnitActivity.Dead)
        {
            return;
        }

        _combat.AdvanceCooldown(delta);

        switch (Activity)
        {
            case UnitActivity.Idle:
                TryBeginIdleEngagement();
                break;
            case UnitActivity.Pursuing:
                UpdatePursuit();
                break;
            case UnitActivity.Attacking:
                UpdateAttack();
                break;
        }
    }

    public void SetSelected(bool selected)
    {
        if (Team != UnitTeam.Friendly || !IsAlive || _isGameplayStopped)
        {
            return;
        }

        IsSelected = selected;
        _presentation.SetSelected(selected);
    }

    public void SetMoveTarget(Vector3 worldTarget)
    {
        if (Team != UnitTeam.Friendly || !IsAlive || _isGameplayStopped)
        {
            return;
        }

        ClearCombatTarget();
        Activity = UnitActivity.Moving;
        _movement.SetMoveTarget(worldTarget, Definition.StoppingDistance);
    }

    public void SetAttackTarget(SelectableUnit target)
    {
        if (Team != UnitTeam.Friendly || !IsAlive || _isGameplayStopped)
        {
            return;
        }

        if (!IsValidCombatTarget(target))
        {
            return;
        }

        _movement.CancelMoveOrder();
        BeginEngagement(target);
    }

    public void TakeDamage(float damage)
    {
        if (!IsAlive || _isGameplayStopped || damage <= 0.0f)
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

    internal void NotifyMovementCompleted()
    {
        if (!IsAlive || _isGameplayStopped)
        {
            return;
        }

        if (Activity == UnitActivity.Moving)
        {
            Activity = UnitActivity.Idle;
        }
        else if (Activity == UnitActivity.Pursuing &&
                 IsValidCombatTarget(_combatTarget) &&
                 IsInsideAttackPosition(_combatTarget))
        {
            Activity = UnitActivity.Attacking;
        }
    }

    public void StopGameplay()
    {
        if (!IsAlive || _isGameplayStopped)
        {
            return;
        }

        _isGameplayStopped = true;
        IsSelected = false;
        _presentation.SetSelected(false);
        ClearCombatTarget();
        _combat.Stop();
        _movement.Stop();
        SetPhysicsProcess(false);
    }

    private void TryBeginIdleEngagement()
    {
        SelectableUnit target = _engagement.FindNearestEnemyWithinRange();
        if (target is not null)
        {
            BeginEngagement(target);
        }
    }

    private void BeginEngagement(SelectableUnit target)
    {
        _combatTarget = target;
        if (IsInsideAttackPosition(target))
        {
            _movement.CancelMoveOrder();
            Activity = UnitActivity.Attacking;
        }
        else
        {
            BeginPursuit();
        }
    }

    private void UpdatePursuit()
    {
        if (!IsValidCombatTarget(_combatTarget))
        {
            _movement.CancelMoveOrder();
            ClearCombatTarget();
            Activity = UnitActivity.Idle;
            return;
        }

        if (IsInsideAttackPosition(_combatTarget))
        {
            _movement.CancelMoveOrder();
            Activity = UnitActivity.Attacking;
            return;
        }

        _movement.SetMoveTarget(
            _combatTarget.GlobalPosition,
            GetPursuitStoppingDistance());
    }

    private void UpdateAttack()
    {
        if (!IsValidCombatTarget(_combatTarget))
        {
            ClearCombatTarget();
            Activity = UnitActivity.Idle;
            return;
        }

        if (!_combat.IsTargetInRange(_combatTarget))
        {
            BeginPursuit();
            return;
        }

        if (!_movement.IsMoving)
        {
            _combat.TryAttack(_combatTarget);
        }
    }

    private void BeginPursuit()
    {
        Activity = UnitActivity.Pursuing;
        _movement.SetMoveTarget(
            _combatTarget.GlobalPosition,
            GetPursuitStoppingDistance());
    }

    private bool IsInsideAttackPosition(SelectableUnit target)
    {
        float stoppingDistance = GetPursuitStoppingDistance();
        return GlobalPosition.DistanceSquaredTo(target.GlobalPosition) <=
            stoppingDistance * stoppingDistance;
    }

    private float GetPursuitStoppingDistance()
    {
        return Mathf.Max(Definition.AttackRange - AttackApproachMargin, 0.0f);
    }

    private bool IsValidCombatTarget(SelectableUnit target)
    {
        return target is not null &&
            IsInstanceValid(target) &&
            target.IsAlive &&
            target.Team != Team;
    }

    private void ClearCombatTarget()
    {
        _combatTarget = null!;
    }

    private void Die()
    {
        Activity = UnitActivity.Dead;
        IsSelected = false;
        ClearCombatTarget();
        _combat.Stop();
        _movement.Stop();
        SetPhysicsProcess(false);
        _presentation.HideUnit();
        RemoveFromGroup(FriendlySelectionGroup);
        RemoveFromGroup(GetCombatGroup(Team));
        QueueFree();
    }
}
