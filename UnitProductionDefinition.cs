using Godot;

[GlobalClass]
public partial class UnitProductionDefinition : Resource
{
    [Export]
    public UnitDefinition ProducedUnitDefinition { get; set; } = null!;

    [Export]
    public int UnitMaterialsCost { get; set; }

    [Export]
    public float ProductionTime { get; set; }

    [Export]
    public int MaximumQueueLength { get; set; }
}
