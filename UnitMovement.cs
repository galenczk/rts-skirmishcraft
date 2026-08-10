using Godot;

public partial class UnitMovement : Node3D
{
    private const float MinimumStoppingDistance = 0.05f;

    private SelectableUnit _unit = null!;
    private UnitDefinition _definition = null!;
    private NavigationAgent3D _navigationAgent = null!;
    private Vector3 _moveTarget;
    private float _stoppingDistance;
    private bool _hasMoveOrder;
    private bool _initialized;
    private bool _isShutdown;

    public bool IsMoving => _hasMoveOrder;

    public override void _Ready()
    {
        SetPhysicsProcess(_initialized);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_hasMoveOrder)
        {
            return;
        }

        Vector3 nextPathPosition = _navigationAgent.GetNextPathPosition();
        if (_navigationAgent.IsNavigationFinished())
        {
            CompleteMoveOrder();
            return;
        }

        Vector3 horizontalPathPosition = new(
            nextPathPosition.X,
            _unit.GlobalPosition.Y,
            nextPathPosition.Z);
        float movementStep = Mathf.Max(_definition.MovementSpeed, 0.0f) * (float)delta;
        _unit.GlobalPosition = _unit.GlobalPosition.MoveToward(
            horizontalPathPosition,
            movementStep);

        Vector2 remainingDistance = new(
            _unit.GlobalPosition.X - _moveTarget.X,
            _unit.GlobalPosition.Z - _moveTarget.Z);
        if (remainingDistance.LengthSquared() <= _stoppingDistance * _stoppingDistance)
        {
            CompleteMoveOrder();
        }
    }

    public void Initialize(SelectableUnit unit, UnitDefinition definition)
    {
        _unit = unit;
        _definition = definition;
        _stoppingDistance = GetStoppingDistance(definition.StoppingDistance);
        _navigationAgent = CreateNavigationAgent();
        AddChild(_navigationAgent);
        _initialized = true;
        SetPhysicsProcess(true);
    }

    public void SetMoveTarget(Vector3 worldTarget, float stoppingDistance)
    {
        if (_isShutdown)
        {
            return;
        }

        _moveTarget = worldTarget;
        _stoppingDistance = GetStoppingDistance(stoppingDistance);
        _navigationAgent.TargetDesiredDistance = _stoppingDistance;
        _navigationAgent.MaxSpeed = Mathf.Max(_definition.MovementSpeed, 0.0f);
        _navigationAgent.TargetPosition = worldTarget;
        _hasMoveOrder = true;
    }

    public void CancelMoveOrder()
    {
        if (!_isShutdown)
        {
            _hasMoveOrder = false;
        }
    }

    public void Stop()
    {
        _isShutdown = true;
        _hasMoveOrder = false;
        SetPhysicsProcess(false);
    }

    private void CompleteMoveOrder()
    {
        _hasMoveOrder = false;
        _unit.NotifyMovementCompleted();
    }

    private NavigationAgent3D CreateNavigationAgent()
    {
        return new NavigationAgent3D
        {
            Name = "NavigationAgent3D",
            PathDesiredDistance = 0.2f,
            PathHeightOffset = _unit.GetAabb().Position.Y,
            TargetDesiredDistance = _stoppingDistance,
            Radius = 0.45f,
            Height = 1.6f,
            MaxSpeed = Mathf.Max(_definition.MovementSpeed, 0.0f),
            AvoidanceEnabled = false,
        };
    }

    private static float GetStoppingDistance(float requestedDistance)
    {
        return Mathf.Max(requestedDistance, MinimumStoppingDistance);
    }
}
