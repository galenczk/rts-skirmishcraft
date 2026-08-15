using Godot;
using System;
using System.Collections.Generic;

public sealed class FormationMovePlanner
{
    public enum MoveDistanceClass
    {
        Short,
        Medium,
        Long,
    }

    public sealed class Settings
    {
        public float ClusterLinkDistance = 3.0f;
        public float RobustRadiusPercentile = 0.95f;
        public float ShortDistanceRadiusMultiplier = 1.0f;
        public float LongDistanceRadiusMultiplier = 3.0f;
        public float LongReorientationAngleDegrees = 75.0f;
        public float ArrivalTransitionRadiusMultiplier = 0.75f;
        public float TopologyCompactnessThreshold = 1.4f;
        public float SlotSeparationMargin = 0.1f;
        public Vector2 DefaultOrientation = Vector2.Up;
    }

    public sealed class SourceClusterSummary
    {
        public int UnitCount;
        public Vector2 SourceCentroid;
        public Vector2 AssignedSlotCentroid;
        public Rect2 AssignedSlotBounds;
        public float RobustRadius;
    }

    public sealed class CommandPlan
    {
        public List<SelectableUnit> Units = new();
        public List<Vector3> InitialDestinations = new();
        public List<Vector3> FinalDestinations = new();
        public List<SourceClusterSummary> SourceClusters = new();
        public Vector2 SourceCentroid;
        public Vector2 TargetCentroid;
        public Vector2 ApproachCentroid;
        public Vector2 ArrivalHeading;
        public Vector2 GridOrientation;
        public Vector2 GridCentroid;
        public Vector2 AssignedSlotCentroid;
        public Vector2 SourceFootprintSize;
        public Vector2 CompactFootprintSize;
        public float RobustRadius;
        public float DispersionRatio;
        public float GridSpacing;
        public float LargestAdjacencyGap;
        public int GridColumns;
        public int GridRows;
        public MoveDistanceClass DistanceClass;
        public bool UsesDirectTranslation;
        public bool HasArrivalTransition;
    }

    private readonly Settings _settings;

    public FormationMovePlanner(Settings settings)
    {
        _settings = settings;
    }

    public CommandPlan CreatePlan(
        IReadOnlyList<SelectableUnit> commandedUnits,
        Vector3 clickedDestination,
        Rect2 playableBounds,
        Rid navigationMap,
        Func<SelectableUnit, Vector2> getStoredOrientation)
    {
        List<SelectableUnit> units = new(commandedUnits);
        units.Sort((first, second) =>
            first.GetInstanceId().CompareTo(second.GetInstanceId()));

        CommandPlan plan = new()
        {
            Units = units,
        };
        if (units.Count == 0)
        {
            return plan;
        }

        List<List<SelectableUnit>> clusters = DetectClusters(units);
        clusters.Sort(CompareClusters);
        Vector2 sourceCentroid = CalculateCentroid(units);
        Vector2 existingOrientation = GetClusterOrientation(
            units,
            getStoredOrientation);
        float maximumUnitRadius = GetMaximumRadius(units);
        float slotSpacing = maximumUnitRadius * 2.0f +
            Mathf.Max(_settings.SlotSeparationMargin, 0.0f);
        int columns = Mathf.CeilToInt(Mathf.Sqrt(units.Count));
        int rows = Mathf.CeilToInt((float)units.Count / columns);
        List<Vector2> canonicalSlots = CreateCenteredLatticeOffsets(
            units.Count,
            columns,
            rows,
            slotSpacing);
        Vector2 sourceFootprintSize = CalculateProjectedFootprintSize(
            units,
            sourceCentroid,
            existingOrientation,
            maximumUnitRadius);
        Vector2 compactFootprintSize = CalculateOffsetFootprintSize(
            canonicalSlots,
            maximumUnitRadius);
        float dispersionRatio = Mathf.Max(
            sourceFootprintSize.X / Mathf.Max(compactFootprintSize.X, 0.01f),
            sourceFootprintSize.Y / Mathf.Max(compactFootprintSize.Y, 0.01f));
        List<Vector2> sourceOffsets = CreateSourceOffsets(units, sourceCentroid);
        float robustRadius = CalculateRobustRadius(sourceOffsets);
        float classificationRadius = GetClassificationRadius(
            clusters,
            maximumUnitRadius);
        Vector2 requestedTarget = new(
            clickedDestination.X,
            clickedDestination.Z);
        float travelDistance = sourceCentroid.DistanceTo(requestedTarget);
        MoveDistanceClass distanceClass = ClassifyDistance(
            travelDistance,
            classificationRadius);
        Vector2 arrivalHeading = GetArrivalHeading(
            navigationMap,
            sourceCentroid,
            requestedTarget,
            existingOrientation);
        float headingDifference = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(
            existingOrientation.Dot(arrivalHeading),
            -1.0f,
            1.0f)));
        bool shouldReorient = distanceClass == MoveDistanceClass.Long &&
            headingDifference >= Mathf.Clamp(
                _settings.LongReorientationAngleDegrees,
                0.0f,
                180.0f);
        bool useDirectTranslation = clusters.Count == 1 &&
            distanceClass == MoveDistanceClass.Short &&
            dispersionRatio <= Mathf.Max(
                _settings.TopologyCompactnessThreshold,
                1.0f);

        plan.SourceCentroid = sourceCentroid;
        plan.ArrivalHeading = arrivalHeading;
        plan.RobustRadius = robustRadius;
        plan.DispersionRatio = dispersionRatio;
        plan.SourceFootprintSize = sourceFootprintSize;
        plan.CompactFootprintSize = compactFootprintSize;
        plan.DistanceClass = distanceClass;
        plan.UsesDirectTranslation = useDirectTranslation;

        if (useDirectTranslation)
        {
            Vector2 targetCentroid = FitCentroidInsideBounds(
                requestedTarget,
                sourceOffsets,
                playableBounds);
            plan.TargetCentroid = targetCentroid;
            plan.ApproachCentroid = targetCentroid;
            plan.GridOrientation = existingOrientation;
            plan.GridCentroid = targetCentroid;
            plan.InitialDestinations = TranslateOffsets(
                units,
                sourceOffsets,
                targetCentroid);
            plan.AssignedSlotCentroid = CalculateDestinationCentroid(
                plan.InitialDestinations);
            plan.SourceClusters = CreateSourceClusterSummaries(
                clusters,
                units,
                plan.InitialDestinations);
            return plan;
        }

        int[] assignedSlotIndices = AssignSlotsBySpatialRank(
            units,
            sourceCentroid,
            existingOrientation,
            canonicalSlots);
        Vector2 finalOrientation = shouldReorient
            ? arrivalHeading
            : existingOrientation;
        List<Vector2> finalOffsets = CreateAssignedOffsets(
            canonicalSlots,
            assignedSlotIndices,
            finalOrientation);
        Vector2 target = FitCentroidInsideBounds(
            requestedTarget,
            finalOffsets,
            playableBounds);

        plan.TargetCentroid = target;
        plan.ApproachCentroid = target;
        plan.GridOrientation = finalOrientation;
        plan.GridCentroid = target;
        plan.GridColumns = columns;
        plan.GridRows = rows;
        plan.GridSpacing = slotSpacing;
        plan.LargestAdjacencyGap = CalculateLargestNearestNeighborDistance(
            canonicalSlots,
            slotSpacing);
        plan.HasArrivalTransition = shouldReorient;
        plan.FinalDestinations = TranslateOffsets(
            units,
            finalOffsets,
            target);

        if (!shouldReorient)
        {
            plan.InitialDestinations = new List<Vector3>(
                plan.FinalDestinations);
        }
        else
        {
            float transitionDistance = Mathf.Min(
                Mathf.Max(classificationRadius, maximumUnitRadius) *
                    Mathf.Max(
                        _settings.ArrivalTransitionRadiusMultiplier,
                        0.25f),
                travelDistance * 0.25f);
            List<Vector2> approachOffsets = CreateAssignedOffsets(
                canonicalSlots,
                assignedSlotIndices,
                existingOrientation);
            Vector2 approachCentroid = target -
                arrivalHeading * transitionDistance;
            approachCentroid = FitCentroidInsideBounds(
                approachCentroid,
                approachOffsets,
                playableBounds);
            plan.ApproachCentroid = approachCentroid;
            plan.InitialDestinations = TranslateOffsets(
                units,
                approachOffsets,
                approachCentroid);
        }

        plan.AssignedSlotCentroid = CalculateDestinationCentroid(
            plan.FinalDestinations);
        plan.SourceClusters = CreateSourceClusterSummaries(
            clusters,
            units,
            plan.FinalDestinations);
        return plan;
    }

    private List<List<SelectableUnit>> DetectClusters(
        IReadOnlyList<SelectableUnit> units)
    {
        float linkDistance = Mathf.Max(_settings.ClusterLinkDistance, 0.5f);
        float linkDistanceSquared = linkDistance * linkDistance;
        Dictionary<Vector2I, List<int>> cells = new();
        for (int index = 0; index < units.Count; index++)
        {
            Vector2I cell = GetCell(units[index].GlobalPosition, linkDistance);
            if (!cells.TryGetValue(cell, out List<int> occupants))
            {
                occupants = new List<int>();
                cells[cell] = occupants;
            }

            occupants.Add(index);
        }

        bool[] visited = new bool[units.Count];
        Queue<int> frontier = new();
        List<List<SelectableUnit>> clusters = new();
        for (int seed = 0; seed < units.Count; seed++)
        {
            if (visited[seed])
            {
                continue;
            }

            List<SelectableUnit> cluster = new();
            visited[seed] = true;
            frontier.Enqueue(seed);
            while (frontier.Count > 0)
            {
                int currentIndex = frontier.Dequeue();
                SelectableUnit current = units[currentIndex];
                cluster.Add(current);
                Vector2I centerCell = GetCell(
                    current.GlobalPosition,
                    linkDistance);
                for (int x = -1; x <= 1; x++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        if (!cells.TryGetValue(
                                centerCell + new Vector2I(x, z),
                                out List<int> neighbors))
                        {
                            continue;
                        }

                        foreach (int neighborIndex in neighbors)
                        {
                            if (visited[neighborIndex] ||
                                HorizontalDistanceSquared(
                                    current.GlobalPosition,
                                    units[neighborIndex].GlobalPosition) >
                                    linkDistanceSquared)
                            {
                                continue;
                            }

                            visited[neighborIndex] = true;
                            frontier.Enqueue(neighborIndex);
                        }
                    }
                }
            }

            cluster.Sort((first, second) =>
                first.GetInstanceId().CompareTo(second.GetInstanceId()));
            clusters.Add(cluster);
        }

        return clusters;
    }

    private static List<Vector2> CreateSourceOffsets(
        IReadOnlyList<SelectableUnit> units,
        Vector2 centroid)
    {
        List<Vector2> offsets = new(units.Count);
        foreach (SelectableUnit unit in units)
        {
            offsets.Add(ToHorizontal(unit.GlobalPosition) - centroid);
        }

        return offsets;
    }

    private static List<Vector2> CreateCenteredLatticeOffsets(
        int unitCount,
        int columns,
        int rows,
        float spacing)
    {
        List<Vector2> offsets = new(unitCount);
        Vector2 mean = Vector2.Zero;
        for (int index = 0; index < unitCount; index++)
        {
            int row = index / columns;
            int column = index % columns;
            int unitsInRow = Mathf.Min(
                columns,
                unitCount - row * columns);
            Vector2 offset = new(
                (column - (unitsInRow - 1) * 0.5f) * spacing,
                (row - (rows - 1) * 0.5f) * spacing);
            offsets.Add(offset);
            mean += offset;
        }

        mean /= Mathf.Max(unitCount, 1);
        for (int index = 0; index < offsets.Count; index++)
        {
            offsets[index] -= mean;
        }

        return offsets;
    }

    private static int[] AssignSlotsBySpatialRank(
        IReadOnlyList<SelectableUnit> units,
        Vector2 sourceCentroid,
        Vector2 sourceOrientation,
        IReadOnlyList<Vector2> canonicalSlots)
    {
        Vector2 sourceRight = GetRightAxis(sourceOrientation);
        List<Vector2> sourceLocal = new(units.Count);
        foreach (SelectableUnit unit in units)
        {
            Vector2 offset = ToHorizontal(unit.GlobalPosition) - sourceCentroid;
            sourceLocal.Add(new Vector2(
                offset.Dot(sourceRight),
                offset.Dot(sourceOrientation)));
        }

        Vector2 sourceSize = CalculatePointBoundsSize(sourceLocal);
        bool splitHorizontalFirst = sourceSize.X >= sourceSize.Y;
        List<int> unitRanks = CreateSpatialPartitionOrder(
            sourceLocal,
            splitHorizontalFirst,
            index => units[index].GetInstanceId());
        List<int> slotRanks = CreateSpatialPartitionOrder(
            canonicalSlots,
            splitHorizontalFirst,
            index => (ulong)index);
        int[] assignedSlotIndices = new int[units.Count];
        for (int rank = 0; rank < unitRanks.Count; rank++)
        {
            assignedSlotIndices[unitRanks[rank]] = slotRanks[rank];
        }

        return assignedSlotIndices;
    }

    private static List<int> CreateSpatialPartitionOrder(
        IReadOnlyList<Vector2> positions,
        bool splitHorizontalFirst,
        Func<int, ulong> getTieBreaker)
    {
        List<int> horizontalOrder = new(positions.Count);
        List<int> depthOrder = new(positions.Count);
        for (int index = 0; index < positions.Count; index++)
        {
            horizontalOrder.Add(index);
            depthOrder.Add(index);
        }

        horizontalOrder.Sort((first, second) => CompareSpatialIndices(
            positions,
            getTieBreaker,
            first,
            second,
            horizontalFirst: true));
        depthOrder.Sort((first, second) => CompareSpatialIndices(
            positions,
            getTieBreaker,
            first,
            second,
            horizontalFirst: false));
        List<int> result = new(positions.Count);
        AppendSpatialPartitionOrder(
            horizontalOrder,
            depthOrder,
            splitHorizontalFirst,
            result);
        return result;
    }

    private static void AppendSpatialPartitionOrder(
        IReadOnlyList<int> horizontalOrder,
        IReadOnlyList<int> depthOrder,
        bool splitHorizontal,
        List<int> result)
    {
        if (horizontalOrder.Count <= 1)
        {
            if (horizontalOrder.Count == 1)
            {
                result.Add(horizontalOrder[0]);
            }

            return;
        }

        IReadOnlyList<int> primaryOrder = splitHorizontal
            ? horizontalOrder
            : depthOrder;
        int leftCount = primaryOrder.Count / 2;
        HashSet<int> leftIndices = new();
        for (int index = 0; index < leftCount; index++)
        {
            leftIndices.Add(primaryOrder[index]);
        }

        List<int> leftHorizontal = new(leftCount);
        List<int> rightHorizontal = new(horizontalOrder.Count - leftCount);
        PartitionSpatialOrder(
            horizontalOrder,
            leftIndices,
            leftHorizontal,
            rightHorizontal);
        List<int> leftDepth = new(leftCount);
        List<int> rightDepth = new(depthOrder.Count - leftCount);
        PartitionSpatialOrder(
            depthOrder,
            leftIndices,
            leftDepth,
            rightDepth);
        AppendSpatialPartitionOrder(
            leftHorizontal,
            leftDepth,
            !splitHorizontal,
            result);
        AppendSpatialPartitionOrder(
            rightHorizontal,
            rightDepth,
            !splitHorizontal,
            result);
    }

    private static void PartitionSpatialOrder(
        IReadOnlyList<int> source,
        IReadOnlySet<int> leftIndices,
        List<int> left,
        List<int> right)
    {
        foreach (int index in source)
        {
            if (leftIndices.Contains(index))
            {
                left.Add(index);
            }
            else
            {
                right.Add(index);
            }
        }
    }

    private static int CompareSpatialIndices(
        IReadOnlyList<Vector2> positions,
        Func<int, ulong> getTieBreaker,
        int first,
        int second,
        bool horizontalFirst)
    {
        float firstPrimary = horizontalFirst
            ? positions[first].X
            : positions[first].Y;
        float secondPrimary = horizontalFirst
            ? positions[second].X
            : positions[second].Y;
        int primaryComparison = firstPrimary.CompareTo(secondPrimary);
        if (primaryComparison != 0)
        {
            return primaryComparison;
        }

        float firstSecondary = horizontalFirst
            ? positions[first].Y
            : positions[first].X;
        float secondSecondary = horizontalFirst
            ? positions[second].Y
            : positions[second].X;
        int secondaryComparison = firstSecondary.CompareTo(secondSecondary);
        return secondaryComparison != 0
            ? secondaryComparison
            : getTieBreaker(first).CompareTo(getTieBreaker(second));
    }

    private static List<Vector2> CreateAssignedOffsets(
        IReadOnlyList<Vector2> canonicalSlots,
        IReadOnlyList<int> assignedSlotIndices,
        Vector2 forward)
    {
        Vector2 right = GetRightAxis(forward);
        List<Vector2> offsets = new(assignedSlotIndices.Count);
        for (int index = 0; index < assignedSlotIndices.Count; index++)
        {
            Vector2 canonical = canonicalSlots[assignedSlotIndices[index]];
            offsets.Add(right * canonical.X + forward * canonical.Y);
        }

        return offsets;
    }

    private float GetClassificationRadius(
        IReadOnlyList<List<SelectableUnit>> clusters,
        float maximumUnitRadius)
    {
        float radius = maximumUnitRadius;
        foreach (List<SelectableUnit> cluster in clusters)
        {
            Vector2 centroid = CalculateCentroid(cluster);
            radius = Mathf.Max(
                radius,
                CalculateRobustRadius(CreateSourceOffsets(cluster, centroid)));
        }

        return radius;
    }

    private MoveDistanceClass ClassifyDistance(
        float travelDistance,
        float formationRadius)
    {
        if (travelDistance <= formationRadius * Mathf.Max(
                _settings.ShortDistanceRadiusMultiplier,
                0.1f))
        {
            return MoveDistanceClass.Short;
        }

        if (travelDistance <= formationRadius * Mathf.Max(
                _settings.LongDistanceRadiusMultiplier,
                _settings.ShortDistanceRadiusMultiplier))
        {
            return MoveDistanceClass.Medium;
        }

        return MoveDistanceClass.Long;
    }

    private float CalculateRobustRadius(IReadOnlyList<Vector2> offsets)
    {
        if (offsets.Count == 0)
        {
            return 0.0f;
        }

        List<float> distances = new(offsets.Count);
        foreach (Vector2 offset in offsets)
        {
            distances.Add(offset.Length());
        }

        distances.Sort();
        float percentile = Mathf.Clamp(
            _settings.RobustRadiusPercentile,
            0.5f,
            1.0f);
        int percentileIndex = Mathf.Clamp(
            Mathf.CeilToInt(distances.Count * percentile) - 1,
            0,
            distances.Count - 1);
        return distances[percentileIndex];
    }

    private Vector2 GetClusterOrientation(
        IReadOnlyList<SelectableUnit> units,
        Func<SelectableUnit, Vector2> getStoredOrientation)
    {
        Vector2 orientation = Vector2.Zero;
        foreach (SelectableUnit unit in units)
        {
            orientation += getStoredOrientation(unit);
        }

        if (orientation.LengthSquared() <= 0.0001f)
        {
            orientation = _settings.DefaultOrientation;
        }

        return orientation.Normalized();
    }

    private static Vector2 GetArrivalHeading(
        Rid navigationMap,
        Vector2 sourceCentroid,
        Vector2 targetCentroid,
        Vector2 fallbackHeading)
    {
        Vector3[] path = NavigationServer3D.MapGetPath(
            navigationMap,
            new Vector3(sourceCentroid.X, 0.0f, sourceCentroid.Y),
            new Vector3(targetCentroid.X, 0.0f, targetCentroid.Y),
            optimize: true,
            navigationLayers: 1);
        for (int index = path.Length - 1; index > 0; index--)
        {
            Vector2 segment = new(
                path[index].X - path[index - 1].X,
                path[index].Z - path[index - 1].Z);
            if (segment.LengthSquared() > 0.01f)
            {
                return segment.Normalized();
            }
        }

        Vector2 directHeading = targetCentroid - sourceCentroid;
        return directHeading.LengthSquared() > 0.01f
            ? directHeading.Normalized()
            : fallbackHeading;
    }

    private static List<Vector3> TranslateOffsets(
        IReadOnlyList<SelectableUnit> units,
        IReadOnlyList<Vector2> offsets,
        Vector2 centroid)
    {
        List<Vector3> destinations = new(units.Count);
        for (int index = 0; index < units.Count; index++)
        {
            destinations.Add(new Vector3(
                centroid.X + offsets[index].X,
                units[index].GlobalPosition.Y,
                centroid.Y + offsets[index].Y));
        }

        return destinations;
    }

    private static Vector2 FitCentroidInsideBounds(
        Vector2 requestedCentroid,
        IReadOnlyList<Vector2> offsets,
        Rect2 bounds)
    {
        Vector2 minimumOffset = new(float.MaxValue, float.MaxValue);
        Vector2 maximumOffset = new(float.MinValue, float.MinValue);
        foreach (Vector2 offset in offsets)
        {
            minimumOffset = new Vector2(
                Mathf.Min(minimumOffset.X, offset.X),
                Mathf.Min(minimumOffset.Y, offset.Y));
            maximumOffset = new Vector2(
                Mathf.Max(maximumOffset.X, offset.X),
                Mathf.Max(maximumOffset.Y, offset.Y));
        }

        Vector2 minimumCentroid = bounds.Position - minimumOffset;
        Vector2 maximumCentroid = bounds.End - maximumOffset;
        return new Vector2(
            ClampCentroidAxis(
                requestedCentroid.X,
                minimumCentroid.X,
                maximumCentroid.X,
                bounds.GetCenter().X),
            ClampCentroidAxis(
                requestedCentroid.Y,
                minimumCentroid.Y,
                maximumCentroid.Y,
                bounds.GetCenter().Y));
    }

    private static float ClampCentroidAxis(
        float requested,
        float minimum,
        float maximum,
        float fallback)
    {
        return minimum <= maximum
            ? Mathf.Clamp(requested, minimum, maximum)
            : fallback;
    }

    private static Vector2 CalculateProjectedFootprintSize(
        IReadOnlyList<SelectableUnit> units,
        Vector2 centroid,
        Vector2 forward,
        float maximumUnitRadius)
    {
        Vector2 right = GetRightAxis(forward);
        Vector2 minimum = new(float.MaxValue, float.MaxValue);
        Vector2 maximum = new(float.MinValue, float.MinValue);
        foreach (SelectableUnit unit in units)
        {
            Vector2 offset = ToHorizontal(unit.GlobalPosition) - centroid;
            Vector2 local = new(
                offset.Dot(right),
                offset.Dot(forward));
            minimum = new Vector2(
                Mathf.Min(minimum.X, local.X),
                Mathf.Min(minimum.Y, local.Y));
            maximum = new Vector2(
                Mathf.Max(maximum.X, local.X),
                Mathf.Max(maximum.Y, local.Y));
        }

        return maximum - minimum + Vector2.One * maximumUnitRadius * 2.0f;
    }

    private static Vector2 CalculateOffsetFootprintSize(
        IReadOnlyList<Vector2> offsets,
        float maximumUnitRadius)
    {
        Vector2 minimum = new(float.MaxValue, float.MaxValue);
        Vector2 maximum = new(float.MinValue, float.MinValue);
        foreach (Vector2 offset in offsets)
        {
            minimum = new Vector2(
                Mathf.Min(minimum.X, offset.X),
                Mathf.Min(minimum.Y, offset.Y));
            maximum = new Vector2(
                Mathf.Max(maximum.X, offset.X),
                Mathf.Max(maximum.Y, offset.Y));
        }

        return maximum - minimum + Vector2.One * maximumUnitRadius * 2.0f;
    }

    private static Vector2 CalculatePointBoundsSize(
        IReadOnlyList<Vector2> positions)
    {
        Vector2 minimum = new(float.MaxValue, float.MaxValue);
        Vector2 maximum = new(float.MinValue, float.MinValue);
        foreach (Vector2 position in positions)
        {
            minimum = new Vector2(
                Mathf.Min(minimum.X, position.X),
                Mathf.Min(minimum.Y, position.Y));
            maximum = new Vector2(
                Mathf.Max(maximum.X, position.X),
                Mathf.Max(maximum.Y, position.Y));
        }

        return maximum - minimum;
    }

    private static Vector2 CalculateDestinationCentroid(
        IReadOnlyList<Vector3> destinations)
    {
        Vector2 centroid = Vector2.Zero;
        foreach (Vector3 destination in destinations)
        {
            centroid += ToHorizontal(destination);
        }

        return centroid / Mathf.Max(destinations.Count, 1);
    }

    private List<SourceClusterSummary> CreateSourceClusterSummaries(
        IReadOnlyList<List<SelectableUnit>> clusters,
        IReadOnlyList<SelectableUnit> units,
        IReadOnlyList<Vector3> destinations)
    {
        Dictionary<ulong, Vector2> destinationsByUnit = new();
        for (int index = 0; index < units.Count; index++)
        {
            destinationsByUnit[units[index].GetInstanceId()] =
                ToHorizontal(destinations[index]);
        }

        List<SourceClusterSummary> summaries = new(clusters.Count);
        foreach (List<SelectableUnit> cluster in clusters)
        {
            Vector2 sourceCentroid = CalculateCentroid(cluster);
            Vector2 assignedCentroid = Vector2.Zero;
            Vector2 minimum = new(float.MaxValue, float.MaxValue);
            Vector2 maximum = new(float.MinValue, float.MinValue);
            foreach (SelectableUnit unit in cluster)
            {
                Vector2 destination = destinationsByUnit[unit.GetInstanceId()];
                assignedCentroid += destination;
                minimum = new Vector2(
                    Mathf.Min(minimum.X, destination.X),
                    Mathf.Min(minimum.Y, destination.Y));
                maximum = new Vector2(
                    Mathf.Max(maximum.X, destination.X),
                    Mathf.Max(maximum.Y, destination.Y));
            }

            assignedCentroid /= Mathf.Max(cluster.Count, 1);
            summaries.Add(new SourceClusterSummary
            {
                UnitCount = cluster.Count,
                SourceCentroid = sourceCentroid,
                AssignedSlotCentroid = assignedCentroid,
                AssignedSlotBounds = new Rect2(minimum, maximum - minimum),
                RobustRadius = CalculateRobustRadius(
                    CreateSourceOffsets(cluster, sourceCentroid)),
            });
        }

        return summaries;
    }

    private static float CalculateLargestNearestNeighborDistance(
        IReadOnlyList<Vector2> positions,
        float spacing)
    {
        if (positions.Count <= 1)
        {
            return 0.0f;
        }

        float cellSize = Mathf.Max(spacing, 0.01f);
        Dictionary<Vector2I, List<int>> cells = new();
        for (int index = 0; index < positions.Count; index++)
        {
            Vector2I cell = GetCell(positions[index], cellSize);
            if (!cells.TryGetValue(cell, out List<int> occupants))
            {
                occupants = new List<int>();
                cells[cell] = occupants;
            }

            occupants.Add(index);
        }

        float largestDistanceSquared = 0.0f;
        for (int index = 0; index < positions.Count; index++)
        {
            Vector2I center = GetCell(positions[index], cellSize);
            float nearestDistanceSquared = float.MaxValue;
            for (int x = -2; x <= 2; x++)
            {
                for (int y = -2; y <= 2; y++)
                {
                    if (!cells.TryGetValue(
                            center + new Vector2I(x, y),
                            out List<int> neighbors))
                    {
                        continue;
                    }

                    foreach (int neighborIndex in neighbors)
                    {
                        if (neighborIndex == index)
                        {
                            continue;
                        }

                        nearestDistanceSquared = Mathf.Min(
                            nearestDistanceSquared,
                            positions[index].DistanceSquaredTo(
                                positions[neighborIndex]));
                    }
                }
            }

            if (nearestDistanceSquared < float.MaxValue)
            {
                largestDistanceSquared = Mathf.Max(
                    largestDistanceSquared,
                    nearestDistanceSquared);
            }
        }

        return Mathf.Sqrt(largestDistanceSquared);
    }

    private static Vector2 CalculateCentroid(
        IReadOnlyList<SelectableUnit> units)
    {
        Vector2 centroid = Vector2.Zero;
        foreach (SelectableUnit unit in units)
        {
            centroid += ToHorizontal(unit.GlobalPosition);
        }

        return centroid / Mathf.Max(units.Count, 1);
    }

    private static float GetMaximumRadius(IReadOnlyList<SelectableUnit> units)
    {
        float radius = 0.1f;
        foreach (SelectableUnit unit in units)
        {
            radius = Mathf.Max(radius, unit.OccupancyRadius);
        }

        return radius;
    }

    private static int CompareClusters(
        List<SelectableUnit> first,
        List<SelectableUnit> second)
    {
        Vector2 firstCentroid = CalculateCentroid(first);
        Vector2 secondCentroid = CalculateCentroid(second);
        int xComparison = firstCentroid.X.CompareTo(secondCentroid.X);
        if (xComparison != 0)
        {
            return xComparison;
        }

        int zComparison = firstCentroid.Y.CompareTo(secondCentroid.Y);
        return zComparison != 0
            ? zComparison
            : first[0].GetInstanceId().CompareTo(second[0].GetInstanceId());
    }

    private static Vector2 GetRightAxis(Vector2 forward)
    {
        return new Vector2(forward.Y, -forward.X);
    }

    private static Vector2I GetCell(Vector3 position, float cellSize)
    {
        return GetCell(ToHorizontal(position), cellSize);
    }

    private static Vector2I GetCell(Vector2 position, float cellSize)
    {
        return new Vector2I(
            Mathf.FloorToInt(position.X / cellSize),
            Mathf.FloorToInt(position.Y / cellSize));
    }

    private static Vector2 ToHorizontal(Vector3 position)
    {
        return new Vector2(position.X, position.Z);
    }

    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        return ToHorizontal(first).DistanceSquaredTo(ToHorizontal(second));
    }
}
