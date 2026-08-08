using Godot;

public partial class SelectableUnit : MeshInstance3D
{
    public static readonly StringName FriendlySelectionGroup = "friendly_selectable_units";

    [Export]
    public float MovementSpeed { get; set; } = 4.0f;

    [Export]
    public float StoppingDistance { get; set; } = 0.3f;

    private MeshInstance3D _selectionMarker = null!;
    private NavigationAgent3D _navigationAgent = null!;
    private Vector3 _moveTarget;
    private bool _hasMoveOrder;

    public bool IsSelected { get; private set; }

    public override void _Ready()
    {
        AddToGroup(FriendlySelectionGroup);
        _navigationAgent = CreateNavigationAgent();
        AddChild(_navigationAgent);
        _selectionMarker = CreateSelectionMarker();
        AddChild(_selectionMarker);
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
            _hasMoveOrder = false;
            return;
        }

        Vector3 horizontalPathPosition = new(
            nextPathPosition.X,
            GlobalPosition.Y,
            nextPathPosition.Z);
        float movementStep = Mathf.Max(MovementSpeed, 0.0f) * (float)delta;
        GlobalPosition = GlobalPosition.MoveToward(horizontalPathPosition, movementStep);

        Vector2 remainingDistance = new(
            GlobalPosition.X - _moveTarget.X,
            GlobalPosition.Z - _moveTarget.Z);
        float stoppingDistance = Mathf.Max(StoppingDistance, 0.05f);
        if (remainingDistance.LengthSquared() <= stoppingDistance * stoppingDistance)
        {
            _hasMoveOrder = false;
        }
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        _selectionMarker.Visible = selected;
    }

    public void SetMoveTarget(Vector3 worldTarget)
    {
        _moveTarget = worldTarget;
        _navigationAgent.TargetDesiredDistance = Mathf.Max(StoppingDistance, 0.05f);
        _navigationAgent.MaxSpeed = Mathf.Max(MovementSpeed, 0.0f);
        _navigationAgent.TargetPosition = worldTarget;
        _hasMoveOrder = true;
    }

    private NavigationAgent3D CreateNavigationAgent()
    {
        return new NavigationAgent3D
        {
            Name = "NavigationAgent3D",
            PathDesiredDistance = 0.2f,
            PathHeightOffset = -0.8f,
            TargetDesiredDistance = Mathf.Max(StoppingDistance, 0.05f),
            Radius = 0.45f,
            Height = 1.6f,
            MaxSpeed = Mathf.Max(MovementSpeed, 0.0f),
            AvoidanceEnabled = false,
        };
    }

    private static MeshInstance3D CreateSelectionMarker()
    {
        StandardMaterial3D markerMaterial = new()
        {
            AlbedoColor = new Color(1.0f, 0.82f, 0.08f, 1.0f),
            Roughness = 1.0f,
        };

        CylinderMesh markerMesh = new()
        {
            TopRadius = 0.68f,
            BottomRadius = 0.68f,
            Height = 0.04f,
            RadialSegments = 24,
            Material = markerMaterial,
        };

        return new MeshInstance3D
        {
            Name = "SelectionMarker",
            Position = new Vector3(0.0f, -0.76f, 0.0f),
            Mesh = markerMesh,
            Visible = false,
        };
    }
}
