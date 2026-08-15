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
    private static readonly StringName LoadMacroScenarioAction = "debug_enemy_macro";
    private static readonly StringName PlaceBuildingAction = "debug_place_building";
    private static readonly StringName CancelConstructionAction = "cancel_construction";
    private static readonly StringName QueueCombatUnitAction = "queue_combat_unit";
    private static readonly StringName QueueWorkerAction = "queue_worker";
    private static readonly StringName CancelProductionAction = "cancel_production";
    private static readonly StringName CancelPlacementAction = "ui_cancel";
    private static readonly StringName RestartMatchAction = "restart_match";
    private static readonly StringName MovementGroundGroup = "movement_ground";
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
    private const int MacroFriendlyCombatUnits = 2;
    private const int MacroEnemyCombatUnits = 0;
    private const int MacroWorkersPerTeam = 3;
    private const float DestinationSpacingTolerance = 0.001f;
    private const int AdditionalDestinationCandidates = 128;
    private const int MaximumDestinationCandidates = 4096;

    private static readonly BattlefieldConfiguration NormalBattlefield = new(
        playableSize: new Vector2(68.0f, 50.0f),
        visibleGroundSize: new Vector2(70.0f, 52.0f),
        navigationCellSize: 0.25f,
        cameraPanSpeed: 14.0f,
        cameraMaximumZoom: 32.0f,
        cameraStartingZoom: 23.2f,
        cameraStartingPosition: new Vector3(2.9f, 0.0f, 6.2f),
        isFormationStressTest: false);

    private static readonly BattlefieldConfiguration FormationStressBattlefield = new(
        playableSize: new Vector2(960.0f, 720.0f),
        visibleGroundSize: new Vector2(968.0f, 728.0f),
        navigationCellSize: 0.5f,
        cameraPanSpeed: 90.0f,
        cameraMaximumZoom: 120.0f,
        cameraStartingZoom: 58.0f,
        cameraStartingPosition: new Vector3(0.0f, 0.0f, 255.0f),
        isFormationStressTest: true);

    [Export]
    public float DebugSpawnSpacing { get; set; } = 1.1f;

    [Export]
    public float DebugTeamCenterSeparation { get; set; } = 16.5f;

    [Export]
    public float MoveDestinationPadding { get; set; } = 0.1f;

    [Export(PropertyHint.Range, "8,128,8")]
    public int MovePathQueriesPerFrame { get; set; } = 64;

    [Export(PropertyHint.Range, "1.0,8.0,0.25")]
    public float FormationClusterLinkDistance { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "0.5,1.0,0.01")]
    public float FormationRobustRadiusPercentile { get; set; } = 0.95f;

    [Export(PropertyHint.Range, "0.25,2.0,0.25")]
    public float FormationShortDistanceRadiusMultiplier { get; set; } = 1.0f;

    [Export(PropertyHint.Range, "1.5,6.0,0.25")]
    public float FormationLongDistanceRadiusMultiplier { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "30.0,150.0,5.0")]
    public float FormationLongReorientationAngleDegrees { get; set; } = 75.0f;

    [Export(PropertyHint.Range, "0.25,2.0,0.25")]
    public float FormationArrivalTransitionRadiusMultiplier { get; set; } = 0.75f;

    [Export(PropertyHint.Range, "1.0,3.0,0.05")]
    public float FormationTopologyCompactnessThreshold { get; set; } = 1.4f;

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
    private Node3D _formationStressObstacles = null!;
    private TeamResourceLedger _resourceLedger = null!;
    private EnemyMacroController _enemyMacroController = null!;
    private UnitOccupancySystem _unitOccupancySystem = null!;
    private NavigationRegion3D _navigationRegion = null!;
    private Mesh _friendlyUnitMesh = null!;
    private Mesh _enemyUnitMesh = null!;
    private Transform3D[] _defaultFriendlyTransforms = null!;
    private Transform3D[] _defaultEnemyTransforms = null!;
    private Rect2 _playableBattlefieldBounds;
    private Vector2 _playableBattlefieldSize;
    private BattlefieldConfiguration _battlefieldConfiguration = NormalBattlefield;
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
    private bool _hasPendingMoveOrder;
    private readonly List<SelectableUnit> _pendingMoveUnits = new();
    private Vector3 _pendingMoveDestination;
    private bool _headquartersConfigurationValid;
    private bool _headquartersRegistrationError;
    private int _runtimeBuildingSerial;
    private int _producedUnitSerial;
    private ulong _moveCommandSerial;
    private MoveCommandBatch _moveCommandBatch = null!;
    private ulong _formationPlanSerial;
    private readonly List<FormationArrivalTransition> _formationTransitions = new();
    private readonly Dictionary<ulong, Vector2> _formationHeadings = new();
    private MovementDiagnosticCommand _movementDiagnosticCommand = null!;

    private sealed class BattlefieldConfiguration
    {
        public readonly Vector2 PlayableSize;
        public readonly Vector2 VisibleGroundSize;
        public readonly float NavigationCellSize;
        public readonly float CameraPanSpeed;
        public readonly float CameraMaximumZoom;
        public readonly float CameraStartingZoom;
        public readonly Vector3 CameraStartingPosition;
        public readonly bool IsFormationStressTest;

        public BattlefieldConfiguration(
            Vector2 playableSize,
            Vector2 visibleGroundSize,
            float navigationCellSize,
            float cameraPanSpeed,
            float cameraMaximumZoom,
            float cameraStartingZoom,
            Vector3 cameraStartingPosition,
            bool isFormationStressTest)
        {
            PlayableSize = playableSize;
            VisibleGroundSize = visibleGroundSize;
            NavigationCellSize = navigationCellSize;
            CameraPanSpeed = cameraPanSpeed;
            CameraMaximumZoom = cameraMaximumZoom;
            CameraStartingZoom = cameraStartingZoom;
            CameraStartingPosition = cameraStartingPosition;
            IsFormationStressTest = isFormationStressTest;
        }
    }

    private sealed class FormationArrivalTransition
    {
        public ulong PlanSerial;
        public List<SelectableUnit> Units = new();
        public List<Vector3> FinalDestinations = new();
        public Vector2 ApproachCentroid;
        public Vector2 ArrivalHeading;
        public float TriggerDistance;
    }

    private sealed class MovementDiagnosticCommand
    {
        public ulong PlanSerial;
        public List<SelectableUnit> Units = new();
        public ulong StartedMilliseconds;
        public ulong LastStatusMilliseconds;
    }

    private sealed class MoveCommandBatch
    {
        public ulong Serial;
        public List<SelectableUnit> Units = null!;
        public List<Vector3> Candidates = null!;
        public List<int> UnitOrder = null!;
        public int[] PreferredCandidateIndices = null!;
        public bool[] ClaimedCandidates = null!;
        public bool[] UnitHandled = null!;
        public Queue<int> RetryUnits = new();
        public int InitialCursor;
        public int QueryBudget;
        public int QueriesUsed;
        public int CurrentRetryUnit = -1;
        public int RetryCandidateCursor;
    }

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("CameraRig/Camera3D");
        _friendlyUnits = GetNode<Node3D>("FriendlyUnits");
        _enemyUnits = GetNode<Node3D>("EnemyUnits");
        _buildings = GetNode<Node3D>("Buildings");
        _resourceNodes = GetNode<Node3D>("ResourceNodes");
        _formationStressObstacles = new Node3D
        {
            Name = "FormationStressObstacles",
        };
        AddChild(_formationStressObstacles);
        _resourceLedger = GetNode<TeamResourceLedger>("TeamResourceLedger");
        _enemyMacroController = GetNode<EnemyMacroController>(
            "EnemyMacroController");
        _enemyMacroController.Initialize(this, ProductionBuildingDefinition);
        _unitOccupancySystem = new UnitOccupancySystem
        {
            Name = "UnitOccupancySystem",
        };
        AddChild(_unitOccupancySystem);
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
        RespawnMacroScenario();
    }

    public override void _Process(double delta)
    {
        ProcessPendingMoveOrder();
        ProcessMoveCommandBatch();
        ProcessFormationArrivalTransitions();
        ProcessMovementDiagnostic();

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
            TryQueueCombatUnitAtSelectedBuilding();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed(QueueWorkerAction))
        {
            TryQueueWorkerAtSelectedHeadquarters();
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
            RespawnFriendlyUnits(500, useFormationStressBattlefield: true);
        }
        else if (@event.IsActionPressed(LoadMixedScenarioAction))
        {
            RespawnMixedRoleScenario();
        }
        else if (@event.IsActionPressed(LoadMacroScenarioAction))
        {
            RespawnMacroScenario();
        }
        else
        {
            return false;
        }

        return true;
    }

    private void RespawnFriendlyUnits(
        int count,
        bool useDefaultLayout = false,
        bool useFormationStressBattlefield = false)
    {
        BeginScenarioReplacement();
        ApplyBattlefieldConfiguration(
            useFormationStressBattlefield
                ? FormationStressBattlefield
                : NormalBattlefield);
        ClearSelection();

        ClearUnitContainer(_friendlyUnits);

        for (int index = 0; index < count; index++)
        {
            Transform3D transform = useDefaultLayout
                ? _defaultFriendlyTransforms[index]
                : CreateTestSpawnTransform(
                    index,
                    count,
                    useFormationStressBattlefield
                        ? 255.0f
                        : DebugTeamCenterSeparation * 0.5f);
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
            RespawnEnemies(
                useDefaultLayout: false,
                centerZOverride: useFormationStressBattlefield
                    ? -255.0f
                    : null);
        }

        ResetScenarioBuildings();
        ResetScenarioResources(includeMaterialsNodes: false);
        EndScenarioReplacement();
        UpdateDebugOverlay();
    }

    private void BeginScenarioReplacement()
    {
        _enemyMacroController.Deactivate();
        InteractionSlotRegistry.Clear();
        _isReplacingScenario = true;
        _isMatchTrackingActive = false;
        ClearRegisteredHeadquarters();
        _isMatchEnded = false;
        _matchOutcomeOverlay.Visible = false;
        _hasPendingMoveOrder = false;
        _pendingMoveUnits.Clear();
        CancelFormationPlan();
        CancelMoveCommandBatch();
        _formationHeadings.Clear();
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

    private void RespawnEnemies(
        bool useDefaultLayout,
        float? centerZOverride = null)
    {
        ClearUnitContainer(_enemyUnits);

        float zOffset = useDefaultLayout
            ? 0.0f
            : (centerZOverride ?? -DebugTeamCenterSeparation * 0.5f) -
                GetAverageZ(_defaultEnemyTransforms);

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
        ApplyBattlefieldConfiguration(NormalBattlefield);
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

    private void RespawnMacroScenario()
    {
        BeginScenarioReplacement();
        ApplyBattlefieldConfiguration(NormalBattlefield);
        ClearSelection();
        ClearUnitContainer(_friendlyUnits);
        ClearUnitContainer(_enemyUnits);

        for (int index = 0; index < MacroFriendlyCombatUnits; index++)
        {
            float x = (index - (MacroFriendlyCombatUnits - 1) * 0.5f) * 3.0f;
            SpawnUnit(
                _friendlyUnits,
                $"MacroFriendlyCombat{index + 1:D2}",
                _friendlyUnitMesh,
                UnitTeam.Friendly,
                CombatDefinition,
                new Transform3D(
                    Basis.Identity,
                    new Vector3(x, FriendlyUnitHeight, 8.0f)));
        }

        for (int index = 0; index < MacroEnemyCombatUnits; index++)
        {
            float x = (index - (MacroEnemyCombatUnits - 1) * 0.5f) * 3.0f;
            SpawnUnit(
                _enemyUnits,
                $"MacroEnemyCombat{index + 1:D2}",
                _enemyUnitMesh,
                UnitTeam.Enemy,
                CombatDefinition,
                new Transform3D(
                    Basis.Identity,
                    new Vector3(x, FriendlyUnitHeight, -8.0f)));
        }

        for (int index = 0; index < MacroWorkersPerTeam; index++)
        {
            float x = (index - (MacroWorkersPerTeam - 1) * 0.5f) * 5.0f;
            SpawnUnit(
                _friendlyUnits,
                $"MacroFriendlyWorker{index + 1:D2}",
                FriendlyWorkerMesh,
                UnitTeam.Friendly,
                WorkerDefinition,
                new Transform3D(
                    Basis.Identity,
                    new Vector3(x, WorkerUnitHeight, 13.0f)));
            SpawnUnit(
                _enemyUnits,
                $"MacroEnemyWorker{index + 1:D2}",
                EnemyWorkerMesh,
                UnitTeam.Enemy,
                WorkerDefinition,
                new Transform3D(
                    Basis.Identity,
                    new Vector3(x, WorkerUnitHeight, -13.0f)));
        }

        ResetScenarioBuildings();
        ResetScenarioResources(includeMaterialsNodes: false);
        SpawnMaterialsNode("MacroMaterialsBlueWest", new Vector2(-15.0f, 12.0f));
        SpawnMaterialsNode("MacroMaterialsBlueEast", new Vector2(15.0f, 12.0f));
        SpawnMaterialsNode("MacroMaterialsRedWest", new Vector2(-15.0f, -12.0f));
        SpawnMaterialsNode("MacroMaterialsRedEast", new Vector2(15.0f, -12.0f));
        EndScenarioReplacement();
        _enemyMacroController.Activate();
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
                    building.Production.Definition.ProducedUnitDefinition,
                    out Vector3 spawnPosition))
            {
                continue;
            }

            BuildingProduction production = building.Production;
            UnitDefinition producedDefinition =
                production.Definition.ProducedUnitDefinition;
            Mesh unitMesh = GetUnitMesh(building.Team, producedDefinition);
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
                producedDefinition,
                new Transform3D(
                    Basis.Identity,
                    new Vector3(
                        spawnPosition.X,
                        spawnHeight,
                        spawnPosition.Z)));
            production.AcknowledgeSpawn();
            if (production.HasRallyPoint &&
                TryFindAvailableUnitDestination(
                    producedUnit,
                    production.RallyPoint,
                    out Vector3 rallyDestination))
            {
                producedUnit.SetMoveTarget(rallyDestination);
            }
        }
    }

    private Mesh GetUnitMesh(UnitTeam team, UnitDefinition definition)
    {
        if (definition.WorkerEconomy is not null)
        {
            return team == UnitTeam.Friendly
                ? FriendlyWorkerMesh
                : EnemyWorkerMesh;
        }

        return team == UnitTeam.Friendly
            ? _friendlyUnitMesh
            : _enemyUnitMesh;
    }

    private static float GetOccupancyRadius(UnitDefinition definition)
    {
        return Mathf.Max(definition.OccupancyRadius, 0.1f);
    }

    private bool TryFindProductionSpawnPosition(
        BuildingEntity productionBuilding,
        UnitDefinition producedDefinition,
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
        float producedRadius = GetOccupancyRadius(producedDefinition);
        float ringSpacing = producedRadius * 2.0f + 0.2f;
        float initialRadius = productionBuilding.TargetRadius +
            producedRadius + 0.75f;
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
                    !IsProductionSpawnPositionValid(
                        navigationPosition,
                        producedRadius))
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

    private bool IsProductionSpawnPositionValid(
        Vector3 position,
        float producedRadius)
    {
        Vector2 horizontalPosition = new(position.X, position.Z);
        Rect2 safeBattlefieldBounds = _playableBattlefieldBounds.Grow(
            -producedRadius);
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
                producedRadius + 0.1f;
            if (horizontalPosition.DistanceSquaredTo(buildingPosition) <
                requiredDistance * requiredDistance)
            {
                return false;
            }
        }

        foreach (SelectableUnit unit in GetUnitsForTeam(teamFilter: null))
        {
            Vector2 unitPosition = new(
                unit.GlobalPosition.X,
                unit.GlobalPosition.Z);
            float requiredDistance = producedRadius + unit.OccupancyRadius;
            if (horizontalPosition.DistanceSquaredTo(unitPosition) <
                requiredDistance * requiredDistance)
            {
                return false;
            }
        }

        foreach (Node child in _resourceNodes.GetChildren())
        {
            if (child is not MaterialsResourceNode resourceNode ||
                !IsInstanceValid(resourceNode) ||
                resourceNode.IsDepleted)
            {
                continue;
            }

            Vector2 resourcePosition = new(
                resourceNode.GlobalPosition.X,
                resourceNode.GlobalPosition.Z);
            float requiredDistance = resourceNode.InteractionRadius +
                producedRadius;
            if (horizontalPosition.DistanceSquaredTo(resourcePosition) <
                requiredDistance * requiredDistance)
            {
                return false;
            }
        }

        return true;
    }

    private bool TryFindAvailableUnitDestination(
        SelectableUnit movingUnit,
        Vector3 requestedCenter,
        out Vector3 destination)
    {
        Rid navigationMap = GetWorld3D().NavigationMap;
        if (NavigationServer3D.MapGetIterationId(navigationMap) == 0)
        {
            destination = Vector3.Zero;
            return false;
        }

        const int maximumCandidates = 128;
        for (int candidateIndex = -1;
             candidateIndex < maximumCandidates;
             candidateIndex++)
        {
            Vector3 requested = candidateIndex < 0
                ? requestedCenter
                : InteractionPositioning.GetRadialPosition(
                    requestedCenter,
                    0.0f,
                    movingUnit.OccupancyRadius,
                    candidateIndex,
                    float.MaxValue,
                    out _);
            Vector3 projected = NavigationServer3D.MapGetClosestPoint(
                navigationMap,
                requested);
            Vector2 projectionDelta = new(
                requested.X - projected.X,
                requested.Z - projected.Z);
            if (projectionDelta.LengthSquared() > 0.25f ||
                !IsUnitDestinationAvailable(
                    movingUnit,
                    projected,
                    movingUnit.OccupancyRadius))
            {
                continue;
            }

            destination = projected;
            return true;
        }

        destination = Vector3.Zero;
        return false;
    }

    private bool IsUnitDestinationAvailable(
        SelectableUnit movingUnit,
        Vector3 position,
        float occupancyRadius)
    {
        Vector2 horizontalPosition = new(position.X, position.Z);
        if (!_playableBattlefieldBounds.Grow(-occupancyRadius)
                .HasPoint(horizontalPosition))
        {
            return false;
        }

        foreach (SelectableUnit unit in GetUnitsForTeam(teamFilter: null))
        {
            if (unit == movingUnit)
            {
                continue;
            }

            Vector2 unitPosition = new(unit.GlobalPosition.X, unit.GlobalPosition.Z);
            float requiredDistance = occupancyRadius + unit.OccupancyRadius;
            if (horizontalPosition.DistanceSquaredTo(unitPosition) <
                requiredDistance * requiredDistance)
            {
                return false;
            }
        }

        foreach (BuildingEntity building in GetLivingBuildings())
        {
            Vector2 buildingPosition = new(
                building.GlobalPosition.X,
                building.GlobalPosition.Z);
            float requiredDistance = occupancyRadius + building.TargetRadius;
            if (horizontalPosition.DistanceSquaredTo(buildingPosition) <
                requiredDistance * requiredDistance)
            {
                return false;
            }
        }

        foreach (Node child in _resourceNodes.GetChildren())
        {
            if (child is not MaterialsResourceNode resource ||
                !IsInstanceValid(resource) ||
                resource.IsDepleted)
            {
                continue;
            }

            Vector2 resourcePosition = new(
                resource.GlobalPosition.X,
                resource.GlobalPosition.Z);
            float requiredDistance = occupancyRadius + resource.InteractionRadius;
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
        bool registerAsHeadquarters = false,
        int constructionMaterialsCost = 0)
    {
        Vector3 dimensions = definition.PlaceholderDimensions;
        BuildingEntity building = new()
        {
            Name = name,
            Team = team,
            Definition = definition,
            StartsComplete = startsComplete,
            ConstructionMaterialsCost = Mathf.Max(constructionMaterialsCost, 0),
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

    public bool TryStartConstruction(
        UnitTeam team,
        SelectableUnit builder,
        BuildingDefinition definition,
        Vector3 groundPosition,
        out BuildingEntity constructionSite)
    {
        constructionSite = null!;
        if (_isReplacingScenario ||
            _isMatchEnded ||
            !IsSupportedTeam(team) ||
            !IsInstanceValid(builder) ||
            builder.IsQueuedForDeletion() ||
            !builder.IsAlive ||
            builder.Team != team ||
            !builder.HasWorkerEconomy ||
            !IsValidConstructionDefinition(definition) ||
            !IsBuildingPlacementValid(definition, groundPosition))
        {
            return false;
        }

        int materialsCost = Mathf.Max(definition.MaterialsCost, 0);
        if (!_resourceLedger.TrySpend(team, materialsCost))
        {
            return false;
        }

        _runtimeBuildingSerial++;
        string teamName = team == UnitTeam.Friendly ? "Friendly" : "Enemy";
        constructionSite = SpawnBuilding(
            $"{teamName}PlacedBuilding{_runtimeBuildingSerial:D3}",
            team,
            groundPosition,
            definition,
            startsComplete: false,
            constructionMaterialsCost: materialsCost);
        if (!builder.SetConstructionTarget(constructionSite))
        {
            constructionSite.CancelConstruction();
            _resourceLedger.Deposit(team, materialsCost);
            constructionSite = null!;
            return false;
        }

        QueueNavigationRebuild();
        return true;
    }

    public bool TryCancelConstruction(
        UnitTeam team,
        BuildingEntity constructionSite)
    {
        if (_isReplacingScenario ||
            _isMatchEnded ||
            !IsSupportedTeam(team) ||
            !IsInstanceValid(constructionSite) ||
            constructionSite.IsQueuedForDeletion() ||
            !constructionSite.IsAlive ||
            constructionSite.IsComplete ||
            constructionSite.Team != team)
        {
            return false;
        }

        int refund = Mathf.FloorToInt(
            constructionSite.ConstructionMaterialsCost *
            Mathf.Clamp(ConstructionRefundFraction, 0.0f, 1.0f));
        if (!constructionSite.CancelConstruction())
        {
            return false;
        }

        _resourceLedger.Deposit(team, refund);
        return true;
    }

    public bool TryQueueUnit(
        UnitTeam team,
        BuildingEntity productionBuilding)
    {
        return !_isReplacingScenario &&
            !_isMatchEnded &&
            IsSupportedTeam(team) &&
            IsInstanceValid(productionBuilding) &&
            !productionBuilding.IsQueuedForDeletion() &&
            productionBuilding.IsAlive &&
            productionBuilding.Team == team &&
            productionBuilding.IsComplete &&
            productionBuilding.HasProduction &&
            productionBuilding.Production.TryQueueUnit(_resourceLedger);
    }

    public bool TryCancelQueuedUnit(
        UnitTeam team,
        BuildingEntity productionBuilding)
    {
        return !_isReplacingScenario &&
            !_isMatchEnded &&
            IsSupportedTeam(team) &&
            IsInstanceValid(productionBuilding) &&
            !productionBuilding.IsQueuedForDeletion() &&
            productionBuilding.IsAlive &&
            productionBuilding.Team == team &&
            productionBuilding.IsComplete &&
            productionBuilding.HasProduction &&
            productionBuilding.Production.CancelMostRecentUnit(_resourceLedger);
    }

    public bool TrySetProductionRallyPoint(
        UnitTeam team,
        BuildingEntity productionBuilding,
        Vector3 requestedWorldPosition)
    {
        if (_isReplacingScenario ||
            _isMatchEnded ||
            !IsSupportedTeam(team) ||
            !IsInstanceValid(productionBuilding) ||
            productionBuilding.IsQueuedForDeletion() ||
            !productionBuilding.IsAlive ||
            productionBuilding.Team != team ||
            !productionBuilding.IsComplete ||
            !productionBuilding.HasProduction)
        {
            return false;
        }

        Rid navigationMap = GetWorld3D().NavigationMap;
        if (NavigationServer3D.MapGetIterationId(navigationMap) == 0)
        {
            return false;
        }

        Vector3 rallyPoint = NavigationServer3D.MapGetClosestPoint(
            navigationMap,
            requestedWorldPosition);
        Vector2 requested = new(requestedWorldPosition.X, requestedWorldPosition.Z);
        Vector2 projected = new(rallyPoint.X, rallyPoint.Z);
        if (requested.DistanceSquaredTo(projected) > 0.25f)
        {
            return false;
        }

        productionBuilding.Production.SetRallyPoint(rallyPoint);
        return true;
    }

    private static bool IsSupportedTeam(UnitTeam team)
    {
        return team == UnitTeam.Friendly || team == UnitTeam.Enemy;
    }

    private static bool IsValidConstructionDefinition(
        BuildingDefinition definition)
    {
        return definition is not null &&
            !definition.IsHeadquarters &&
            definition.PlaceholderDimensions.X > 0.0f &&
            definition.PlaceholderDimensions.Y > 0.0f &&
            definition.PlaceholderDimensions.Z > 0.0f;
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
        resourceNode.Depleted += HandleResourceDepleted;
        _resourceNodes.AddChild(resourceNode);
    }

    private void HandleResourceDepleted(MaterialsResourceNode resourceNode)
    {
        QueueNavigationRebuild();
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
        Vector2 panLimits = new(
            Mathf.Max(_playableBattlefieldSize.X * 0.5f - 1.0f, 0.0f),
            Mathf.Max(_playableBattlefieldSize.Y * 0.5f - 1.0f, 0.0f));
        GetNode<RtsCameraController>("CameraRig").ApplyBattlefieldView(
            panLimits,
            _battlefieldConfiguration.CameraPanSpeed,
            _battlefieldConfiguration.CameraMaximumZoom,
            _battlefieldConfiguration.CameraStartingPosition,
            _battlefieldConfiguration.CameraStartingZoom);
    }

    private void ApplyBattlefieldConfiguration(
        BattlefieldConfiguration configuration)
    {
        _battlefieldConfiguration = configuration;
        _playableBattlefieldSize = configuration.PlayableSize;
        _playableBattlefieldBounds = new Rect2(
            -configuration.PlayableSize * 0.5f,
            configuration.PlayableSize);

        BoxMesh groundMesh = (BoxMesh)GetNode<MeshInstance3D>("Ground").Mesh;
        groundMesh.Size = new Vector3(
            configuration.VisibleGroundSize.X,
            groundMesh.Size.Y,
            configuration.VisibleGroundSize.Y);
        BoxShape3D groundShape = (BoxShape3D)GetNode<CollisionShape3D>(
            "GroundClickSurface/CollisionShape3D").Shape;
        groundShape.Size = new Vector3(
            configuration.VisibleGroundSize.X,
            groundShape.Size.Y,
            configuration.VisibleGroundSize.Y);

        NavigationMesh navigationMesh = _navigationRegion.NavigationMesh;
        Vector2 halfSize = configuration.PlayableSize * 0.5f;
        navigationMesh.Vertices = new Vector3[]
        {
            new(-halfSize.X, 0.0f, halfSize.Y),
            new(halfSize.X, 0.0f, halfSize.Y),
            new(halfSize.X, 0.0f, -halfSize.Y),
            new(-halfSize.X, 0.0f, -halfSize.Y),
        };
        navigationMesh.ClearPolygons();
        navigationMesh.AddPolygon(new int[] { 0, 1, 2, 3 });
        navigationMesh.CellSize = configuration.NavigationCellSize;
        ConfigureCameraBounds();
        ConfigureFormationStressObstacles(configuration.IsFormationStressTest);
        MovementDiagnostics.Log(
            $"BATTLEFIELD stress={configuration.IsFormationStressTest} " +
            $"playable={configuration.PlayableSize} " +
            $"nav_cell={configuration.NavigationCellSize:F2} " +
            $"camera_pan={configuration.CameraPanSpeed:F1}");
    }

    private void ConfigureFormationStressObstacles(bool enabled)
    {
        foreach (Node child in _formationStressObstacles.GetChildren())
        {
            _formationStressObstacles.RemoveChild(child);
            child.QueueFree();
        }

        if (!enabled)
        {
            return;
        }

        AddFormationStressObstacle(
            "WesternLongBlock",
            new Vector2(-170.0f, 70.0f),
            new Vector2(24.0f, 180.0f));
        AddFormationStressObstacle(
            "SouthernLongBlock",
            new Vector2(145.0f, -135.0f),
            new Vector2(220.0f, 24.0f));
        AddFormationStressObstacle(
            "ChokeNorth",
            new Vector2(75.0f, 205.0f),
            new Vector2(20.0f, 150.0f));
        AddFormationStressObstacle(
            "ChokeSouth",
            new Vector2(75.0f, 50.0f),
            new Vector2(20.0f, 120.0f));
        AddFormationStressObstacle(
            "SouthwestBlock",
            new Vector2(-270.0f, -205.0f),
            new Vector2(110.0f, 24.0f));
    }

    private void AddFormationStressObstacle(
        string name,
        Vector2 center,
        Vector2 horizontalSize)
    {
        const float height = 4.0f;
        BoxShape3D shape = new()
        {
            Size = new Vector3(horizontalSize.X, height, horizontalSize.Y),
        };
        BoxMesh mesh = new()
        {
            Size = shape.Size,
            Material = BuildingEntity.CreateMaterial(
                new Color(0.32f, 0.31f, 0.29f, 1.0f),
                translucent: false),
        };
        StaticBody3D obstacle = new()
        {
            Name = name,
            Position = new Vector3(center.X, height * 0.5f, center.Y),
            CollisionLayer = GroundCollisionMask,
            CollisionMask = 0,
        };
        obstacle.AddToGroup(NavigationPathing.NavigationSourceGroup);
        obstacle.AddChild(new CollisionShape3D
        {
            Name = "CollisionShape3D",
            Shape = shape,
        });
        obstacle.AddChild(new MeshInstance3D
        {
            Name = "MeshInstance3D",
            Mesh = mesh,
        });
        _formationStressObstacles.AddChild(obstacle);
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
        _isPlacementValid = IsBuildingPlacementValid(
                ProductionBuildingDefinition,
                groundPosition) &&
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
        if (builder is null ||
            !TryStartConstruction(
                UnitTeam.Friendly,
                builder,
                ProductionBuildingDefinition,
                groundPosition,
                out _))
        {
            return;
        }

        CancelPlacementMode();
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

        if (TryCancelConstruction(UnitTeam.Friendly, _selectedBuilding))
        {
            _selectedBuilding = null!;
        }
    }

    private void TryQueueCombatUnitAtSelectedBuilding()
    {
        PruneInvalidBuildingSelection();
        if (_selectedBuilding is null ||
            _selectedBuilding.Team != UnitTeam.Friendly ||
            !_selectedBuilding.IsComplete ||
            !_selectedBuilding.HasProduction ||
            !_selectedBuilding.Production.Definition.ProducedUnitDefinition.CanAttack)
        {
            return;
        }

        TryQueueUnit(UnitTeam.Friendly, _selectedBuilding);
        UpdateDebugOverlay();
    }

    private void TryQueueWorkerAtSelectedHeadquarters()
    {
        PruneInvalidBuildingSelection();
        if (_selectedBuilding is null ||
            _selectedBuilding.Team != UnitTeam.Friendly ||
            !_selectedBuilding.IsComplete ||
            !_selectedBuilding.Definition.IsHeadquarters ||
            !_selectedBuilding.HasProduction ||
            _selectedBuilding.Production.Definition.ProducedUnitDefinition
                .WorkerEconomy is null)
        {
            return;
        }

        TryQueueUnit(UnitTeam.Friendly, _selectedBuilding);
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

        TryCancelQueuedUnit(UnitTeam.Friendly, _selectedBuilding);
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

    private bool IsBuildingPlacementValid(
        BuildingDefinition definition,
        Vector3 groundPosition)
    {
        if (_navigationRegion.IsBaking() ||
            NavigationServer3D.MapGetIterationId(GetWorld3D().NavigationMap) == 0)
        {
            return false;
        }

        if (!IsValidConstructionDefinition(definition))
        {
            return false;
        }

        Vector3 dimensions = definition.PlaceholderDimensions;
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
            definition.FootprintRadius,
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
            float requiredDistance = footprintRadius + unit.OccupancyRadius;
            if (candidateCenter.DistanceSquaredTo(unitCenter) <
                requiredDistance * requiredDistance)
            {
                return false;
            }
        }

        foreach (Node child in _resourceNodes.GetChildren())
        {
            if (child is not MaterialsResourceNode resourceNode ||
                !IsInstanceValid(resourceNode) ||
                resourceNode.IsDepleted)
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
        NavigationPathing.BeginMapUpdate(GetWorld3D().NavigationMap);
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
        navigationMesh.GeometrySourceGroupName = NavigationPathing.NavigationSourceGroup;
        navigationMesh.GeometryCollisionMask = GroundCollisionMask;
        navigationMesh.AgentRadius = Mathf.Max(
            GetOccupancyRadius(CombatDefinition),
            GetOccupancyRadius(WorkerDefinition));
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

    private void ProcessPendingMoveOrder()
    {
        if (!_hasPendingMoveOrder ||
            _isReplacingScenario ||
            _isMatchEnded)
        {
            return;
        }

        Rid navigationMap = GetWorld3D().NavigationMap;
        if (NavigationServer3D.MapGetIterationId(navigationMap) == 0 ||
            NavigationPathing.IsMapSynchronizing(navigationMap))
        {
            return;
        }

        List<SelectableUnit> units = new(_pendingMoveUnits);
        Vector3 destination = _pendingMoveDestination;
        _hasPendingMoveOrder = false;
        _pendingMoveUnits.Clear();
        IssueMoveOrder(units, destination);
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
            $"Building selected: {GetSelectedBuildingStatus()}\n" +
            $"{_enemyMacroController.GetDebugSummary()}\n\n" +
            "LMB select/drag | RMB ground move\n" +
            "Worker + RMB resource gather | Combat + RMB enemy attack\n" +
            $"Worker selected: B build ({Mathf.Max(ProductionBuildingDefinition.MaterialsCost, 0)}) | LMB place | Esc/RMB cancel\n" +
            "HQ selected: Q worker | Combat production: U combat unit\n" +
            "Production selected: RMB ground rally\n" +
            $"Delete cancel selected site " +
            $"({Mathf.RoundToInt(Mathf.Clamp(ConstructionRefundFraction, 0.0f, 1.0f) * 100.0f)}% refund)\n" +
            "X cancel newest queue | R restart after result\n" +
            "WASD/arrows pan | Wheel zoom\n" +
            "Debug: F1 8 | F2 20 | F3 100 | F4 250 | F5 500 large-map\n" +
            "F6 economy test | F7 reset complete MVP";
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
        UnitDefinition producedDefinition = definition.ProducedUnitDefinition;
        string rallyStatus = production.HasRallyPoint ? "set" : "none";
        string progressStatus = production.HasCompletedUnitWaiting
            ? "waiting for spawn"
            : $"{production.ProductionProgress * 100.0f:0}%";
        string productionCommand = producedDefinition.WorkerEconomy is not null
            ? "Q queue worker"
            : "U queue combat unit";
        return $"{_selectedBuilding.Definition.DisplayName}\n" +
            $"{producedDefinition.DisplayName} queue: {production.QueueCount}/" +
                $"{Mathf.Max(definition.MaximumQueueLength, 1)} | " +
                $"Progress: {progressStatus}\n" +
            $"{productionCommand} ({Mathf.Max(definition.UnitMaterialsCost, 0)} Materials) | " +
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
        _enemyMacroController.Deactivate();
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
            if (node is SelectableUnit unit &&
                IsInstanceValid(unit) &&
                !unit.IsQueuedForDeletion() &&
                unit.IsAlive)
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
            IssueResourceOrder(resourceTarget);
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
            else if (_selectedBuilding is not null &&
                     _selectedBuilding.Team == UnitTeam.Friendly &&
                     _selectedBuilding.IsComplete &&
                     _selectedBuilding.HasProduction)
            {
                TrySetSelectedBuildingRallyPoint(screenPosition);
            }
            else
            {
                TryIssueMoveOrder(screenPosition);
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

        if (TrySetProductionRallyPoint(
                UnitTeam.Friendly,
                _selectedBuilding,
                groundPosition))
        {
            UpdateDebugOverlay();
        }
    }

    private void IssueAttackOrder(ICombatTarget target)
    {
        CancelFormationPlan();
        PruneInvalidSelection();
        foreach (SelectableUnit unit in _selectedUnits)
        {
            if (IsInstanceValid(unit) && unit.Team == UnitTeam.Friendly)
            {
                unit.SetAttackTarget(target);
            }
        }
    }

    private void IssueResourceOrder(MaterialsResourceNode resourceTarget)
    {
        CancelFormationPlan();
        PruneInvalidSelection();
        List<SelectableUnit> workers = new();
        List<SelectableUnit> combatUnits = new();
        foreach (SelectableUnit unit in _selectedUnits)
        {
            if (!IsInstanceValid(unit) || unit.Team != UnitTeam.Friendly)
            {
                continue;
            }

            if (unit.HasWorkerEconomy)
            {
                workers.Add(unit);
            }
            else if (unit.CanAttack)
            {
                combatUnits.Add(unit);
            }
        }

        workers.Sort((first, second) =>
            first.GetInstanceId().CompareTo(second.GetInstanceId()));
        for (int index = 0; index < workers.Count; index++)
        {
            workers[index].SetGatherTarget(resourceTarget);
        }

        IssueAdjacentMoveOrder(
            combatUnits,
            resourceTarget.GlobalPosition,
            resourceTarget.InteractionRadius,
            workers.Count);
    }

    private void IssueManualDropOffOrder(BuildingEntity building)
    {
        CancelFormationPlan();
        PruneInvalidSelection();
        List<SelectableUnit> depositingWorkers = new();
        List<SelectableUnit> ordinaryMovers = new();
        foreach (SelectableUnit unit in _selectedUnits)
        {
            if (!IsInstanceValid(unit) || unit.Team != UnitTeam.Friendly)
            {
                continue;
            }

            if (unit.HasWorkerEconomy && unit.CarriedMaterials > 0)
            {
                depositingWorkers.Add(unit);
            }
            else
            {
                ordinaryMovers.Add(unit);
            }
        }

        depositingWorkers.Sort((first, second) =>
            first.GetInstanceId().CompareTo(second.GetInstanceId()));
        for (int index = 0; index < depositingWorkers.Count; index++)
        {
            depositingWorkers[index].SetManualDropOff(building);
        }

        IssueAdjacentMoveOrder(
            ordinaryMovers,
            building.GlobalPosition,
            building.TargetRadius,
            depositingWorkers.Count);
    }

    private void IssueConstructionOrder(BuildingEntity constructionSite)
    {
        CancelFormationPlan();
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
            if (!IsInstanceValid(unit) ||
                unit.Team != UnitTeam.Friendly ||
                !unit.HasWorkerEconomy)
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

        IssueMoveOrder(_selectedUnits, commandDestination);
    }

    private void IssueMoveOrder(
        IReadOnlyList<SelectableUnit> requestedUnits,
        Vector3 commandDestination)
    {
        List<SelectableUnit> units = GetCommandableUnits(requestedUnits);
        if (units.Count == 0)
        {
            return;
        }

        CancelFormationPlan();

        Rid navigationMap = GetWorld3D().NavigationMap;
        if (NavigationServer3D.MapGetIterationId(navigationMap) == 0)
        {
            return;
        }

        if (NavigationPathing.IsMapSynchronizing(navigationMap))
        {
            _pendingMoveUnits.Clear();
            _pendingMoveUnits.AddRange(units);
            _pendingMoveDestination = commandDestination;
            _hasPendingMoveOrder = true;
            return;
        }

        FormationMovePlanner planner = new(new FormationMovePlanner.Settings
        {
            ClusterLinkDistance = FormationClusterLinkDistance,
            RobustRadiusPercentile = FormationRobustRadiusPercentile,
            ShortDistanceRadiusMultiplier =
                FormationShortDistanceRadiusMultiplier,
            LongDistanceRadiusMultiplier = FormationLongDistanceRadiusMultiplier,
            LongReorientationAngleDegrees =
                FormationLongReorientationAngleDegrees,
            ArrivalTransitionRadiusMultiplier =
                FormationArrivalTransitionRadiusMultiplier,
            TopologyCompactnessThreshold =
                FormationTopologyCompactnessThreshold,
            SlotSeparationMargin = MoveDestinationPadding,
            DefaultOrientation = Vector2.Up,
        });
        ulong planningStartedMicroseconds = Time.GetTicksUsec();
        FormationMovePlanner.CommandPlan plan = planner.CreatePlan(
            units,
            commandDestination,
            _playableBattlefieldBounds,
            navigationMap,
            GetStoredFormationHeading);
        ulong planningMicroseconds = Time.GetTicksUsec() -
            planningStartedMicroseconds;
        ulong planSerial = _formationPlanSerial;
        MovementDiagnostics.Log(
            $"COMMAND plan={planSerial} units={plan.Units.Count} " +
            $"click=({commandDestination.X:F2},{commandDestination.Z:F2}) " +
            $"source_clusters={plan.SourceClusters.Count} " +
            $"sizes={FormatClusterSizes(plan.SourceClusters)} " +
            $"decision={(plan.UsesDirectTranslation ? "short_translation" : "unified_lattice")} " +
            $"class={plan.DistanceClass} dispersion={plan.DispersionRatio:F2} " +
            $"compact_threshold={FormationTopologyCompactnessThreshold:F2} " +
            $"source_footprint={FormatHorizontal(plan.SourceFootprintSize)} " +
            $"compact_footprint={FormatHorizontal(plan.CompactFootprintSize)} " +
            $"planning_us={planningMicroseconds}");
        if (!plan.UsesDirectTranslation)
        {
            MovementDiagnostics.Log(
                $"FORMATION plan={planSerial} grid={plan.GridColumns}x{plan.GridRows} " +
                $"spacing={plan.GridSpacing:F2} " +
                $"grid_centroid={FormatHorizontal(plan.GridCentroid)} " +
                $"orientation={FormatHorizontal(plan.GridOrientation)} " +
                $"assigned_centroid={FormatHorizontal(plan.AssignedSlotCentroid)} " +
                $"largest_adjacency_gap={plan.LargestAdjacencyGap:F2} " +
                $"target={FormatHorizontal(plan.TargetCentroid)} " +
                $"approach={FormatHorizontal(plan.ApproachCentroid)} " +
                $"heading={FormatHorizontal(plan.ArrivalHeading)} " +
                $"transition={plan.HasArrivalTransition}");
        }

        for (int clusterIndex = 0;
             clusterIndex < plan.SourceClusters.Count;
             clusterIndex++)
        {
            FormationMovePlanner.SourceClusterSummary cluster =
                plan.SourceClusters[clusterIndex];
            MovementDiagnostics.Log(
                $"SOURCE_CLUSTER plan={planSerial} index={clusterIndex} " +
                $"units={cluster.UnitCount} radius={cluster.RobustRadius:F2} " +
                $"source={FormatHorizontal(cluster.SourceCentroid)} " +
                $"assigned_centroid={FormatHorizontal(cluster.AssignedSlotCentroid)} " +
                $"assigned_bounds={FormatBounds(cluster.AssignedSlotBounds)}");
        }

        if (plan.HasArrivalTransition)
        {
            _formationTransitions.Add(new FormationArrivalTransition
            {
                PlanSerial = planSerial,
                Units = new List<SelectableUnit>(plan.Units),
                FinalDestinations = new List<Vector3>(
                    plan.FinalDestinations),
                ApproachCentroid = plan.ApproachCentroid,
                ArrivalHeading = plan.ArrivalHeading,
                TriggerDistance = Mathf.Max(
                    plan.RobustRadius * 0.35f,
                    2.0f),
            });
        }

        if (plan.Units.Count == 0 || plan.InitialDestinations.Count == 0)
        {
            foreach (SelectableUnit unit in units)
            {
                unit.CancelCurrentOrder();
            }

            return;
        }

        StartMovementDiagnostic(planSerial, plan.Units);
        IssuePreassignedDestinations(
            plan.Units,
            plan.InitialDestinations,
            "travel",
            preserveExistingTopologySpacing: plan.UsesDirectTranslation);
    }

    private void IssueAdjacentMoveOrder(
        IReadOnlyList<SelectableUnit> requestedUnits,
        Vector3 targetPosition,
        float targetRadius,
        int ordinalOffset)
    {
        List<SelectableUnit> units = GetCommandableUnits(requestedUnits);
        if (units.Count == 0)
        {
            return;
        }

        CancelFormationPlan();

        Rid navigationMap = GetWorld3D().NavigationMap;
        if (NavigationServer3D.MapGetIterationId(navigationMap) == 0)
        {
            return;
        }

        float maximumRadius = GetMaximumOccupancyRadius(units);
        float minimumSpacing = maximumRadius * 2.0f +
            Mathf.Max(MoveDestinationPadding, 0.0f);
        HashSet<ulong> commandedUnitIds = CreateUnitIdSet(units);
        Dictionary<Vector2I, List<Vector2>> occupiedCandidateCells = new();
        List<Vector3> candidates = new(
            Mathf.Min(
                units.Count + AdditionalDestinationCandidates,
                MaximumDestinationCandidates));
        int ordinal = Mathf.Max(ordinalOffset, 0);
        int maximumAttempts = ordinal +
            Mathf.Min(
                units.Count * 8 + AdditionalDestinationCandidates,
                MaximumDestinationCandidates);
        int desiredCandidateCount = Mathf.Min(
            units.Count + AdditionalDestinationCandidates,
            MaximumDestinationCandidates);
        while (candidates.Count < desiredCandidateCount &&
               ordinal < maximumAttempts)
        {
            Vector3 requested = InteractionPositioning.GetRadialPosition(
                targetPosition,
                targetRadius,
                maximumRadius,
                ordinal,
                float.MaxValue,
                out _);
            ordinal++;
            Vector3 projected = NavigationServer3D.MapGetClosestPoint(
                navigationMap,
                requested);
            Vector2 projectionDelta = new(
                requested.X - projected.X,
                requested.Z - projected.Z);
            if (projectionDelta.LengthSquared() >
                    maximumRadius * maximumRadius ||
                !IsCandidateDestinationValid(
                    projected,
                    maximumRadius,
                    commandedUnitIds,
                    target: null))
            {
                continue;
            }

            if (TryReserveDestinationCandidate(
                    occupiedCandidateCells,
                    projected,
                    minimumSpacing))
            {
                candidates.Add(projected);
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        IssueReachableDestinations(units, candidates);
    }

    private static List<SelectableUnit> GetCommandableUnits(
        IReadOnlyList<SelectableUnit> requestedUnits)
    {
        List<SelectableUnit> units = new(requestedUnits.Count);
        foreach (SelectableUnit unit in requestedUnits)
        {
            if (IsInstanceValid(unit) &&
                !unit.IsQueuedForDeletion() &&
                unit.IsAlive &&
                unit.Team == UnitTeam.Friendly)
            {
                units.Add(unit);
            }
        }

        return units;
    }

    private static float GetMaximumOccupancyRadius(
        IReadOnlyList<SelectableUnit> units)
    {
        float maximumRadius = 0.1f;
        foreach (SelectableUnit unit in units)
        {
            maximumRadius = Mathf.Max(maximumRadius, unit.OccupancyRadius);
        }

        return maximumRadius;
    }

    private void IssuePreassignedDestinations(
        IReadOnlyList<SelectableUnit> units,
        IReadOnlyList<Vector3> intendedDestinations,
        string diagnosticPhase,
        bool preserveExistingTopologySpacing = false)
    {
        CancelMoveCommandBatch();
        if (units.Count == 0 || units.Count != intendedDestinations.Count)
        {
            return;
        }

        foreach (SelectableUnit unit in units)
        {
            unit.CancelCurrentOrder();
        }

        HashSet<ulong> commandedUnitIds = CreateUnitIdSet(units);
        float maximumRadius = GetMaximumOccupancyRadius(units);
        float minimumSpacing = maximumRadius * 2.0f +
            Mathf.Max(MoveDestinationPadding, 0.0f);
        Dictionary<Vector2I, List<Vector2>> occupiedCandidateCells = new();
        List<Vector3> candidates = new(units.Count);
        int[] preferredCandidateIndices = new int[units.Count];
        System.Array.Fill(preferredCandidateIndices, -1);
        int repairedDestinations = 0;
        int rejectedDestinations = 0;
        for (int index = 0; index < units.Count; index++)
        {
            if (!TryCreateStableFormationDestination(
                    units[index],
                    intendedDestinations[index],
                    minimumSpacing,
                    commandedUnitIds,
                    occupiedCandidateCells,
                    preserveExistingTopologySpacing,
                    out Vector3 destination))
            {
                rejectedDestinations++;
                continue;
            }

            Vector2 intendedHorizontal = new(
                intendedDestinations[index].X,
                intendedDestinations[index].Z);
            Vector2 actualHorizontal = new(destination.X, destination.Z);
            if (intendedHorizontal.DistanceSquaredTo(actualHorizontal) > 0.0025f)
            {
                repairedDestinations++;
            }

            preferredCandidateIndices[index] = candidates.Count;
            candidates.Add(destination);
        }

        if (candidates.Count == 0)
        {
            MovementDiagnostics.Log(
                $"SLOTS plan={_formationPlanSerial} phase={diagnosticPhase} " +
                $"accepted=0 repaired={repairedDestinations} " +
                $"rejected={rejectedDestinations}");
            return;
        }

        MovementDiagnostics.Log(
            $"SLOTS plan={_formationPlanSerial} phase={diagnosticPhase} " +
            $"accepted={candidates.Count} repaired={repairedDestinations} " +
            $"rejected={rejectedDestinations}");

        BeginMoveCommandBatch(
            units,
            candidates,
            CreateTopologyUnitOrder(units),
            preferredCandidateIndices);
    }

    private bool TryCreateStableFormationDestination(
        SelectableUnit unit,
        Vector3 intendedDestination,
        float minimumSpacing,
        IReadOnlySet<ulong> commandedUnitIds,
        IDictionary<Vector2I, List<Vector2>> occupiedCandidateCells,
        bool preserveExistingTopologySpacing,
        out Vector3 destination)
    {
        const int maximumRepairRings = 16;
        Rect2 safeBounds = _playableBattlefieldBounds.Grow(
            -unit.OccupancyRadius);
        Vector2 intendedHorizontal = new(
            intendedDestination.X,
            intendedDestination.Z);
        intendedHorizontal = new Vector2(
            Mathf.Clamp(
                intendedHorizontal.X,
                safeBounds.Position.X,
                safeBounds.End.X),
            Mathf.Clamp(
                intendedHorizontal.Y,
                safeBounds.Position.Y,
                safeBounds.End.Y));

        for (int ring = 0; ring <= maximumRepairRings; ring++)
        {
            int minimumOffset = -ring;
            int maximumOffset = ring;
            for (int x = minimumOffset; x <= maximumOffset; x++)
            {
                for (int z = minimumOffset; z <= maximumOffset; z++)
                {
                    if (ring > 0 &&
                        Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) != ring)
                    {
                        continue;
                    }

                    Vector2 requestedHorizontal = intendedHorizontal +
                        new Vector2(x * minimumSpacing, z * minimumSpacing);
                    if (!safeBounds.HasPoint(requestedHorizontal))
                    {
                        continue;
                    }

                    Vector3 requested = new(
                        requestedHorizontal.X,
                        intendedDestination.Y,
                        requestedHorizontal.Y);
                    if (!IsCandidateDestinationValid(
                            requested,
                            unit.OccupancyRadius,
                            commandedUnitIds,
                            target: null))
                    {
                        continue;
                    }

                    if (preserveExistingTopologySpacing && ring == 0)
                    {
                        RecordDestinationCandidate(
                            occupiedCandidateCells,
                            requested,
                            minimumSpacing);
                    }
                    else if (!TryReserveDestinationCandidate(
                                 occupiedCandidateCells,
                                 requested,
                                 minimumSpacing))
                    {
                        continue;
                    }

                    destination = requested;
                    destination.Y = unit.GlobalPosition.Y;
                    return true;
                }
            }
        }

        destination = unit.GlobalPosition;
        return false;
    }

    private void IssueReachableDestinations(
        IReadOnlyList<SelectableUnit> units,
        IReadOnlyList<Vector3> candidates)
    {
        CancelMoveCommandBatch();
        foreach (SelectableUnit unit in units)
        {
            unit.CancelCurrentOrder();
        }

        int preferredCount = Mathf.Min(units.Count, candidates.Count);
        List<Vector3> preferredPositions = new(preferredCount);
        for (int index = 0; index < preferredCount; index++)
        {
            preferredPositions.Add(candidates[index]);
        }

        List<int> unitOrder = CreateTopologyUnitOrder(units);
        preferredPositions.Sort((first, second) =>
        {
            int xComparison = first.X.CompareTo(second.X);
            return xComparison != 0
                ? xComparison
                : first.Z.CompareTo(second.Z);
        });

        Dictionary<Vector3, int> candidateIndices = new();
        for (int index = 0; index < candidates.Count; index++)
        {
            candidateIndices[candidates[index]] = index;
        }

        int[] preferredCandidateIndices = new int[units.Count];
        System.Array.Fill(preferredCandidateIndices, -1);
        for (int rank = 0; rank < preferredCount; rank++)
        {
            int unitIndex = unitOrder[rank];
            if (candidateIndices.TryGetValue(
                    preferredPositions[rank],
                    out int candidateIndex))
            {
                preferredCandidateIndices[unitIndex] = candidateIndex;
            }
        }

        BeginMoveCommandBatch(
            units,
            candidates,
            unitOrder,
            preferredCandidateIndices);
    }

    private void BeginMoveCommandBatch(
        IReadOnlyList<SelectableUnit> units,
        IReadOnlyList<Vector3> candidates,
        List<int> unitOrder,
        int[] preferredCandidateIndices)
    {
        _moveCommandSerial++;
        _moveCommandBatch = new MoveCommandBatch
        {
            Serial = _moveCommandSerial,
            Units = new List<SelectableUnit>(units),
            Candidates = new List<Vector3>(candidates),
            UnitOrder = unitOrder,
            PreferredCandidateIndices = preferredCandidateIndices,
            ClaimedCandidates = new bool[candidates.Count],
            UnitHandled = new bool[units.Count],
            QueryBudget = units.Count * 2 + AdditionalDestinationCandidates,
        };
        MovementDiagnostics.Log(
            $"BATCH_BEGIN plan={_formationPlanSerial} " +
            $"batch={_moveCommandSerial} units={units.Count} " +
            $"candidates={candidates.Count} " +
            $"per_frame={Mathf.Max(MovePathQueriesPerFrame, 1)} " +
            $"query_budget={_moveCommandBatch.QueryBudget}");
        ProcessMoveCommandBatch();
    }

    private void ProcessMoveCommandBatch()
    {
        MoveCommandBatch batch = _moveCommandBatch;
        if (batch is null ||
            batch.Serial != _moveCommandSerial ||
            _isReplacingScenario ||
            _isMatchEnded)
        {
            return;
        }

        Rid navigationMap = GetWorld3D().NavigationMap;
        if (NavigationServer3D.MapGetIterationId(navigationMap) == 0 ||
            NavigationPathing.IsMapSynchronizing(navigationMap))
        {
            return;
        }

        int remainingQueries = Mathf.Max(MovePathQueriesPerFrame, 1);
        while (remainingQueries > 0 && batch.QueriesUsed < batch.QueryBudget)
        {
            if (batch.InitialCursor < batch.UnitOrder.Count)
            {
                int unitIndex = batch.UnitOrder[batch.InitialCursor++];
                SelectableUnit unit = batch.Units[unitIndex];
                if (!IsBatchUnitValid(unit))
                {
                    batch.UnitHandled[unitIndex] = true;
                    continue;
                }

                int candidateIndex = batch.PreferredCandidateIndices[unitIndex];
                if (candidateIndex >= 0 &&
                    !batch.ClaimedCandidates[candidateIndex])
                {
                    batch.QueriesUsed++;
                    remainingQueries--;
                    if (NavigationPathing.TryResolveReachablePoint(
                            unit,
                            batch.Candidates[candidateIndex],
                            unit.OccupancyRadius,
                            out Vector3 reachable))
                    {
                        batch.ClaimedCandidates[candidateIndex] = true;
                        batch.UnitHandled[unitIndex] = true;
                        unit.SetValidatedMoveTarget(reachable);
                        continue;
                    }
                }

                batch.RetryUnits.Enqueue(unitIndex);
                continue;
            }

            if (batch.CurrentRetryUnit < 0)
            {
                if (batch.RetryUnits.Count == 0)
                {
                    CompleteMoveCommandBatch(batch);
                    return;
                }

                batch.CurrentRetryUnit = batch.RetryUnits.Dequeue();
                batch.RetryCandidateCursor = 0;
            }

            int retryUnitIndex = batch.CurrentRetryUnit;
            SelectableUnit retryUnit = batch.Units[retryUnitIndex];
            if (!IsBatchUnitValid(retryUnit))
            {
                batch.UnitHandled[retryUnitIndex] = true;
                batch.CurrentRetryUnit = -1;
                continue;
            }

            bool queryIssued = false;
            while (batch.RetryCandidateCursor < batch.Candidates.Count)
            {
                int candidateIndex = batch.RetryCandidateCursor++;
                if (batch.ClaimedCandidates[candidateIndex])
                {
                    continue;
                }

                batch.QueriesUsed++;
                remainingQueries--;
                queryIssued = true;
                if (NavigationPathing.TryResolveReachablePoint(
                        retryUnit,
                        batch.Candidates[candidateIndex],
                        retryUnit.OccupancyRadius,
                        out Vector3 reachable))
                {
                    batch.ClaimedCandidates[candidateIndex] = true;
                    batch.UnitHandled[retryUnitIndex] = true;
                    retryUnit.SetValidatedMoveTarget(reachable);
                    batch.CurrentRetryUnit = -1;
                }

                break;
            }

            if (!queryIssued ||
                batch.RetryCandidateCursor >= batch.Candidates.Count)
            {
                if (!batch.UnitHandled[retryUnitIndex])
                {
                    retryUnit.CancelCurrentOrder();
                    batch.UnitHandled[retryUnitIndex] = true;
                }

                batch.CurrentRetryUnit = -1;
            }
        }

        if (batch.QueriesUsed >= batch.QueryBudget)
        {
            CompleteMoveCommandBatch(batch);
        }
    }

    private void CompleteMoveCommandBatch(MoveCommandBatch batch)
    {
        int handledUnits = 0;
        for (int index = 0; index < batch.Units.Count; index++)
        {
            if (batch.UnitHandled[index])
            {
                handledUnits++;
            }

            if (!batch.UnitHandled[index] && IsBatchUnitValid(batch.Units[index]))
            {
                batch.Units[index].CancelCurrentOrder();
            }
        }

        if (_moveCommandBatch == batch)
        {
            _moveCommandBatch = null!;
        }

        MovementDiagnostics.Log(
            $"BATCH_COMPLETE plan={_formationPlanSerial} batch={batch.Serial} " +
            $"handled={handledUnits}/{batch.Units.Count} " +
            $"queries={batch.QueriesUsed} retries={batch.RetryUnits.Count}");
    }

    private void CancelMoveCommandBatch()
    {
        if (_moveCommandBatch is not null)
        {
            MovementDiagnostics.Log(
                $"BATCH_CANCEL plan={_formationPlanSerial} " +
                $"batch={_moveCommandBatch.Serial} " +
                $"cursor={_moveCommandBatch.InitialCursor}/" +
                $"{_moveCommandBatch.UnitOrder.Count} " +
                $"queries={_moveCommandBatch.QueriesUsed}");
        }

        _moveCommandSerial++;
        _moveCommandBatch = null!;
    }

    private void CancelFormationPlan()
    {
        if (_movementDiagnosticCommand is not null)
        {
            ulong elapsed = Time.GetTicksMsec() -
                _movementDiagnosticCommand.StartedMilliseconds;
            MovementDiagnostics.Log(
                $"COMMAND_CANCEL plan={_movementDiagnosticCommand.PlanSerial} " +
                $"elapsed_ms={elapsed}");
            _movementDiagnosticCommand = null!;
        }

        _formationPlanSerial++;
        _formationTransitions.Clear();
        CancelMoveCommandBatch();
    }

    private void ProcessFormationArrivalTransitions()
    {
        if (_moveCommandBatch is not null ||
            _formationTransitions.Count == 0 ||
            _isReplacingScenario ||
            _isMatchEnded)
        {
            return;
        }

        for (int transitionIndex = _formationTransitions.Count - 1;
             transitionIndex >= 0;
             transitionIndex--)
        {
            FormationArrivalTransition transition =
                _formationTransitions[transitionIndex];
            if (transition.PlanSerial != _formationPlanSerial)
            {
                _formationTransitions.RemoveAt(transitionIndex);
                continue;
            }

            Vector2 centroid = Vector2.Zero;
            int validUnitCount = 0;
            for (int unitIndex = 0;
                 unitIndex < transition.Units.Count;
                 unitIndex++)
            {
                SelectableUnit unit = transition.Units[unitIndex];
                if (!IsBatchUnitValid(unit) ||
                    unit.CurrentCombatTarget is not null)
                {
                    continue;
                }

                centroid += new Vector2(
                    unit.GlobalPosition.X,
                    unit.GlobalPosition.Z);
                validUnitCount++;
            }

            if (validUnitCount == 0)
            {
                _formationTransitions.RemoveAt(transitionIndex);
                continue;
            }

            centroid /= validUnitCount;
            if (centroid.DistanceSquaredTo(transition.ApproachCentroid) >
                    transition.TriggerDistance * transition.TriggerDistance)
            {
                continue;
            }

            List<SelectableUnit> transitionUnits = new(validUnitCount);
            List<Vector3> transitionDestinations = new(validUnitCount);
            for (int unitIndex = 0;
                 unitIndex < transition.Units.Count;
                 unitIndex++)
            {
                SelectableUnit unit = transition.Units[unitIndex];
                if (!IsBatchUnitValid(unit) ||
                    unit.CurrentCombatTarget is not null)
                {
                    continue;
                }

                transitionUnits.Add(unit);
                transitionDestinations.Add(
                    transition.FinalDestinations[unitIndex]);
                _formationHeadings[unit.GetInstanceId()] =
                    transition.ArrivalHeading;
            }

            _formationTransitions.RemoveAt(transitionIndex);
            MovementDiagnostics.Log(
                $"TRANSITION plan={transition.PlanSerial} " +
                $"units={transitionUnits.Count} " +
                $"centroid={FormatHorizontal(centroid)} " +
                $"approach={FormatHorizontal(transition.ApproachCentroid)} " +
                $"heading={FormatHorizontal(transition.ArrivalHeading)}");
            IssuePreassignedDestinations(
                transitionUnits,
                transitionDestinations,
                "arrival");
            return;
        }
    }

    private Vector2 GetStoredFormationHeading(SelectableUnit unit)
    {
        return _formationHeadings.TryGetValue(
            unit.GetInstanceId(),
            out Vector2 heading) &&
            heading.LengthSquared() > 0.0001f
                ? heading.Normalized()
                : Vector2.Up;
    }

    private void StartMovementDiagnostic(
        ulong planSerial,
        IReadOnlyList<SelectableUnit> units)
    {
        if (!MovementDiagnostics.Enabled)
        {
            return;
        }

        ulong now = Time.GetTicksMsec();
        _movementDiagnosticCommand = new MovementDiagnosticCommand
        {
            PlanSerial = planSerial,
            Units = new List<SelectableUnit>(units),
            StartedMilliseconds = now,
            LastStatusMilliseconds = now,
        };
    }

    private void ProcessMovementDiagnostic()
    {
        if (_movementDiagnosticCommand is null || !MovementDiagnostics.Enabled)
        {
            return;
        }

        MovementDiagnosticCommand command = _movementDiagnosticCommand;
        if (command.PlanSerial != _formationPlanSerial)
        {
            _movementDiagnosticCommand = null!;
            return;
        }

        int livingUnits = 0;
        int movingUnits = 0;
        foreach (SelectableUnit unit in command.Units)
        {
            if (!IsInstanceValid(unit) ||
                unit.IsQueuedForDeletion() ||
                !unit.IsAlive)
            {
                continue;
            }

            livingUnits++;
            if (unit.IsMovingForOccupancy)
            {
                movingUnits++;
            }
        }

        int pendingTransitions = 0;
        foreach (FormationArrivalTransition transition in _formationTransitions)
        {
            if (transition.PlanSerial == command.PlanSerial)
            {
                pendingTransitions++;
            }
        }

        ulong now = Time.GetTicksMsec();
        ulong elapsed = now - command.StartedMilliseconds;
        bool batchActive = _moveCommandBatch is not null;
        if (movingUnits == 0 && pendingTransitions == 0 && !batchActive)
        {
            MovementDiagnostics.Log(
                $"SETTLED plan={command.PlanSerial} elapsed_ms={elapsed} " +
                $"living={livingUnits}");
            _movementDiagnosticCommand = null!;
            return;
        }

        if (now - command.LastStatusMilliseconds < 1000)
        {
            return;
        }

        command.LastStatusMilliseconds = now;
        MovementDiagnostics.Log(
            $"STATUS plan={command.PlanSerial} elapsed_ms={elapsed} " +
            $"moving={movingUnits}/{livingUnits} " +
            $"transitions={pendingTransitions} batch={batchActive}");
    }

    private static string FormatHorizontal(Vector2 value)
    {
        return $"({value.X:F2},{value.Y:F2})";
    }

    private static string FormatBounds(Rect2 bounds)
    {
        return $"{FormatHorizontal(bounds.Position)}-" +
            $"{FormatHorizontal(bounds.End)}";
    }

    private static string FormatClusterSizes(
        IReadOnlyList<FormationMovePlanner.SourceClusterSummary> clusters)
    {
        if (clusters.Count == 0)
        {
            return "[]";
        }

        string result = "[";
        for (int index = 0; index < clusters.Count; index++)
        {
            if (index > 0)
            {
                result += ",";
            }

            result += clusters[index].UnitCount;
        }

        return result + "]";
    }

    private static bool IsBatchUnitValid(SelectableUnit unit)
    {
        return IsInstanceValid(unit) &&
            !unit.IsQueuedForDeletion() &&
            unit.IsAlive &&
            unit.Team == UnitTeam.Friendly;
    }

    private bool IsCandidateDestinationValid(
        Vector3 position,
        float occupancyRadius,
        IReadOnlySet<ulong> commandedUnitIds,
        GodotObject target)
    {
        Vector2 horizontal = new(position.X, position.Z);
        if (!_playableBattlefieldBounds.Grow(-occupancyRadius)
                .HasPoint(horizontal) ||
            !NavigationPathing.IsClearOfStaticFootprints(
                GetTree(),
                position,
                occupancyRadius,
                target))
        {
            return false;
        }

        return !_unitOccupancySystem.IsPositionOccupied(
            position,
            occupancyRadius + Mathf.Max(MoveDestinationPadding, 0.0f),
            excludedUnitIds: commandedUnitIds);
    }

    private static HashSet<ulong> CreateUnitIdSet(
        IReadOnlyList<SelectableUnit> units)
    {
        HashSet<ulong> unitIds = new();
        foreach (SelectableUnit unit in units)
        {
            unitIds.Add(unit.GetInstanceId());
        }

        return unitIds;
    }

    private static List<int> CreateTopologyUnitOrder(
        IReadOnlyList<SelectableUnit> units)
    {
        List<int> unitOrder = new(units.Count);
        for (int index = 0; index < units.Count; index++)
        {
            unitOrder.Add(index);
        }

        unitOrder.Sort((first, second) =>
        {
            int xComparison = units[first].GlobalPosition.X.CompareTo(
                units[second].GlobalPosition.X);
            if (xComparison != 0)
            {
                return xComparison;
            }

            int zComparison = units[first].GlobalPosition.Z.CompareTo(
                units[second].GlobalPosition.Z);
            return zComparison != 0
                ? zComparison
                : units[first].GetInstanceId().CompareTo(
                    units[second].GetInstanceId());
        });
        return unitOrder;
    }

    private static bool TryReserveDestinationCandidate(
        IDictionary<Vector2I, List<Vector2>> occupiedCells,
        Vector3 candidate,
        float minimumSpacing)
    {
        Vector2 horizontal = new(candidate.X, candidate.Z);
        float effectiveSpacing = Mathf.Max(
            minimumSpacing - DestinationSpacingTolerance,
            0.01f);
        float spacingSquared = effectiveSpacing * effectiveSpacing;
        Vector2I cell = new(
            Mathf.FloorToInt(horizontal.X / effectiveSpacing),
            Mathf.FloorToInt(horizontal.Y / effectiveSpacing));
        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                if (!occupiedCells.TryGetValue(
                        cell + new Vector2I(x, z),
                        out List<Vector2> occupiedPositions))
                {
                    continue;
                }

                foreach (Vector2 occupied in occupiedPositions)
                {
                    if (horizontal.DistanceSquaredTo(occupied) < spacingSquared)
                    {
                        return false;
                    }
                }
            }
        }

        if (!occupiedCells.TryGetValue(cell, out List<Vector2> positions))
        {
            positions = new List<Vector2>();
            occupiedCells[cell] = positions;
        }

        positions.Add(horizontal);
        return true;
    }

    private static void RecordDestinationCandidate(
        IDictionary<Vector2I, List<Vector2>> occupiedCells,
        Vector3 candidate,
        float minimumSpacing)
    {
        Vector2 horizontal = new(candidate.X, candidate.Z);
        float effectiveSpacing = Mathf.Max(
            minimumSpacing - DestinationSpacingTolerance,
            0.01f);
        Vector2I cell = new(
            Mathf.FloorToInt(horizontal.X / effectiveSpacing),
            Mathf.FloorToInt(horizontal.Y / effectiveSpacing));
        if (!occupiedCells.TryGetValue(cell, out List<Vector2> positions))
        {
            positions = new List<Vector2>();
            occupiedCells[cell] = positions;
        }

        positions.Add(horizontal);
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
        if (!IsInstanceValid(unit) ||
            !unit.IsAlive ||
            unit.Team != UnitTeam.Friendly)
        {
            return;
        }

        ClearBuildingSelection();
        unit.SetSelected(true);
        _selectedUnits.Add(unit);
    }

    private void SelectBuilding(BuildingEntity building)
    {
        if (!IsInstanceValid(building) ||
            !building.IsAlive ||
            building.Team != UnitTeam.Friendly)
        {
            return;
        }

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
                !building.IsQueuedForDeletion() &&
                building.IsAlive)
            {
                yield return building;
            }
        }
    }

    public BuildingEntity GetHeadquarters(UnitTeam team)
    {
        BuildingEntity headquarters = team == UnitTeam.Friendly
            ? _friendlyHeadquarters
            : _enemyHeadquarters;
        return IsSupportedTeam(team) && IsHeadquartersAlive(headquarters)
            ? headquarters
            : null!;
    }

    public IReadOnlyList<SelectableUnit> GetLivingWorkers(UnitTeam team)
    {
        List<SelectableUnit> workers = new();
        if (!IsSupportedTeam(team))
        {
            return workers;
        }

        foreach (SelectableUnit unit in GetUnitsForTeam(team))
        {
            if (unit.HasWorkerEconomy)
            {
                workers.Add(unit);
            }
        }

        workers.Sort((first, second) =>
            first.GetInstanceId().CompareTo(second.GetInstanceId()));
        return workers;
    }

    public IReadOnlyList<SelectableUnit> GetLivingCombatUnits(UnitTeam team)
    {
        List<SelectableUnit> combatUnits = new();
        if (!IsSupportedTeam(team))
        {
            return combatUnits;
        }

        foreach (SelectableUnit unit in GetUnitsForTeam(team))
        {
            if (unit.CanAttack)
            {
                combatUnits.Add(unit);
            }
        }

        combatUnits.Sort((first, second) =>
            first.GetInstanceId().CompareTo(second.GetInstanceId()));
        return combatUnits;
    }

    public IReadOnlyList<BuildingEntity> GetCompletedCombatProductionBuildings(
        UnitTeam team)
    {
        List<BuildingEntity> buildings = new();
        if (!IsSupportedTeam(team))
        {
            return buildings;
        }

        foreach (BuildingEntity building in GetLivingBuildings())
        {
            if (building.Team == team &&
                building.IsComplete &&
                building.HasProduction &&
                building.Production.Definition.ProducedUnitDefinition.CanAttack)
            {
                buildings.Add(building);
            }
        }

        buildings.Sort((first, second) =>
            first.GetInstanceId().CompareTo(second.GetInstanceId()));
        return buildings;
    }

    public IReadOnlyList<BuildingEntity> GetCompletedResourceDropOffs(
        UnitTeam team)
    {
        List<BuildingEntity> buildings = new();
        if (!IsSupportedTeam(team))
        {
            return buildings;
        }

        foreach (BuildingEntity building in GetLivingBuildings())
        {
            if (building.Team == team && building.AcceptsMaterials)
            {
                buildings.Add(building);
            }
        }

        buildings.Sort((first, second) =>
            first.GetInstanceId().CompareTo(second.GetInstanceId()));
        return buildings;
    }

    public IReadOnlyList<BuildingEntity> GetActiveConstructionSites(UnitTeam team)
    {
        List<BuildingEntity> buildings = new();
        if (!IsSupportedTeam(team))
        {
            return buildings;
        }

        foreach (BuildingEntity building in GetLivingBuildings())
        {
            if (building.Team == team && !building.IsComplete)
            {
                buildings.Add(building);
            }
        }

        buildings.Sort((first, second) =>
            first.GetInstanceId().CompareTo(second.GetInstanceId()));
        return buildings;
    }

    public IReadOnlyList<MaterialsResourceNode> GetAvailableMaterialsNodes()
    {
        List<MaterialsResourceNode> resourceNodes = new();
        foreach (Node child in _resourceNodes.GetChildren())
        {
            if (child is MaterialsResourceNode resourceNode &&
                IsInstanceValid(resourceNode) &&
                !resourceNode.IsQueuedForDeletion() &&
                !resourceNode.IsDepleted)
            {
                resourceNodes.Add(resourceNode);
            }
        }

        resourceNodes.Sort((first, second) =>
            first.GetInstanceId().CompareTo(second.GetInstanceId()));
        return resourceNodes;
    }

    public int GetDepositedMaterials(UnitTeam team)
    {
        return IsSupportedTeam(team)
            ? _resourceLedger.GetMaterials(team)
            : 0;
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
