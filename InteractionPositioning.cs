using Godot;

public static class InteractionPositioning
{
    private const float MinimumGap = 0.1f;

    public static Vector3 GetRadialPosition(
        Vector3 targetPosition,
        float targetRadius,
        float unitRadius,
        int ordinal,
        float maximumUsableCenterDistance,
        out bool isWithinUsableDistance)
    {
        float effectiveUnitRadius = Mathf.Max(unitRadius, 0.1f);
        float spacing = effectiveUnitRadius * 2.0f + MinimumGap;
        float firstRadius = Mathf.Max(targetRadius, 0.0f) +
            effectiveUnitRadius + MinimumGap;
        int remainingOrdinal = Mathf.Max(ordinal, 0);

        for (int ring = 0; ; ring++)
        {
            float radius = firstRadius + ring * spacing;
            int capacity = Mathf.Max(
                Mathf.FloorToInt(Mathf.Tau * radius / spacing),
                1);
            if (remainingOrdinal >= capacity)
            {
                remainingOrdinal -= capacity;
                continue;
            }

            float angle = -Mathf.Pi * 0.5f +
                Mathf.Tau * remainingOrdinal / capacity;
            isWithinUsableDistance = radius <=
                maximumUsableCenterDistance + 0.001f;
            return targetPosition + new Vector3(
                Mathf.Cos(angle) * radius,
                0.0f,
                Mathf.Sin(angle) * radius);
        }
    }
}
