using Godot;

public partial class TeamResourceLedger : Node
{
    public static readonly StringName LedgerGroup = "team_resource_ledger";

    [Export]
    public int InitialMaterials { get; set; }

    private readonly int[] _materialsByTeam = new int[2];

    public override void _Ready()
    {
        AddToGroup(LedgerGroup);
        Reset();
    }

    public int GetMaterials(UnitTeam team)
    {
        return _materialsByTeam[(int)team];
    }

    public void Deposit(UnitTeam team, int amount)
    {
        if (amount > 0)
        {
            _materialsByTeam[(int)team] += amount;
        }
    }

    public void Reset()
    {
        int initialMaterials = Mathf.Max(InitialMaterials, 0);
        _materialsByTeam[(int)UnitTeam.Friendly] = initialMaterials;
        _materialsByTeam[(int)UnitTeam.Enemy] = initialMaterials;
    }
}
