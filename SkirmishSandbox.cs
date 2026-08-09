using Godot;
using System.Collections.Generic;

public partial class SkirmishSandbox : Node3D
{
    private static readonly StringName SelectUnitsAction = "select_units";
    private static readonly StringName MoveUnitsAction = "move_units";
    private static readonly StringName LoadDefaultScenarioAction = "debug_units_default";
    private static readonly StringName Load20UnitScenarioAction = "debug_units_20";
    private static readonly StringName Load100UnitScenarioAction = "debug_units_100";
    private static readonly StringName Load250UnitScenarioAction = "debug_units_250";
    private static readonly StringName Load500UnitScenarioAction = "debug_units_500";
    private static readonly StringName RestartMatchAction = "restart_match";
    private static readonly StringName MovementGroundGroup = "movement_ground";
    private const float DragThresholdPixels = 6.0f;
    private const float ClickBoundsPaddingPixels = 4.0f;
    private const float GroundRayLength = 1000.0f;
    private const uint GroundCollisionMask = 1u;
    private const float DebugOverlayUpdateInterval = 0.25f;
    private const float FriendlyUnitHeight = 0.8f;

    [Export]
    public float MoveSlotSpacing { get; set; } = 1.5f;

    [Export]
    public float DebugSpawnSpacing { get; set; } = 1.1f;

    [Export]
    public float DebugTeamCenterSeparation { get; set; } = 16.5f;

    private readonly List<SelectableUnit> _selectedUnits = new();
    private Camera3D _camera = null!;
    private Node3D _friendlyUnits = null!;
    private Node3D _enemyUnits = null!;
    private Mesh _friendlyUnitMesh = null!;
    private Mesh _enemyUnitMesh = null!;
    private UnitDefinition _friendlyUnitDefinition = null!;
    private UnitDefinition _enemyUnitDefinition = null!;
    private Transform3D[] _defaultFriendlyTransforms = null!;
    private Transform3D[] _defaultEnemyTransforms = null!;
    private Vector2 _playableBattlefieldSize;
    private Control _selectionRectangle = null!;
    private Label _debugMetrics = null!;
    private CanvasLayer _matchOutcomeOverlay = null!;
    private Label _matchOutcomeLabel = null!;
    private Vector2 _dragStart;
    private Vector2 _dragCurrent;
    private double _debugOverlayUpdateTime;
    private bool _isDragging;
    private bool _isReplacingScenario;
    private bool _isMatchTrackingActive;
    private bool _isMatchEnded;

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("CameraRig/Camera3D");
        _friendlyUnits = GetNode<Node3D>("FriendlyUnits");
        _enemyUnits = GetNode<Node3D>("EnemyUnits");
        _friendlyUnitMesh = GetNode<MeshInstance3D>("FriendlyUnits/Friendly01").Mesh;
        _enemyUnitMesh = GetNode<MeshInstance3D>("EnemyUnits/Enemy01").Mesh;
        _friendlyUnitDefinition = GetNode<SelectableUnit>(
            "FriendlyUnits/Friendly01").Definition;
        _enemyUnitDefinition = GetNode<SelectableUnit>("EnemyUnits/Enemy01").Definition;
        _defaultFriendlyTransforms = CaptureTransforms(_friendlyUnits);
        _defaultEnemyTransforms = CaptureTransforms(_enemyUnits);
        _playableBattlefieldSize = GetNavigationSize();
        ConfigureCameraBounds();
        _selectionRectangle = GetNode<Control>("SelectionOverlay/SelectionRectangle");
        _debugMetrics = GetNode<Label>("DebugOverlay/MetricsPanel/MetricsLabel");
        _matchOutcomeOverlay = GetNode<CanvasLayer>("MatchOutcomeOverlay");
        _matchOutcomeLabel = GetNode<Label>(
            "MatchOutcomeOverlay/CenterContainer/OutcomePanel/OutcomeText");
        _matchOutcomeOverlay.Visible = false;
        _isMatchTrackingActive = true;
        UpdateDebugOverlay();
    }

    public override void _Process(double delta)
    {
        EvaluateMatchOutcome();

        _debugOverlayUpdateTime += delta;
        if (_debugOverlayUpdateTime < DebugOverlayUpdateInterval)
        {
            return;
        }

        _debugOverlayUpdateTime = 0.0;
        UpdateDebugOverlay();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_isMatchEnded)
        {
            if (@event.IsActionPressed(RestartMatchAction))
            {
                CallDeferred(MethodName.RestartCurrentScene);
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (TryLoadDebugScenario(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.IsActionPressed(MoveUnitsAction))
            {
                HandleContextCommand(mouseButton.Position);
                GetViewport().SetInputAsHandled();
            }
            else if (mouseButton.IsActionPressed(SelectUnitsAction))
            {
                BeginSelectionDrag(mouseButton.Position);
                GetViewport().SetInputAsHandled();
            }
            else if (_isDragging && mouseButton.IsActionReleased(SelectUnitsAction))
            {
                FinishSelectionDrag(mouseButton.Position);
                GetViewport().SetInputAsHandled();
            }
        }
        else if (_isDragging && @event is InputEventMouseMotion mouseMotion)
        {
            _dragCurrent = mouseMotion.Position;
            UpdateSelectionRectangle();
            GetViewport().SetInputAsHandled();
        }
    }

    private bool TryLoadDebugScenario(InputEvent @event)
    {
        if (@event.IsActionPressed(LoadDefaultScenarioAction))
        {
            RespawnFriendlyUnits(8, useDefaultLayout: true);
        }
        else if (@event.IsActionPressed(Load20UnitScenarioAction))
        {
            RespawnFriendlyUnits(20);
        }
        else if (@event.IsActionPressed(Load100UnitScenarioAction))
        {
            RespawnFriendlyUnits(100);
        }
        else if (@event.IsActionPressed(Load250UnitScenarioAction))
        {
            RespawnFriendlyUnits(250);
        }
        else if (@event.IsActionPressed(Load500UnitScenarioAction))
        {
            RespawnFriendlyUnits(500);
        }
        else
        {
            return false;
        }

        return true;
    }

    private void RespawnFriendlyUnits(int count, bool useDefaultLayout = false)
    {
        BeginScenarioReplacement();
        ClearSelection();

        foreach (Node child in _friendlyUnits.GetChildren())
        {
            _friendlyUnits.RemoveChild(child);
            child.QueueFree();
        }

        for (int index = 0; index < count; index++)
        {
            Transform3D transform = useDefaultLayout
                ? _defaultFriendlyTransforms[index]
                : CreateTestSpawnTransform(
                    index,
                    count,
                    DebugTeamCenterSeparation * 0.5f);
            SelectableUnit unit = new()
            {
                Name = $"Friendly{index + 1:D3}",
                Mesh = _friendlyUnitMesh,
                Definition = _friendlyUnitDefinition,
                Transform = transform,
            };
            _friendlyUnits.AddChild(unit);
        }

        if (useDefaultLayout)
        {
            RespawnEnemies(useDefaultLayout: true);
        }
        else
        {
            RespawnEnemies(useDefaultLayout: false);
        }

        EndScenarioReplacement();
        UpdateDebugOverlay();
    }

    private void BeginScenarioReplacement()
    {
        _isReplacingScenario = true;
        _isMatchTrackingActive = false;
        _isMatchEnded = false;
        _matchOutcomeOverlay.Visible = false;
        _isDragging = false;
        _selectionRectangle.Visible = false;
    }

    private void EndScenarioReplacement()
    {
        _isReplacingScenario = false;
        _isMatchTrackingActive = true;
    }

    private void RespawnEnemies(bool useDefaultLayout)
    {
        foreach (Node child in _enemyUnits.GetChildren())
        {
            _enemyUnits.RemoveChild(child);
            child.QueueFree();
        }

        float zOffset = useDefaultLayout
            ? 0.0f
            : -DebugTeamCenterSeparation * 0.5f - GetAverageZ(_defaultEnemyTransforms);

        for (int index = 0; index < _defaultEnemyTransforms.Length; index++)
        {
            Transform3D transform = _defaultEnemyTransforms[index];
            transform.Origin = new Vector3(
                transform.Origin.X,
                transform.Origin.Y,
                transform.Origin.Z + zOffset);

            SelectableUnit unit = new()
            {
                Name = $"Enemy{index + 1:D3}",
                Mesh = _enemyUnitMesh,
                Team = SelectableUnit.UnitTeam.Enemy,
                Definition = _enemyUnitDefinition,
                Transform = transform,
            };
            _enemyUnits.AddChild(unit);
        }
    }

    private static Transform3D[] CaptureTransforms(Node3D units)
    {
        Transform3D[] transforms = new Transform3D[units.GetChildCount()];
        for (int index = 0; index < transforms.Length; index++)
        {
            transforms[index] = units.GetChild<Node3D>(index).Transform;
        }

        return transforms;
    }

    private Transform3D CreateTestSpawnTransform(int index, int count, float centerZ)
    {
        Vector2 playableSize = new(
            Mathf.Max(_playableBattlefieldSize.X, 1.0f),
            Mathf.Max(_playableBattlefieldSize.Y, 1.0f));
        float spacing = Mathf.Max(DebugSpawnSpacing, 0.1f);
        float aspectRatio = playableSize.X / playableSize.Y;
        int columns = Mathf.CeilToInt(Mathf.Sqrt(count * aspectRatio));
        int rows = Mathf.CeilToInt((float)count / columns);
        int row = index / columns;
        int column = index % columns;
        int unitsInRow = Mathf.Min(columns, count - row * columns);
        float x = (column - (unitsInRow - 1) * 0.5f) * spacing;
        float z = centerZ + (row - (rows - 1) * 0.5f) * spacing;
        return new Transform3D(Basis.Identity, new Vector3(x, FriendlyUnitHeight, z));
    }

    private static float GetAverageZ(Transform3D[] transforms)
    {
        float totalZ = 0.0f;
        foreach (Transform3D transform in transforms)
        {
            totalZ += transform.Origin.Z;
        }

        return totalZ / transforms.Length;
    }

    private Vector2 GetNavigationSize()
    {
        NavigationMesh navigationMesh = GetNode<NavigationRegion3D>(
            "NavigationRegion3D").NavigationMesh;
        Vector3[] vertices = navigationMesh.Vertices;
        float minimumX = float.MaxValue;
        float maximumX = float.MinValue;
        float minimumZ = float.MaxValue;
        float maximumZ = float.MinValue;

        foreach (Vector3 vertex in vertices)
        {
            minimumX = Mathf.Min(minimumX, vertex.X);
            maximumX = Mathf.Max(maximumX, vertex.X);
            minimumZ = Mathf.Min(minimumZ, vertex.Z);
            maximumZ = Mathf.Max(maximumZ, vertex.Z);
        }

        return new Vector2(maximumX - minimumX, maximumZ - minimumZ);
    }

    private void ConfigureCameraBounds()
    {
        GetNode<RtsCameraController>("CameraRig").PanLimits = new Vector2(
            Mathf.Max(_playableBattlefieldSize.X * 0.5f - 1.0f, 0.0f),
            Mathf.Max(_playableBattlefieldSize.Y * 0.5f - 1.0f, 0.0f));
    }

    private void UpdateDebugOverlay()
    {
        PruneInvalidSelection();
        _debugMetrics.Text =
            $"FPS: {Engine.GetFramesPerSecond():0}\n" +
            $"Friendly: {_friendlyUnits.GetChildCount()}\n" +
            $"Enemy: {_enemyUnits.GetChildCount()}\n" +
            $"Selected: {_selectedUnits.Count}\n\n" +
            "Unit presets: F1 8 | F2 20 | F3 100 | F4 250 | F5 500";
    }

    private void EvaluateMatchOutcome()
    {
        if (!_isMatchTrackingActive || _isReplacingScenario || _isMatchEnded)
        {
            return;
        }

        int livingFriendlyCount = CountLivingUnits(SelectableUnit.UnitTeam.Friendly);
        int livingEnemyCount = CountLivingUnits(SelectableUnit.UnitTeam.Enemy);
        if (livingFriendlyCount > 0 && livingEnemyCount > 0)
        {
            return;
        }

        if (livingFriendlyCount == 0 && livingEnemyCount == 0)
        {
            EndMatch("Draw");
        }
        else if (livingEnemyCount == 0)
        {
            EndMatch("Victory");
        }
        else
        {
            EndMatch("Defeat");
        }
    }

    private int CountLivingUnits(SelectableUnit.UnitTeam team)
    {
        int count = 0;
        foreach (SelectableUnit unit in GetUnitsInGroup(SelectableUnit.GetCombatGroup(team)))
        {
            count++;
        }

        return count;
    }

    private void EndMatch(string outcome)
    {
        _isMatchEnded = true;
        _isMatchTrackingActive = false;
        ClearSelection();
        _isDragging = false;
        _selectionRectangle.Visible = false;

        foreach (SelectableUnit unit in GetUnitsForTeam(teamFilter: null))
        {
            unit.StopGameplay();
        }

        _matchOutcomeLabel.Text = outcome;
        _matchOutcomeOverlay.Visible = true;
        UpdateDebugOverlay();
    }

    private void RestartCurrentScene()
    {
        Error reloadError = GetTree().ReloadCurrentScene();
        if (reloadError != Error.Ok)
        {
            GD.PushError($"Unable to restart the skirmish scene: {reloadError}");
        }
    }

    private void BeginSelectionDrag(Vector2 position)
    {
        _isDragging = true;
        _dragStart = position;
        _dragCurrent = position;
        _selectionRectangle.Visible = false;
    }

    private void FinishSelectionDrag(Vector2 position)
    {
        _dragCurrent = position;

        if ((_dragCurrent - _dragStart).LengthSquared() >=
            DragThresholdPixels * DragThresholdPixels)
        {
            SelectUnitsInRectangle(CreateSelectionRectangle());
        }
        else
        {
            SelectSingleUnit(position);
        }

        _isDragging = false;
        _selectionRectangle.Visible = false;
    }

    private void UpdateSelectionRectangle()
    {
        Rect2 rectangle = CreateSelectionRectangle();
        _selectionRectangle.Position = rectangle.Position;
        _selectionRectangle.Size = rectangle.Size;
        _selectionRectangle.Visible = rectangle.Size.LengthSquared() >=
            DragThresholdPixels * DragThresholdPixels;
    }

    private Rect2 CreateSelectionRectangle()
    {
        Vector2 topLeft = new(
            Mathf.Min(_dragStart.X, _dragCurrent.X),
            Mathf.Min(_dragStart.Y, _dragCurrent.Y));
        Vector2 bottomRight = new(
            Mathf.Max(_dragStart.X, _dragCurrent.X),
            Mathf.Max(_dragStart.Y, _dragCurrent.Y));
        return new Rect2(topLeft, bottomRight - topLeft);
    }

    private void SelectSingleUnit(Vector2 screenPosition)
    {
        SelectableUnit closestUnit = FindUnitAtScreenPosition(
            screenPosition,
            SelectableUnit.UnitTeam.Friendly);

        ClearSelection();
        if (closestUnit is not null)
        {
            AddToSelection(closestUnit);
        }
    }

    private void SelectUnitsInRectangle(Rect2 rectangle)
    {
        ClearSelection();

        foreach (SelectableUnit unit in GetUnitsForTeam(SelectableUnit.UnitTeam.Friendly))
        {
            if (_camera.IsPositionBehind(unit.GlobalPosition))
            {
                continue;
            }

            Vector2 screenPosition = _camera.UnprojectPosition(unit.GlobalPosition);
            if (rectangle.HasPoint(screenPosition))
            {
                AddToSelection(unit);
            }
        }
    }

    private SelectableUnit FindUnitAtScreenPosition(
        Vector2 screenPosition,
        SelectableUnit.UnitTeam? teamFilter)
    {
        SelectableUnit closestUnit = null!;
        float closestDistanceSquared = float.MaxValue;

        foreach (SelectableUnit unit in GetUnitsForTeam(teamFilter))
        {
            if (!TryGetUnitScreenBounds(unit, out Rect2 screenBounds) ||
                !screenBounds.Grow(ClickBoundsPaddingPixels).HasPoint(screenPosition))
            {
                continue;
            }

            float distanceSquared = _camera.GlobalPosition.DistanceSquaredTo(
                unit.GlobalPosition);
            if (distanceSquared < closestDistanceSquared)
            {
                closestDistanceSquared = distanceSquared;
                closestUnit = unit;
            }
        }

        return closestUnit;
    }

    private IEnumerable<SelectableUnit> GetUnitsForTeam(
        SelectableUnit.UnitTeam? teamFilter)
    {
        if (teamFilter.HasValue)
        {
            foreach (SelectableUnit unit in GetUnitsInGroup(
                SelectableUnit.GetCombatGroup(teamFilter.Value)))
            {
                yield return unit;
            }

            yield break;
        }

        foreach (SelectableUnit unit in GetUnitsInGroup(
            SelectableUnit.GetCombatGroup(SelectableUnit.UnitTeam.Friendly)))
        {
            yield return unit;
        }

        foreach (SelectableUnit unit in GetUnitsInGroup(
            SelectableUnit.GetCombatGroup(SelectableUnit.UnitTeam.Enemy)))
        {
            yield return unit;
        }
    }

    private IEnumerable<SelectableUnit> GetUnitsInGroup(StringName group)
    {
        foreach (Node node in GetTree().GetNodesInGroup(group))
        {
            if (node is SelectableUnit unit && IsInstanceValid(unit) && unit.IsAlive)
            {
                yield return unit;
            }
        }
    }

    private void HandleContextCommand(Vector2 screenPosition)
    {
        SelectableUnit clickedUnit = FindUnitAtScreenPosition(
            screenPosition,
            teamFilter: null);
        if (clickedUnit is not null)
        {
            if (clickedUnit.Team == SelectableUnit.UnitTeam.Enemy)
            {
                IssueAttackOrder(clickedUnit);
            }

            return;
        }

        TryIssueMoveOrder(screenPosition);
    }

    private void IssueAttackOrder(SelectableUnit target)
    {
        PruneInvalidSelection();
        foreach (SelectableUnit unit in _selectedUnits)
        {
            if (IsInstanceValid(unit))
            {
                unit.SetAttackTarget(target);
            }
        }
    }

    private void TryIssueMoveOrder(Vector2 screenPosition)
    {
        PruneInvalidSelection();
        if (_selectedUnits.Count == 0)
        {
            return;
        }

        Vector3 rayOrigin = _camera.ProjectRayOrigin(screenPosition);
        Vector3 rayEnd = rayOrigin +
            _camera.ProjectRayNormal(screenPosition) * GroundRayLength;
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
            rayOrigin,
            rayEnd,
            GroundCollisionMask);
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

        if (hit.Count == 0 ||
            hit["collider"].AsGodotObject() is not Node collider ||
            !collider.IsInGroup(MovementGroundGroup))
        {
            return;
        }

        Rid navigationMap = GetWorld3D().NavigationMap;
        if (NavigationServer3D.MapGetIterationId(navigationMap) == 0)
        {
            return;
        }

        Vector3 commandDestination = hit["position"].AsVector3();
        float spacing = Mathf.Max(MoveSlotSpacing, 0.1f);

        for (int unitIndex = 0; unitIndex < _selectedUnits.Count; unitIndex++)
        {
            SelectableUnit unit = _selectedUnits[unitIndex];
            if (!IsInstanceValid(unit))
            {
                continue;
            }

            Vector3 requestedDestination = commandDestination +
                GetMoveSlotOffset(unitIndex, _selectedUnits.Count, spacing);
            Vector3 navigationDestination = NavigationServer3D.MapGetClosestPoint(
                navigationMap,
                requestedDestination);
            unit.SetMoveTarget(navigationDestination);
        }
    }

    private static Vector3 GetMoveSlotOffset(int index, int count, float spacing)
    {
        if (count <= 1)
        {
            return Vector3.Zero;
        }

        int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
        int rows = Mathf.CeilToInt((float)count / columns);
        int row = index / columns;
        int column = index % columns;
        int unitsInRow = Mathf.Min(columns, count - row * columns);

        float xOffset = column * spacing - (unitsInRow - 1) * spacing * 0.5f;
        float zOffset = row * spacing - (rows - 1) * spacing * 0.5f;
        return new Vector3(xOffset, 0.0f, zOffset);
    }

    private bool TryGetUnitScreenBounds(SelectableUnit unit, out Rect2 bounds)
    {
        bounds = default;
        Aabb localBounds = unit.GetAabb();
        Vector2 minimum = new(float.MaxValue, float.MaxValue);
        Vector2 maximum = new(float.MinValue, float.MinValue);

        for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
        {
            Vector3 localCorner = localBounds.Position + new Vector3(
                (cornerIndex & 1) == 0 ? 0.0f : localBounds.Size.X,
                (cornerIndex & 2) == 0 ? 0.0f : localBounds.Size.Y,
                (cornerIndex & 4) == 0 ? 0.0f : localBounds.Size.Z);
            Vector3 worldCorner = unit.ToGlobal(localCorner);

            if (_camera.IsPositionBehind(worldCorner))
            {
                return false;
            }

            Vector2 screenCorner = _camera.UnprojectPosition(worldCorner);
            minimum = new Vector2(
                Mathf.Min(minimum.X, screenCorner.X),
                Mathf.Min(minimum.Y, screenCorner.Y));
            maximum = new Vector2(
                Mathf.Max(maximum.X, screenCorner.X),
                Mathf.Max(maximum.Y, screenCorner.Y));
        }

        bounds = new Rect2(minimum, maximum - minimum);
        return true;
    }

    private void AddToSelection(SelectableUnit unit)
    {
        unit.SetSelected(true);
        _selectedUnits.Add(unit);
    }

    private void ClearSelection()
    {
        foreach (SelectableUnit unit in _selectedUnits)
        {
            if (IsInstanceValid(unit))
            {
                unit.SetSelected(false);
            }
        }

        _selectedUnits.Clear();
    }

    private void PruneInvalidSelection()
    {
        for (int index = _selectedUnits.Count - 1; index >= 0; index--)
        {
            if (!IsInstanceValid(_selectedUnits[index]))
            {
                _selectedUnits.RemoveAt(index);
            }
        }
    }
}
