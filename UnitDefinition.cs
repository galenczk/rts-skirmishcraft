using Godot;

[GlobalClass]
public partial class UnitDefinition : Resource
{
    [Export]
    public float MaxHealth { get; set; } = 100.0f;

    [Export]
    public float MovementSpeed { get; set; } = 4.0f;

    [Export]
    public float StoppingDistance { get; set; } = 0.3f;

    [Export]
    public float AttackRange { get; set; } = 2.5f;

    [Export]
    public float EngagementRange { get; set; } = 4.0f;

    [Export]
    public float AttackDamage { get; set; } = 20.0f;

    [Export]
    public float AttackCooldown { get; set; } = 1.0f;
}
