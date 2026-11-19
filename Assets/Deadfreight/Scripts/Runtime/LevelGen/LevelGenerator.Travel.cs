// Scripts/Runtime/LevelGen/LevelGenerator.Travel.cs
using System.Collections.Generic;
using UnityEngine;

// Stage 4: how fast each cell drives, and what each route adds up to.
public sealed partial class LevelGenerator
{
    const float MetresPerMile = 1609f;
    const float MetresPerSecondPerMph = 0.447f;
    const float DiagonalStep = 1.41421356f;

    static readonly RoadCondition[] ConditionFlags = { RoadCondition.Steep };

    /// <summary>
    /// Driving speed on a cell. The main road and trunk are graded; everything else is a dirt back
    /// road. Steep ground slows either. Reads MainRoad, so it is only right once the main road exists.
    /// </summary>
    public static float SpeedMph(LevelLayout map, LevelSettings settings, int i)
    {
        float mph = settings.mainRoadSpeed;
        if (!map.MainRoad[i]) mph *= settings.backRoadFactor;
        if (map.Slope[i] > settings.steepSlope) mph *= settings.steepFactor;
        return mph;
    }

    /// <summary>Seconds to drive a path from its first cell to its last.</summary>
    float DrivingSeconds(List<Vector2Int> cells)
    {
        float seconds = 0f;
        for (int k = 1; k < cells.Count; k++)
        {
            float metres = StepLength(cells[k - 1], cells[k]) * map.CellSize;
            seconds += metres / (SpeedMph(map, settings, map.Index(cells[k])) * MetresPerSecondPerMph);
        }
        return seconds;
    }

    /// <summary>Miles and minutes for the trunk and every route; One Way adds the trunk to each route.</summary>
    void MeasureRoutes()
    {
        MeasureLeg(map.Trunk);
        map.Trunk.OneWayMiles = map.Trunk.LegMiles;
        map.Trunk.OneWayMinutes = map.Trunk.LegMinutes;

        foreach (var route in map.Routes)
        {
            MeasureLeg(route);
            route.OneWayMiles = route.LegMiles + map.Trunk.LegMiles;
            route.OneWayMinutes = route.LegMinutes + map.Trunk.LegMinutes;
        }
    }

    /// <summary>Length, driving time and condition shares of one route's own cells.</summary>
    void MeasureLeg(Route route)
    {
        var cells = route.Cells;
        float metres = 0f;
        for (int k = 1; k < cells.Count; k++) metres += StepLength(cells[k - 1], cells[k]) * map.CellSize;

        route.LegMiles = metres / MetresPerMile;
        route.LegMinutes = DrivingSeconds(cells) / 60f;

        var counts = new Dictionary<RoadCondition, int>();
        foreach (var c in cells)
        {
            var condition = map.ConditionAt(map.Index(c), settings.steepSlope);
            foreach (var flag in ConditionFlags)
                if ((condition & flag) != 0) counts[flag] = counts.TryGetValue(flag, out int n) ? n + 1 : 1;
        }
        foreach (var pair in counts) route.Share[pair.Key] = pair.Value / (float)cells.Count;
    }

    /// <summary>1 for a straight step between neighbouring cells, √2 for a diagonal one.</summary>
    static float StepLength(Vector2Int from, Vector2Int to) =>
        (to - from).sqrMagnitude == 2 ? DiagonalStep : 1f;
}
