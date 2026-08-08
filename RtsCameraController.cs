using Godot;

public partial class RtsCameraController : Node3D
{
    private static readonly StringName PanLeftAction = "camera_pan_left";
    private static readonly StringName PanRightAction = "camera_pan_right";
    private static readonly StringName PanForwardAction = "camera_pan_forward";
    private static readonly StringName PanBackwardAction = "camera_pan_backward";
    private static readonly StringName ZoomInAction = "camera_zoom_in";
    private static readonly StringName ZoomOutAction = "camera_zoom_out";

    [Export]
    public float PanSpeed { get; set; } = 14.0f;

    [Export]
    public float ZoomSpeed { get; set; } = 2.5f;

    [Export]
    public float MinimumZoomDistance { get; set; } = 10.0f;

    [Export]
    public float MaximumZoomDistance { get; set; } = 32.0f;

    [Export]
    public Vector2 PanLimits { get; set; } = new(18.0f, 13.0f);

    private Camera3D _camera = null!;
    private float _zoomDistance;

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("Camera3D");
        _zoomDistance = ClampZoomDistance(_camera.Position.Length());
        ApplyZoomDistance();
    }

    public override void _Process(double delta)
    {
        Vector2 inputDirection = Input.GetVector(
            PanLeftAction,
            PanRightAction,
            PanForwardAction,
            PanBackwardAction);

        Vector3 movement = new(inputDirection.X, 0.0f, inputDirection.Y);
        Position += movement * Mathf.Max(PanSpeed, 0.0f) * (float)delta;

        Vector2 limits = PanLimits.Abs();
        Position = new Vector3(
            Mathf.Clamp(Position.X, -limits.X, limits.X),
            Position.Y,
            Mathf.Clamp(Position.Z, -limits.Y, limits.Y));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(ZoomInAction))
        {
            ChangeZoom(-1.0f);
        }
        else if (@event.IsActionPressed(ZoomOutAction))
        {
            ChangeZoom(1.0f);
        }
    }

    private void ChangeZoom(float direction)
    {
        _zoomDistance = ClampZoomDistance(
            _zoomDistance + direction * Mathf.Abs(ZoomSpeed));
        ApplyZoomDistance();
    }

    private float ClampZoomDistance(float distance)
    {
        float minimum = Mathf.Max(MinimumZoomDistance, 0.1f);
        float maximum = Mathf.Max(MaximumZoomDistance, minimum);
        return Mathf.Clamp(distance, minimum, maximum);
    }

    private void ApplyZoomDistance()
    {
        _camera.Position = _camera.Basis.Z * _zoomDistance;
    }
}
