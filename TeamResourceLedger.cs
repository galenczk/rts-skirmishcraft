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

    public bool CanAfford(UnitTeam team, int amount)
    {
        return amount >= 0 && _materialsByTeam[(int)team] >= amount;
    }

    public bool TrySpend(UnitTeam team, int amount)
    {
        if (!CanAfford(team, amount))
        {
            return false;
        }

        _materialsByTeam[(int)team] -= amount;
        return true;
    }

    public void Reset()
    {
        ResetTeam(UnitTeam.Friendly);
        ResetTeam(UnitTeam.Enemy);
    }

    public void ResetTeam(UnitTeam team)
    {
        _materialsByTeam[(int)team] = Mathf.Max(InitialMaterials, 0);
    }
}
