using Godot;
using System.Collections.Generic;

public partial class UnitOccupancySystem : Node
{
    public static readonly StringName SystemGroup = "unit_occupancy_system";

    [Export(PropertyHint.Range, "0.5,10.0,0.5")]
    public float MaximumCorrectionSpeed { get; set; } = 4.0f;

    [Export(PropertyHint.Range, "0.0,0.2,0.01")]
    public float SeparationBuffer { get; set; } = 0.02f;

    [Export(PropertyHint.Range, "0.0,0.2,0.01")]
    public float OverlapTolerance { get; set; } = 0.04f;

    [Export(PropertyHint.Range, "1.0,5.0,0.25")]
    public float NeighborActivationDistance { get; set; } = 2.0f;

    [Export(PropertyHint.Range, "4,32,1")]
    public int MaximumLocalNeighbors { get; set; } = 8;

    private readonly Dictionary<ulong, SelectableUnit> _units = new();
    private readonly HashSet<ulong> _movingUnitIds = new();
    private readonly Dictionary<Vector2I, List<SelectableUnit>> _cells = new();
    private readonly Dictionary<ulong, Vector2I> _unitCells = new();
    private readonly HashSet<SelectableUnit> _avoidanceParticipants = new();
    private readonly HashSet<SelectableUnit> _nextAvoidanceParticipants = new();
    private readonly HashSet<UnitPair> _processedPairs = new();
    private readonly List<NeighborCandidate> _neighborCandidates = new();
    private readonly Dictionary<ulong, FallbackReservation> _fallbackReservations = new();
    private float _cellSize = 1.0f;

    private readonly record struct UnitPair(ulong First, ulong Second);

    private readonly record struct NeighborCandidate(
        SelectableUnit Unit,
        float DistanceSquared);

    private readonly record struct FallbackReservation(
        Vector3 Position,
        float Radius);

    public override void _Ready()
    {
        AddToGroup(SystemGroup);
        foreach (Node node in GetTree().GetNodesInGroup(
                     SelectableUnit.OccupancyGroup))
        {
            if (node is SelectableUnit unit)
            {
                Register(unit);
            }
        }

        SetPhysicsProcess(false);
    }

    public override void _PhysicsProcess(double delta)
    {
        PruneInvalidUnits();
        if (_movingUnitIds.Count == 0)
        {
            DisableUnusedAvoidanceParticipants();
            SetPhysicsProcess(false);
            return;
        }

        _nextAvoidanceParticipants.Clear();
        _processedPairs.Clear();
        foreach (ulong unitId in _movingUnitIds)
        {
            if (_units.TryGetValue(unitId, out SelectableUnit mover) &&
                IsActive(mover))
            {
                UpdateMembership(mover);
            }
        }

        foreach (ulong unitId in _movingUnitIds)
        {
            if (!_units.TryGetValue(unitId, out SelectableUnit mover) ||
                !IsActive(mover))
            {
                continue;
            }

            _nextAvoidanceParticipants.Add(mover);
            CollectNearestNeighbors(mover);
            foreach (NeighborCandidate candidate in _neighborCandidates)
            {
                SelectableUnit neighbor = candidate.Unit;
                _nextAvoidanceParticipants.Add(neighbor);
                UnitPair pair = CreatePair(mover, neighbor);
                if (_processedPairs.Add(pair))
                {
                    ResolveOverlap(mover, neighbor, (float)delta);
                }
            }
        }

        UpdateAvoidanceParticipation();
    }

    public void Register(SelectableUnit unit)
    {
        if (!IsInstanceValid(unit))
        {
            return;
        }

        ulong unitId = unit.GetInstanceId();
        if (_units.ContainsKey(unitId))
        {
            unit.AttachOccupancySystem(this);
            return;
        }

        _units[unitId] = unit;
        unit.AttachOccupancySystem(this);
        float requiredCellSize = Mathf.Max(
            _cellSize,
            unit.OccupancyRadius * 2.0f + Mathf.Max(SeparationBuffer, 0.0f));
        if (requiredCellSize > _cellSize + 0.001f)
        {
            _cellSize = requiredCellSize;
            RebuildSpatialIndex();
        }
        else
        {
            AddToCell(unit, GetCell(unit.GlobalPosition));
        }
    }

    public void Unregister(SelectableUnit unit)
    {
        if (unit is null)
        {
            return;
        }

        ulong unitId = unit.GetInstanceId();
        RemoveFromCell(unitId, unit);
        _movingUnitIds.Remove(unitId);
        _units.Remove(unitId);
        _fallbackReservations.Remove(unitId);
        _avoidanceParticipants.Remove(unit);
        _nextAvoidanceParticipants.Remove(unit);
    }

    public void SetMoving(SelectableUnit unit, bool moving)
    {
        Register(unit);
        ulong unitId = unit.GetInstanceId();
        if (moving)
        {
            _movingUnitIds.Add(unitId);
            unit.SetAvoidanceParticipation(true);
            SetPhysicsProcess(true);
        }
        else
        {
            _movingUnitIds.Remove(unitId);
            _fallbackReservations.Remove(unitId);
        }
    }

    public void UpdatePosition(SelectableUnit unit)
    {
        if (_units.ContainsKey(unit.GetInstanceId()))
        {
            UpdateMembership(unit);
        }
    }

    public bool IsPositionOccupied(
        Vector3 position,
        float occupancyRadius,
        SelectableUnit excludedUnit = null!,
        IReadOnlySet<ulong> excludedUnitIds = null!)
    {
        Vector2 horizontal = new(position.X, position.Z);
        int cellRange = Mathf.Max(
            Mathf.CeilToInt((occupancyRadius + _cellSize) / _cellSize),
            1);
        Vector2I centerCell = GetCell(position);
        for (int x = -cellRange; x <= cellRange; x++)
        {
            for (int z = -cellRange; z <= cellRange; z++)
            {
                if (!_cells.TryGetValue(
                        centerCell + new Vector2I(x, z),
                        out List<SelectableUnit> occupants))
                {
                    continue;
                }

                foreach (SelectableUnit unit in occupants)
                {
                    if (unit == excludedUnit ||
                        (excludedUnitIds is not null &&
                            excludedUnitIds.Contains(unit.GetInstanceId())) ||
                        !IsActive(unit))
                    {
                        continue;
                    }

                    Vector2 unitPosition = new(
                        unit.GlobalPosition.X,
                        unit.GlobalPosition.Z);
                    float requiredDistance = occupancyRadius +
                        unit.OccupancyRadius + Mathf.Max(SeparationBuffer, 0.0f);
                    requiredDistance = Mathf.Max(
                        requiredDistance - Mathf.Max(OverlapTolerance, 0.0f),
                        0.0f);
                    if (horizontal.DistanceSquaredTo(unitPosition) <
                        requiredDistance * requiredDistance)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public bool TryFindNearbyClearPosition(
        SelectableUnit unit,
        out Vector3 clearPosition)
    {
        const int directionsPerRing = 16;
        const int ringCount = 5;
        ulong unitId = unit.GetInstanceId();
        _fallbackReservations.Remove(unitId);
        float spacing = unit.OccupancyRadius * 2.0f +
            Mathf.Max(SeparationBuffer, 0.0f) + 0.1f;
        int angleOffset = (int)(unit.GetInstanceId() % directionsPerRing);
        for (int ring = 1; ring <= ringCount; ring++)
        {
            float radius = spacing * ring;
            for (int slot = 0; slot < directionsPerRing; slot++)
            {
                int directionIndex = (slot + angleOffset) % directionsPerRing;
                float angle = Mathf.Tau * directionIndex / directionsPerRing;
                Vector3 requested = unit.GlobalPosition + new Vector3(
                    Mathf.Cos(angle) * radius,
                    0.0f,
                    Mathf.Sin(angle) * radius);
                if (IsPositionOccupied(
                        requested,
                        unit.OccupancyRadius,
                        unit) ||
                    IsReservedFallbackPosition(
                        requested,
                        unit.OccupancyRadius) ||
                    !NavigationPathing.TryResolveReachablePoint(
                        unit,
                        requested,
                        unit.OccupancyRadius,
                        out clearPosition))
                {
                    continue;
                }

                _fallbackReservations[unitId] = new FallbackReservation(
                    clearPosition,
                    unit.OccupancyRadius);
                return true;
            }
        }

        clearPosition = unit.GlobalPosition;
        return false;
    }

    public void ReleaseFallbackReservation(SelectableUnit unit)
    {
        if (unit is not null)
        {
            _fallbackReservations.Remove(unit.GetInstanceId());
        }
    }

    private bool IsReservedFallbackPosition(
        Vector3 position,
        float radius)
    {
        foreach (FallbackReservation reservation in _fallbackReservations.Values)
        {
            float requiredDistance = radius + reservation.Radius +
                Mathf.Max(SeparationBuffer, 0.0f);
            if (HorizontalDistanceSquared(position, reservation.Position) <
                requiredDistance * requiredDistance)
            {
                return true;
            }
        }

        return false;
    }

    private void CollectNearestNeighbors(SelectableUnit mover)
    {
        _neighborCandidates.Clear();
        float activationDistance = Mathf.Max(
            NeighborActivationDistance,
            mover.OccupancyRadius * 2.0f);
        float activationDistanceSquared = activationDistance * activationDistance;
        int cellRange = Mathf.Max(
            Mathf.CeilToInt(activationDistance / _cellSize),
            1);
        Vector2I centerCell = GetCell(mover.GlobalPosition);
        for (int x = -cellRange; x <= cellRange; x++)
        {
            for (int z = -cellRange; z <= cellRange; z++)
            {
                if (!_cells.TryGetValue(
                        centerCell + new Vector2I(x, z),
                        out List<SelectableUnit> occupants))
                {
                    continue;
                }

                foreach (SelectableUnit unit in occupants)
                {
                    if (unit == mover || !IsActive(unit))
                    {
                        continue;
                    }

                    float distanceSquared = HorizontalDistanceSquared(
                        mover.GlobalPosition,
                        unit.GlobalPosition);
                    if (distanceSquared <= activationDistanceSquared)
                    {
                        _neighborCandidates.Add(new NeighborCandidate(
                            unit,
                            distanceSquared));
                    }
                }
            }
        }

        _neighborCandidates.Sort((first, second) =>
        {
            int distanceComparison = first.DistanceSquared.CompareTo(
                second.DistanceSquared);
            return distanceComparison != 0
                ? distanceComparison
                : first.Unit.GetInstanceId().CompareTo(
                    second.Unit.GetInstanceId());
        });
        int maximumNeighbors = Mathf.Max(MaximumLocalNeighbors, 1);
        if (_neighborCandidates.Count > maximumNeighbors)
        {
            _neighborCandidates.RemoveRange(
                maximumNeighbors,
                _neighborCandidates.Count - maximumNeighbors);
        }
    }

    private void ResolveOverlap(
        SelectableUnit first,
        SelectableUnit second,
        float delta)
    {
        Vector2 firstPosition = new(first.GlobalPosition.X, first.GlobalPosition.Z);
        Vector2 secondPosition = new(second.GlobalPosition.X, second.GlobalPosition.Z);
        Vector2 separation = firstPosition - secondPosition;
        float requiredDistance = first.OccupancyRadius + second.OccupancyRadius +
            Mathf.Max(SeparationBuffer, 0.0f);
        float distanceSquared = separation.LengthSquared();
        float correctionThreshold = Mathf.Max(
            requiredDistance - Mathf.Max(OverlapTolerance, 0.0f),
            0.0f);
        if (distanceSquared >= correctionThreshold * correctionThreshold)
        {
            return;
        }

        float distance = Mathf.Sqrt(distanceSquared);
        Vector2 direction = distance > 0.0001f
            ? separation / distance
            : GetDeterministicDirection(first, second);
        float correctionDistance = Mathf.Min(
            requiredDistance - distance,
            Mathf.Max(MaximumCorrectionSpeed, 0.0f) * delta);
        bool secondMoving = _movingUnitIds.Contains(second.GetInstanceId());
        // Arrived units yield only when a real overlap exists, and only enough
        // to let the active mover escape a compressed pocket.
        float firstShare = secondMoving ? 0.5f : 0.8f;
        float secondShare = secondMoving ? 0.5f : 0.2f;

        if (TryApplyCorrection(first, direction * correctionDistance * firstShare))
        {
            UpdateMembership(first);
        }

        if (secondShare > 0.0f &&
            TryApplyCorrection(second, -direction * correctionDistance * secondShare))
        {
            UpdateMembership(second);
        }
    }

    private bool TryApplyCorrection(SelectableUnit unit, Vector2 correction)
    {
        if (correction.IsZeroApprox())
        {
            return false;
        }

        Vector3 current = unit.GlobalPosition;
        Vector3 candidate = new(
            current.X + correction.X,
            current.Y,
            current.Z + correction.Y);
        Rid navigationMap = unit.GetWorld3D().NavigationMap;
        if (NavigationServer3D.MapGetIterationId(navigationMap) != 0)
        {
            Vector3 projected = NavigationServer3D.MapGetClosestPoint(
                navigationMap,
                candidate);
            if (HorizontalDistanceSquared(candidate, projected) >
                unit.OccupancyRadius * unit.OccupancyRadius)
            {
                return false;
            }

            candidate.X = projected.X;
            candidate.Z = projected.Z;
        }

        if (!NavigationPathing.IsClearOfStaticFootprints(
                GetTree(),
                candidate,
                unit.OccupancyRadius))
        {
            return false;
        }

        unit.GlobalPosition = candidate;
        return true;
    }

    private void UpdateAvoidanceParticipation()
    {
        foreach (SelectableUnit previous in _avoidanceParticipants)
        {
            if (!_nextAvoidanceParticipants.Contains(previous) &&
                IsInstanceValid(previous))
            {
                previous.SetAvoidanceParticipation(false);
            }
        }

        foreach (SelectableUnit current in _nextAvoidanceParticipants)
        {
            if (IsInstanceValid(current))
            {
                current.SetAvoidanceParticipation(true);
            }
        }

        _avoidanceParticipants.Clear();
        _avoidanceParticipants.UnionWith(_nextAvoidanceParticipants);
    }

    private void DisableUnusedAvoidanceParticipants()
    {
        foreach (SelectableUnit unit in _avoidanceParticipants)
        {
            if (IsInstanceValid(unit))
            {
                unit.SetAvoidanceParticipation(false);
            }
        }

        _avoidanceParticipants.Clear();
        _nextAvoidanceParticipants.Clear();
    }

    private void UpdateMembership(SelectableUnit unit)
    {
        ulong unitId = unit.GetInstanceId();
        Vector2I newCell = GetCell(unit.GlobalPosition);
        if (_unitCells.TryGetValue(unitId, out Vector2I oldCell) &&
            oldCell == newCell)
        {
            return;
        }

        RemoveFromCell(unitId, unit);
        AddToCell(unit, newCell);
    }

    private void AddToCell(SelectableUnit unit, Vector2I cell)
    {
        ulong unitId = unit.GetInstanceId();
        if (!_cells.TryGetValue(cell, out List<SelectableUnit> occupants))
        {
            occupants = new List<SelectableUnit>();
            _cells[cell] = occupants;
        }

        occupants.Add(unit);
        _unitCells[unitId] = cell;
    }

    private void RemoveFromCell(ulong unitId, SelectableUnit unit)
    {
        if (!_unitCells.TryGetValue(unitId, out Vector2I cell) ||
            !_cells.TryGetValue(cell, out List<SelectableUnit> occupants))
        {
            _unitCells.Remove(unitId);
            return;
        }

        occupants.Remove(unit);
        if (occupants.Count == 0)
        {
            _cells.Remove(cell);
        }

        _unitCells.Remove(unitId);
    }

    private void RebuildSpatialIndex()
    {
        _cells.Clear();
        _unitCells.Clear();
        foreach (SelectableUnit unit in _units.Values)
        {
            if (IsActive(unit))
            {
                AddToCell(unit, GetCell(unit.GlobalPosition));
            }
        }
    }

    private void PruneInvalidUnits()
    {
        List<ulong> invalidIds = null!;
        foreach ((ulong unitId, SelectableUnit unit) in _units)
        {
            if (!IsActive(unit))
            {
                invalidIds ??= new List<ulong>();
                invalidIds.Add(unitId);
            }
        }

        if (invalidIds is null)
        {
            return;
        }

        foreach (ulong unitId in invalidIds)
        {
            if (_units.TryGetValue(unitId, out SelectableUnit unit))
            {
                Unregister(unit);
            }
        }
    }

    private Vector2I GetCell(Vector3 position)
    {
        return new Vector2I(
            Mathf.FloorToInt(position.X / _cellSize),
            Mathf.FloorToInt(position.Z / _cellSize));
    }

    private static bool IsActive(SelectableUnit unit)
    {
        return IsInstanceValid(unit) &&
            !unit.IsQueuedForDeletion() &&
            unit.IsOccupancyActive;
    }

    private static UnitPair CreatePair(
        SelectableUnit first,
        SelectableUnit second)
    {
        ulong firstId = first.GetInstanceId();
        ulong secondId = second.GetInstanceId();
        return firstId < secondId
            ? new UnitPair(firstId, secondId)
            : new UnitPair(secondId, firstId);
    }

    private static Vector2 GetDeterministicDirection(
        SelectableUnit first,
        SelectableUnit second)
    {
        ulong hash = first.GetInstanceId() * 11400714819323198485ul ^
            second.GetInstanceId();
        float angle = Mathf.Tau * (hash % 4096ul) / 4096.0f;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }

    private static float HorizontalDistanceSquared(
        Vector3 first,
        Vector3 second)
    {
        Vector2 delta = new(first.X - second.X, first.Z - second.Z);
        return delta.LengthSquared();
    }
}
