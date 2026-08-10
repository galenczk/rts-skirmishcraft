using Godot;

public partial class MaterialsResourceNode : MeshInstance3D
{
    public static readonly StringName ResourceNodeGroup = "materials_resource_nodes";

    [Export]
    public MaterialsNodeDefinition Definition { get; set; } = null!;

    private StandardMaterial3D _depletedMaterial = null!;

    public int RemainingQuantity { get; private set; }
    public bool IsDepleted => RemainingQuantity <= 0;
    public float InteractionRadius => Mathf.Max(Definition.InteractionRadius, 0.0f);

    public override void _Ready()
    {
        if (Definition is null)
        {
            GD.PushWarning($"{Name} has no MaterialsNodeDefinition; using temporary defaults.");
            Definition = new MaterialsNodeDefinition();
        }

        if (Mesh is null)
        {
            Mesh = CreatePlaceholderMesh(Definition);
        }

        RemainingQuantity = Mathf.Max(Definition.StartingQuantity, 0);
        _depletedMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.28f, 0.28f, 0.25f, 1.0f),
            Roughness = 1.0f,
        };
        AddToGroup(ResourceNodeGroup);
        UpdateDepletedPresentation();
    }

    public int TakeMaterials(int requestedAmount)
    {
        if (requestedAmount <= 0 || IsDepleted)
        {
            return 0;
        }

        int gatheredAmount = Mathf.Min(requestedAmount, RemainingQuantity);
        RemainingQuantity -= gatheredAmount;
        UpdateDepletedPresentation();
        return gatheredAmount;
    }

    public Vector3 GetInteractionPosition(
        int slotIndex,
        int slotCount,
        float workerInteractionRange)
    {
        int effectiveSlotCount = Mathf.Max(slotCount, 1);
        int effectiveSlotIndex = Mathf.PosMod(slotIndex, effectiveSlotCount);
        float angle = Mathf.Tau * effectiveSlotIndex / effectiveSlotCount;
        float distance = InteractionRadius +
            Mathf.Max(workerInteractionRange, 0.0f) * 0.5f;
        return GlobalPosition + new Vector3(
            Mathf.Cos(angle) * distance,
            0.0f,
            Mathf.Sin(angle) * distance);
    }

    public static SphereMesh CreatePlaceholderMesh(MaterialsNodeDefinition definition)
    {
        Vector3 dimensions = definition.PlaceholderDimensions;
        float radius = Mathf.Max(dimensions.X, dimensions.Z) * 0.5f;
        StandardMaterial3D material = new()
        {
            AlbedoColor = new Color(0.95f, 0.66f, 0.08f, 1.0f),
            Metallic = 0.15f,
            Roughness = 0.65f,
        };
        return new SphereMesh
        {
            Radius = radius,
            Height = Mathf.Max(dimensions.Y, radius * 2.0f),
            RadialSegments = 12,
            Rings = 6,
            Material = material,
        };
    }

    private void UpdateDepletedPresentation()
    {
        if (!IsDepleted)
        {
            return;
        }

        MaterialOverride = _depletedMaterial;
        Scale = new Vector3(1.0f, 0.2f, 1.0f);
    }
}
