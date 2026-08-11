using Godot;
using System.Collections.Generic;

public partial class EnemyMacroController : Node
{
    public enum MacroState
    {
        Inactive,
        EstablishingEconomy,
        SavingForProduction,
        ConstructingProduction,
        Producing,
        AssemblingWave,
        Attacking,
        Recovering,
        Stalled,
    }

    [Export(PropertyHint.Range, "0.1,5.0,0.05")]
    public float DecisionInterval { get; set; } = 0.75f;

    [Export(PropertyHint.Range, "1,12,1")]
    public int WaveSize { get; set; } = 4;

    [Export(PropertyHint.Range, "0,12,1")]
    public int WorkerTarget { get; set; } = 3;

    [Export(PropertyHint.Range, "1.0,12.0,0.5")]
    public float RallyDistance { get; set; } = 5.0f;

    [Export(PropertyHint.Range, "1.0,20.0,0.5")]
    public float ObstructionTargetRange { get; set; } = 7.0f;

    [Export(PropertyHint.Range, "0.5,10.0,0.5")]
    public float ConstructionRetryDelay { get; set; } = 2.0f;

    private static readonly Vector2[] ConstructionOffsets =
    {
        new(-8.0f, 4.5f),
        new(8.0f, 4.5f),
        new(-10.0f, 7.5f),
        new(10.0f, 7.5f),
        new(0.0f, 8.0f),
    };

    private readonly List<SelectableUnit> _activeWave = new();
    private readonly HashSet<ulong> _launchedUnitIds = new();
    private SkirmishSandbox _sandbox = null!;
    private BuildingDefinition _productionDefinition = null!;
    private BuildingEntity _productionBuilding = null!;
    private BuildingEntity _constructionSite = null!;
    private BuildingEntity _rallyConfiguredBuilding = null!;
    private SelectableUnit _builder = null!;
    private double _decisionElapsed;
    private float _constructionRetryRemaining;
    private int _nextConstructionCandidate;
    private int _assemblingCount;
    private bool _active;
    private bool _hasEstablishedProduction;

    public bool IsActive => _active;
    public MacroState State { get; private set; } = MacroState.Inactive;

    public override void _Ready()
    {
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (!_active)
        {
            return;
        }

        _constructionRetryRemaining = Mathf.Max(
            _constructionRetryRemaining - (float)delta,
            0.0f);
        _decisionElapsed += delta;
        if (_decisionElapsed < Mathf.Max(DecisionInterval, 0.1f))
        {
            return;
        }

        _decisionElapsed = 0.0;
        MakeDecision();
    }

    public void Initialize(
        SkirmishSandbox sandbox,
        BuildingDefinition productionDefinition)
    {
        _sandbox = sandbox;
        _productionDefinition = productionDefinition;
        SetProcess(false);
    }

    public void Activate()
    {
        ClearTrackedState();
        _active = true;
        State = MacroState.EstablishingEconomy;
        SetProcess(true);
    }

    public void Deactivate()
    {
        _active = false;
        State = MacroState.Inactive;
        ClearTrackedState();
        SetProcess(false);
    }

    public string GetDebugSummary()
    {
        if (!_active)
        {
            return "Red macro: inactive";
        }

        int workers = _sandbox.GetLivingWorkers(UnitTeam.Enemy).Count;
        BuildingEntity headquarters = _sandbox.GetHeadquarters(UnitTeam.Enemy);
        int queuedWorkers = GetQueuedWorkers(headquarters);
        int materials = _sandbox.GetDepositedMaterials(UnitTeam.Enemy);
        string productionStatus = "none";
        int queuedUnits = 0;
        if (IsValidBuilding(_productionBuilding) &&
            _productionBuilding.IsComplete)
        {
            BuildingProduction production = _productionBuilding.Production;
            queuedUnits = production.QueueCount;
            productionStatus = production.HasCompletedUnitWaiting
                ? "spawn blocked"
                : "complete";
        }
        else if (IsValidBuilding(_constructionSite))
        {
            productionStatus =
                $"site {_constructionSite.ConstructionProgress * 100.0f:0}%";
        }

        return $"Red macro: active | {State}\n" +
            $"Red Materials: {materials} | Workers: {workers}+{queuedWorkers}/" +
                $"{Mathf.Max(WorkerTarget, 0)}\n" +
            $"Red production: {productionStatus} | Queue: {queuedUnits}\n" +
            $"Assembling: {_assemblingCount}/{Mathf.Max(WaveSize, 1)}";
    }

    private void MakeDecision()
    {
        BuildingEntity redHeadquarters = _sandbox.GetHeadquarters(UnitTeam.Enemy);
        BuildingEntity blueHeadquarters = _sandbox.GetHeadquarters(UnitTeam.Friendly);
        if (!IsValidBuilding(redHeadquarters) || !IsValidBuilding(blueHeadquarters))
        {
            Deactivate();
            return;
        }

        PruneActiveWave();
        _assemblingCount = GetAssemblingUnits().Count;
        IReadOnlyList<SelectableUnit> workers =
            _sandbox.GetLivingWorkers(UnitTeam.Enemy);
        IReadOnlyList<MaterialsResourceNode> resources =
            _sandbox.GetAvailableMaterialsNodes();

        MaintainWorkerPopulation(redHeadquarters, workers.Count);
        RefreshInfrastructure();
        if (!IsValidBuilding(_productionBuilding))
        {
            HandleMissingProduction(redHeadquarters, workers);
        }
        else
        {
            _constructionSite = null!;
            _builder = null!;
            _hasEstablishedProduction = true;
            EnsureRallyPoint(_productionBuilding, blueHeadquarters);
        }

        AssignIdleWorkers(workers, resources);
        if (IsValidBuilding(_productionBuilding))
        {
            ManageProductionAndWaves(_productionBuilding, blueHeadquarters);
        }
        else if (_activeWave.Count > 0)
        {
            State = MacroState.Attacking;
            UpdateActiveWaveTargets(blueHeadquarters);
        }
        else if (workers.Count == 0 || resources.Count == 0)
        {
            State = MacroState.Stalled;
        }
    }

    private void RefreshInfrastructure()
    {
        IReadOnlyList<BuildingEntity> completedProduction =
            _sandbox.GetCompletedCombatProductionBuildings(UnitTeam.Enemy);
        _productionBuilding = completedProduction.Count > 0
            ? completedProduction[0]
            : null!;

        IReadOnlyList<BuildingEntity> constructionSites =
            _sandbox.GetActiveConstructionSites(UnitTeam.Enemy);
        _constructionSite = constructionSites.Count > 0
            ? constructionSites[0]
            : null!;

        if (!IsValidBuilding(_productionBuilding))
        {
            _rallyConfiguredBuilding = null!;
        }
    }

    private void MaintainWorkerPopulation(
        BuildingEntity headquarters,
        int livingWorkerCount)
    {
        int effectiveTarget = Mathf.Max(WorkerTarget, 0);
        if (!IsWorkerProducingHeadquarters(headquarters) ||
            livingWorkerCount + headquarters.Production.QueueCount >= effectiveTarget)
        {
            return;
        }

        _sandbox.TryQueueUnit(UnitTeam.Enemy, headquarters);
    }

    private static int GetQueuedWorkers(BuildingEntity headquarters)
    {
        return IsWorkerProducingHeadquarters(headquarters)
            ? headquarters.Production.QueueCount
            : 0;
    }

    private static bool IsWorkerProducingHeadquarters(BuildingEntity headquarters)
    {
        return IsValidBuilding(headquarters) &&
            headquarters.IsComplete &&
            headquarters.Definition.IsHeadquarters &&
            headquarters.HasProduction &&
            headquarters.Production.Definition.ProducedUnitDefinition
                .WorkerEconomy is not null;
    }

    private void HandleMissingProduction(
        BuildingEntity headquarters,
        IReadOnlyList<SelectableUnit> workers)
    {
        if (IsValidBuilding(_constructionSite))
        {
            State = MacroState.ConstructingProduction;
            EnsureConstructionHasBuilder(workers);
            return;
        }

        _builder = null!;
        int cost = Mathf.Max(_productionDefinition.MaterialsCost, 0);
        if (_sandbox.GetDepositedMaterials(UnitTeam.Enemy) < cost)
        {
            State = _hasEstablishedProduction
                ? MacroState.Recovering
                : MacroState.SavingForProduction;
            return;
        }

        if (workers.Count == 0)
        {
            State = MacroState.Stalled;
            return;
        }

        State = _hasEstablishedProduction
            ? MacroState.Recovering
            : MacroState.EstablishingEconomy;
        if (_constructionRetryRemaining > 0.0f)
        {
            return;
        }

        SelectableUnit candidateBuilder = workers[0];
        Vector2 offset = ConstructionOffsets[
            _nextConstructionCandidate % ConstructionOffsets.Length];
        _nextConstructionCandidate =
            (_nextConstructionCandidate + 1) % ConstructionOffsets.Length;
        Vector3 position = headquarters.GlobalPosition +
            new Vector3(offset.X, 0.0f, offset.Y);
        position.Y = 0.0f;

        if (_sandbox.TryStartConstruction(
                UnitTeam.Enemy,
                candidateBuilder,
                _productionDefinition,
                position,
                out BuildingEntity site))
        {
            _builder = candidateBuilder;
            _constructionSite = site;
            State = MacroState.ConstructingProduction;
        }

        _constructionRetryRemaining = Mathf.Max(
            ConstructionRetryDelay,
            Mathf.Max(DecisionInterval, 0.1f));
    }

    private void EnsureConstructionHasBuilder(
        IReadOnlyList<SelectableUnit> workers)
    {
        if (IsValidUnit(_builder) && _builder.HasActiveConstructionTask)
        {
            return;
        }

        _builder = null!;
        foreach (SelectableUnit worker in workers)
        {
            if (worker.SetConstructionTarget(_constructionSite))
            {
                _builder = worker;
                return;
            }
        }
    }

    private void AssignIdleWorkers(
        IReadOnlyList<SelectableUnit> workers,
        IReadOnlyList<MaterialsResourceNode> resources)
    {
        if (resources.Count == 0)
        {
            return;
        }

        for (int index = 0; index < workers.Count; index++)
        {
            SelectableUnit worker = workers[index];
            if (worker == _builder || !worker.IsWorkerTaskIdle)
            {
                continue;
            }

            MaterialsResourceNode resource = FindNearestResource(worker, resources);
            worker.SetGatherTarget(
                resource,
                index,
                workers.Count);
        }
    }

    private static MaterialsResourceNode FindNearestResource(
        SelectableUnit worker,
        IReadOnlyList<MaterialsResourceNode> resources)
    {
        MaterialsResourceNode nearest = resources[0];
        float nearestDistance = worker.GlobalPosition.DistanceSquaredTo(
            nearest.GlobalPosition);
        for (int index = 1; index < resources.Count; index++)
        {
            MaterialsResourceNode candidate = resources[index];
            float distance = worker.GlobalPosition.DistanceSquaredTo(
                candidate.GlobalPosition);
            bool isCloser = distance < nearestDistance;
            bool winsTie = Mathf.IsEqualApprox(distance, nearestDistance) &&
                candidate.GetInstanceId() < nearest.GetInstanceId();
            if (isCloser || winsTie)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private void EnsureRallyPoint(
        BuildingEntity productionBuilding,
        BuildingEntity blueHeadquarters)
    {
        if (_rallyConfiguredBuilding == productionBuilding)
        {
            return;
        }

        Vector3 direction = blueHeadquarters.GlobalPosition -
            productionBuilding.GlobalPosition;
        direction.Y = 0.0f;
        if (direction.LengthSquared() <= 0.001f)
        {
            direction = Vector3.Forward;
        }

        Vector3 rallyPoint = productionBuilding.GlobalPosition +
            direction.Normalized() * Mathf.Max(RallyDistance, 1.0f);
        rallyPoint.Y = 0.0f;
        if (_sandbox.TrySetProductionRallyPoint(
                UnitTeam.Enemy,
                productionBuilding,
                rallyPoint))
        {
            _rallyConfiguredBuilding = productionBuilding;
        }
    }

    private void ManageProductionAndWaves(
        BuildingEntity productionBuilding,
        BuildingEntity blueHeadquarters)
    {
        PruneActiveWave();
        List<SelectableUnit> assemblingUnits = GetAssemblingUnits();
        _assemblingCount = assemblingUnits.Count;
        int effectiveWaveSize = Mathf.Max(WaveSize, 1);
        int plannedUnits = assemblingUnits.Count +
            productionBuilding.Production.QueueCount;

        if (plannedUnits < effectiveWaveSize)
        {
            _sandbox.TryQueueUnit(UnitTeam.Enemy, productionBuilding);
        }

        if (_activeWave.Count == 0 && assemblingUnits.Count >= effectiveWaveSize)
        {
            LaunchWave(assemblingUnits, effectiveWaveSize, blueHeadquarters);
            return;
        }

        if (_activeWave.Count > 0)
        {
            State = MacroState.Attacking;
            UpdateActiveWaveTargets(blueHeadquarters);
        }
        else if (productionBuilding.Production.QueueCount > 0)
        {
            State = MacroState.Producing;
        }
        else
        {
            State = MacroState.AssemblingWave;
        }
    }

    private List<SelectableUnit> GetAssemblingUnits()
    {
        List<SelectableUnit> assembling = new();
        foreach (SelectableUnit unit in
                 _sandbox.GetLivingCombatUnits(UnitTeam.Enemy))
        {
            if (!_launchedUnitIds.Contains(unit.GetInstanceId()))
            {
                assembling.Add(unit);
            }
        }

        return assembling;
    }

    private void LaunchWave(
        IReadOnlyList<SelectableUnit> assemblingUnits,
        int waveSize,
        BuildingEntity blueHeadquarters)
    {
        _activeWave.Clear();
        for (int index = 0; index < waveSize; index++)
        {
            SelectableUnit unit = assemblingUnits[index];
            _activeWave.Add(unit);
            _launchedUnitIds.Add(unit.GetInstanceId());
        }

        _assemblingCount = assemblingUnits.Count - waveSize;
        State = MacroState.Attacking;
        UpdateActiveWaveTargets(blueHeadquarters);
    }

    private void UpdateActiveWaveTargets(BuildingEntity blueHeadquarters)
    {
        foreach (SelectableUnit attacker in _activeWave)
        {
            if (!IsValidUnit(attacker))
            {
                continue;
            }

            ICombatTarget currentTarget = attacker.CurrentCombatTarget;
            ICombatTarget nearbyCombat = FindNearestCombatTargetInEngagementRange(
                attacker);
            if (CombatTargetGroups.IsValid(currentTarget))
            {
                if (currentTarget == blueHeadquarters &&
                    nearbyCombat is not null &&
                    nearbyCombat != currentTarget)
                {
                    attacker.SetAttackTarget(nearbyCombat);
                }

                continue;
            }

            ICombatTarget target = nearbyCombat ??
                FindNearestObstructionTarget(attacker) ??
                blueHeadquarters;
            if (CombatTargetGroups.IsValid(target))
            {
                attacker.SetAttackTarget(target);
            }
        }
    }

    private ICombatTarget FindNearestCombatTargetInEngagementRange(
        SelectableUnit attacker)
    {
        float maximumRange = Mathf.Max(attacker.Definition.EngagementRange, 0.0f);
        return FindNearestTarget(
            attacker,
            _sandbox.GetLivingCombatUnits(UnitTeam.Friendly),
            maximumRange);
    }

    private ICombatTarget FindNearestObstructionTarget(SelectableUnit attacker)
    {
        ICombatTarget nearest = FindNearestTarget(
            attacker,
            _sandbox.GetLivingWorkers(UnitTeam.Friendly),
            Mathf.Max(ObstructionTargetRange, 0.0f));
        ICombatTarget production = FindNearestTarget(
            attacker,
            _sandbox.GetCompletedCombatProductionBuildings(UnitTeam.Friendly),
            Mathf.Max(ObstructionTargetRange, 0.0f));
        return ChooseNearer(attacker, nearest, production);
    }

    private static ICombatTarget FindNearestTarget<T>(
        SelectableUnit attacker,
        IReadOnlyList<T> candidates,
        float maximumSurfaceDistance)
        where T : Node3D, ICombatTarget
    {
        ICombatTarget nearest = null!;
        float nearestDistance = float.MaxValue;
        foreach (T candidate in candidates)
        {
            if (!CombatTargetGroups.IsValid(candidate) ||
                candidate.Team == attacker.Team)
            {
                continue;
            }

            float distance = GetSurfaceDistance(attacker, candidate);
            if (distance > maximumSurfaceDistance)
            {
                continue;
            }

            bool isCloser = distance < nearestDistance;
            bool winsTie = Mathf.IsEqualApprox(distance, nearestDistance) &&
                (nearest is null ||
                    candidate.GetInstanceId() <
                    ((Node)nearest).GetInstanceId());
            if (isCloser || winsTie)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private static ICombatTarget ChooseNearer(
        SelectableUnit attacker,
        ICombatTarget first,
        ICombatTarget second)
    {
        if (!CombatTargetGroups.IsValid(first))
        {
            return second;
        }

        if (!CombatTargetGroups.IsValid(second))
        {
            return first;
        }

        float firstDistance = GetSurfaceDistance(attacker, first);
        float secondDistance = GetSurfaceDistance(attacker, second);
        if (!Mathf.IsEqualApprox(firstDistance, secondDistance))
        {
            return firstDistance < secondDistance ? first : second;
        }

        return ((Node)first).GetInstanceId() < ((Node)second).GetInstanceId()
            ? first
            : second;
    }

    private static float GetSurfaceDistance(
        SelectableUnit attacker,
        ICombatTarget target)
    {
        return Mathf.Max(
            attacker.GlobalPosition.DistanceTo(target.TargetPosition) -
                target.TargetRadius,
            0.0f);
    }

    private void PruneActiveWave()
    {
        for (int index = _activeWave.Count - 1; index >= 0; index--)
        {
            if (!IsValidUnit(_activeWave[index]))
            {
                _activeWave.RemoveAt(index);
            }
        }
    }

    private void ClearTrackedState()
    {
        _decisionElapsed = 0.0;
        _constructionRetryRemaining = 0.0f;
        _nextConstructionCandidate = 0;
        _assemblingCount = 0;
        _productionBuilding = null!;
        _constructionSite = null!;
        _rallyConfiguredBuilding = null!;
        _builder = null!;
        _activeWave.Clear();
        _launchedUnitIds.Clear();
        _hasEstablishedProduction = false;
    }

    private static bool IsValidUnit(SelectableUnit unit)
    {
        return IsInstanceValid(unit) &&
            !unit.IsQueuedForDeletion() &&
            unit.IsAlive;
    }

    private static bool IsValidBuilding(BuildingEntity building)
    {
        return IsInstanceValid(building) &&
            !building.IsQueuedForDeletion() &&
            building.IsAlive;
    }
}
