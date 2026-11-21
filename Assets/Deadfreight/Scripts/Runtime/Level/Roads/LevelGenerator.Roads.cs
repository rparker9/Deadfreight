// Scripts/Runtime/Level/Roads/LevelGenerator.Roads.cs
using System.Collections.Generic;
using UnityEngine;

// Stage 4: roads. The main road from the start to the site, then a branch from each settlement
// to the nearest road, nearest settlements first, so later branches can join earlier ones.
// The roads form a tree: there's one way between any two places.
//
// Each road is the cheapest path over the cells (an A* search). From a cell, a road can step
// to any cell up to Step Reach away, not just its 8 neighbours, so it can head in 32
// directions and doesn't zig-zag. A step costs its length, more for climbing steeply and much
// more for crossing water. Grade is measured along the step, so a road can run across a steep
// hillside but not straight up it.
// (After Galin et al., "Procedural Generation of Roads", Eurographics 2010.)
public sealed partial class LevelGenerator
{
    const int StepReach = 3;            // cells a road can step at once: 32 directions
    const float SteepPenalty = 10f;     // extra cost multiplier on climbs steeper than Max Road Grade

    static readonly Vector2Int[] RoadSteps = MakeRoadSteps(StepReach);

    readonly CellQueue roadQueue = new();

    // For every cell a road passes over: the index of the road point a branch joins at, or -1.
    int[] roadJoin;

    void PlaceRoads()
    {
        roadJoin = new int[cellCount];
        System.Array.Fill(roadJoin, -1);

        var main = FindRoad(plan.Start.Cell, plan.Site.Cell);
        if (main == null)
        {
            plan.Failure = "No road could reach the site from the start.";
            return;
        }
        main.Insert(0, plan.Entry.Cell);        // the road in: straight from the map edge to the start
        AddRoad(RoadKind.Main, main);

        foreach (var place in SettlementsNearestFirst())
        {
            if (roadJoin[plan.Index(place.Cell)] >= 0)
                continue;       // a road already passes through it

            var branch = FindRoad(place.Cell, null);
            if (branch == null)
                continue;

            // The search stops on the first road cell it reaches; end exactly on that road's
            // nearest point, so the two roads meet.
            var join = plan.CellAt(roadJoin[plan.Index(branch[^1])]);
            if (join != branch[^1])
                branch.Add(join);
            AddRoad(RoadKind.Branch, branch);
        }
    }

    // ---------------- search ----------------

    /// <summary>
    /// The cheapest path from a cell to another (<paramref name="to"/>), or to the nearest road
    /// (<paramref name="to"/> null). Null if there's no way there inside the playable area.
    /// </summary>
    List<Vector2Int> FindRoad(Vector2Int from, Vector2Int? to)
    {
        var cost = new float[cellCount];
        var cameFrom = new int[cellCount];
        var done = new bool[cellCount];
        System.Array.Fill(cost, float.MaxValue);

        int start = plan.Index(from);
        int goal = to.HasValue ? plan.Index(to.Value) : -1;
        cost[start] = 0f;
        cameFrom[start] = -1;
        roadQueue.Clear();
        roadQueue.Push(start, Estimate(from, to));

        while (roadQueue.Count > 0)
        {
            int i = roadQueue.Pop().cell;
            if (done[i])
                continue;       // a costlier copy, queued before a cheaper way here was found
            done[i] = true;

            bool reached = goal >= 0 ? i == goal : roadJoin[i] >= 0;
            if (reached)
                return Trace(cameFrom, i);

            var cell = plan.CellAt(i);
            foreach (var step in RoadSteps)
            {
                var next = cell + step;
                if (!plan.InPlayArea(next))
                    continue;

                int n = plan.Index(next);
                if (done[n])
                    continue;

                float c = cost[i] + StepCost(cell, step);
                if (c >= cost[n])
                    continue;

                cost[n] = c;
                cameFrom[n] = i;
                roadQueue.Push(n, c + Estimate(next, to));
            }
        }
        return null;
    }

    /// <summary>
    /// A* guess at the cost left: the straight-line metres to the target. Never more than the
    /// real cost (a metre of road costs at least 1), so the search still finds the cheapest path.
    /// Zero when searching for the nearest road, which makes it a plain Dijkstra search.
    /// </summary>
    float Estimate(Vector2Int cell, Vector2Int? to) =>
        to.HasValue ? Vector2Int.Distance(cell, to.Value) * settings.cellSize : 0f;

    /// <summary>
    /// The cost of one step, summed one cell-length at a time along it: each part costs its
    /// length, times more for its grade and more again under water.
    /// </summary>
    float StepCost(Vector2Int from, Vector2Int step)
    {
        int parts = Mathf.Max(Mathf.Abs(step.x), Mathf.Abs(step.y));
        float partLength = step.magnitude * settings.cellSize / parts;

        float cost = 0f;
        float previous = plan.GroundHeight[plan.Index(from)];
        for (int k = 1; k <= parts; k++)
        {
            Vector2 at = (Vector2)from + (Vector2)step * ((float)k / parts);
            float height = PlanGrid.Bilinear(plan.GroundHeight, cellsX, cellsY, at);
            float grade = Mathf.Abs(height - previous) / partLength;

            float steepness = grade / settings.maxRoadGrade;
            float factor = 1f + settings.roadGradeCost * steepness * steepness;
            if (grade > settings.maxRoadGrade)
                factor *= SteepPenalty;
            if (height < plan.WaterHeight)
                factor *= settings.roadWaterCost;

            cost += partLength * factor;
            previous = height;
        }
        return cost;
    }

    /// <summary>The path back from the goal to the start, in start-to-goal order.</summary>
    List<Vector2Int> Trace(int[] cameFrom, int goal)
    {
        var path = new List<Vector2Int>();
        for (int i = goal; i >= 0; i = cameFrom[i])
            path.Add(plan.CellAt(i));
        path.Reverse();
        return path;
    }

    // ---------------- network ----------------

    /// <summary>
    /// Adds a road to the plan, and marks every cell it passes over, so later branches can join
    /// it. Each cell joins at whichever end of its step is nearer.
    /// </summary>
    void AddRoad(RoadKind kind, List<Vector2Int> points)
    {
        plan.Roads.Add(new Road(kind, points));

        for (int p = 1; p < points.Count; p++)
        {
            Vector2Int a = points[p - 1], b = points[p];
            int parts = Mathf.Max(1, Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y)));
            for (int k = 0; k <= parts; k++)
            {
                var cell = Vector2Int.RoundToInt(Vector2.Lerp(a, b, (float)k / parts));
                if (!plan.InBounds(cell))
                    continue;

                int i = plan.Index(cell);
                if (roadJoin[i] < 0)
                    roadJoin[i] = plan.Index(k * 2 <= parts ? a : b);
            }
        }
    }

    /// <summary>Settlements, nearest the start first.</summary>
    List<Place> SettlementsNearestFirst()
    {
        var settlements = new List<Place>();
        foreach (var place in plan.Places)
            if (place.Kind is not (PlaceKind.Start or PlaceKind.Site))
                settlements.Add(place);

        var start = plan.Start.Cell;
        settlements.Sort((p, q) => Vector2Int.Distance(p.Cell, start).CompareTo(Vector2Int.Distance(q.Cell, start)));
        return settlements;
    }

    /// <summary>
    /// Every step from (0, 0) to a cell up to <paramref name="reach"/> away, leaving out steps
    /// that just repeat a shorter one (like (2, 2), which is (1, 1) twice).
    /// </summary>
    static Vector2Int[] MakeRoadSteps(int reach)
    {
        var steps = new List<Vector2Int>();
        for (int y = -reach; y <= reach; y++)
        for (int x = -reach; x <= reach; x++)
            if ((x != 0 || y != 0) && GreatestCommonDivisor(Mathf.Abs(x), Mathf.Abs(y)) == 1)
                steps.Add(new Vector2Int(x, y));
        return steps.ToArray();
    }

    static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return a;
    }
}
