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
    public static readonly StringName OccupancyGroup = "living_unit_occupancy";
    private const float InteractionPositionTolerance = 0.15f;

    [Export]
    public UnitTeam Team { get; set; } = UnitTeam.Friendly;

    [Export]
    public UnitDefinition Definition { get; set; } = null!;

    private UnitMovement _movement = null!;
    private UnitCombat _combat = null!;
    private UnitEngagement _engagement = null!;
    private WorkerEconomy _workerEconomy = null!;
    private UnitPresentation _presentation = null!;
    private UnitOccupancySystem _occupancySystem = null!;
    private ICombatTarget _combatTarget = null!;
    private Vector3 _attackApproachPosition;
    private Vector3 _attackApproachTargetPosition;
    private int _attackApproachOrdinal = -1;
    private uint _attackApproachMapIteration;
    private bool _hasAttackApproach;
    private bool _attackApproachCanAttack;
    private bool _isGameplayStopped;

    public float Health { get; private set; }
    public bool IsAlive => Activity != UnitActivity.Dead;
    public bool IsOccupancyActive => IsAlive && !_isGameplayStopped;
    public bool IsMovingForOccupancy => _movement is not null && _movement.IsMoving;
    public float OccupancyRadius => Mathf.Max(Definition.OccupancyRadius, 0.1f);
    public bool CanAttack => Definition.CanAttack;
    public bool HasWorkerEconomy => _workerEconomy is not null;
    public int CarriedMaterials => _workerEconomy?.CarriedMaterials ?? 0;
    public bool IsWorkerTaskIdle => _workerEconomy is not null &&
        _workerEconomy.Task == WorkerEconomy.WorkerTask.Idle;
    public bool HasActiveConstructionTask => _workerEconomy is not null &&
        (_workerEconomy.Task == WorkerEconomy.WorkerTask.MovingToConstruction ||
            _workerEconomy.Task == WorkerEconomy.WorkerTask.Constructing);
    public ICombatTarget CurrentCombatTarget =>
        IsValidCombatTarget(_combatTarget) ? _combatTarget : null!;
    public bool IsSelected { get; private set; }
    public UnitActivity Activity { get; private set; } = UnitActivity.Idle;
    public Vector3 TargetPosition => GlobalPosition;
    public float TargetRadius => OccupancyRadius;

    public override void _Ready()
    {
        if (Definition is null)
        {
            GD.PushWarning($"{Name} has no UnitDefinition; using temporary defaults.");
            Definition = new UnitDefinition();
        }

        Health = Mathf.Max(Definition.MaxHealth, 1.0f);
        AddToGroup(CombatTargetGroups.ForTeam(Team));
        AddToGroup(OccupancyGroup);

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

        EnsureOccupancySystem();
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

    public override void _ExitTree()
    {
        if (IsInstanceValid(_occupancySystem))
        {
            _occupancySystem.Unregister(this);
        }

        InteractionSlotRegistry.ReleaseAll(this);
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
        return BeginMoveOrder(worldTarget, destinationValidated: false);
    }

    internal bool SetValidatedMoveTarget(Vector3 worldTarget)
    {
        return BeginMoveOrder(worldTarget, destinationValidated: true);
    }

    private bool BeginMoveOrder(
        Vector3 worldTarget,
        bool destinationValidated)
    {
        if (!IsAlive || _isGameplayStopped)
        {
            return false;
        }

        _workerEconomy?.CancelTask();
        ClearCombatTarget();
        Activity = UnitActivity.Moving;
        _movement.SetMoveTarget(
            worldTarget,
            Definition.StoppingDistance,
            destinationValidated: destinationValidated);
        return true;
    }

    public bool SetGatherTarget(MaterialsResourceNode target)
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
        return _workerEconomy.BeginGathering(target);
    }

    public bool SetManualDropOff(BuildingEntity building)
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
        return _workerEconomy.BeginManualDropOff(building);
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

    internal void AttachOccupancySystem(UnitOccupancySystem occupancySystem)
    {
        _occupancySystem = occupancySystem;
    }

    internal void NotifyOccupancyMovementChanged(bool moving)
    {
        EnsureOccupancySystem();
        _occupancySystem?.SetMoving(this, moving);
    }

    internal void NotifyOccupancyPositionChanged()
    {
        _occupancySystem?.UpdatePosition(this);
    }

    internal void SetAvoidanceParticipation(bool active)
    {
        _movement?.SetAvoidanceParticipation(active);
    }

    internal bool CanAcceptMovementFallback()
    {
        if (Activity != UnitActivity.Moving)
        {
            return false;
        }

        EnsureOccupancySystem();
        return _occupancySystem is null ||
            !_occupancySystem.IsPositionOccupied(
                GlobalPosition,
                OccupancyRadius,
                this);
    }

    internal bool TryGetCongestionFallback(out Vector3 fallbackPosition)
    {
        if (Activity != UnitActivity.Moving)
        {
            fallbackPosition = GlobalPosition;
            return false;
        }

        EnsureOccupancySystem();
        if (_occupancySystem is not null)
        {
            return _occupancySystem.TryFindNearbyClearPosition(
                this,
                out fallbackPosition);
        }

        fallbackPosition = GlobalPosition;
        return false;
    }

    internal void ReleaseCongestionFallback()
    {
        _occupancySystem?.ReleaseFallbackReservation(this);
    }

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
        _movement.SetMoveTarget(
            worldTarget,
            stoppingDistance,
            destinationValidated: true);
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

        if (!TryGetAttackApproachPosition(
                _combatTarget,
                out Vector3 approachPosition,
                out bool canAttackFromPosition))
        {
            _movement.CancelMoveOrder();
            ClearCombatTarget();
            Activity = UnitActivity.Idle;
            return;
        }

        if (canAttackFromPosition && IsAtInteractionPosition(approachPosition) &&
            _combat.IsTargetInRange(_combatTarget))
        {
            _movement.CancelMoveOrder();
            Activity = UnitActivity.Attacking;
            return;
        }

        if (!canAttackFromPosition && IsAtInteractionPosition(approachPosition))
        {
            _movement.CancelMoveOrder();
            return;
        }

        _movement.SetMoveTarget(
            approachPosition,
            Definition.StoppingDistance,
            replaceCurrentPath: false,
            destinationValidated: true);
    }

    private void UpdateAttack()
    {
        if (!IsValidCombatTarget(_combatTarget))
        {
            ClearCombatTarget();
            Activity = UnitActivity.Idle;
            return;
        }

        if (!IsInsideAttackPosition(_combatTarget))
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
        if (TryGetAttackApproachPosition(
                _combatTarget,
                out Vector3 approachPosition,
                out _))
        {
            _movement.SetMoveTarget(
                approachPosition,
                Definition.StoppingDistance,
                destinationValidated: true);
        }
    }

    private bool IsInsideAttackPosition(ICombatTarget target)
    {
        return TryGetAttackApproachPosition(
                target,
                out Vector3 approachPosition,
                out bool canAttackFromPosition) &&
            canAttackFromPosition &&
            IsAtInteractionPosition(approachPosition) &&
            _combat.IsTargetInRange(target);
    }

    private bool TryGetAttackApproachPosition(
        ICombatTarget target,
        out Vector3 approachPosition,
        out bool canAttackFromPosition)
    {
        if (!IsValidCombatTarget(target) || target is not GodotObject targetObject)
        {
            approachPosition = GlobalPosition;
            canAttackFromPosition = false;
            return false;
        }

        int ordinal = InteractionSlotRegistry.Reserve(
            this,
            targetObject,
            InteractionSlotRegistry.InteractionKind.Attack);
        Rid navigationMap = GetWorld3D().NavigationMap;
        uint mapIteration = NavigationServer3D.MapGetIterationId(navigationMap);
        float refreshDistance = Mathf.Max(
            Definition.MovingTargetRefreshDistance,
            0.1f);
        bool needsRefresh = !_hasAttackApproach ||
            _attackApproachOrdinal != ordinal ||
            _attackApproachMapIteration != mapIteration ||
            HorizontalDistanceSquared(
                _attackApproachTargetPosition,
                target.TargetPosition) >= refreshDistance * refreshDistance;
        if (!needsRefresh)
        {
            approachPosition = _attackApproachPosition;
            canAttackFromPosition = _attackApproachCanAttack;
            return true;
        }

        float maximumAttackCenterDistance = Mathf.Max(
            Definition.AttackRange + target.TargetRadius,
            0.0f);
        Vector3 requestedPosition = InteractionPositioning.GetRadialPosition(
            target.TargetPosition,
            target.TargetRadius,
            OccupancyRadius,
            ordinal,
            maximumAttackCenterDistance,
            out canAttackFromPosition);
        if (!NavigationPathing.TryResolveReachablePoint(
                this,
                requestedPosition,
                OccupancyRadius,
                out approachPosition,
                targetObject))
        {
            return false;
        }

        _attackApproachPosition = approachPosition;
        _attackApproachTargetPosition = target.TargetPosition;
        _attackApproachOrdinal = ordinal;
        _attackApproachMapIteration = mapIteration;
        _hasAttackApproach = true;
        _attackApproachCanAttack = canAttackFromPosition;
        return true;
    }

    private bool IsAtInteractionPosition(Vector3 position)
    {
        Vector2 delta = new(
            GlobalPosition.X - position.X,
            GlobalPosition.Z - position.Z);
        float tolerance = Mathf.Max(
            Definition.StoppingDistance + InteractionPositionTolerance,
            0.1f);
        return delta.LengthSquared() <= tolerance * tolerance;
    }

    private bool IsValidCombatTarget(ICombatTarget target)
    {
        return CombatTargetGroups.IsValid(target) &&
            target.Team != Team;
    }

    private void ClearCombatTarget()
    {
        InteractionSlotRegistry.Release(
            this,
            InteractionSlotRegistry.InteractionKind.Attack);
        _hasAttackApproach = false;
        _attackApproachOrdinal = -1;
        _combatTarget = null!;
    }

    private static float HorizontalDistanceSquared(
        Vector3 first,
        Vector3 second)
    {
        Vector2 delta = new(first.X - second.X, first.Z - second.Z);
        return delta.LengthSquared();
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
        RemoveFromGroup(OccupancyGroup);
        if (IsInstanceValid(_occupancySystem))
        {
            _occupancySystem.Unregister(this);
        }

        QueueFree();
    }

    private void EnsureOccupancySystem()
    {
        if (IsInstanceValid(_occupancySystem))
        {
            return;
        }

        _occupancySystem = GetTree().GetFirstNodeInGroup(
            UnitOccupancySystem.SystemGroup) as UnitOccupancySystem;
        if (IsInstanceValid(_occupancySystem))
        {
            _occupancySystem.Register(this);
        }
    }
}
