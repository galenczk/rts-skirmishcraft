using Godot;

public partial class UnitMovement : Node3D
{
    private const float MinimumStoppingDistance = 0.05f;
    private const float MinimumNavigationProjectionTolerance = 0.25f;

    private SelectableUnit _unit = null!;
    private UnitDefinition _definition = null!;
    private NavigationAgent3D _navigationAgent = null!;
    private Vector3 _moveTarget;
    private float _stoppingDistance;
    private bool _hasMoveOrder;
    private bool _initialized;
    private bool _isShutdown;
    private bool _awaitingPath;
    private Vector3 _safeVelocity;
    private uint _pathMapIteration;
    private float _stuckElapsed;
    private float _intervalStartWaypointDistance = float.MaxValue;
    private int _trackedPathIndex = -1;
    private int _stuckRepathCount;
    private float _moveElapsed;
    private float _maximumMoveDuration;
    private bool _isWaitingForClearance;
    private bool _hasUsedCongestionFallback;

    public bool IsMoving => _hasMoveOrder;

    public override void _Ready()
    {
        SetPhysicsProcess(_initialized);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_hasMoveOrder)
        {
            _navigationAgent.AvoidancePriority = 1.0f;
            _navigationAgent.Velocity = Vector3.Zero;
            return;
        }

        Rid navigationMap = _unit.GetWorld3D().NavigationMap;
        uint currentMapIteration = NavigationServer3D.MapGetIterationId(
            navigationMap);
        if (currentMapIteration == 0 ||
            NavigationPathing.IsMapSynchronizing(navigationMap))
        {
            ClearVelocity();
            _awaitingPath = true;
            return;
        }

        if (_pathMapIteration != 0 &&
            currentMapIteration != _pathMapIteration)
        {
            ClearVelocity();
            _awaitingPath = true;
        }

        if (_awaitingPath && !TryBeginPath())
        {
            CompleteMoveOrder();
            return;
        }

        if (_isWaitingForClearance)
        {
            ClearVelocity();
            if (_unit.CanAcceptMovementFallback())
            {
                CompleteMoveOrder();
            }

            return;
        }

        _moveElapsed += (float)delta;
        if (_moveElapsed >= _maximumMoveDuration)
        {
            EnterCongestionWait();
            return;
        }

        ApplySafeVelocity((float)delta);
        if (!_hasMoveOrder)
        {
            return;
        }

        float fallbackArrivalDistance = _stoppingDistance +
            _unit.OccupancyRadius;
        if (_hasUsedCongestionFallback &&
            HorizontalDistanceSquared(_unit.GlobalPosition, _moveTarget) <=
                fallbackArrivalDistance * fallbackArrivalDistance &&
            _unit.CanAcceptMovementFallback())
        {
            CompleteMoveOrder();
            return;
        }

        _navigationAgent.AvoidancePriority = 0.5f;

        Vector3 nextPathPosition = _navigationAgent.GetNextPathPosition();
        if (_navigationAgent.IsNavigationFinished())
        {
            _navigationAgent.Velocity = Vector3.Zero;
            CompleteMoveOrder();
            return;
        }

        Vector3 horizontalPathPosition = new(
            nextPathPosition.X,
            _unit.GlobalPosition.Y,
            nextPathPosition.Z);
        Vector3 desiredVelocity = horizontalPathPosition - _unit.GlobalPosition;
        desiredVelocity.Y = 0.0f;
        if (!desiredVelocity.IsZeroApprox())
        {
            desiredVelocity = desiredVelocity.Normalized() *
                Mathf.Max(_definition.MovementSpeed, 0.0f);
        }

        if (UpdateStuckState(
                (float)delta,
                horizontalPathPosition,
                desiredVelocity))
        {
            return;
        }

        _navigationAgent.Velocity = desiredVelocity;
    }

    public void Initialize(SelectableUnit unit, UnitDefinition definition)
    {
        _unit = unit;
        _definition = definition;
        _stoppingDistance = GetStoppingDistance(definition.StoppingDistance);
        _navigationAgent = CreateNavigationAgent();
        AddChild(_navigationAgent);
        _navigationAgent.VelocityComputed += OnVelocityComputed;
        _initialized = true;
        SetPhysicsProcess(false);
    }

    public void SetMoveTarget(
        Vector3 worldTarget,
        float stoppingDistance,
        bool replaceCurrentPath = true,
        bool destinationValidated = false)
    {
        if (_isShutdown)
        {
            return;
        }

        float refreshDistance = Mathf.Max(
            _definition.MovingTargetRefreshDistance,
            0.1f);
        if (_hasMoveOrder &&
            !replaceCurrentPath &&
            HorizontalDistanceSquared(_moveTarget, worldTarget) <
                refreshDistance * refreshDistance)
        {
            return;
        }

        _moveTarget = worldTarget;
        _unit.ReleaseCongestionFallback();
        _stoppingDistance = GetStoppingDistance(stoppingDistance);
        _hasMoveOrder = true;
        SetPhysicsProcess(true);
        _navigationAgent.AvoidanceEnabled = true;
        _unit.NotifyOccupancyMovementChanged(true);
        _awaitingPath = true;
        _pathMapIteration = 0;
        _stuckRepathCount = 0;
        _isWaitingForClearance = false;
        _hasUsedCongestionFallback = false;
        _moveElapsed = 0.0f;
        float directDistance = Mathf.Sqrt(HorizontalDistanceSquared(
            _unit.GlobalPosition,
            worldTarget));
        float estimatedTravelTime = directDistance /
            Mathf.Max(_definition.MovementSpeed, 0.1f);
        _maximumMoveDuration = Mathf.Max(
            estimatedTravelTime * Mathf.Max(
                _definition.NavigationTimeAllowanceMultiplier,
                1.0f) + Mathf.Max(_definition.CongestionGracePeriod, 1.0f),
            Mathf.Max(_definition.CongestionGracePeriod, 1.0f));
        ResetProgressSample();
        ClearVelocity();
        _navigationAgent.AvoidancePriority = 0.5f;
        Rid navigationMap = _unit.GetWorld3D().NavigationMap;
        if (NavigationServer3D.MapGetIterationId(navigationMap) != 0 &&
            !NavigationPathing.IsMapSynchronizing(navigationMap))
        {
            if (destinationValidated)
            {
                BeginAgentPath(worldTarget);
            }
            else if (!TryBeginPath())
            {
                CompleteMoveOrder();
            }
        }
    }

    public void CancelMoveOrder()
    {
        if (!_isShutdown)
        {
            _hasMoveOrder = false;
            _isWaitingForClearance = false;
            _awaitingPath = false;
            _pathMapIteration = 0;
            ResetProgressSample();
            _navigationAgent.AvoidancePriority = 1.0f;
            ClearVelocity();
            _unit.NotifyOccupancyMovementChanged(false);
            SetPhysicsProcess(false);
        }
    }

    public void Stop()
    {
        _isShutdown = true;
        _hasMoveOrder = false;
        _isWaitingForClearance = false;
        _awaitingPath = false;
        _pathMapIteration = 0;
        ResetProgressSample();
        _navigationAgent.AvoidancePriority = 1.0f;
        ClearVelocity();
        _navigationAgent.AvoidanceEnabled = false;
        _unit.NotifyOccupancyMovementChanged(false);
        SetPhysicsProcess(false);
    }

    private void CompleteMoveOrder()
    {
        _hasMoveOrder = false;
        _isWaitingForClearance = false;
        _awaitingPath = false;
        _pathMapIteration = 0;
        ResetProgressSample();
        ClearVelocity();
        _unit.NotifyOccupancyMovementChanged(false);
        SetPhysicsProcess(false);
        _unit.NotifyMovementCompleted();
    }

    private void OnVelocityComputed(Vector3 safeVelocity)
    {
        if (_isShutdown || !_hasMoveOrder || _awaitingPath)
        {
            _safeVelocity = Vector3.Zero;
            return;
        }

        float maximumSpeed = Mathf.Max(_definition.MovementSpeed, 0.0f);
        _safeVelocity = new Vector3(safeVelocity.X, 0.0f, safeVelocity.Z);
        if (_safeVelocity.LengthSquared() > maximumSpeed * maximumSpeed &&
            maximumSpeed > 0.0f)
        {
            _safeVelocity = _safeVelocity.Normalized() * maximumSpeed;
        }
    }

    private void ApplySafeVelocity(float delta)
    {
        if (_safeVelocity.IsZeroApprox() || delta <= 0.0f)
        {
            return;
        }

        Vector3 candidatePosition = _unit.GlobalPosition +
            _safeVelocity * delta;
        Rid navigationMap = _unit.GetWorld3D().NavigationMap;
        if (NavigationServer3D.MapGetIterationId(navigationMap) != 0)
        {
            Vector3 projectedPosition = NavigationServer3D.MapGetClosestPoint(
                navigationMap,
                candidatePosition);
            Vector2 projectionDelta = new(
                candidatePosition.X - projectedPosition.X,
                candidatePosition.Z - projectedPosition.Z);
            float projectionTolerance = Mathf.Max(
                _unit.OccupancyRadius,
                MinimumNavigationProjectionTolerance);
            if (projectionDelta.LengthSquared() <=
                projectionTolerance * projectionTolerance)
            {
                candidatePosition.X = projectedPosition.X;
                candidatePosition.Z = projectedPosition.Z;
            }
            else
            {
                return;
            }
        }

        candidatePosition.Y = _unit.GlobalPosition.Y;
        if (!NavigationPathing.IsClearOfStaticFootprints(
                _unit.GetTree(),
                candidatePosition,
                _unit.OccupancyRadius))
        {
            return;
        }

        _unit.GlobalPosition = candidatePosition;
        _unit.NotifyOccupancyPositionChanged();

        if (!_hasMoveOrder)
        {
            return;
        }

        Vector2 remainingDistance = new(
            _unit.GlobalPosition.X - _moveTarget.X,
            _unit.GlobalPosition.Z - _moveTarget.Z);
        if (remainingDistance.LengthSquared() <= _stoppingDistance * _stoppingDistance)
        {
            CompleteMoveOrder();
        }
    }

    private NavigationAgent3D CreateNavigationAgent()
    {
        return new NavigationAgent3D
        {
            Name = "NavigationAgent3D",
            PathDesiredDistance = 0.2f,
            TargetDesiredDistance = _stoppingDistance,
            Radius = Mathf.Max(_definition.OccupancyRadius, 0.1f),
            Height = 1.6f,
            MaxSpeed = Mathf.Max(_definition.MovementSpeed, 0.0f),
            NeighborDistance = Mathf.Max(_definition.OccupancyRadius * 4.0f, 2.0f),
            MaxNeighbors = 8,
            TimeHorizonAgents = 0.5f,
            TimeHorizonObstacles = 1.0f,
            AvoidancePriority = 0.5f,
            AvoidanceEnabled = false,
        };
    }

    public void SetAvoidanceParticipation(bool active)
    {
        if (_isShutdown || !IsInstanceValid(_navigationAgent))
        {
            return;
        }

        bool enabled = active || _hasMoveOrder;
        _navigationAgent.AvoidanceEnabled = enabled;
        if (!_hasMoveOrder)
        {
            _safeVelocity = Vector3.Zero;
            _navigationAgent.Velocity = Vector3.Zero;
        }
    }

    private void AlignPathHeightToNavigationSurface()
    {
        Rid navigationMap = _unit.GetWorld3D().NavigationMap;
        if (NavigationServer3D.MapGetIterationId(navigationMap) == 0)
        {
            return;
        }

        Vector3 closestNavigationPoint = NavigationServer3D.MapGetClosestPoint(
            navigationMap,
            _unit.GlobalPosition);
        _navigationAgent.PathHeightOffset =
            closestNavigationPoint.Y - _unit.GlobalPosition.Y;
    }

    private static float GetStoppingDistance(float requestedDistance)
    {
        return Mathf.Max(requestedDistance, MinimumStoppingDistance);
    }

    private bool TryBeginPath()
    {
        if (!NavigationPathing.TryResolveReachablePoint(
                _unit,
                _moveTarget,
                _unit.OccupancyRadius,
                out Vector3 reachableTarget))
        {
            return false;
        }

        BeginAgentPath(reachableTarget);
        return true;
    }

    private void BeginAgentPath(Vector3 reachableTarget)
    {
        _moveTarget = reachableTarget;
        AlignPathHeightToNavigationSurface();
        _navigationAgent.TargetDesiredDistance = _stoppingDistance;
        _navigationAgent.MaxSpeed = Mathf.Max(_definition.MovementSpeed, 0.0f);
        _navigationAgent.TargetPosition = reachableTarget;
        _pathMapIteration = NavigationServer3D.MapGetIterationId(
            _unit.GetWorld3D().NavigationMap);
        _awaitingPath = false;
        ResetProgressSample();
    }

    private bool UpdateStuckState(
        float delta,
        Vector3 nextPathPosition,
        Vector3 desiredVelocity)
    {
        if (desiredVelocity.IsZeroApprox())
        {
            ResetProgressSample();
            return false;
        }

        float waypointDistance = Mathf.Sqrt(HorizontalDistanceSquared(
            _unit.GlobalPosition,
            nextPathPosition));
        int pathIndex = _navigationAgent.GetCurrentNavigationPathIndex();
        if (pathIndex != _trackedPathIndex)
        {
            if (_trackedPathIndex >= 0 && pathIndex > _trackedPathIndex)
            {
                _stuckRepathCount = 0;
            }

            _trackedPathIndex = pathIndex;
            _intervalStartWaypointDistance = waypointDistance;
            _stuckElapsed = 0.0f;
            return false;
        }

        _stuckElapsed += delta;
        if (_stuckElapsed < Mathf.Max(
                _definition.StuckCheckInterval,
                0.25f))
        {
            return false;
        }

        float progress = _intervalStartWaypointDistance - waypointDistance;
        _stuckElapsed = 0.0f;
        _intervalStartWaypointDistance = waypointDistance;
        if (progress >= Mathf.Max(
                _definition.StuckProgressThreshold,
                0.01f))
        {
            return false;
        }

        if (_stuckRepathCount >= Mathf.Max(
                _definition.StuckRepathLimit,
                0))
        {
            EnterCongestionWait();
            return true;
        }

        _stuckRepathCount++;
        ClearVelocity();
        _awaitingPath = true;
        if (!TryBeginPath())
        {
            CompleteMoveOrder();
        }

        return true;
    }

    private void ResetProgressSample()
    {
        _stuckElapsed = 0.0f;
        _intervalStartWaypointDistance = float.MaxValue;
        _trackedPathIndex = -1;
    }

    private void ClearVelocity()
    {
        _safeVelocity = Vector3.Zero;
        if (IsInstanceValid(_navigationAgent))
        {
            _navigationAgent.Velocity = Vector3.Zero;
        }
    }

    private void EnterCongestionWait()
    {
        if (!_hasUsedCongestionFallback &&
            _unit.TryGetCongestionFallback(out Vector3 fallbackPosition))
        {
            _hasUsedCongestionFallback = true;
            _moveTarget = fallbackPosition;
            _moveElapsed = 0.0f;
            _maximumMoveDuration = Mathf.Max(
                _definition.CongestionGracePeriod,
                1.0f);
            _stuckRepathCount = 0;
            _awaitingPath = true;
            ResetProgressSample();
            ClearVelocity();
            return;
        }

        _isWaitingForClearance = true;
        _awaitingPath = false;
        ResetProgressSample();
        _navigationAgent.AvoidancePriority = 1.0f;
        ClearVelocity();
    }

    private static float HorizontalDistanceSquared(
        Vector3 first,
        Vector3 second)
    {
        Vector2 delta = new(first.X - second.X, first.Z - second.Z);
        return delta.LengthSquared();
    }
}
