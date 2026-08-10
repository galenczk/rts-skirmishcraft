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
    private MeshInstance3D _carryingMarker = null!;
    private WorkerTask _task;
    private float _gatherTimeRemaining;
    private int _resourceSlotIndex;
    private int _resourceSlotCount = 1;
    private int _dropOffSlotIndex;
    private int _dropOffSlotCount = 1;
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
                UpdateMovingToDropOff(manual: false);
                break;
            case WorkerTask.MovingToManualDropOff:
                UpdateMovingToDropOff(manual: true);
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

    public void BeginGathering(
        MaterialsResourceNode resourceTarget,
        int slotIndex,
        int slotCount)
    {
        if (_isStopped || !IsValidResource(resourceTarget))
        {
            return;
        }

        CancelTask();
        _resourceTarget = resourceTarget;
        _dropOffTarget = null!;
        _resourceSlotIndex = slotIndex;
        _resourceSlotCount = Mathf.Max(slotCount, 1);
        _gatherTimeRemaining = 0.0f;

        if (CarriedMaterials >= GetCarryingCapacity())
        {
            BeginReturnToDropOff();
            return;
        }

        MoveToResource();
    }

    public void BeginManualDropOff(
        BuildingEntity building,
        int slotIndex,
        int slotCount)
    {
        if (_isStopped ||
            CarriedMaterials <= 0 ||
            !IsValidDropOff(building))
        {
            return;
        }

        CancelTask();
        _resourceTarget = null!;
        _dropOffTarget = building;
        _dropOffSlotIndex = slotIndex;
        _dropOffSlotCount = Mathf.Max(slotCount, 1);
        _gatherTimeRemaining = 0.0f;
        _task = WorkerTask.MovingToManualDropOff;
        MoveToDropOff(building);
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

        _constructionTarget = null!;
        _task = WorkerTask.Idle;
        _unit.StopWorkerTaskMovement();
    }

    public void CancelTask()
    {
        ClearConstructionAssignment();
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

    private void UpdateMovingToDropOff(bool manual)
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
            MoveToDropOff(_dropOffTarget);
            return;
        }

        DepositCarriedMaterials(manual);
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

        if (!IsWithinDropOffInteractionRange(_constructionTarget))
        {
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
            !IsWithinDropOffInteractionRange(_constructionTarget))
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

        _dropOffTarget = dropOff;
        _dropOffSlotIndex = _resourceSlotIndex;
        _dropOffSlotCount = _resourceSlotCount;
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

        Vector3 interactionPosition = _resourceTarget.GetInteractionPosition(
            _resourceSlotIndex,
            _resourceSlotCount,
            _definition.InteractionRange);
        _task = WorkerTask.MovingToResource;
        _unit.MoveForWorkerTask(
            ProjectToNavigation(interactionPosition),
            _unit.Definition.StoppingDistance);
    }

    private void MoveToDropOff(BuildingEntity building)
    {
        Vector3 interactionPosition = building.GetInteractionPosition(
            _dropOffSlotIndex,
            _dropOffSlotCount,
            _definition.InteractionRange);
        _unit.MoveForWorkerTask(
            ProjectToNavigation(interactionPosition),
            _unit.Definition.StoppingDistance);
    }

    private void MoveToConstruction()
    {
        if (!IsValidConstructionSite(_constructionTarget))
        {
            BecomeIdleAndStopMoving();
            return;
        }

        Vector2 approachDirection = new(
            _unit.GlobalPosition.X - _constructionTarget.GlobalPosition.X,
            _unit.GlobalPosition.Z - _constructionTarget.GlobalPosition.Z);
        if (approachDirection.IsZeroApprox())
        {
            approachDirection = Vector2.Right;
        }

        approachDirection = approachDirection.Normalized();
        float distance = _constructionTarget.TargetRadius +
            Mathf.Max(_definition.InteractionRange, 0.0f) * 0.5f;
        Vector3 interactionPosition = _constructionTarget.GlobalPosition +
            new Vector3(
                approachDirection.X * distance,
                0.0f,
                approachDirection.Y * distance);
        _task = WorkerTask.MovingToConstruction;
        _unit.MoveForWorkerTask(
            ProjectToNavigation(interactionPosition),
            _unit.Definition.StoppingDistance);
    }

    private void DepositCarriedMaterials(bool manual)
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

        if (!manual && IsValidResource(_resourceTarget))
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
        _task = WorkerTask.Idle;
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

    private Vector3 ProjectToNavigation(Vector3 position)
    {
        Rid navigationMap = _unit.GetWorld3D().NavigationMap;
        if (NavigationServer3D.MapGetIterationId(navigationMap) == 0)
        {
            return position;
        }

        return NavigationServer3D.MapGetClosestPoint(navigationMap, position);
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
