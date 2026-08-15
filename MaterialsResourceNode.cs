using Godot;
using System;

public partial class MaterialsResourceNode : MeshInstance3D
{
    public static readonly StringName ResourceNodeGroup = "materials_resource_nodes";

    [Export]
    public MaterialsNodeDefinition Definition { get; set; } = null!;

    private StandardMaterial3D _depletedMaterial = null!;
    private NavigationObstacle3D _navigationObstacle = null!;

    public event Action<MaterialsResourceNode> Depleted;

    public int RemainingQuantity { get; private set; }
    public bool IsDepleted => RemainingQuantity <= 0;
    public float InteractionRadius => Mathf.Max(Definition.FootprintRadius, 0.0f);

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
        _navigationObstacle = CreateNavigationObstacle();
        AddChild(_navigationObstacle);
        _navigationObstacle.AddToGroup(NavigationPathing.NavigationSourceGroup);
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
        if (IsDepleted)
        {
            DisableNavigationInfluence();
            Depleted?.Invoke(this);
        }

        return gatheredAmount;
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

    private NavigationObstacle3D CreateNavigationObstacle()
    {
        const int segments = 12;
        float radius = Mathf.Max(InteractionRadius, 0.1f);
        Vector3[] vertices = new Vector3[segments];
        for (int index = 0; index < segments; index++)
        {
            float angle = -Mathf.Pi * 0.5f +
                Mathf.Tau * index / segments;
            vertices[index] = new Vector3(
                Mathf.Cos(angle) * radius,
                0.0f,
                Mathf.Sin(angle) * radius);
        }

        float height = Mathf.Max(Definition.PlaceholderDimensions.Y, 0.1f);
        return new NavigationObstacle3D
        {
            Name = "NavigationObstacle3D",
            Position = new Vector3(0.0f, -height * 0.5f, 0.0f),
            Vertices = vertices,
            Height = height,
            AffectNavigationMesh = true,
            CarveNavigationMesh = false,
            AvoidanceEnabled = true,
        };
    }

    private void DisableNavigationInfluence()
    {
        if (!IsInstanceValid(_navigationObstacle))
        {
            return;
        }

        _navigationObstacle.AffectNavigationMesh = false;
        _navigationObstacle.AvoidanceEnabled = false;
        _navigationObstacle.RemoveFromGroup(
            NavigationPathing.NavigationSourceGroup);
    }
}
