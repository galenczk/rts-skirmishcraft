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
    private static readonly StringName LoadMixedScenarioAction = "debug_units_mixed";
    private static readonly StringName PlaceBuildingAction = "debug_place_building";
    private static readonly StringName CancelConstructionAction = "cancel_construction";
    private static readonly StringName QueueCombatUnitAction = "queue_combat_unit";
    private static readonly StringName CancelProductionAction = "cancel_production";
    private static readonly StringName CancelPlacementAction = "ui_cancel";
    private static readonly StringName RestartMatchAction = "restart_match";
    private static readonly StringName MovementGroundGroup = "movement_ground";
    private static readonly StringName NavigationSourceGroup = "navigation_source";
    private const float DragThresholdPixels = 6.0f;
    private const float ClickBoundsPaddingPixels = 4.0f;
    private const float GroundRayLength = 1000.0f;
    private const uint GroundCollisionMask = 1u;
    private const float DebugOverlayUpdateInterval = 0.25f;
    private const float FriendlyUnitHeight = 0.8f;
    private const float WorkerUnitHeight = 0.5f;
    private const int MixedCombatUnitsPerTeam = 8;
    private const int MixedWorkersPerTeam = 4;
    private const float MixedWorkerRowOffset = 3.0f;
    private const float DestinationSpacingTolerance = 0.001f;
    private const float UnitPlacementRadius = 0.5f;

    [Export]
    public float DebugSpawnSpacing { get; set; } = 1.1f;

    [Export]
    public float DebugTeamCenterSeparation { get; set; } = 16.5f;

    [Export]
    public float MinimumMoveDestinationSpacing { get; set; } = 1.1f;

    [Export]
    public UnitDefinition CombatDefinition { get; set; } = null!;

    [Export]
    public UnitDefinition WorkerDefinition { get; set; } = null!;

    [Export]
    public Mesh FriendlyWorkerMesh { get; set; } = null!;

    [Export]
    public Mesh EnemyWorkerMesh { get; set; } = null!;

    [Export]
    public BuildingDefinition DropOffBuildingDefinition { get; set; } = null!;

    [Export]
    public BuildingDefinition ProductionBuildingDefinition { get; set; } = null!;

    [Export]
    public MaterialsNodeDefinition MaterialsDefinition { get; set; } = null!;

    [Export(PropertyHint.Range, "0,1,0.05")]
    public float ConstructionRefundFraction { get; set; } = 0.75f;

    [Export]
    public int MixedScenarioStartingMaterials { get; set; } = 1000;

    private readonly List<SelectableUnit> _selectedUnits = new();
    private Camera3D _camera = null!;
    private Node3D _friendlyUnits = null!;
    private Node3D _enemyUnits = null!;
    private Node3D _buildings = null!;
    private Node3D _resourceNodes = null!;
    private TeamResourceLedger _resourceLedger = null!;
    private NavigationRegion3D _navigationRegion = null!;
    private Mesh _friendlyUnitMesh = null!;
    private Mesh _enemyUnitMesh = null!;
    private Transform3D[] _defaultFriendlyTransforms = null!;
    private Transform3D[] _defaultEnemyTransforms = null!;
    private Rect2 _playableBattlefieldBounds;
    private Vector2 _playableBattlefieldSize;
    private Control _selectionRectangle = null!;
    private Label _debugMetrics = null!;
    private CanvasLayer _matchOutcomeOverlay = null!;
    private Label _matchOutcomeLabel = null!;
    private BuildingEntity _selectedBuilding = null!;
    private BuildingEntity _friendlyHeadquarters = null!;
    private BuildingEntity _enemyHeadquarters = null!;
    private MeshInstance3D _placementPreview = null!;
    private StandardMaterial3D _validPlacementMaterial = null!;
    private StandardMaterial3D _invalidPlacementMaterial = null!;
    private Vector2 _dragStart;
    private Vector2 _dragCurrent;
    private double _debugOverlayUpdateTime;
    private bool _isDragging;
    private bool _isReplacingScenario;
    private bool _isMatchTrackingActive;
    private bool _isMatchEnded;
    private bool _isPlacementMode;
    private bool _isPlacementValid;
    private bool _navigationRebuildQueued;
    private bool _headquartersConfigurationValid;
    private bool _headquartersRegistrationError;
    private int _runtimeBuildingSerial;
    private int _producedUnitSerial;

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("CameraRig/Camera3D");
        _friendlyUnits = GetNode<Node3D>("FriendlyUnits");
        _enemyUnits = GetNode<Node3D>("EnemyUnits");
        _buildings = GetNode<Node3D>("Buildings");
        _resourceNodes = GetNode<Node3D>("ResourceNodes");
        _resourceLedger = GetNode<TeamResourceLedger>("TeamResourceLedger");
        _navigationRegion = GetNode<NavigationRegion3D>("NavigationRegion3D");
        _friendlyUnitMesh = GetNode<MeshInstance3D>("FriendlyUnits/Friendly01").Mesh;
        _enemyUnitMesh = GetNode<MeshInstance3D>("EnemyUnits/Enemy01").Mesh;
        _defaultFriendlyTransforms = CaptureTransforms(_friendlyUnits);
        _defaultEnemyTransforms = CaptureTransforms(_enemyUnits);
        _playableBattlefieldBounds = GetNavigationBounds();
        _playableBattlefieldSize = _playableBattlefieldBounds.Size;
        ConfigureCameraBounds();
        _selectionRectangle = GetNode<Control>("SelectionOverlay/SelectionRectangle");
        _debugMetrics = GetNode<Label>("DebugOverlay/MetricsPanel/MetricsLabel");
        _matchOutcomeOverlay = GetNode<CanvasLayer>("MatchOutcomeOverlay");
        _matchOutcomeLabel = GetNode<Label>(
            "MatchOutcomeOverlay/CenterContainer/OutcomePanel/OutcomeText");
        _matchOutcomeOverlay.Visible = false;
        _validPlacementMaterial = BuildingEntity.CreateMaterial(
            new Color(0.08f, 0.9f, 0.28f, 0.5f),
            translucent: true);
        _invalidPlacementMaterial = BuildingEntity.CreateMaterial(
            new Color(0.95f, 0.12f, 0.08f, 0.5f),
            translucent: true);
        ResetScenarioBuildings();
        ResetScenarioResources(includeMaterialsNodes: false);
        QueueNavigationRebuild();
        _isMatchTrackingActive = _headquartersConfigurationValid;
        UpdateDebugOverlay();
    }

    public override void _Process(double delta)
    {
        if (_isPlacementMode)
        {
            UpdatePlacementPreview(GetViewport().GetMousePosition());
        }

        ProcessCompletedProductionSpawns();
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

        if (_isPlacementMode)
        {
            if (TryLoadDebugScenario(@event))
            {
                GetViewport().SetInputAsHandled();
                return;
            }

            HandlePlacementInput(@event);
            return;
        }

        if (@event.IsActionPressed(PlaceBuildingAction))
        {
            EnterPlacementMode();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed(CancelConstructionAction))
        {
            TryCancelSelectedConstruction();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed(QueueCombatUnitAction))
        {
            TryQueueUnitAtSelectedBuilding();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed(CancelProductionAction))
        {
            TryCancelProductionAtSelectedBuilding();
            GetViewport().SetInputAsHandled();
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
        else if (@event.IsActionPressed(LoadMixedScenarioAction))
        {
            RespawnMixedRoleScenario();
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

        ClearUnitContainer(_friendlyUnits);

        for (int index = 0; index < count; index++)
        {
            Transform3D transform = useDefaultLayout
                ? _defaultFriendlyTransforms[index]
                : CreateTestSpawnTransform(
                    index,
                    count,
                    DebugTeamCenterSeparation * 0.5f);
            SpawnUnit(
                _friendlyUnits,
                $"Friendly{index + 1:D3}",
                _friendlyUnitMesh,
                UnitTeam.Friendly,
                CombatDefinition,
                transform);
        }

        if (useDefaultLayout)
        {
            RespawnEnemies(useDefaultLayout: true);
        }
        else
        {
            RespawnEnemies(useDefaultLayout: false);
        }

        ResetScenarioBuildings();
        ResetScenarioResources(includeMaterialsNodes: false);
        EndScenarioReplacement();
        UpdateDebugOverlay();
    }

    private void BeginScenarioReplacement()
    {
        _isReplacingScenario = true;
        _isMatchTrackingActive = false;
        ClearRegisteredHeadquarters();
        _isMatchEnded = false;
        _matchOutcomeOverlay.Visible = false;
        _isDragging = false;
        _selectionRectangle.Visible = false;
        ClearBuildingSelection();
        CancelPlacementMode();
        _resourceLedger.Reset();
    }

    private void EndScenarioReplacement()
    {
        _isReplacingScenario = false;
        _isMatchTrackingActive = _headquartersConfigurationValid;
        QueueNavigationRebuild();
    }

    private void RespawnEnemies(bool useDefaultLayout)
    {
        ClearUnitContainer(_enemyUnits);

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

            SpawnUnit(
                _enemyUnits,
                $"Enemy{index + 1:D3}",
                _enemyUnitMesh,
                UnitTeam.Enemy,
                CombatDefinition,
                transform);
        }
    }

    private void RespawnMixedRoleScenario()
    {
        BeginScenarioReplacement();
        _resourceLedger.Deposit(
            UnitTeam.Friendly,
            Mathf.Max(MixedScenarioStartingMaterials, 0));
        ClearSelection();
        ClearUnitContainer(_friendlyUnits);
        ClearUnitContainer(_enemyUnits);

        for (int index = 0; index < MixedCombatUnitsPerTeam; index++)
        {
            SpawnUnit(
                _friendlyUnits,
                $"FriendlyCombat{index + 1:D2}",
                _friendlyUnitMesh,
                UnitTeam.Friendly,
                CombatDefinition,
                _defaultFriendlyTransforms[index]);
            SpawnUnit(
                _enemyUnits,
                $"EnemyCombat{index + 1:D2}",
                _enemyUnitMesh,
                UnitTeam.Enemy,
                CombatDefinition,
                _defaultEnemyTransforms[index]);
        }

        for (int index = 0; index < MixedWorkersPerTeam; index++)
        {
            float x = (index - (MixedWorkersPerTeam - 1) * 0.5f) * MixedWorkerRowOffset;
            SpawnUnit(
                _friendlyUnits,
                $"FriendlyWorker{index + 1:D2}",
                FriendlyWorkerMesh,
                UnitTeam.Friendly,
                WorkerDefinition,
                new Transform3D(
                    Basis.Identity,
                    new Vector3(x, WorkerUnitHeight, 10.0f)));
            SpawnUnit(
                _enemyUnits,
                $"EnemyWorker{index + 1:D2}",
                EnemyWorkerMesh,
                UnitTeam.Enemy,
                WorkerDefinition,
                new Transform3D(
                    Basis.Identity,
                    new Vector3(x, WorkerUnitHeight, -10.0f)));
        }

        ResetScenarioBuildings();
        ResetScenarioResources(includeMaterialsNodes: true);
        EndScenarioReplacement();
        UpdateDebugOverlay();
    }

    private static void ClearUnitContainer(Node3D container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static SelectableUnit SpawnUnit(
        Node3D container,
        string name,
        Mesh mesh,
        UnitTeam team,
        UnitDefinition definition,
        Transform3D transform)
    {
        SelectableUnit unit = new()
        {
            Name = name,
            Mesh = mesh,
            Team = team,
            Definition = definition,
            Transform = transform,
        };
        container.AddChild(unit);
        return unit;
    }

    private void ProcessCompletedProductionSpawns()
    {
        if (_isReplacingScenario || _isMatchEnded)
        {
            return;
        }

        foreach (BuildingEntity building in GetLivingBuildings())
        {
            if (!building.IsComplete ||
                !building.HasProduction ||
                !building.Production.HasCompletedUnitWaiting ||
                !TryFindProductionSpawnPosition(
                    building,
                    out Vector3 spawnPosition))
            {
                continue;
            }

            BuildingProduction production = building.Production;
            Mesh unitMesh = building.Team == UnitTeam.Friendly
                ? _friendlyUnitMesh
                : _enemyUnitMesh;
            Node3D unitContainer = building.Team == UnitTeam.Friendly
                ? _friendlyUnits
                : _enemyUnits;
            float spawnHeight = Mathf.Max(
                -unitMesh.GetAabb().Position.Y,
                0.0f);
            _producedUnitSerial++;
            SelectableUnit producedUnit = SpawnUnit(
                unitContainer,
                $"{building.Team}Produced{_producedUnitSerial:D3}",
                unitMesh,
                building.Team,
                production.Definition.ProducedUnitDefinition,
                new Transform3D(
                    Basis.Identity,
                    new Vector3(
                        spawnPosition.X,
                        spawnHeight,
                        spawnPosition.Z)));
            production.AcknowledgeSpawn();
            if (production.HasRallyPoint && building.Team == UnitTeam.Friendly)
            {
                producedUnit.SetMoveTarget(production.RallyPoint);
            }
        }
    }

    private bool TryFindProductionSpawnPosition(
        BuildingEntity productionBuilding,
        out Vector3 spawnPosition)
    {
        Rid navigationMap = GetWorld3D().NavigationMap;
        if (NavigationServer3D.MapGetIterationId(navigationMap) == 0)
        {
            spawnPosition = Vector3.Zero;
            return false;
        }

        const int positionsPerRing = 16;
        const int ringCount = 5;
        const float ringSpacing = 1.2f;
        float initialRadius = productionBuilding.TargetRadius +
            UnitPlacementRadius + 0.75f;
        for (int ring = 0; ring < ringCount; ring++)
        {
            float radius = initialRadius + ring * ringSpacing;
            for (int slot = 0; slot < positionsPerRing; slot++)
            {
                float angle = Mathf.Tau * slot / positionsPerRing;
                Vector3 requestedPosition = productionBuilding.GlobalPosition +
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        0.0f,
                        Mathf.Sin(angle) * radius);
                Vector3 navigationPosition = NavigationServer3D.MapGetClosestPoint(
                    navigationMap,
                    requestedPosition);
                Vector2 requestedHorizontal = new(
                    requestedPosition.X,
                    requestedPosition.Z);
                Vector2 navigationHorizontal = new(
                    navigationPosition.X,
                    navigationPosition.Z);
                if (requestedHorizontal.DistanceSquaredTo(navigationHorizontal) >
                        0.25f ||
                    !IsProductionSpawnPositionValid(navigationPosition))
                {
                    continue;
                }

                spawnPosition = navigationPosition;
                return true;
            }
        }

        spawnPosition = Vector3.Zero;
        return false;
    }

    private bool IsProductionSpawnPositionValid(Vector3 position)
    {
        Vector2 horizontalPosition = new(position.X, position.Z);
        Rect2 safeBattlefieldBounds = _playableBattlefieldBounds.Grow(
            -UnitPlacementRadius);
        if (!safeBattlefieldBounds.HasPoint(horizontalPosition))
        {
            return false;
        }

        foreach (BuildingEntity building in GetLivingBuildings())
        {
            Vector2 buildingPosition = new(
                building.GlobalPosition.X,
                building.GlobalPosition.Z);
            float requiredDistance = building.TargetRadius +
                UnitPlacementRadius + 0.1f;
            if (horizontalPosition.DistanceSquaredTo(buildingPosition) <
                requiredDistance * requiredDistance)
            {
                return false;
            }
        }

        const float minimumUnitSpacing = UnitPlacementRadius * 2.0f;
        foreach (SelectableUnit unit in GetUnitsForTeam(teamFilter: null))
        {
            Vector2 unitPosition = new(
                unit.GlobalPosition.X,
                unit.GlobalPosition.Z);
            if (horizontalPosition.DistanceSquaredTo(unitPosition) <
                minimumUnitSpacing * minimumUnitSpacing)
            {
                return false;
            }
        }

        foreach (Node child in _resourceNodes.GetChildren())
        {
            if (child is not MaterialsResourceNode resourceNode ||
                !IsInstanceValid(resourceNode))
            {
                continue;
            }

            Vector2 resourcePosition = new(
                resourceNode.GlobalPosition.X,
                resourceNode.GlobalPosition.Z);
            float requiredDistance = resourceNode.InteractionRadius +
                UnitPlacementRadius;
            if (horizontalPosition.DistanceSquaredTo(resourcePosition) <
                requiredDistance * requiredDistance)
            {
                return false;
            }
        }

        return true;
    }

    private void ResetScenarioBuildings()
    {
        ClearBuildingSelection();
        ClearRegisteredHeadquarters();
        ClearUnitContainer(_buildings);
        _runtimeBuildingSerial = 0;
        _producedUnitSerial = 0;
        SpawnBuilding(
            "FriendlyHeadquarters",
            UnitTeam.Friendly,
            new Vector3(0.0f, 0.0f, 21.5f),
            DropOffBuildingDefinition,
            registerAsHeadquarters: true);
        SpawnBuilding(
            "EnemyHeadquarters",
            UnitTeam.Enemy,
            new Vector3(0.0f, 0.0f, -21.5f),
            DropOffBuildingDefinition,
            registerAsHeadquarters: true);
        ValidateHeadquartersConfiguration();
    }

    private BuildingEntity SpawnBuilding(
        string name,
        UnitTeam team,
        Vector3 groundPosition,
        BuildingDefinition definition,
        bool startsComplete = true,
        bool registerAsHeadquarters = false)
    {
        Vector3 dimensions = definition.PlaceholderDimensions;
        BuildingEntity building = new()
        {
            Name = name,
            Team = team,
            Definition = definition,
            StartsComplete = startsComplete,
            Mesh = BuildingEntity.CreatePlaceholderMesh(
                definition,
                team,
                translucent: false),
            Position = new Vector3(
                groundPosition.X,
                Mathf.Max(dimensions.Y * 0.5f, 0.0f),
                groundPosition.Z),
        };
        building.Destroyed += HandleBuildingDestroyed;
        _buildings.AddChild(building);
        if (registerAsHeadquarters)
        {
            RegisterHeadquarters(building);
        }

        return building;
    }

    private void ClearRegisteredHeadquarters()
    {
        _friendlyHeadquarters = null!;
        _enemyHeadquarters = null!;
        _headquartersConfigurationValid = false;
        _headquartersRegistrationError = false;
    }

    private void RegisterHeadquarters(BuildingEntity building)
    {
        if (!building.Definition.IsHeadquarters)
        {
            GD.PushError(
                $"Scenario building '{building.Name}' was registered as the " +
                "headquarters, but its BuildingDefinition is not designated " +
                "as a headquarters.");
            _headquartersRegistrationError = true;
            return;
        }

        BuildingEntity existing = building.Team == UnitTeam.Friendly
            ? _friendlyHeadquarters
            : _enemyHeadquarters;
        if (IsInstanceValid(existing))
        {
            GD.PushError(
                $"Scenario contains more than one {building.Team} headquarters: " +
                $"'{existing.Name}' and '{building.Name}'.");
            _headquartersRegistrationError = true;
            return;
        }

        if (building.Team == UnitTeam.Friendly)
        {
            _friendlyHeadquarters = building;
        }
        else
        {
            _enemyHeadquarters = building;
        }
    }

    private void ValidateHeadquartersConfiguration()
    {
        bool hasFriendlyHeadquarters = IsRegisteredHeadquarters(
            _friendlyHeadquarters,
            UnitTeam.Friendly);
        bool hasEnemyHeadquarters = IsRegisteredHeadquarters(
            _enemyHeadquarters,
            UnitTeam.Enemy);

        if (!hasFriendlyHeadquarters)
        {
            GD.PushError("Scenario must contain exactly one blue headquarters.");
        }

        if (!hasEnemyHeadquarters)
        {
            GD.PushError("Scenario must contain exactly one red headquarters.");
        }

        _headquartersConfigurationValid =
            !_headquartersRegistrationError &&
            hasFriendlyHeadquarters &&
            hasEnemyHeadquarters;
    }

    private static bool IsRegisteredHeadquarters(
        BuildingEntity headquarters,
        UnitTeam expectedTeam)
    {
        return IsInstanceValid(headquarters) &&
            headquarters.IsAlive &&
            headquarters.Team == expectedTeam &&
            headquarters.Definition.IsHeadquarters;
    }

    private void ResetScenarioResources(bool includeMaterialsNodes)
    {
        ClearUnitContainer(_resourceNodes);
        if (!includeMaterialsNodes)
        {
            return;
        }

        SpawnMaterialsNode("MaterialsNorth", new Vector2(-11.0f, 13.0f));
        SpawnMaterialsNode("MaterialsSouth", new Vector2(11.0f, -13.0f));
    }

    private void SpawnMaterialsNode(string name, Vector2 horizontalPosition)
    {
        float height = Mathf.Max(
            MaterialsDefinition.PlaceholderDimensions.Y,
            0.0f);
        MaterialsResourceNode resourceNode = new()
        {
            Name = name,
            Definition = MaterialsDefinition,
            Position = new Vector3(
                horizontalPosition.X,
                height * 0.5f,
                horizontalPosition.Y),
        };
        _resourceNodes.AddChild(resourceNode);
    }

    private void HandleBuildingDestroyed(BuildingEntity building)
    {
        if (_selectedBuilding == building)
        {
            _selectedBuilding = null!;
        }

        QueueNavigationRebuild();
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

    private Rect2 GetNavigationBounds()
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

        return new Rect2(
            new Vector2(minimumX, minimumZ),
            new Vector2(maximumX - minimumX, maximumZ - minimumZ));
    }

    private void ConfigureCameraBounds()
    {
        GetNode<RtsCameraController>("CameraRig").PanLimits = new Vector2(
            Mathf.Max(_playableBattlefieldSize.X * 0.5f - 1.0f, 0.0f),
            Mathf.Max(_playableBattlefieldSize.Y * 0.5f - 1.0f, 0.0f));
    }

    private void EnterPlacementMode()
    {
        PruneInvalidSelection();
        if (FindClosestSelectedWorker(Vector3.Zero) is null)
        {
            return;
        }

        ClearBuildingSelection();
        _isDragging = false;
        _selectionRectangle.Visible = false;
        _placementPreview = new MeshInstance3D
        {
            Name = "BuildingPlacementPreview",
            Mesh = BuildingEntity.CreatePlaceholderMesh(
                ProductionBuildingDefinition,
                UnitTeam.Friendly,
                translucent: true),
            MaterialOverride = _invalidPlacementMaterial,
            Visible = false,
        };
        AddChild(_placementPreview);
        _isPlacementMode = true;
        _isPlacementValid = false;
        UpdatePlacementPreview(GetViewport().GetMousePosition());
    }

    private void HandlePlacementInput(InputEvent @event)
    {
        if (@event.IsActionPressed(CancelPlacementAction) ||
            @event.IsActionPressed(PlaceBuildingAction) ||
            @event.IsActionPressed(MoveUnitsAction))
        {
            CancelPlacementMode();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton mouseButton &&
            mouseButton.IsActionPressed(SelectUnitsAction))
        {
            TryConfirmBuildingPlacement(mouseButton.Position);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    private void UpdatePlacementPreview(Vector2 screenPosition)
    {
        if (!_isPlacementMode || !IsInstanceValid(_placementPreview))
        {
            return;
        }

        if (!TryGetGroundPosition(screenPosition, out Vector3 groundPosition))
        {
            _placementPreview.Visible = false;
            _isPlacementValid = false;
            return;
        }

        float height = Mathf.Max(
            ProductionBuildingDefinition.PlaceholderDimensions.Y,
            0.0f);
        _placementPreview.Position = new Vector3(
            groundPosition.X,
            height * 0.5f,
            groundPosition.Z);
        _isPlacementValid = IsBuildingPlacementValid(groundPosition) &&
            FindClosestSelectedWorker(groundPosition) is not null &&
            _resourceLedger.CanAfford(
                UnitTeam.Friendly,
                Mathf.Max(ProductionBuildingDefinition.MaterialsCost, 0));
        _placementPreview.MaterialOverride = _isPlacementValid
            ? _validPlacementMaterial
            : _invalidPlacementMaterial;
        _placementPreview.Visible = true;
    }

    private void TryConfirmBuildingPlacement(Vector2 screenPosition)
    {
        UpdatePlacementPreview(screenPosition);
        if (!_isPlacementValid || !IsInstanceValid(_placementPreview))
        {
            return;
        }

        Vector3 groundPosition = new(
            _placementPreview.Position.X,
            0.0f,
            _placementPreview.Position.Z);
        SelectableUnit builder = FindClosestSelectedWorker(groundPosition);
        int materialsCost = Mathf.Max(
            ProductionBuildingDefinition.MaterialsCost,
            0);
        if (builder is null ||
            !_resourceLedger.TrySpend(UnitTeam.Friendly, materialsCost))
        {
            return;
        }

        _runtimeBuildingSerial++;
        BuildingEntity constructionSite = SpawnBuilding(
            $"FriendlyPlacedBuilding{_runtimeBuildingSerial:D3}",
            UnitTeam.Friendly,
            groundPosition,
            ProductionBuildingDefinition,
            startsComplete: false);
        if (!builder.SetConstructionTarget(constructionSite))
        {
            constructionSite.CancelConstruction();
            _resourceLedger.Deposit(UnitTeam.Friendly, materialsCost);
            return;
        }

        CancelPlacementMode();
        QueueNavigationRebuild();
    }

    private void TryCancelSelectedConstruction()
    {
        PruneInvalidBuildingSelection();
        if (_selectedBuilding is null ||
            _selectedBuilding.Team != UnitTeam.Friendly ||
            _selectedBuilding.IsComplete)
        {
            return;
        }

        BuildingEntity constructionSite = _selectedBuilding;
        int materialsCost = Mathf.Max(
            constructionSite.Definition.MaterialsCost,
            0);
        int refund = Mathf.FloorToInt(
            materialsCost * Mathf.Clamp(ConstructionRefundFraction, 0.0f, 1.0f));
        if (constructionSite.CancelConstruction())
        {
            _resourceLedger.Deposit(UnitTeam.Friendly, refund);
            _selectedBuilding = null!;
        }
    }

    private void TryQueueUnitAtSelectedBuilding()
    {
        PruneInvalidBuildingSelection();
        if (_selectedBuilding is null ||
            _selectedBuilding.Team != UnitTeam.Friendly ||
            !_selectedBuilding.IsComplete ||
            !_selectedBuilding.HasProduction)
        {
            return;
        }

        _selectedBuilding.Production.TryQueueUnit(_resourceLedger);
        UpdateDebugOverlay();
    }

    private void TryCancelProductionAtSelectedBuilding()
    {
        PruneInvalidBuildingSelection();
        if (_selectedBuilding is null ||
            _selectedBuilding.Team != UnitTeam.Friendly ||
            !_selectedBuilding.IsComplete ||
            !_selectedBuilding.HasProduction)
        {
            return;
        }

        _selectedBuilding.Production.CancelMostRecentUnit(_resourceLedger);
        UpdateDebugOverlay();
    }

    private void CancelPlacementMode()
    {
        _isPlacementMode = false;
        _isPlacementValid = false;
        if (IsInstanceValid(_placementPreview))
        {
            _placementPreview.QueueFree();
        }

        _placementPreview = null!;
    }

    private bool IsBuildingPlacementValid(Vector3 groundPosition)
    {
        if (_navigationRegion.IsBaking() ||
            NavigationServer3D.MapGetIterationId(GetWorld3D().NavigationMap) == 0)
        {
            return false;
        }

        Vector3 dimensions = ProductionBuildingDefinition.PlaceholderDimensions;
        Vector2 halfSize = new(
            Mathf.Max(dimensions.X * 0.5f, 0.0f),
            Mathf.Max(dimensions.Z * 0.5f, 0.0f));
        Vector2 placementCenter = new(groundPosition.X, groundPosition.Z);
        Vector2 minimum = placementCenter - halfSize;
        Vector2 maximum = placementCenter + halfSize;
        Vector2 battlefieldMinimum = _playableBattlefieldBounds.Position;
        Vector2 battlefieldMaximum = _playableBattlefieldBounds.End;
        if (minimum.X < battlefieldMinimum.X ||
            minimum.Y < battlefieldMinimum.Y ||
            maximum.X > battlefieldMaximum.X ||
            maximum.Y > battlefieldMaximum.Y)
        {
            return false;
        }

        float footprintRadius = Mathf.Max(
            ProductionBuildingDefinition.FootprintRadius,
            0.0f);
        Vector2 candidateCenter = new(groundPosition.X, groundPosition.Z);
        foreach (BuildingEntity building in GetLivingBuildings())
        {
            Vector2 buildingCenter = new(
                building.GlobalPosition.X,
                building.GlobalPosition.Z);
            float requiredDistance = footprintRadius + building.TargetRadius;
            if (candidateCenter.DistanceSquaredTo(buildingCenter) <
                requiredDistance * requiredDistance)
            {
                return false;
            }
        }

        foreach (SelectableUnit unit in GetUnitsForTeam(teamFilter: null))
        {
            Vector2 unitCenter = new(unit.GlobalPosition.X, unit.GlobalPosition.Z);
            float requiredDistance = footprintRadius + UnitPlacementRadius;
            if (candidateCenter.DistanceSquaredTo(unitCenter) <
                requiredDistance * requiredDistance)
            {
                return false;
            }
        }

        foreach (Node child in _resourceNodes.GetChildren())
        {
            if (child is not MaterialsResourceNode resourceNode ||
                !IsInstanceValid(resourceNode))
            {
                continue;
            }

            Vector2 resourceCenter = new(
                resourceNode.GlobalPosition.X,
                resourceNode.GlobalPosition.Z);
            float requiredDistance = footprintRadius +
                resourceNode.InteractionRadius;
            if (candidateCenter.DistanceSquaredTo(resourceCenter) <
                requiredDistance * requiredDistance)
            {
                return false;
            }
        }

        return true;
    }

    private void QueueNavigationRebuild()
    {
        if (_navigationRebuildQueued)
        {
            return;
        }

        _navigationRebuildQueued = true;
        Callable.From(RebuildNavigationMesh).CallDeferred();
    }

    private void RebuildNavigationMesh()
    {
        _navigationRebuildQueued = false;
        NavigationMesh navigationMesh = _navigationRegion.NavigationMesh;
        navigationMesh.GeometryParsedGeometryType =
            NavigationMesh.ParsedGeometryType.StaticColliders;
        navigationMesh.GeometrySourceGeometryMode =
            NavigationMesh.SourceGeometryMode.GroupsExplicit;
        navigationMesh.GeometrySourceGroupName = NavigationSourceGroup;
        navigationMesh.GeometryCollisionMask = GroundCollisionMask;
        navigationMesh.AgentRadius = 0.5f;
        navigationMesh.AgentHeight = 1.75f;
        navigationMesh.AgentMaxClimb = 0.25f;
        navigationMesh.FilterBakingAabb = new Aabb(
            new Vector3(
                _playableBattlefieldBounds.Position.X,
                -1.0f,
                _playableBattlefieldBounds.Position.Y),
            new Vector3(
                _playableBattlefieldBounds.Size.X,
                5.0f,
                _playableBattlefieldBounds.Size.Y));
        _navigationRegion.BakeNavigationMesh(onThread: false);
    }

    private void UpdateDebugOverlay()
    {
        PruneInvalidSelection();
        PruneInvalidBuildingSelection();
        int friendlyBuildingCount = 0;
        int enemyBuildingCount = 0;
        foreach (BuildingEntity building in GetLivingBuildings())
        {
            if (building.Team == UnitTeam.Friendly)
            {
                friendlyBuildingCount++;
            }
            else
            {
                enemyBuildingCount++;
            }
        }

        _debugMetrics.Text =
            $"FPS: {Engine.GetFramesPerSecond():0}\n" +
            $"Friendly: {_friendlyUnits.GetChildCount()}\n" +
            $"Enemy: {_enemyUnits.GetChildCount()}\n" +
            $"Selected units: {_selectedUnits.Count}\n" +
            $"Blue Materials: {_resourceLedger.GetMaterials(UnitTeam.Friendly)}\n" +
            $"Buildings: {friendlyBuildingCount} blue | {enemyBuildingCount} red\n" +
            $"Building selected: {GetSelectedBuildingStatus()}\n\n" +
            "Unit presets: F1 8 | F2 20 | F3 100 | F4 250 | F5 500\n" +
            "F6 mixed roles + Materials economy\n" +
            $"B build production ({Mathf.Max(ProductionBuildingDefinition.MaterialsCost, 0)} Materials)\n" +
            $"Delete cancel selected site " +
            $"({Mathf.RoundToInt(Mathf.Clamp(ConstructionRefundFraction, 0.0f, 1.0f) * 100.0f)}% refund)\n" +
            "U queue combat unit | X cancel newest\n" +
            "Esc/right-click cancel placement";
    }

    private string GetSelectedBuildingStatus()
    {
        if (_selectedBuilding is null)
        {
            return "no";
        }

        if (!_selectedBuilding.IsComplete)
        {
            return $"{_selectedBuilding.Definition.DisplayName} site " +
                $"{_selectedBuilding.ConstructionProgress * 100.0f:0}% " +
                $"({_selectedBuilding.Health:0} HP)";
        }

        if (!_selectedBuilding.HasProduction)
        {
            return $"{_selectedBuilding.Definition.DisplayName} (complete)";
        }

        BuildingProduction production = _selectedBuilding.Production;
        UnitProductionDefinition definition = production.Definition;
        string rallyStatus = production.HasRallyPoint ? "set" : "none";
        string progressStatus = production.HasCompletedUnitWaiting
            ? "waiting for spawn"
            : $"{production.ProductionProgress * 100.0f:0}%";
        return $"{_selectedBuilding.Definition.DisplayName}\n" +
            $"Queue: {production.QueueCount}/" +
                $"{Mathf.Max(definition.MaximumQueueLength, 1)} | " +
                $"Progress: {progressStatus}\n" +
            $"U produce ({Mathf.Max(definition.UnitMaterialsCost, 0)} Materials) | " +
                $"X cancel newest | Rally: {rallyStatus}";
    }

    private void EvaluateMatchOutcome()
    {
        if (!_isMatchTrackingActive || _isReplacingScenario || _isMatchEnded)
        {
            return;
        }

        bool friendlyHeadquartersAlive = IsHeadquartersAlive(
            _friendlyHeadquarters);
        bool enemyHeadquartersAlive = IsHeadquartersAlive(
            _enemyHeadquarters);
        if (friendlyHeadquartersAlive && enemyHeadquartersAlive)
        {
            return;
        }

        if (!friendlyHeadquartersAlive && !enemyHeadquartersAlive)
        {
            EndMatch("Draw");
        }
        else if (!enemyHeadquartersAlive)
        {
            EndMatch("Victory");
        }
        else
        {
            EndMatch("Defeat");
        }
    }

    private static bool IsHeadquartersAlive(BuildingEntity headquarters)
    {
        return IsInstanceValid(headquarters) &&
            !headquarters.IsQueuedForDeletion() &&
            headquarters.IsAlive;
    }

    private void EndMatch(string outcome)
    {
        _isMatchEnded = true;
        _isMatchTrackingActive = false;
        ClearSelection();
        ClearBuildingSelection();
        CancelPlacementMode();
        _isDragging = false;
        _selectionRectangle.Visible = false;

        foreach (SelectableUnit unit in GetUnitsForTeam(teamFilter: null))
        {
            unit.StopGameplay();
        }

        foreach (BuildingEntity building in GetLivingBuildings())
        {
            building.StopGameplay();
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
        ClearSelection();
        ClearBuildingSelection();
        ICombatTarget target = FindCombatTargetAtScreenPosition(screenPosition);
        if (target is SelectableUnit unit && unit.Team == UnitTeam.Friendly)
        {
            AddToSelection(unit);
        }
        else if (target is BuildingEntity building &&
                 building.Team == UnitTeam.Friendly)
        {
            SelectBuilding(building);
        }
    }

    private void SelectUnitsInRectangle(Rect2 rectangle)
    {
        ClearSelection();
        ClearBuildingSelection();

        foreach (SelectableUnit unit in GetUnitsForTeam(UnitTeam.Friendly))
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

    private ICombatTarget FindCombatTargetAtScreenPosition(Vector2 screenPosition)
    {
        ICombatTarget closestTarget = null!;
        float closestDistanceSquared = float.MaxValue;

        foreach (ICombatTarget target in GetCombatTargets())
        {
            if (target is not Node3D targetNode ||
                !TryGetTargetScreenBounds(target, out Rect2 screenBounds) ||
                !screenBounds.Grow(ClickBoundsPaddingPixels).HasPoint(screenPosition))
            {
                continue;
            }

            float distanceSquared = _camera.GlobalPosition.DistanceSquaredTo(
                targetNode.GlobalPosition);
            if (distanceSquared < closestDistanceSquared)
            {
                closestDistanceSquared = distanceSquared;
                closestTarget = target;
            }
        }

        return closestTarget;
    }

    private IEnumerable<ICombatTarget> GetCombatTargets()
    {
        foreach (UnitTeam team in new[] { UnitTeam.Friendly, UnitTeam.Enemy })
        {
            foreach (Node node in GetTree().GetNodesInGroup(
                         CombatTargetGroups.ForTeam(team)))
            {
                if (node is ICombatTarget target && CombatTargetGroups.IsValid(target))
                {
                    yield return target;
                }
            }
        }
    }

    private IEnumerable<SelectableUnit> GetUnitsForTeam(
        UnitTeam? teamFilter)
    {
        if (teamFilter.HasValue)
        {
            foreach (SelectableUnit unit in GetUnitsInGroup(
                CombatTargetGroups.ForTeam(teamFilter.Value)))
            {
                yield return unit;
            }

            yield break;
        }

        foreach (SelectableUnit unit in GetUnitsInGroup(
            CombatTargetGroups.ForTeam(UnitTeam.Friendly)))
        {
            yield return unit;
        }

        foreach (SelectableUnit unit in GetUnitsInGroup(
            CombatTargetGroups.ForTeam(UnitTeam.Enemy)))
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
        MaterialsResourceNode resourceTarget =
            FindResourceAtScreenPosition(screenPosition);
        if (resourceTarget is not null)
        {
            IssueGatherOrder(resourceTarget);
            return;
        }

        ICombatTarget clickedTarget = FindCombatTargetAtScreenPosition(screenPosition);
        if (clickedTarget is not null)
        {
            if (clickedTarget.Team == UnitTeam.Enemy)
            {
                IssueAttackOrder(clickedTarget);
            }
            else if (clickedTarget is BuildingEntity building &&
                     !building.IsComplete)
            {
                IssueConstructionOrder(building);
            }
            else if (clickedTarget is BuildingEntity completedBuilding &&
                     completedBuilding.AcceptsMaterials)
            {
                IssueManualDropOffOrder(completedBuilding);
            }

            return;
        }

        if (_selectedBuilding is not null &&
            _selectedBuilding.Team == UnitTeam.Friendly &&
            _selectedBuilding.IsComplete &&
            _selectedBuilding.HasProduction)
        {
            TrySetSelectedBuildingRallyPoint(screenPosition);
            return;
        }

        TryIssueMoveOrder(screenPosition);
    }

    private void TrySetSelectedBuildingRallyPoint(Vector2 screenPosition)
    {
        if (!TryGetGroundPosition(screenPosition, out Vector3 groundPosition))
        {
            return;
        }

        Rid navigationMap = GetWorld3D().NavigationMap;
        if (NavigationServer3D.MapGetIterationId(navigationMap) == 0)
        {
            return;
        }

        Vector3 rallyPoint = NavigationServer3D.MapGetClosestPoint(
            navigationMap,
            groundPosition);
        Vector2 requested = new(groundPosition.X, groundPosition.Z);
        Vector2 projected = new(rallyPoint.X, rallyPoint.Z);
        if (requested.DistanceSquaredTo(projected) > 0.25f)
        {
            return;
        }

        _selectedBuilding.Production.SetRallyPoint(rallyPoint);
        UpdateDebugOverlay();
    }

    private void IssueAttackOrder(ICombatTarget target)
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

    private void IssueGatherOrder(MaterialsResourceNode resourceTarget)
    {
        PruneInvalidSelection();
        List<SelectableUnit> workers = new();
        foreach (SelectableUnit unit in _selectedUnits)
        {
            if (IsInstanceValid(unit) && unit.HasWorkerEconomy)
            {
                workers.Add(unit);
            }
        }

        workers.Sort((first, second) =>
            first.GetInstanceId().CompareTo(second.GetInstanceId()));
        for (int index = 0; index < workers.Count; index++)
        {
            workers[index].SetGatherTarget(resourceTarget, index, workers.Count);
        }
    }

    private void IssueManualDropOffOrder(BuildingEntity building)
    {
        PruneInvalidSelection();
        List<SelectableUnit> workers = new();
        foreach (SelectableUnit unit in _selectedUnits)
        {
            if (IsInstanceValid(unit) && unit.HasWorkerEconomy)
            {
                workers.Add(unit);
            }
        }

        workers.Sort((first, second) =>
            first.GetInstanceId().CompareTo(second.GetInstanceId()));
        for (int index = 0; index < workers.Count; index++)
        {
            workers[index].SetManualDropOff(building, index, workers.Count);
        }
    }

    private void IssueConstructionOrder(BuildingEntity constructionSite)
    {
        SelectableUnit builder = FindClosestSelectedWorker(
            constructionSite.GlobalPosition);
        builder?.SetConstructionTarget(constructionSite);
    }

    private SelectableUnit FindClosestSelectedWorker(Vector3 position)
    {
        PruneInvalidSelection();
        SelectableUnit closestWorker = null!;
        float closestDistanceSquared = float.MaxValue;
        foreach (SelectableUnit unit in _selectedUnits)
        {
            if (!IsInstanceValid(unit) || !unit.HasWorkerEconomy)
            {
                continue;
            }

            Vector2 horizontalDelta = new(
                unit.GlobalPosition.X - position.X,
                unit.GlobalPosition.Z - position.Z);
            float distanceSquared = horizontalDelta.LengthSquared();
            bool isCloser = distanceSquared < closestDistanceSquared;
            bool winsTie = Mathf.IsEqualApprox(
                    distanceSquared,
                    closestDistanceSquared) &&
                (closestWorker is null ||
                    unit.GetInstanceId() < closestWorker.GetInstanceId());
            if (isCloser || winsTie)
            {
                closestWorker = unit;
                closestDistanceSquared = distanceSquared;
            }
        }

        return closestWorker;
    }

    private void TryIssueMoveOrder(Vector2 screenPosition)
    {
        PruneInvalidSelection();
        if (_selectedUnits.Count == 0)
        {
            return;
        }

        if (!TryGetGroundPosition(screenPosition, out Vector3 commandDestination))
        {
            return;
        }

        Rid navigationMap = GetWorld3D().NavigationMap;
        if (NavigationServer3D.MapGetIterationId(navigationMap) == 0)
        {
            return;
        }

        if (_selectedUnits.Count == 1)
        {
            Vector3 navigationDestination = NavigationServer3D.MapGetClosestPoint(
                navigationMap,
                commandDestination);
            _selectedUnits[0].SetMoveTarget(navigationDestination);
            return;
        }

        float minimumSpacing = Mathf.Max(MinimumMoveDestinationSpacing, 0.1f);
        List<Vector2> offsets = CalculateHorizontalOffsets(_selectedUnits);
        if (!TryRepairMinimumSpacing(offsets, minimumSpacing, out offsets))
        {
            GD.PushWarning("Unable to create separated movement destinations for the selected group.");
            return;
        }

        Vector2 requestedCentroid = new(commandDestination.X, commandDestination.Z);
        if (!TryCreateProjectedDestinations(
                navigationMap,
                requestedCentroid,
                commandDestination.Y,
                offsets,
                minimumSpacing,
                out List<Vector3> navigationDestinations))
        {
            GD.PushWarning("Unable to fit separated movement destinations on navigable ground.");
            return;
        }

        for (int index = 0; index < _selectedUnits.Count; index++)
        {
            _selectedUnits[index].SetMoveTarget(navigationDestinations[index]);
        }
    }

    private bool TryGetGroundPosition(
        Vector2 screenPosition,
        out Vector3 groundPosition)
    {
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
            groundPosition = Vector3.Zero;
            return false;
        }

        groundPosition = hit["position"].AsVector3();
        return true;
    }

    private static List<Vector2> CalculateHorizontalOffsets(
        IReadOnlyList<SelectableUnit> units)
    {
        Vector2 centroid = Vector2.Zero;
        foreach (SelectableUnit unit in units)
        {
            centroid += new Vector2(unit.GlobalPosition.X, unit.GlobalPosition.Z);
        }

        centroid /= units.Count;
        List<Vector2> offsets = new(units.Count);
        foreach (SelectableUnit unit in units)
        {
            offsets.Add(new Vector2(unit.GlobalPosition.X, unit.GlobalPosition.Z) - centroid);
        }

        return offsets;
    }

    private bool TryCreateProjectedDestinations(
        Rid navigationMap,
        Vector2 requestedCentroid,
        float destinationHeight,
        List<Vector2> offsets,
        float minimumSpacing,
        out List<Vector3> destinations)
    {
        destinations = ProjectDestinations(
            navigationMap,
            FitCentroidInsideBattlefield(requestedCentroid, offsets, minimumSpacing),
            destinationHeight,
            offsets);
        if (HasMinimumSpacing(destinations, minimumSpacing))
        {
            return true;
        }

        Vector2 projectedCentroid = CalculateHorizontalCentroid(destinations);
        List<Vector2> projectedOffsets = new(destinations.Count);
        foreach (Vector3 destination in destinations)
        {
            projectedOffsets.Add(
                new Vector2(destination.X, destination.Z) - projectedCentroid);
        }

        if (!TryRepairMinimumSpacing(
                projectedOffsets,
                minimumSpacing,
                out projectedOffsets))
        {
            return false;
        }

        destinations = ProjectDestinations(
            navigationMap,
            FitCentroidInsideBattlefield(
                projectedCentroid,
                projectedOffsets,
                minimumSpacing),
            destinationHeight,
            projectedOffsets);
        return HasMinimumSpacing(destinations, minimumSpacing);
    }

    private List<Vector3> ProjectDestinations(
        Rid navigationMap,
        Vector2 centroid,
        float destinationHeight,
        IReadOnlyList<Vector2> offsets)
    {
        List<Vector3> destinations = new(offsets.Count);
        foreach (Vector2 offset in offsets)
        {
            Vector2 horizontalDestination = centroid + offset;
            destinations.Add(NavigationServer3D.MapGetClosestPoint(
                navigationMap,
                new Vector3(
                    horizontalDestination.X,
                    destinationHeight,
                    horizontalDestination.Y)));
        }

        return destinations;
    }

    private Vector2 FitCentroidInsideBattlefield(
        Vector2 requestedCentroid,
        IReadOnlyList<Vector2> offsets,
        float minimumSpacing)
    {
        GetOffsetBounds(offsets, out Vector2 minimumOffset, out Vector2 maximumOffset);
        Vector2 footprintSize = maximumOffset - minimumOffset;
        float preferredMargin = minimumSpacing * 0.5f;
        Vector2 availableMargin = new(
            Mathf.Max((_playableBattlefieldBounds.Size.X - footprintSize.X) * 0.5f, 0.0f),
            Mathf.Max((_playableBattlefieldBounds.Size.Y - footprintSize.Y) * 0.5f, 0.0f));
        Vector2 margin = new(
            Mathf.Min(preferredMargin, availableMargin.X),
            Mathf.Min(preferredMargin, availableMargin.Y));
        Vector2 minimumCentroid = _playableBattlefieldBounds.Position +
            margin - minimumOffset;
        Vector2 maximumCentroid = _playableBattlefieldBounds.End -
            margin - maximumOffset;

        return new Vector2(
            Mathf.Clamp(requestedCentroid.X, minimumCentroid.X, maximumCentroid.X),
            Mathf.Clamp(requestedCentroid.Y, minimumCentroid.Y, maximumCentroid.Y));
    }

    private static void GetOffsetBounds(
        IReadOnlyList<Vector2> offsets,
        out Vector2 minimum,
        out Vector2 maximum)
    {
        minimum = new Vector2(float.MaxValue, float.MaxValue);
        maximum = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector2 offset in offsets)
        {
            minimum = new Vector2(
                Mathf.Min(minimum.X, offset.X),
                Mathf.Min(minimum.Y, offset.Y));
            maximum = new Vector2(
                Mathf.Max(maximum.X, offset.X),
                Mathf.Max(maximum.Y, offset.Y));
        }
    }

    private static bool TryRepairMinimumSpacing(
        IReadOnlyList<Vector2> intendedOffsets,
        float minimumSpacing,
        out List<Vector2> repairedOffsets)
    {
        int count = intendedOffsets.Count;
        bool[] needsRepair = new bool[count];
        float effectiveMinimumSpacing = Mathf.Max(
            minimumSpacing - DestinationSpacingTolerance,
            0.0f);
        float minimumSpacingSquared = effectiveMinimumSpacing * effectiveMinimumSpacing;
        bool repairNeeded = false;

        for (int firstIndex = 0; firstIndex < count; firstIndex++)
        {
            for (int secondIndex = firstIndex + 1; secondIndex < count; secondIndex++)
            {
                if (intendedOffsets[firstIndex].DistanceSquaredTo(
                        intendedOffsets[secondIndex]) >= minimumSpacingSquared)
                {
                    continue;
                }

                needsRepair[firstIndex] = true;
                needsRepair[secondIndex] = true;
                repairNeeded = true;
            }
        }

        repairedOffsets = new List<Vector2>(intendedOffsets);
        if (!repairNeeded)
        {
            return true;
        }

        Dictionary<Vector2I, List<Vector2>> occupiedCells = new();
        bool[] assigned = new bool[count];
        for (int index = 0; index < count; index++)
        {
            if (needsRepair[index])
            {
                continue;
            }

            AddOccupiedPosition(occupiedCells, intendedOffsets[index], minimumSpacing);
            assigned[index] = true;
        }

        for (int index = 0; index < count; index++)
        {
            if (assigned[index])
            {
                continue;
            }

            Vector2 intended = intendedOffsets[index];
            if (IsPositionAvailable(
                    occupiedCells,
                    intended,
                    minimumSpacing,
                    minimumSpacingSquared))
            {
                repairedOffsets[index] = intended;
                AddOccupiedPosition(occupiedCells, intended, minimumSpacing);
                continue;
            }

            bool positionFound = false;
            for (int ring = 1; ring <= count && !positionFound; ring++)
            {
                for (int x = -ring; x <= ring && !positionFound; x++)
                {
                    for (int z = -ring; z <= ring; z++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) != ring)
                        {
                            continue;
                        }

                        Vector2 candidate = intended +
                            new Vector2(x * minimumSpacing, z * minimumSpacing);
                        if (!IsPositionAvailable(
                                occupiedCells,
                                candidate,
                                minimumSpacing,
                                minimumSpacingSquared))
                        {
                            continue;
                        }

                        repairedOffsets[index] = candidate;
                        AddOccupiedPosition(occupiedCells, candidate, minimumSpacing);
                        positionFound = true;
                        break;
                    }
                }
            }

            if (!positionFound)
            {
                return false;
            }
        }

        Vector2 intendedCentroid = CalculateHorizontalCentroid(intendedOffsets);
        Vector2 repairedCentroid = CalculateHorizontalCentroid(repairedOffsets);
        Vector2 recenterOffset = intendedCentroid - repairedCentroid;
        for (int index = 0; index < repairedOffsets.Count; index++)
        {
            repairedOffsets[index] += recenterOffset;
        }

        return true;
    }

    private static bool IsPositionAvailable(
        IReadOnlyDictionary<Vector2I, List<Vector2>> occupiedCells,
        Vector2 position,
        float cellSize,
        float minimumSpacingSquared)
    {
        Vector2I cell = GetSpacingCell(position, cellSize);
        for (int xOffset = -1; xOffset <= 1; xOffset++)
        {
            for (int yOffset = -1; yOffset <= 1; yOffset++)
            {
                Vector2I neighboringCell = cell + new Vector2I(xOffset, yOffset);
                if (!occupiedCells.TryGetValue(
                        neighboringCell,
                        out List<Vector2> occupiedPositions))
                {
                    continue;
                }

                foreach (Vector2 occupiedPosition in occupiedPositions)
                {
                    if (position.DistanceSquaredTo(occupiedPosition) <
                        minimumSpacingSquared)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static void AddOccupiedPosition(
        IDictionary<Vector2I, List<Vector2>> occupiedCells,
        Vector2 position,
        float cellSize)
    {
        Vector2I cell = GetSpacingCell(position, cellSize);
        if (!occupiedCells.TryGetValue(cell, out List<Vector2> occupiedPositions))
        {
            occupiedPositions = new List<Vector2>();
            occupiedCells[cell] = occupiedPositions;
        }

        occupiedPositions.Add(position);
    }

    private static Vector2I GetSpacingCell(Vector2 position, float cellSize)
    {
        return new Vector2I(
            Mathf.FloorToInt(position.X / cellSize),
            Mathf.FloorToInt(position.Y / cellSize));
    }

    private static bool HasMinimumSpacing(
        IReadOnlyList<Vector3> destinations,
        float minimumSpacing)
    {
        float effectiveMinimumSpacing = Mathf.Max(
            minimumSpacing - DestinationSpacingTolerance,
            0.0f);
        float minimumSpacingSquared = effectiveMinimumSpacing * effectiveMinimumSpacing;
        for (int firstIndex = 0; firstIndex < destinations.Count; firstIndex++)
        {
            Vector2 first = new(
                destinations[firstIndex].X,
                destinations[firstIndex].Z);
            for (int secondIndex = firstIndex + 1;
                 secondIndex < destinations.Count;
                 secondIndex++)
            {
                Vector2 second = new(
                    destinations[secondIndex].X,
                    destinations[secondIndex].Z);
                if (first.DistanceSquaredTo(second) < minimumSpacingSquared)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static Vector2 CalculateHorizontalCentroid(
        IReadOnlyList<Vector2> positions)
    {
        Vector2 centroid = Vector2.Zero;
        foreach (Vector2 position in positions)
        {
            centroid += position;
        }

        return centroid / positions.Count;
    }

    private static Vector2 CalculateHorizontalCentroid(
        IReadOnlyList<Vector3> positions)
    {
        Vector2 centroid = Vector2.Zero;
        foreach (Vector3 position in positions)
        {
            centroid += new Vector2(position.X, position.Z);
        }

        return centroid / positions.Count;
    }

    private bool TryGetTargetScreenBounds(ICombatTarget target, out Rect2 bounds)
    {
        if (target is not MeshInstance3D meshInstance)
        {
            bounds = default;
            return false;
        }

        return TryGetMeshScreenBounds(meshInstance, out bounds);
    }

    private MaterialsResourceNode FindResourceAtScreenPosition(
        Vector2 screenPosition)
    {
        MaterialsResourceNode closestResource = null!;
        float closestDistanceSquared = float.MaxValue;
        foreach (Node node in GetTree().GetNodesInGroup(
                     MaterialsResourceNode.ResourceNodeGroup))
        {
            if (node is not MaterialsResourceNode resource ||
                !IsInstanceValid(resource) ||
                !TryGetMeshScreenBounds(resource, out Rect2 screenBounds) ||
                !screenBounds.Grow(ClickBoundsPaddingPixels).HasPoint(screenPosition))
            {
                continue;
            }

            float distanceSquared = _camera.GlobalPosition.DistanceSquaredTo(
                resource.GlobalPosition);
            if (distanceSquared < closestDistanceSquared)
            {
                closestDistanceSquared = distanceSquared;
                closestResource = resource;
            }
        }

        return closestResource;
    }

    private bool TryGetMeshScreenBounds(
        MeshInstance3D meshInstance,
        out Rect2 bounds)
    {
        bounds = default;

        Aabb localBounds = meshInstance.GetAabb();
        Vector2 minimum = new(float.MaxValue, float.MaxValue);
        Vector2 maximum = new(float.MinValue, float.MinValue);

        for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
        {
            Vector3 localCorner = localBounds.Position + new Vector3(
                (cornerIndex & 1) == 0 ? 0.0f : localBounds.Size.X,
                (cornerIndex & 2) == 0 ? 0.0f : localBounds.Size.Y,
                (cornerIndex & 4) == 0 ? 0.0f : localBounds.Size.Z);
            Vector3 worldCorner = meshInstance.ToGlobal(localCorner);

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
        ClearBuildingSelection();
        unit.SetSelected(true);
        _selectedUnits.Add(unit);
    }

    private void SelectBuilding(BuildingEntity building)
    {
        ClearSelection();
        ClearBuildingSelection();
        _selectedBuilding = building;
        building.SetSelected(true);
    }

    private void ClearBuildingSelection()
    {
        if (IsInstanceValid(_selectedBuilding))
        {
            _selectedBuilding.SetSelected(false);
        }

        _selectedBuilding = null!;
    }

    private void PruneInvalidBuildingSelection()
    {
        if (_selectedBuilding is not null &&
            (!IsInstanceValid(_selectedBuilding) || !_selectedBuilding.IsAlive))
        {
            _selectedBuilding = null!;
        }
    }

    private IEnumerable<BuildingEntity> GetLivingBuildings()
    {
        foreach (Node child in _buildings.GetChildren())
        {
            if (child is BuildingEntity building &&
                IsInstanceValid(building) &&
                building.IsAlive)
            {
                yield return building;
            }
        }
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
