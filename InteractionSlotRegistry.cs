using Godot;
using System.Collections.Generic;

public static class InteractionSlotRegistry
{
    public enum InteractionKind
    {
        Attack,
        Resource,
        DropOff,
        Construction,
    }

    private readonly record struct SlotKey(
        ulong TargetId,
        InteractionKind Kind);

    private readonly record struct UnitClaimKey(
        ulong UnitId,
        InteractionKind Kind);

    private sealed class SlotSet
    {
        private readonly HashSet<ulong> _unitIds = new();
        private readonly Dictionary<ulong, int> _ranks = new();
        private readonly bool _compactRanks;
        private bool _isDirty = true;

        public SlotSet(bool compactRanks)
        {
            _compactRanks = compactRanks;
        }

        public int Count => _unitIds.Count;

        public void Add(ulong unitId)
        {
            if (_unitIds.Add(unitId))
            {
                if (_compactRanks)
                {
                    _isDirty = true;
                }
                else
                {
                    HashSet<int> occupiedRanks = new(_ranks.Values);
                    int rank = 0;
                    while (occupiedRanks.Contains(rank))
                    {
                        rank++;
                    }

                    _ranks[unitId] = rank;
                }
            }
        }

        public void Remove(ulong unitId)
        {
            if (_unitIds.Remove(unitId))
            {
                _ranks.Remove(unitId);
                if (_compactRanks)
                {
                    _isDirty = true;
                }
            }
        }

        public int GetRank(ulong unitId)
        {
            if (_compactRanks && _isDirty)
            {
                List<ulong> sortedIds = new(_unitIds);
                sortedIds.Sort();
                _ranks.Clear();
                for (int index = 0; index < sortedIds.Count; index++)
                {
                    _ranks[sortedIds[index]] = index;
                }

                _isDirty = false;
            }

            return _ranks.TryGetValue(unitId, out int rank) ? rank : 0;
        }
    }

    private static readonly Dictionary<SlotKey, SlotSet> Slots = new();
    private static readonly Dictionary<UnitClaimKey, SlotKey> Claims = new();

    public static int Reserve(
        SelectableUnit unit,
        GodotObject target,
        InteractionKind kind)
    {
        ulong unitId = unit.GetInstanceId();
        UnitClaimKey claimKey = new(unitId, kind);
        SlotKey requestedKey = new(target.GetInstanceId(), kind);
        if (Claims.TryGetValue(claimKey, out SlotKey existingKey) &&
            existingKey != requestedKey)
        {
            ReleaseClaim(claimKey, existingKey);
        }

        if (!Slots.TryGetValue(requestedKey, out SlotSet slotSet))
        {
            slotSet = new SlotSet(
                kind == InteractionKind.Attack);
            Slots[requestedKey] = slotSet;
        }

        slotSet.Add(unitId);
        Claims[claimKey] = requestedKey;
        return slotSet.GetRank(unitId);
    }

    public static void Release(
        SelectableUnit unit,
        InteractionKind kind)
    {
        UnitClaimKey claimKey = new(unit.GetInstanceId(), kind);
        if (Claims.TryGetValue(claimKey, out SlotKey slotKey))
        {
            ReleaseClaim(claimKey, slotKey);
        }
    }

    public static void ReleaseAll(SelectableUnit unit)
    {
        ulong unitId = unit.GetInstanceId();
        foreach (InteractionKind kind in System.Enum.GetValues<InteractionKind>())
        {
            UnitClaimKey claimKey = new(unitId, kind);
            if (Claims.TryGetValue(claimKey, out SlotKey slotKey))
            {
                ReleaseClaim(claimKey, slotKey);
            }
        }
    }

    public static void Clear()
    {
        Slots.Clear();
        Claims.Clear();
    }

    private static void ReleaseClaim(UnitClaimKey claimKey, SlotKey slotKey)
    {
        Claims.Remove(claimKey);
        if (!Slots.TryGetValue(slotKey, out SlotSet slotSet))
        {
            return;
        }

        slotSet.Remove(claimKey.UnitId);
        if (slotSet.Count == 0)
        {
            Slots.Remove(slotKey);
        }
    }
}
