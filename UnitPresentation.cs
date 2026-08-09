using Godot;

public partial class UnitPresentation : Node3D
{
    private const float DamageFlashDuration = 0.12f;

    private SelectableUnit _unit = null!;
    private MeshInstance3D _selectionMarker = null!;
    private Material _baseMaterialOverride = null!;
    private StandardMaterial3D _damageFlashMaterial = null!;
    private float _damageFlashRemaining;
    private bool _hasSelectionMarker;

    public override void _Ready()
    {
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        _damageFlashRemaining = Mathf.Max(
            _damageFlashRemaining - (float)delta,
            0.0f);
        if (_damageFlashRemaining > 0.0f)
        {
            return;
        }

        _unit.MaterialOverride = _baseMaterialOverride;
        SetProcess(false);
    }

    public void Initialize(SelectableUnit unit, bool showSelectionMarker)
    {
        _unit = unit;
        _baseMaterialOverride = unit.MaterialOverride;
        _damageFlashMaterial = CreateDamageFlashMaterial();

        if (showSelectionMarker)
        {
            _selectionMarker = CreateSelectionMarker();
            AddChild(_selectionMarker);
            _hasSelectionMarker = true;
        }
    }

    public void SetSelected(bool selected)
    {
        if (_hasSelectionMarker)
        {
            _selectionMarker.Visible = selected;
        }
    }

    public void ShowDamageFlash()
    {
        _unit.MaterialOverride = _damageFlashMaterial;
        _damageFlashRemaining = DamageFlashDuration;
        SetProcess(true);
    }

    public void HideUnit()
    {
        _unit.Visible = false;
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

    private static StandardMaterial3D CreateDamageFlashMaterial()
    {
        return new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            Roughness = 1.0f,
        };
    }
}
