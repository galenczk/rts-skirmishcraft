using Godot;

[GlobalClass]
public partial class BuildingDefinition : Resource
{
    [Export]
    public string DisplayName { get; set; } = "Building";

    [Export]
    public float MaxHealth { get; set; }

    [Export]
    public Vector3 PlaceholderDimensions { get; set; } = Vector3.One;

    public Vector2 FootprintHalfExtents => new(
        Mathf.Max(PlaceholderDimensions.X * 0.5f, 0.0f),
        Mathf.Max(PlaceholderDimensions.Z * 0.5f, 0.0f));

    public float FootprintRadius => FootprintHalfExtents.Length();

    [Export]
    public bool AcceptsMaterials { get; set; }

    [Export]
    public bool IsHeadquarters { get; set; }

    [Export]
    public int MaterialsCost { get; set; }

    [Export]
    public float ConstructionTime { get; set; }

    [Export]
    public UnitProductionDefinition Production { get; set; } = null!;
}
