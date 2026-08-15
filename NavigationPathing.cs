using Godot;
using System.Collections.Generic;

public static class NavigationPathing
{
    public static readonly StringName NavigationSourceGroup = "navigation_source";
    private const float MinimumProjectionTolerance = 0.25f;
    private const float MinimumEndpointTolerance = 0.35f;
    private const float FootprintBoundaryTolerance = 0.03f;

    private static Rid _synchronizingMap;
    private static uint _iterationBeforeUpdate;
    private static bool _isSynchronizing;
    private static readonly List<BuildingEntity> CachedBuildings = new();
    private static readonly List<MaterialsResourceNode> CachedResources = new();
    private static ulong _cachedPhysicsFrame = ulong.MaxValue;
    private static SceneTree _cachedTree = null!;

    public static void BeginMapUpdate(Rid navigationMap)
    {
        _synchronizingMap = navigationMap;
        _iterationBeforeUpdate = NavigationServer3D.MapGetIterationId(
            navigationMap);
        _isSynchronizing = true;
    }

    public static bool IsMapSynchronizing(Rid navigationMap)
    {
        if (!_isSynchronizing || navigationMap != _synchronizingMap)
        {
            return false;
        }

        uint currentIteration = NavigationServer3D.MapGetIterationId(
            navigationMap);
        if (currentIteration != 0 && currentIteration != _iterationBeforeUpdate)
        {
            _isSynchronizing = false;
        }

        return _isSynchronizing;
    }

    public static bool TryResolveReachablePoint(
        Node3D mover,
        Vector3 requestedPosition,
        float occupancyRadius,
        out Vector3 resolvedPosition,
        GodotObject allowedTarget = null!)
    {
        Rid navigationMap = mover.GetWorld3D().NavigationMap;
        if (NavigationServer3D.MapGetIterationId(navigationMap) == 0 ||
            IsMapSynchronizing(navigationMap))
        {
            resolvedPosition = mover.GlobalPosition;
            return false;
        }

        float effectiveRadius = Mathf.Max(occupancyRadius, 0.1f);
        Vector3 projectedPosition = NavigationServer3D.MapGetClosestPoint(
            navigationMap,
            requestedPosition);
        float projectionTolerance = Mathf.Max(
            effectiveRadius,
            MinimumProjectionTolerance);
        if (HorizontalDistanceSquared(requestedPosition, projectedPosition) >
                projectionTolerance * projectionTolerance ||
            !IsClearOfStaticFootprints(
                mover.GetTree(),
                projectedPosition,
                effectiveRadius,
                allowedTarget))
        {
            resolvedPosition = mover.GlobalPosition;
            return false;
        }

        Vector3 pathStart = NavigationServer3D.MapGetClosestPoint(
            navigationMap,
            mover.GlobalPosition);
        Vector3[] path = NavigationServer3D.MapGetPath(
            navigationMap,
            pathStart,
            projectedPosition,
            optimize: true,
            navigationLayers: 1);
        float endpointTolerance = Mathf.Max(
            effectiveRadius,
            MinimumEndpointTolerance);
        if (path.Length == 0 ||
            HorizontalDistanceSquared(path[^1], projectedPosition) >
                endpointTolerance * endpointTolerance)
        {
            resolvedPosition = mover.GlobalPosition;
            return false;
        }

        resolvedPosition = projectedPosition;
        resolvedPosition.Y = mover.GlobalPosition.Y;
        return true;
    }

    public static bool IsClearOfStaticFootprints(
        SceneTree tree,
        Vector3 position,
        float occupancyRadius,
        GodotObject allowedTarget = null!)
    {
        RefreshStaticFootprintCache(tree);
        Vector2 horizontal = new(position.X, position.Z);
        float effectiveRadius = Mathf.Max(occupancyRadius, 0.1f);

        foreach (BuildingEntity building in CachedBuildings)
        {
            if (building == allowedTarget ||
                !GodotObject.IsInstanceValid(building) ||
                building.IsQueuedForDeletion() ||
                !building.IsAlive)
            {
                continue;
            }

            Rect2 footprint = building.GetFootprintRect();
            Vector2 closestFootprintPoint = new(
                Mathf.Clamp(
                    horizontal.X,
                    footprint.Position.X,
                    footprint.End.X),
                Mathf.Clamp(
                    horizontal.Y,
                    footprint.Position.Y,
                    footprint.End.Y));
            float requiredClearance = Mathf.Max(
                effectiveRadius - FootprintBoundaryTolerance,
                0.0f);
            if (footprint.HasPoint(horizontal) ||
                horizontal.DistanceSquaredTo(closestFootprintPoint) <
                    requiredClearance * requiredClearance)
            {
                return false;
            }
        }

        foreach (MaterialsResourceNode resource in CachedResources)
        {
            if (resource == allowedTarget ||
                !GodotObject.IsInstanceValid(resource) ||
                resource.IsQueuedForDeletion() ||
                resource.IsDepleted)
            {
                continue;
            }

            Vector2 resourcePosition = new(
                resource.GlobalPosition.X,
                resource.GlobalPosition.Z);
            float requiredDistance = effectiveRadius +
                resource.InteractionRadius - FootprintBoundaryTolerance;
            if (horizontal.DistanceSquaredTo(resourcePosition) <
                requiredDistance * requiredDistance)
            {
                return false;
            }
        }

        return true;
    }

    private static void RefreshStaticFootprintCache(SceneTree tree)
    {
        ulong physicsFrame = Engine.GetPhysicsFrames();
        if (_cachedTree == tree && _cachedPhysicsFrame == physicsFrame)
        {
            return;
        }

        _cachedTree = tree;
        _cachedPhysicsFrame = physicsFrame;
        CachedBuildings.Clear();
        foreach (Node node in tree.GetNodesInGroup(BuildingEntity.BuildingGroup))
        {
            if (node is BuildingEntity building)
            {
                CachedBuildings.Add(building);
            }
        }

        CachedResources.Clear();
        foreach (Node node in tree.GetNodesInGroup(
                     MaterialsResourceNode.ResourceNodeGroup))
        {
            if (node is MaterialsResourceNode resource)
            {
                CachedResources.Add(resource);
            }
        }
    }

    private static float HorizontalDistanceSquared(
        Vector3 first,
        Vector3 second)
    {
        Vector2 delta = new(first.X - second.X, first.Z - second.Z);
        return delta.LengthSquared();
    }
}
