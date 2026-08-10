using Godot;
using System;

public partial class BuildingEntity : MeshInstance3D, ICombatTarget
{
    private static readonly StringName NavigationSourceGroup = "navigation_source";
    private const float DamageFlashDuration = 0.12f;

    [Export]
    public UnitTeam Team { get; set; } = UnitTeam.Friendly;

    [Export]
    public BuildingDefinition Definition { get; set; } = null!;

    public event Action<BuildingEntity> Destroyed;

    private MeshInstance3D _selectionMarker = null!;
    private NavigationObstacle3D _navigationObstacle = null!;
    private Material _baseMaterialOverride = null!;
    private StandardMaterial3D _damageFlashMaterial = null!;
    private float _damageFlashRemaining;
    private bool _isGameplayStopped;

    public float Health { get; private set; }
    public bool IsAlive { get; private set; } = true;
    public bool IsSelected { get; private set; }
    public Vector3 TargetPosition => GlobalPosition;
    public float TargetRadius => Mathf.Max(Definition.FootprintRadius, 0.0f);

    public override void _Ready()
    {
        if (Definition is null)
        {
            GD.PushWarning($"{Name} has no BuildingDefinition; using temporary defaults.");
            Definition = new BuildingDefinition();
        }

        if (Mesh is null)
        {
            Mesh = CreatePlaceholderMesh(Definition, Team, translucent: false);
        }

        Health = Mathf.Max(Definition.MaxHealth, 1.0f);
        AddToGroup(CombatTargetGroups.ForTeam(Team));
        _baseMaterialOverride = MaterialOverride;
        _damageFlashMaterial = CreateMaterial(Colors.White, translucent: false);
        _selectionMarker = CreateSelectionMarker();
        AddChild(_selectionMarker);
        _navigationObstacle = CreateNavigationObstacle();
        AddChild(_navigationObstacle);
        _navigationObstacle.AddToGroup(NavigationSourceGroup);
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        _damageFlashRemaining = Mathf.Max(
            _damageFlashRemaining - (float)delta,
            0.0f);
        if (_damageFlashRemaining > 0.0f)
        {
            return;
        }

        MaterialOverride = _baseMaterialOverride;
        SetProcess(false);
    }

    public void SetSelected(bool selected)
    {
        if (Team != UnitTeam.Friendly || !IsAlive || _isGameplayStopped)
        {
            return;
        }

        IsSelected = selected;
        _selectionMarker.Visible = selected;
    }

    public void TakeDamage(float damage)
    {
        if (!IsAlive || _isGameplayStopped || damage <= 0.0f)
        {
            return;
        }

        Health = Mathf.Max(Health - damage, 0.0f);
        MaterialOverride = _damageFlashMaterial;
        _damageFlashRemaining = DamageFlashDuration;
        SetProcess(true);

        if (Health <= 0.0f)
        {
            Die();
        }
    }

    public void StopGameplay()
    {
        if (!IsAlive || _isGameplayStopped)
        {
            return;
        }

        _isGameplayStopped = true;
        IsSelected = false;
        _selectionMarker.Visible = false;
    }

    public Rect2 GetFootprintRect(Vector3? positionOverride = null)
    {
        Vector3 position = positionOverride ?? GlobalPosition;
        Vector2 halfSize = new(
            Mathf.Max(Definition.PlaceholderDimensions.X * 0.5f, 0.0f),
            Mathf.Max(Definition.PlaceholderDimensions.Z * 0.5f, 0.0f));
        return new Rect2(
            new Vector2(position.X, position.Z) - halfSize,
            halfSize * 2.0f);
    }

    public static BoxMesh CreatePlaceholderMesh(
        BuildingDefinition definition,
        UnitTeam team,
        bool translucent)
    {
        Color color = team == UnitTeam.Friendly
            ? new Color(0.08f, 0.3f, 0.72f, translucent ? 0.48f : 1.0f)
            : new Color(0.68f, 0.08f, 0.06f, translucent ? 0.48f : 1.0f);
        return new BoxMesh
        {
            Size = definition.PlaceholderDimensions,
            Material = CreateMaterial(color, translucent),
        };
    }

    public static StandardMaterial3D CreateMaterial(Color color, bool translucent)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = 0.85f,
            Transparency = translucent
                ? BaseMaterial3D.TransparencyEnum.Alpha
                : BaseMaterial3D.TransparencyEnum.Disabled,
        };
    }

    private MeshInstance3D CreateSelectionMarker()
    {
        CylinderMesh markerMesh = new()
        {
            TopRadius = TargetRadius + 0.2f,
            BottomRadius = TargetRadius + 0.2f,
            Height = 0.05f,
            RadialSegments = 32,
            Material = CreateMaterial(
                new Color(1.0f, 0.82f, 0.08f, 1.0f),
                translucent: false),
        };
        return new MeshInstance3D
        {
            Name = "SelectionMarker",
            Position = new Vector3(
                0.0f,
                GetAabb().Position.Y + 0.04f,
                0.0f),
            Mesh = markerMesh,
            Visible = false,
        };
    }

    private NavigationObstacle3D CreateNavigationObstacle()
    {
        Vector3 halfDimensions = Definition.PlaceholderDimensions * 0.5f;
        return new NavigationObstacle3D
        {
            Name = "NavigationObstacle3D",
            Position = new Vector3(0.0f, -halfDimensions.Y, 0.0f),
            Vertices = new Vector3[]
            {
                new(-halfDimensions.X, 0.0f, -halfDimensions.Z),
                new(-halfDimensions.X, 0.0f, halfDimensions.Z),
                new(halfDimensions.X, 0.0f, halfDimensions.Z),
                new(halfDimensions.X, 0.0f, -halfDimensions.Z),
            },
            Height = Mathf.Max(Definition.PlaceholderDimensions.Y, 0.1f),
            AffectNavigationMesh = true,
            CarveNavigationMesh = true,
            AvoidanceEnabled = false,
        };
    }

    private void Die()
    {
        IsAlive = false;
        IsSelected = false;
        _selectionMarker.Visible = false;
        Visible = false;
        RemoveFromGroup(CombatTargetGroups.ForTeam(Team));
        _navigationObstacle.AffectNavigationMesh = false;
        Destroyed?.Invoke(this);
        QueueFree();
    }
}
