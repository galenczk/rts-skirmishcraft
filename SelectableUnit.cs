using Godot;

public partial class SelectableUnit : MeshInstance3D, ICombatTarget
{
    public enum UnitActivity
    {
        Idle,
        Moving,
        Pursuing,
        Attacking,
        Dead,
    }

    public static readonly StringName FriendlySelectionGroup = "friendly_selectable_units";
    private const float AttackApproachMargin = 0.2f;

    [Export]
    public UnitTeam Team { get; set; } = UnitTeam.Friendly;

    [Export]
    public UnitDefinition Definition { get; set; } = null!;

    private UnitMovement _movement = null!;
    private UnitCombat _combat = null!;
    private UnitEngagement _engagement = null!;
    private WorkerEconomy _workerEconomy = null!;
    private UnitPresentation _presentation = null!;
    private ICombatTarget _combatTarget = null!;
    private bool _isGameplayStopped;

    public float Health { get; private set; }
    public bool IsAlive => Activity != UnitActivity.Dead;
    public bool CanAttack => Definition.CanAttack;
    public bool HasWorkerEconomy => _workerEconomy is not null;
    public int CarriedMaterials => _workerEconomy?.CarriedMaterials ?? 0;
    public bool IsSelected { get; private set; }
    public UnitActivity Activity { get; private set; } = UnitActivity.Idle;
    public Vector3 TargetPosition => GlobalPosition;
    public float TargetRadius => 0.0f;

    public override void _Ready()
    {
        if (Definition is null)
        {
            GD.PushWarning($"{Name} has no UnitDefinition; using temporary defaults.");
            Definition = new UnitDefinition();
        }

        Health = Mathf.Max(Definition.MaxHealth, 1.0f);
        AddToGroup(CombatTargetGroups.ForTeam(Team));

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

        if (Definition.WorkerEconomy is not null)
        {
            _workerEconomy = new WorkerEconomy { Name = "WorkerEconomy" };
            AddChild(_workerEconomy);
            _workerEconomy.Initialize(this, Definition.WorkerEconomy);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_isGameplayStopped || Activity == UnitActivity.Dead)
        {
            return;
        }

        if (!CanAttack)
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
        if (Team != UnitTeam.Friendly ||
            !IsAlive ||
            _isGameplayStopped)
        {
            return;
        }

        IsSelected = selected;
        _presentation.SetSelected(selected);
    }

    public bool SetMoveTarget(Vector3 worldTarget)
    {
        if (!IsAlive || _isGameplayStopped)
        {
            return false;
        }

        _workerEconomy?.CancelTask();
        ClearCombatTarget();
        Activity = UnitActivity.Moving;
        _movement.SetMoveTarget(worldTarget, Definition.StoppingDistance);
        return true;
    }

    public bool SetGatherTarget(
        MaterialsResourceNode target,
        int slotIndex,
        int slotCount)
    {
        if (!IsAlive ||
            _isGameplayStopped ||
            _workerEconomy is null)
        {
            return false;
        }

        ClearCombatTarget();
        _movement.CancelMoveOrder();
        Activity = UnitActivity.Idle;
        return _workerEconomy.BeginGathering(target, slotIndex, slotCount);
    }

    public bool SetManualDropOff(
        BuildingEntity building,
        int slotIndex,
        int slotCount)
    {
        if (!IsAlive ||
            _isGameplayStopped ||
            _workerEconomy is null)
        {
            return false;
        }

        ClearCombatTarget();
        _movement.CancelMoveOrder();
        Activity = UnitActivity.Idle;
        return _workerEconomy.BeginManualDropOff(building, slotIndex, slotCount);
    }

    public bool SetConstructionTarget(BuildingEntity building)
    {
        if (!IsAlive ||
            _isGameplayStopped ||
            _workerEconomy is null)
        {
            return false;
        }

        ClearCombatTarget();
        return _workerEconomy.BeginConstruction(building);
    }

    public bool SetAttackTarget(ICombatTarget target)
    {
        if (!IsAlive ||
            _isGameplayStopped ||
            !CanAttack)
        {
            return false;
        }

        if (!IsValidCombatTarget(target))
        {
            return false;
        }

        _movement.CancelMoveOrder();
        BeginEngagement(target);
        return true;
    }

    public bool CancelCurrentOrder()
    {
        if (!IsAlive || _isGameplayStopped)
        {
            return false;
        }

        _workerEconomy?.CancelTask();
        _movement.CancelMoveOrder();
        ClearCombatTarget();
        Activity = UnitActivity.Idle;
        return true;
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
        _workerEconomy?.Stop(discardCarriedMaterials: false);
        SetPhysicsProcess(false);
    }

    internal bool IsWorkerTaskMoving => _movement.IsMoving;

    internal void MoveForWorkerTask(
        Vector3 worldTarget,
        float stoppingDistance)
    {
        if (!IsAlive || _isGameplayStopped)
        {
            return;
        }

        ClearCombatTarget();
        Activity = UnitActivity.Moving;
        _movement.SetMoveTarget(worldTarget, stoppingDistance);
    }

    internal void StopWorkerTaskMovement()
    {
        _movement.CancelMoveOrder();
        if (Activity == UnitActivity.Moving)
        {
            Activity = UnitActivity.Idle;
        }
    }

    internal void NotifyConstructionSiteRemoved(BuildingEntity building)
    {
        _workerEconomy?.NotifyConstructionSiteRemoved(building);
    }

    private void TryBeginIdleEngagement()
    {
        ICombatTarget target = _engagement.FindNearestEnemyWithinRange();
        if (target is not null)
        {
            BeginEngagement(target);
        }
    }

    private void BeginEngagement(ICombatTarget target)
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
            _combatTarget.TargetPosition,
            GetPursuitStoppingDistance(_combatTarget));
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
            _combatTarget.TargetPosition,
            GetPursuitStoppingDistance(_combatTarget));
    }

    private bool IsInsideAttackPosition(ICombatTarget target)
    {
        float stoppingDistance = GetPursuitStoppingDistance(target);
        return GlobalPosition.DistanceSquaredTo(target.TargetPosition) <=
            stoppingDistance * stoppingDistance;
    }

    private float GetPursuitStoppingDistance(ICombatTarget target)
    {
        return Mathf.Max(
            Definition.AttackRange + target.TargetRadius - AttackApproachMargin,
            0.0f);
    }

    private bool IsValidCombatTarget(ICombatTarget target)
    {
        return CombatTargetGroups.IsValid(target) &&
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
        _workerEconomy?.Stop(discardCarriedMaterials: true);
        SetPhysicsProcess(false);
        _presentation.HideUnit();
        RemoveFromGroup(FriendlySelectionGroup);
        RemoveFromGroup(CombatTargetGroups.ForTeam(Team));
        QueueFree();
    }
}
