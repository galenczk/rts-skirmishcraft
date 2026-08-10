using Godot;

public partial class BuildingProduction : Node
{
    private BuildingEntity _building = null!;
    private UnitProductionDefinition _definition = null!;
    private MeshInstance3D _rallyMarker = null!;
    private float _productionElapsed;
    private int _queueCount;
    private bool _hasCompletedUnitWaiting;
    private bool _isStopped;

    public UnitProductionDefinition Definition => _definition;
    public int QueueCount => _queueCount;
    public bool HasCompletedUnitWaiting => _hasCompletedUnitWaiting;
    public bool HasRallyPoint { get; private set; }
    public Vector3 RallyPoint { get; private set; }
    public float ProductionProgress
    {
        get
        {
            if (_queueCount <= 0)
            {
                return 0.0f;
            }

            if (_hasCompletedUnitWaiting)
            {
                return 1.0f;
            }

            return Mathf.Clamp(
                _productionElapsed / Mathf.Max(_definition.ProductionTime, 0.01f),
                0.0f,
                1.0f);
        }
    }

    public override void _Ready()
    {
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (_isStopped ||
            !_building.IsAlive ||
            !_building.IsComplete ||
            _queueCount <= 0 ||
            _hasCompletedUnitWaiting)
        {
            return;
        }

        _productionElapsed += (float)delta;
        if (_productionElapsed >= Mathf.Max(_definition.ProductionTime, 0.01f))
        {
            _productionElapsed = Mathf.Max(_definition.ProductionTime, 0.01f);
            _hasCompletedUnitWaiting = true;
        }
    }

    public void Initialize(
        BuildingEntity building,
        UnitProductionDefinition definition)
    {
        _building = building;
        _definition = definition;
        SetProcess(true);
    }

    public bool TryQueueUnit(TeamResourceLedger ledger)
    {
        if (_isStopped ||
            !_building.IsAlive ||
            !_building.IsComplete ||
            _queueCount >= Mathf.Max(_definition.MaximumQueueLength, 1))
        {
            return false;
        }

        int materialsCost = Mathf.Max(_definition.UnitMaterialsCost, 0);
        if (!ledger.TrySpend(_building.Team, materialsCost))
        {
            return false;
        }

        _queueCount++;
        return true;
    }

    public bool CancelMostRecentUnit(TeamResourceLedger ledger)
    {
        if (_isStopped ||
            !_building.IsAlive ||
            !_building.IsComplete ||
            _queueCount <= 0)
        {
            return false;
        }

        _queueCount--;
        ledger.Deposit(
            _building.Team,
            Mathf.Max(_definition.UnitMaterialsCost, 0));
        if (_queueCount == 0)
        {
            _productionElapsed = 0.0f;
            _hasCompletedUnitWaiting = false;
        }

        return true;
    }

    public void AcknowledgeSpawn()
    {
        if (!_hasCompletedUnitWaiting || _queueCount <= 0)
        {
            return;
        }

        _queueCount--;
        _productionElapsed = 0.0f;
        _hasCompletedUnitWaiting = false;
    }

    public void SetRallyPoint(Vector3 worldPosition)
    {
        if (_isStopped || !_building.IsAlive || !_building.IsComplete)
        {
            return;
        }

        RallyPoint = worldPosition;
        HasRallyPoint = true;
        if (!IsInstanceValid(_rallyMarker))
        {
            _rallyMarker = CreateRallyMarker();
            _building.AddChild(_rallyMarker);
        }

        _rallyMarker.GlobalPosition = new Vector3(
            worldPosition.X,
            worldPosition.Y + 0.05f,
            worldPosition.Z);
        _rallyMarker.Visible = true;
    }

    public void Stop()
    {
        _isStopped = true;
        _queueCount = 0;
        _productionElapsed = 0.0f;
        _hasCompletedUnitWaiting = false;
        HasRallyPoint = false;
        if (IsInstanceValid(_rallyMarker))
        {
            _rallyMarker.QueueFree();
        }

        _rallyMarker = null!;
        SetProcess(false);
    }

    private static MeshInstance3D CreateRallyMarker()
    {
        StandardMaterial3D material = BuildingEntity.CreateMaterial(
            new Color(1.0f, 0.78f, 0.12f, 0.9f),
            translucent: true);
        return new MeshInstance3D
        {
            Name = "RallyMarker",
            TopLevel = true,
            Mesh = new CylinderMesh
            {
                TopRadius = 0.45f,
                BottomRadius = 0.45f,
                Height = 0.05f,
                RadialSegments = 24,
                Material = material,
            },
        };
    }
}
