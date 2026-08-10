using Godot;

[GlobalClass]
public partial class UnitDefinition : Resource
{
    public enum UnitRole
    {
        Combat,
        Worker,
    }

    [Export]
    public string DisplayName { get; set; } = "Unit";

    [Export]
    public UnitRole Role { get; set; } = UnitRole.Combat;

    [Export]
    public float MaxHealth { get; set; }

    [Export]
    public float MovementSpeed { get; set; }

    [Export]
    public float StoppingDistance { get; set; }

    [Export]
    public bool CanAttack { get; set; }

    [Export]
    public float AttackRange { get; set; }

    [Export]
    public float EngagementRange { get; set; }

    [Export]
    public float AttackDamage { get; set; }

    [Export]
    public float AttackCooldown { get; set; }

    [Export]
    public WorkerEconomyDefinition WorkerEconomy { get; set; } = null!;
}
