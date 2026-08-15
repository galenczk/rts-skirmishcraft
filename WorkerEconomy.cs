using Godot;

public partial class WorkerEconomy : Node
{
    public enum WorkerTask
    {
        Idle,
        MovingToResource,
        Gathering,
        ReturningToDropOff,
        MovingToManualDropOff,
        MovingToConstruction,
        Constructing,
    }

    private const float MinimumGatherInterval = 0.05f;

    private SelectableUnit _unit = null!;
    private WorkerEconomyDefinition _definition = null!;
    private MaterialsResourceNode _resourceTarget = null!;
    private BuildingEntity _dropOffTarget = null!;
    private BuildingEntity _constructionTarget = null!;
    private Vector3 _constructionInteractionPosition;
    private bool _hasConstructionInteractionPosition;
    private MeshInstance3D _carryingMarker = null!;
    private WorkerTask _task;
    private float _gatherTimeRemaining;
    private bool _isStopped;

    public int CarriedMaterials { get; private set; }
    public WorkerTask Task => _task;

    public override void _Ready()
    {
        SetPhysicsProcess(false);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_isStopped || !_unit.IsAlive)
        {
            return;
        }

        switch (_task)
        {
            case WorkerTask.MovingToResource:
                UpdateMovingToResource();
                break;
            case WorkerTask.Gathering:
                UpdateGathering(delta);
                break;
            case WorkerTask.ReturningToDropOff:
                UpdateMovingToDropOff();
                break;
            case WorkerTask.MovingToManualDropOff:
                UpdateMovingToDropOff();
                break;
            case WorkerTask.MovingToConstruction:
                UpdateMovingToConstruction();
                break;
            case WorkerTask.Constructing:
                UpdateConstruction(delta);
                break;
        }
    }

    public void Initialize(
        SelectableUnit unit,
        WorkerEconomyDefinition definition)
    {
        _unit = unit;
        _definition = definition;
        _carryingMarker = CreateCarryingMarker();
        _unit.AddChild(_carryingMarker);
        UpdateCarryingPresentation();
        SetPhysicsProcess(true);
    }

    public bool BeginGathering(MaterialsResourceNode resourceTarget)
    {
        if (_isStopped || !IsValidResource(resourceTarget))
        {
            return false;
        }

        CancelTask();
        _resourceTarget = resourceTarget;
        _dropOffTarget = null!;
        _hasConstructionInteractionPosition = false;
        _gatherTimeRemaining = 0.0f;

        if (CarriedMaterials >= GetCarryingCapacity())
        {
            BeginReturnToDropOff();
            return true;
        }

        MoveToResource();
        return true;
    }

    public bool BeginManualDropOff(BuildingEntity building)
    {
        if (_isStopped ||
            CarriedMaterials <= 0 ||
            !IsValidDropOff(building))
        {
            return false;
        }

        MaterialsResourceNode resumeTarget = IsValidResource(_resourceTarget)
            ? _resourceTarget
            : null!;
        CancelTask();
        _resourceTarget = resumeTarget;
        _dropOffTarget = building;
        _gatherTimeRemaining = 0.0f;
        _task = WorkerTask.MovingToManualDropOff;
        MoveToDropOff(building);
        return true;
    }

    public bool BeginConstruction(BuildingEntity building)
    {
        if (_isStopped || !IsValidConstructionSite(building))
        {
            return false;
        }

        if (!building.TryAssignBuilder(_unit))
        {
            return false;
        }

        CancelTask();
        if (!building.TryAssignBuilder(_unit))
        {
            return false;
        }

        _constructionTarget = building;
        MoveToConstruction();
        return true;
    }

    public void NotifyConstructionSiteRemoved(BuildingEntity building)
    {
        if (_constructionTarget != building)
        {
            return;
        }

        BecomeIdleAndStopMoving();
    }

    public void CancelTask()
    {
        ClearConstructionAssignment();
        InteractionSlotRegistry.ReleaseAll(_unit);
        _task = WorkerTask.Idle;
        _resourceTarget = null!;
        _dropOffTarget = null!;
        _gatherTimeRemaining = 0.0f;
    }

    public void Stop(bool discardCarriedMaterials)
    {
        _isStopped = true;
        CancelTask();
        if (discardCarriedMaterials)
        {
            CarriedMaterials = 0;
            UpdateCarryingPresentation();
        }

        SetPhysicsProcess(false);
    }

    private void UpdateMovingToResource()
    {
        if (!IsValidResource(_resourceTarget))
        {
            HandleUnavailableResource();
            return;
        }

        if (_unit.IsWorkerTaskMoving)
        {
            return;
        }

        if (!IsWithinResourceInteractionRange(_resourceTarget))
        {
            InteractionSlotRegistry.Release(
                _unit,
                InteractionSlotRegistry.InteractionKind.Resource);
            MoveToResource();
            return;
        }

        _task = WorkerTask.Gathering;
        _gatherTimeRemaining = Mathf.Max(
            _definition.GatherInterval,
            MinimumGatherInterval);
    }

    private void UpdateGathering(double delta)
    {
        if (!IsValidResource(_resourceTarget))
        {
            HandleUnavailableResource();
            return;
        }

        if (!IsWithinResourceInteractionRange(_resourceTarget))
        {
            MoveToResource();
            return;
        }

        _gatherTimeRemaining -= (float)delta;
        if (_gatherTimeRemaining > 0.0f)
        {
            return;
        }

        _gatherTimeRemaining += Mathf.Max(
            _definition.GatherInterval,
            MinimumGatherInterval);
        int remainingCapacity = GetCarryingCapacity() - CarriedMaterials;
        int gatheredAmount = _resourceTarget.TakeMaterials(Mathf.Min(
            Mathf.Max(_definition.GatherAmount, 1),
            remainingCapacity));
        CarriedMaterials += gatheredAmount;
        UpdateCarryingPresentation();

        if (CarriedMaterials >= GetCarryingCapacity() ||
            !IsValidResource(_resourceTarget))
        {
            if (CarriedMaterials > 0)
            {
                BeginReturnToDropOff();
            }
            else
            {
                BecomeIdleAndStopMoving();
            }
        }
    }

    private void UpdateMovingToDropOff()
    {
        if (!IsValidDropOff(_dropOffTarget))
        {
            if (TryFindNearestDropOff(out BuildingEntity replacement))
            {
                _dropOffTarget = replacement;
                MoveToDropOff(replacement);
            }
            else
            {
                BecomeIdleAndStopMoving();
            }

            return;
        }

        if (_unit.IsWorkerTaskMoving)
        {
            return;
        }

        if (!IsWithinDropOffInteractionRange(_dropOffTarget))
        {
            InteractionSlotRegistry.Release(
                _unit,
                InteractionSlotRegistry.InteractionKind.DropOff);
            MoveToDropOff(_dropOffTarget);
            return;
        }

        DepositCarriedMaterials();
    }

    private void UpdateMovingToConstruction()
    {
        if (!IsValidConstructionSite(_constructionTarget))
        {
            BecomeIdleAndStopMoving();
            return;
        }

        if (_unit.IsWorkerTaskMoving)
        {
            return;
        }

        if (!IsWithinConstructionInteractionRange(_constructionTarget))
        {
            _hasConstructionInteractionPosition = false;
            MoveToConstruction();
            return;
        }

        _task = WorkerTask.Constructing;
    }

    private void UpdateConstruction(double delta)
    {
        if (!IsValidConstructionSite(_constructionTarget))
        {
            BecomeIdleAndStopMoving();
            return;
        }

        if (_unit.IsWorkerTaskMoving ||
            !IsWithinConstructionInteractionRange(_constructionTarget))
        {
            return;
        }

        if (_constructionTarget.AdvanceConstruction(_unit, delta))
        {
            _constructionTarget = null!;
            BecomeIdleAndStopMoving();
        }
    }

    private void BeginReturnToDropOff()
    {
        if (CarriedMaterials <= 0)
        {
            BecomeIdleAndStopMoving();
            return;
        }

        if (!TryFindNearestDropOff(out BuildingEntity dropOff))
        {
            BecomeIdleAndStopMoving();
            return;
        }

        InteractionSlotRegistry.Release(
            _unit,
            InteractionSlotRegistry.InteractionKind.Resource);
        _dropOffTarget = dropOff;
        _task = WorkerTask.ReturningToDropOff;
        MoveToDropOff(dropOff);
    }

    private void MoveToResource()
    {
        if (!IsValidResource(_resourceTarget))
        {
            HandleUnavailableResource();
            return;
        }

        int ordinal = InteractionSlotRegistry.Reserve(
            _unit,
            _resourceTarget,
            InteractionSlotRegistry.InteractionKind.Resource);
        Vector3 interactionPosition = InteractionPositioning.GetRadialPosition(
            _resourceTarget.GlobalPosition,
            _resourceTarget.InteractionRadius,
            _unit.OccupancyRadius,
            ordinal,
            _resourceTarget.InteractionRadius +
                Mathf.Max(_definition.InteractionRange, 0.0f),
            out _);
        if (!NavigationPathing.TryResolveReachablePoint(
                _unit,
                interactionPosition,
                _unit.OccupancyRadius,
                out Vector3 reachablePosition,
                _resourceTarget))
        {
            BecomeIdleAndStopMoving();
            return;
        }

        _task = WorkerTask.MovingToResource;
        _unit.MoveForWorkerTask(
            reachablePosition,
            _unit.Definition.StoppingDistance);
    }

    private void MoveToDropOff(BuildingEntity building)
    {
        int ordinal = InteractionSlotRegistry.Reserve(
            _unit,
            building,
            InteractionSlotRegistry.InteractionKind.DropOff);
        Vector3 interactionPosition = InteractionPositioning.GetRadialPosition(
            building.GlobalPosition,
            building.TargetRadius,
            _unit.OccupancyRadius,
            ordinal,
            building.TargetRadius +
                Mathf.Max(_definition.InteractionRange, 0.0f),
            out _);
        if (!NavigationPathing.TryResolveReachablePoint(
                _unit,
                interactionPosition,
                _unit.OccupancyRadius,
                out Vector3 reachablePosition,
                building))
        {
            BecomeIdleAndStopMoving();
            return;
        }

        _unit.MoveForWorkerTask(
            reachablePosition,
            _unit.Definition.StoppingDistance);
    }

    private void MoveToConstruction()
    {
        if (!IsValidConstructionSite(_constructionTarget))
        {
            BecomeIdleAndStopMoving();
            return;
        }

        InteractionSlotRegistry.Reserve(
            _unit,
            _constructionTarget,
            InteractionSlotRegistry.InteractionKind.Construction);
        if (!_hasConstructionInteractionPosition &&
            !TryChooseConstructionInteractionPosition(
                _constructionTarget,
                out _constructionInteractionPosition))
        {
            BecomeIdleAndStopMoving();
            return;
        }

        _hasConstructionInteractionPosition = true;
        _task = WorkerTask.MovingToConstruction;
        _unit.MoveForWorkerTask(
            _constructionInteractionPosition,
            _unit.Definition.StoppingDistance);
    }

    private void DepositCarriedMaterials()
    {
        Node ledgerNode = GetTree().GetFirstNodeInGroup(
            TeamResourceLedger.LedgerGroup);
        if (ledgerNode is not TeamResourceLedger ledger)
        {
            BecomeIdleAndStopMoving();
            return;
        }

        ledger.Deposit(_unit.Team, CarriedMaterials);
        CarriedMaterials = 0;
        UpdateCarryingPresentation();
        InteractionSlotRegistry.Release(
            _unit,
            InteractionSlotRegistry.InteractionKind.DropOff);

        if (IsValidResource(_resourceTarget))
        {
            MoveToResource();
        }
        else
        {
            BecomeIdleAndStopMoving();
        }
    }

    private void HandleUnavailableResource()
    {
        if (CarriedMaterials > 0)
        {
            BeginReturnToDropOff();
        }
        else
        {
            BecomeIdleAndStopMoving();
        }
    }

    private void BecomeIdleAndStopMoving()
    {
        ClearConstructionAssignment();
        InteractionSlotRegistry.ReleaseAll(_unit);
        _task = WorkerTask.Idle;
        _resourceTarget = null!;
        _dropOffTarget = null!;
        _gatherTimeRemaining = 0.0f;
        _unit.StopWorkerTaskMovement();
    }

    private bool TryFindNearestDropOff(out BuildingEntity nearestDropOff)
    {
        nearestDropOff = null!;
        float nearestDistanceSquared = float.MaxValue;
        foreach (Node node in GetTree().GetNodesInGroup(
                     CombatTargetGroups.ForTeam(_unit.Team)))
        {
            if (node is not BuildingEntity building ||
                !IsValidDropOff(building))
            {
                continue;
            }

            float distanceSquared = _unit.GlobalPosition.DistanceSquaredTo(
                building.GlobalPosition);
            bool isCloser = distanceSquared < nearestDistanceSquared;
            bool winsTie = Mathf.IsEqualApprox(
                    distanceSquared,
                    nearestDistanceSquared) &&
                (nearestDropOff is null ||
                    building.GetInstanceId() < nearestDropOff.GetInstanceId());
            if (isCloser || winsTie)
            {
                nearestDropOff = building;
                nearestDistanceSquared = distanceSquared;
            }
        }

        return nearestDropOff is not null;
    }

    private bool IsValidResource(MaterialsResourceNode resource)
    {
        return IsInstanceValid(resource) && !resource.IsQueuedForDeletion() &&
            !resource.IsDepleted;
    }

    private bool IsValidDropOff(BuildingEntity building)
    {
        return IsInstanceValid(building) &&
            !building.IsQueuedForDeletion() &&
            building.IsAlive &&
            building.Team == _unit.Team &&
            building.AcceptsMaterials;
    }

    private bool IsValidConstructionSite(BuildingEntity building)
    {
        return IsInstanceValid(building) &&
            !building.IsQueuedForDeletion() &&
            building.IsAlive &&
            !building.IsComplete &&
            building.Team == _unit.Team;
    }

    private void ClearConstructionAssignment()
    {
        if (IsInstanceValid(_constructionTarget))
        {
            _constructionTarget.ReleaseBuilder(_unit);
        }

        _constructionTarget = null!;
        _hasConstructionInteractionPosition = false;
    }

    private bool IsWithinResourceInteractionRange(MaterialsResourceNode resource)
    {
        return HorizontalDistanceSquaredTo(resource.GlobalPosition) <=
            GetCombinedInteractionRange(resource.InteractionRadius) *
            GetCombinedInteractionRange(resource.InteractionRadius);
    }

    private bool IsWithinDropOffInteractionRange(BuildingEntity building)
    {
        return HorizontalDistanceSquaredTo(building.GlobalPosition) <=
            GetCombinedInteractionRange(building.TargetRadius) *
            GetCombinedInteractionRange(building.TargetRadius);
    }

    private bool IsWithinConstructionInteractionRange(BuildingEntity building)
    {
        Rect2 footprint = building.GetFootprintRect();
        Vector2 workerPosition = new(
            _unit.GlobalPosition.X,
            _unit.GlobalPosition.Z);
        Vector2 closestPoint = new(
            Mathf.Clamp(workerPosition.X, footprint.Position.X, footprint.End.X),
            Mathf.Clamp(workerPosition.Y, footprint.Position.Y, footprint.End.Y));
        float interactionRange = Mathf.Max(_definition.InteractionRange, 0.0f);
        return workerPosition.DistanceSquaredTo(closestPoint) <=
            interactionRange * interactionRange;
    }

    private bool TryChooseConstructionInteractionPosition(
        BuildingEntity building,
        out Vector3 interactionPosition)
    {
        const int candidateCount = 16;
        const float exteriorGap = 0.1f;
        Vector2 halfExtents = building.Definition.FootprintHalfExtents;
        float nearestDistanceSquared = float.MaxValue;
        interactionPosition = _unit.GlobalPosition;
        UnitOccupancySystem occupancySystem = GetTree().GetFirstNodeInGroup(
            UnitOccupancySystem.SystemGroup) as UnitOccupancySystem;

        for (int index = 0; index < candidateCount; index++)
        {
            float angle = -Mathf.Pi * 0.5f +
                Mathf.Tau * index / candidateCount;
            Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
            float xDistance = Mathf.Abs(direction.X) > 0.0001f
                ? halfExtents.X / Mathf.Abs(direction.X)
                : float.MaxValue;
            float zDistance = Mathf.Abs(direction.Y) > 0.0001f
                ? halfExtents.Y / Mathf.Abs(direction.Y)
                : float.MaxValue;
            float footprintDistance = Mathf.Min(xDistance, zDistance);
            float centerDistance = footprintDistance +
                _unit.OccupancyRadius + exteriorGap;
            Vector3 requestedPosition = building.GlobalPosition + new Vector3(
                direction.X * centerDistance,
                0.0f,
                direction.Y * centerDistance);
            if (!NavigationPathing.TryResolveReachablePoint(
                    _unit,
                    requestedPosition,
                    _unit.OccupancyRadius,
                    out Vector3 reachablePosition,
                    building) ||
                (occupancySystem is not null &&
                    occupancySystem.IsPositionOccupied(
                        reachablePosition,
                        _unit.OccupancyRadius,
                        _unit)))
            {
                continue;
            }

            float distanceSquared = HorizontalDistanceSquaredTo(
                reachablePosition);
            if (distanceSquared < nearestDistanceSquared)
            {
                interactionPosition = reachablePosition;
                nearestDistanceSquared = distanceSquared;
            }
        }

        return nearestDistanceSquared < float.MaxValue;
    }

    private float HorizontalDistanceSquaredTo(Vector3 position)
    {
        Vector2 delta = new(
            _unit.GlobalPosition.X - position.X,
            _unit.GlobalPosition.Z - position.Z);
        return delta.LengthSquared();
    }

    private float GetCombinedInteractionRange(float targetRadius)
    {
        return Mathf.Max(targetRadius, 0.0f) +
            Mathf.Max(_definition.InteractionRange, 0.0f);
    }

    private int GetCarryingCapacity()
    {
        return Mathf.Max(_definition.CarryingCapacity, 1);
    }

    private MeshInstance3D CreateCarryingMarker()
    {
        StandardMaterial3D material = new()
        {
            AlbedoColor = new Color(1.0f, 0.72f, 0.12f, 1.0f),
            EmissionEnabled = true,
            Emission = new Color(0.35f, 0.18f, 0.01f, 1.0f),
        };
        return new MeshInstance3D
        {
            Name = "CarryingMarker",
            Position = new Vector3(0.0f, 0.85f, 0.0f),
            Mesh = new SphereMesh
            {
                Radius = 0.16f,
                Height = 0.32f,
                RadialSegments = 8,
                Rings = 4,
                Material = material,
            },
            Visible = false,
        };
    }

    private void UpdateCarryingPresentation()
    {
        if (IsInstanceValid(_carryingMarker))
        {
            _carryingMarker.Visible = CarriedMaterials > 0;
        }
    }
}
