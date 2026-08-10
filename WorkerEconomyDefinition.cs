using Godot;

[GlobalClass]
public partial class WorkerEconomyDefinition : Resource
{
    [Export]
    public int CarryingCapacity { get; set; }

    [Export]
    public int GatherAmount { get; set; }

    [Export]
    public float GatherInterval { get; set; }

    [Export]
    public float InteractionRange { get; set; }
}
