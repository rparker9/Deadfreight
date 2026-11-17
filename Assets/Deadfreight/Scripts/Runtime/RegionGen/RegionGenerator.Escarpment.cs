// Scripts/Runtime/RegionGen/RegionGenerator.Escarpment.cs
using UnityEngine;

// The escarpment: one side of the region raised into an upland, behind a steep face that roads
// can only climb at a few passes. The entry and the heist site go on opposite sides, so every
// trip crosses it and choosing a pass is part of choosing a route.
//
//   Land:   RaiseEscarpment, after hill country; MarkCliffs, once rivers and slope are known.
//   Places: entry on an edge the face doesn't cross (EdgeTouchesFace); site on the far side (AcrossEscarpment).
//   Roads:  cliff cells cost cliffCost; any road that still crosses one becomes a cutting (CutThroughCliffs).
public sealed partial class RegionGenerator
{
    const float WobbleNoiseScale = 40f;      // cells across one bend of the face
    const float PassJitter = 0.25f;          // passes sit within this share of even spacing
    const float CliffPassLimit = 0.5f;       // face cells less of a pass than this are cliff
    const int CuttingRadius = 1;             // cells either side of a road cleared when it cuts through

    /// <summary>
    /// A line across the map at a random angle, wandering a little. Land on the upland side is
    /// raised by escarpmentHeight, with a steep face along the line. At each pass the climb is
    /// spread over passRamp cells instead: a long ramp a road can drive.
    /// </summary>
    void RaiseEscarpment()
    {
        // Its own random stream, like hill country.
        var noise = new System.Random(unchecked(seed * 31337 + 6));
        float angle = (float)noise.NextDouble() * Mathf.PI;
        var along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        var upland = new Vector2(-along.y, along.x);
        if (noise.NextDouble() < 0.5) upland = -upland;
        var centre = new Vector2(
            width * (0.5f + ((float)noise.NextDouble() * 2f - 1f) * settings.escarpmentOffset),
            height * (0.5f + ((float)noise.NextDouble() * 2f - 1f) * settings.escarpmentOffset));
        var wobble = RegionNoise.Fbm(width, height, WobbleNoiseScale, 3, noise);

        // Signed distance from the face, and position along it.
        var alongFace = new float[cellCount];
        float faceStart = float.MaxValue, faceEnd = float.MinValue;
        for (int i = 0; i < cellCount; i++)
        {
            var offset = new Vector2(i % width, i / width) - centre;
            alongFace[i] = Vector2.Dot(offset, along);
            map.Scarp[i] = Vector2.Dot(offset, upland) + settings.escarpmentWobble * wobble[i] * 2f;
            if (Mathf.Abs(map.Scarp[i]) < 1f)
            {
                faceStart = Mathf.Min(faceStart, alongFace[i]);
                faceEnd = Mathf.Max(faceEnd, alongFace[i]);
            }
        }
        if (faceStart > faceEnd) return;    // the line missed the map: no escarpment this time

        // Passes, spread along the part of the face inside the map.
        int passCount = noise.Next(settings.passCountMin, Mathf.Max(settings.passCountMin, settings.passCountMax) + 1);
        var spots = new float[passCount];
        for (int k = 0; k < passCount; k++)
        {
            float jitter = ((float)noise.NextDouble() * 2f - 1f) * PassJitter;
            spots[k] = faceStart + (faceEnd - faceStart) * (k + 0.5f + jitter) / passCount;
        }

        float rise = settings.escarpmentHeight / settings.relief;
        float waterShare = GridOps.ShareBelow(map.Elevation, settings.seaLevel);
        for (int i = 0; i < cellCount; i++)
        {
            float pass = 0f;
            foreach (float spot in spots)
            {
                float t = (alongFace[i] - spot) / settings.passWidth;
                pass = Mathf.Max(pass, Mathf.Exp(-t * t));
            }
            map.ScarpPass[i] = pass;

            float halfWidth = Mathf.Lerp(settings.escarpmentFace, settings.passRamp, pass);
            map.Elevation[i] += rise * RegionMath.SmoothStep(-halfWidth, halfWidth, map.Scarp[i]);
        }

        // The rescale stretches or squashes heights; remember the face's true height for the 3D build.
        float stretch = GridOps.KeepWaterShare(map.Elevation, settings.seaLevel, waterShare);
        map.ScarpRise = settings.escarpmentHeight * stretch;
        map.HasEscarpment = true;
    }

    /// <summary>The face, except at passes and where a river has already cut through it.</summary>
    void MarkCliffs()
    {
        if (!map.HasEscarpment) return;
        for (int i = 0; i < cellCount; i++)
        {
            map.Cliff[i] = Mathf.Abs(map.Scarp[i]) < settings.escarpmentFace
                && map.ScarpPass[i] < CliffPassLimit
                && !map.Water[i] && !map.River[i];
        }
    }

    /// <summary>Whether the face reaches this map edge. Entries go on edges it doesn't, so they start on one side.</summary>
    bool EdgeTouchesFace(MapEdge edge)
    {
        if (!map.HasEscarpment) return false;
        int edgeLength = edge is MapEdge.West or MapEdge.East ? height : width;
        for (int t = 0; t < edgeLength; t++)
            if (Mathf.Abs(map.Scarp[map.Index(EdgeCell(edge, t))]) < settings.escarpmentFace) return true;
        return false;
    }

    /// <summary>True if the cell is on the other side of the escarpment from the entry (always true without one).</summary>
    bool AcrossEscarpment(Vector2Int cell)
    {
        if (!map.HasEscarpment) return true;
        return Mathf.Sign(map.Scarp[map.Index(cell)]) != Mathf.Sign(map.Scarp[map.Index(map.Entry.Cell)]);
    }

    /// <summary>
    /// Roads that crossed the face anyway (no pass could be reached) become cuttings: the cells
    /// around them stop being cliff and count as a pass, so the 3D build doesn't draw a sheer face
    /// across the road.
    /// </summary>
    void CutThroughCliffs()
    {
        if (!map.HasEscarpment) return;
        for (int i = 0; i < cellCount; i++)
        {
            if (!map.Road[i] || !map.Cliff[i]) continue;
            map.Cuttings++;

            int cx = i % width, cy = i / width;
            for (int dy = -CuttingRadius; dy <= CuttingRadius; dy++)
            for (int dx = -CuttingRadius; dx <= CuttingRadius; dx++)
            {
                var c = new Vector2Int(cx + dx, cy + dy);
                if (!map.InBounds(c)) continue;
                int j = map.Index(c);
                map.Cliff[j] = false;
                map.ScarpPass[j] = 1f;
            }
        }
    }
}
