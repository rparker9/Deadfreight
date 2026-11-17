// Scripts/Runtime/RegionGen/RegionGenerator.Roads.cs
using System.Collections.Generic;
using UnityEngine;

// Stage 3: roads. Entry → trunk → fork → main road or an alternate → site; lanes to the stops.
public sealed partial class RegionGenerator
{
    const float Impassable = 1e6f;
    const float LaneOnRoadCost = 0.01f;     // lanes run along existing road almost for free
    const int ViaMargin = 8;                // cells a via keeps from the map edge
    const int ViaSearchTries = 1500;        // random cells looked at to find one via
    const float ViaSpacing = 0.35f;         // vias keep this fraction of the fork-site distance apart
    const float MinRatioWindow = 0.02f;     // stop steering once the detour window is this narrow

    // Places a back road would plausibly run past.
    static readonly PlaceKind[] ViaKinds =
        { PlaceKind.Farm, PlaceKind.Church, PlaceKind.Mill, PlaceKind.Store, PlaceKind.Graveyard };

    // ---------------- main road and trunk ----------------

    /// <summary>
    /// The main road: a straight stub in from the edge, then the easiest ground to the site.
    /// Its first trunkLength cells are the trunk every route shares; the fork is where it ends.
    /// </summary>
    bool BuildMainRoad()
    {
        finder = new GridPathfinder(width, height);
        baseCost = BaseCost();

        var cells = new List<Vector2Int>();
        for (int k = 0; k < settings.entryStub; k++) cells.Add(map.Entry.StubCell(k));

        var rest = finder.Find(map.Entry.StubCell(settings.entryStub), map.Site.Cell, baseCost, settings.heuristicWeight);
        if (rest == null)
        {
            map.Failure = "Water cuts the entry off from the site.";
            return false;
        }
        cells.AddRange(rest);
        MarkRoad(cells, isMain: true);

        // Short roads keep most of their length past the fork.
        int forkIndex = Mathf.Min(settings.trunkLength, cells.Count / 3);
        map.Fork = cells[forkIndex];

        map.Trunk = new Route { Name = "Trunk" };
        map.Trunk.Cells.AddRange(cells.GetRange(0, forkIndex + 1));                // the fork cell ends the trunk...
        var main = new Route { Name = "Main" };
        main.Cells.AddRange(cells.GetRange(forkIndex, cells.Count - forkIndex));   // ...and starts each route
        map.Routes.Add(main);

        trunkSeconds = DrivingSeconds(map.Trunk.Cells);
        return true;
    }

    /// <summary>Cost of building road on each cell, from the land alone.</summary>
    float[] BaseCost()
    {
        var cost = new float[cellCount];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            float c = 1f + map.Slope[i] * settings.slopeCost;
            if (map.Forest[i]) c += settings.forestCost;
            if (map.Marsh[i]) c += settings.marshCost;
            if (map.River[i]) c += settings.fordCost;
            if (map.Water[i]) c += Impassable;

            // Keep roads off the map edge, except where the road comes in.
            bool atEntry = Distance(new Vector2Int(x, y), map.Entry.Cell) < settings.entryClear;
            int toEdge = Mathf.Min(Mathf.Min(x, width - 1 - x), Mathf.Min(y, height - 1 - y));
            if (!atEntry) c += Mathf.Max(0, settings.edgeMargin - toEdge) * settings.edgeCost;

            cost[i] = c;
        }
        return cost;
    }

    // ---------------- alternates ----------------

    /// <summary>
    /// Alternates leave the fork and meet the main road again at the site. Each bows out through
    /// a via point, on alternating sides of the main road, and must take between altMinMinutes and
    /// altMaxMinutes one way (trunk included). Each is routed after the last, so it avoids all
    /// road built so far.
    /// </summary>
    void BuildAlternates()
    {
        var vias = new List<Vector2Int>();
        for (int k = 0; k < settings.alternates; k++)
        {
            int side = k % 2 == 0 ? 1 : -1;
            var cost = AlternateCost();
            if (!TryBuildAlternate(side, cost, vias, out var cells, out var viaCell)) continue;

            var via = new Place { Kind = ViaKinds[rng.Next(ViaKinds.Length)], Cell = viaCell, IsVia = true };
            map.Places.Add(via);
            vias.Add(viaCell);
            MarkRoad(cells, isMain: false);

            var route = new Route { Name = $"Alt {map.Routes.Count}", Via = via };
            route.Cells.AddRange(cells);
            map.Routes.Add(route);
        }
    }

    /// <summary>
    /// Tries via points until a route lands inside the time window. Each miss steers the next
    /// try: too short, and the next via must be a bigger detour; too long, a smaller one. If none
    /// fits, keeps the closest miss when it is within altTolerance minutes of the window.
    /// </summary>
    bool TryBuildAlternate(int side, float[] cost, List<Vector2Int> usedVias, out List<Vector2Int> bestCells, out Vector2Int bestVia)
    {
        // Allowed detour ratio; narrows as tries come back too short or too long.
        float ratioMin = settings.viaMin, ratioMax = settings.viaMax;

        bestCells = null;
        bestVia = default;
        float bestMiss = float.MaxValue;    // minutes outside the window

        for (int attempt = 0; attempt < settings.viaTries; attempt++)
        {
            if (ratioMax - ratioMin < MinRatioWindow) break;    // nothing left to try on this side
            if (!TryPickVia(side, ratioMin, ratioMax, usedVias, out var via, out float ratio)) break;

            var cells = PathThrough(via, cost);
            if (cells == null) continue;

            float minutes = (DrivingSeconds(cells) + trunkSeconds) / 60f;
            float miss = Mathf.Max(settings.altMinMinutes - minutes, minutes - settings.altMaxMinutes, 0f);
            if (miss < bestMiss)
            {
                bestMiss = miss;
                bestCells = cells;
                bestVia = via;
            }
            if (miss == 0f) return true;

            map.RejectedAlternates++;
            if (minutes < settings.altMinMinutes) ratioMin = ratio;     // too short: next via further out
            else ratioMax = ratio;                                      // too long: next via further in
        }

        return bestCells != null && bestMiss <= settings.altTolerance;
    }

    /// <summary>
    /// A random via point that:
    ///   - is buildable and on the wanted side of the fork-site line,
    ///   - makes a detour (fork→via + via→site, over fork→site) within [ratioMin, ratioMax],
    ///   - sits over the middle of the fork-site line (viaAlongMin..viaAlongMax), so the route
    ///     bows out instead of leaving a long leg alongside the main road,
    ///   - is well clear of earlier vias.
    /// </summary>
    bool TryPickVia(int side, float ratioMin, float ratioMax, List<Vector2Int> usedVias, out Vector2Int via, out float ratio)
    {
        Vector2Int fork = map.Fork, site = map.Site.Cell;
        float direct = Distance(fork, site);

        for (int t = 0; t < ViaSearchTries; t++)
        {
            var c = new Vector2Int(rng.Next(ViaMargin, width - ViaMargin), rng.Next(ViaMargin, height - ViaMargin));
            if (!Buildable(c) || Side(fork, site, c) != side) continue;

            float r = (Distance(fork, c) + Distance(c, site)) / direct;
            if (r < ratioMin || r > ratioMax) continue;

            float along = AlongLine(fork, site, c);
            if (along < settings.viaAlongMin || along > settings.viaAlongMax) continue;

            if (IsNearAny(c, usedVias, direct * ViaSpacing)) continue;

            via = c;
            ratio = r;
            return true;
        }

        via = default;
        ratio = 0f;
        return false;
    }

    /// <summary>Fork → via → site as one list of cells, or null if water blocks it.</summary>
    List<Vector2Int> PathThrough(Vector2Int via, float[] cost)
    {
        var toVia = finder.Find(map.Fork, via, cost, settings.heuristicWeight);
        var fromVia = finder.Find(via, map.Site.Cell, cost, settings.heuristicWeight);
        if (toVia == null || fromVia == null) return null;

        toVia.RemoveAt(toVia.Count - 1);    // the via is also the first cell of fromVia
        toVia.AddRange(fromVia);
        return toVia;
    }

    /// <summary>
    /// Routing cost for the next alternate: the land's cost, raised near road built so far so the
    /// route keeps its distance. With falloff the push is strongest on the road and fades to
    /// nothing at avoidRadius, so there is no band edge to run along. Reusing road is cheap near
    /// the fork and site, where routes have to meet.
    /// </summary>
    float[] AlternateCost()
    {
        int radius = settings.avoidRadius;
        var distance = StepsFromRoad(radius);
        var cost = new float[cellCount];

        for (int i = 0; i < cellCount; i++)
        {
            float push;
            if (settings.avoidFalloff)
                push = 1f + (settings.avoidCost - 1f) * Mathf.Clamp01(1f - distance[i] / (radius + 1f));
            else
                push = distance[i] <= radius ? settings.avoidCost : 1f;
            cost[i] = baseCost[i] * push;

            if (map.Road[i] && NearForkOrSite(map.CellAt(i)))
                cost[i] = baseCost[i] * settings.reuseCost;
        }
        return cost;
    }

    /// <summary>Steps (4-neighbour) from each cell to the nearest road, capped at maxSteps + 1.</summary>
    int[] StepsFromRoad(int maxSteps)
    {
        var steps = new int[cellCount];
        var frontier = new Queue<int>();
        for (int i = 0; i < cellCount; i++)
        {
            steps[i] = map.Road[i] ? 0 : maxSteps + 1;
            if (map.Road[i]) frontier.Enqueue(i);
        }

        // Breadth-first outward from every road cell at once.
        while (frontier.Count > 0)
        {
            int i = frontier.Dequeue();
            int next = steps[i] + 1;
            if (next > maxSteps) continue;

            int x = i % width, y = i / width;
            if (x > 0) Reach(i - 1, next, steps, frontier);
            if (x < width - 1) Reach(i + 1, next, steps, frontier);
            if (y > 0) Reach(i - width, next, steps, frontier);
            if (y < height - 1) Reach(i + width, next, steps, frontier);
        }
        return steps;
    }

    static void Reach(int cell, int stepCount, int[] steps, Queue<int> frontier)
    {
        if (steps[cell] <= stepCount) return;     // already reached as close or closer
        steps[cell] = stepCount;
        frontier.Enqueue(cell);
    }

    bool NearForkOrSite(Vector2Int c) =>
        Distance(c, map.Fork) < settings.sharedEnds || Distance(c, map.Site.Cell) < settings.sharedEnds;

    // ---------------- lanes ----------------

    /// <summary>Every stop that isn't on a road gets a lane to the nearest road (not right at the entry).</summary>
    void BuildLanes()
    {
        var cost = new float[cellCount];
        for (int i = 0; i < cellCount; i++) cost[i] = map.Road[i] ? LaneOnRoadCost : baseCost[i];

        foreach (var place in map.Places)
        {
            if (place.IsVia || map.Road[map.Index(place.Cell)]) continue;

            var target = NearestJoinableRoad(place.Cell);
            if (target == null) continue;

            var lane = finder.Find(place.Cell, target.Value, cost, settings.heuristicWeight);
            if (lane == null) continue;

            MarkRoad(lane, isMain: false);
            foreach (var c in lane) cost[map.Index(c)] = LaneOnRoadCost;
        }
    }

    Vector2Int? NearestJoinableRoad(Vector2Int from)
    {
        Vector2Int? best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < cellCount; i++)
        {
            if (!map.Road[i]) continue;
            var c = map.CellAt(i);
            if (Distance(c, map.Entry.Cell) < settings.entryClear) continue;

            float sqr = (c - from).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = c;
            }
        }
        return best;
    }

    // ---------------- small helpers ----------------

    void MarkRoad(List<Vector2Int> cells, bool isMain)
    {
        foreach (var c in cells)
        {
            int i = map.Index(c);
            map.Road[i] = true;
            if (isMain) map.MainRoad[i] = true;
        }
    }

    /// <summary>Which side of the line a→b point p is on: +1 or -1.</summary>
    static int Side(Vector2Int a, Vector2Int b, Vector2Int p)
    {
        float cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        return cross >= 0f ? 1 : -1;
    }

    /// <summary>How far along the line a→b point p sits: 0 at a, 1 at b (outside 0-1 if beyond).</summary>
    static float AlongLine(Vector2Int a, Vector2Int b, Vector2Int p)
    {
        Vector2 ab = b - a, ap = p - a;
        return Vector2.Dot(ap, ab) / ab.sqrMagnitude;
    }

    static bool IsNearAny(Vector2Int c, List<Vector2Int> others, float distance)
    {
        foreach (var o in others)
            if (Distance(c, o) < distance) return true;
        return false;
    }
}
