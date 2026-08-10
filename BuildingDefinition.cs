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
}
