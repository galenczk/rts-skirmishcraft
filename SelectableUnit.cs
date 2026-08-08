using Godot;

public partial class SelectableUnit : MeshInstance3D
{
    public static readonly StringName FriendlySelectionGroup = "friendly_selectable_units";

    private MeshInstance3D _selectionMarker = null!;

    public bool IsSelected { get; private set; }

    public override void _Ready()
    {
        AddToGroup(FriendlySelectionGroup);
        _selectionMarker = CreateSelectionMarker();
        AddChild(_selectionMarker);
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        _selectionMarker.Visible = selected;
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
