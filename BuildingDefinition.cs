using Godot;

[GlobalClass]
public partial class BuildingDefinition : Resource
{
    [Export]
    public string DisplayName { get; set; } = "Building";

    [Export]
    public float MaxHealth { get; set; }

    [Export]
    public float FootprintRadius { get; set; }

    [Export]
    public Vector3 PlaceholderDimensions { get; set; } = Vector3.One;

    [Export]
    public bool AcceptsMaterials { get; set; }

    [Export]
    public int MaterialsCost { get; set; }

    [Export]
    public float ConstructionTime { get; set; }
}
