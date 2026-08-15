using Godot;

[GlobalClass]
public partial class MaterialsNodeDefinition : Resource
{
    [Export]
    public int StartingQuantity { get; set; }

    [Export]
    public Vector3 PlaceholderDimensions { get; set; } = Vector3.One;

    public float FootprintRadius => Mathf.Max(
        PlaceholderDimensions.X,
        PlaceholderDimensions.Z) * 0.5f;
}
