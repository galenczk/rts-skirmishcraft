using Godot;

[GlobalClass]
public partial class MaterialsNodeDefinition : Resource
{
    [Export]
    public int StartingQuantity { get; set; }

    [Export]
    public float InteractionRadius { get; set; }

    [Export]
    public Vector3 PlaceholderDimensions { get; set; } = Vector3.One;
}
