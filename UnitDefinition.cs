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

    [Export(PropertyHint.Range, "0.1,2.0,0.05")]
    public float OccupancyRadius { get; set; } = 0.45f;

    [Export(PropertyHint.Range, "0.25,5.0,0.05")]
    public float StuckCheckInterval { get; set; } = 1.5f;

    [Export(PropertyHint.Range, "0.01,1.0,0.01")]
    public float StuckProgressThreshold { get; set; } = 0.2f;

    [Export(PropertyHint.Range, "0,5,1")]
    public int StuckRepathLimit { get; set; } = 2;

    [Export(PropertyHint.Range, "0.1,3.0,0.05")]
    public float MovingTargetRefreshDistance { get; set; } = 0.75f;

    [Export(PropertyHint.Range, "1.0,4.0,0.25")]
    public float NavigationTimeAllowanceMultiplier { get; set; } = 2.0f;

    [Export(PropertyHint.Range, "1.0,15.0,0.5")]
    public float CongestionGracePeriod { get; set; } = 5.0f;

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
