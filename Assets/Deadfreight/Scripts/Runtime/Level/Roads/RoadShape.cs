// Scripts/Runtime/Level/Roads/RoadShape.cs
using System.Collections.Generic;
using UnityEngine;

namespace Deadfreight.Level
{
    /// <summary>
    /// A plan Road as the builder needs it: a smooth curve through the road's points, sampled
    /// every couple of metres, each sample at the height of the road surface. LevelBuilder carves
    /// the terrain to it (TerrainHeights.CarveRoads) and lays a mesh along it (ToMesh).
    ///
    /// The surface follows the ground, averaged along the road so it doesn't copy every bump, and
    /// never dips below the water line: across water it becomes a raised causeway.
    /// </summary>
    public sealed class RoadShape
    {
        const float Spacing = 2f;           // metres between samples
        const float Smoothing = 40f;        // metres of road the surface height is averaged over
        const float CausewayHeight = 0.6f;  // metres a road over water stands above the water line

        public readonly RoadKind Kind;
        public readonly Vector3[] Points;   // builder-local metres: x east, y the road surface, z north

        RoadShape(RoadKind kind, Vector3[] points)
        {
            Kind = kind;
            Points = points;
        }

        public static RoadShape FromRoad(Road road, LevelPlan plan)
        {
            var flat = Curve(road, plan);

            // The ground under each sample, then averaged along the road.
            var ground = new float[flat.Count];
            for (int i = 0; i < flat.Count; i++)
                ground[i] = PlanGrid.Bicubic(plan.GroundHeight, plan.CellsX, plan.CellsY, flat[i] / plan.CellSize);
            var surface = MovingAverage(ground, Mathf.RoundToInt(Smoothing * 0.5f / Spacing));

            var points = new Vector3[flat.Count];
            for (int i = 0; i < flat.Count; i++)
            {
                float y = plan.HasWater ? Mathf.Max(surface[i], plan.WaterHeight + CausewayHeight) : surface[i];
                points[i] = new Vector3(flat[i].x, y, flat[i].y);
            }
            return new RoadShape(road.Kind, points);
        }

        // ---------- curve ----------

        /// <summary>
        /// A Catmull-Rom curve through the road's points, in world (x, z), sampled about every
        /// Spacing metres. It passes through every point, so branches still meet the road they join.
        /// </summary>
        static List<Vector2> Curve(Road road, LevelPlan plan)
        {
            var controls = new List<Vector2>(road.Points.Count);
            foreach (var cell in road.Points)
                controls.Add(plan.WorldXZ(cell));

            var samples = new List<Vector2>();
            int last = controls.Count - 1;
            for (int i = 0; i < last; i++)
            {
                Vector2 p0 = controls[Mathf.Max(i - 1, 0)], p1 = controls[i], p2 = controls[i + 1], p3 = controls[Mathf.Min(i + 2, last)];
                int count = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / Spacing));
                for (int k = 0; k < count; k++)
                    AddSample(samples, CatmullRom(p0, p1, p2, p3, (float)k / count));
            }
            AddSample(samples, controls[last]);
            return samples;
        }

        /// <summary>Adds a sample unless it repeats the one before (two equal points would leave the road no direction there).</summary>
        static void AddSample(List<Vector2> samples, Vector2 sample)
        {
            if (samples.Count == 0 || (sample - samples[^1]).sqrMagnitude > 0.01f)
                samples.Add(sample);
        }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        /// <summary>Each value averaged with up to <paramref name="half"/> neighbours either side (fewer near the ends).</summary>
        static float[] MovingAverage(float[] values, int half)
        {
            var sums = new float[values.Length + 1];          // sums[i] = values[0] + ... + values[i - 1]
            for (int i = 0; i < values.Length; i++)
                sums[i + 1] = sums[i] + values[i];

            var averaged = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                int from = Mathf.Max(0, i - half), to = Mathf.Min(values.Length, i + half + 1);
                averaged[i] = (sums[to] - sums[from]) / (to - from);
            }
            return averaged;
        }

        // ---------- mesh ----------

        /// <summary>
        /// A flat strip <paramref name="width"/> metres wide along the road, <paramref name="lift"/>
        /// metres above the carved ground so the terrain doesn't show through. UVs: u across the
        /// road (0 left to 1 right), v along it, one unit per road width, so a texture stays square.
        /// </summary>
        public Mesh ToMesh(float width, float lift)
        {
            int count = Points.Length;
            var vertices = new Vector3[count * 2];
            var uvs = new Vector2[count * 2];
            var triangles = new int[(count - 1) * 6];

            float along = 0f;
            var forward = Vector3.forward;
            for (int i = 0; i < count; i++)
            {
                // Direction along the road here, flat: from the sample before to the sample after.
                var span = Points[Mathf.Min(i + 1, count - 1)] - Points[Mathf.Max(i - 1, 0)];
                span.y = 0f;
                if (span.sqrMagnitude > 1e-6f)
                    forward = span.normalized;
                var right = new Vector3(forward.z, 0f, -forward.x) * (width * 0.5f);

                if (i > 0)
                    along += Vector3.Distance(Points[i - 1], Points[i]);
                var centre = Points[i] + Vector3.up * lift;
                vertices[i * 2] = centre - right;
                vertices[i * 2 + 1] = centre + right;
                uvs[i * 2] = new Vector2(0f, along / width);
                uvs[i * 2 + 1] = new Vector2(1f, along / width);
            }

            // Two triangles per stretch, clockwise seen from above so they face up.
            for (int i = 0; i < count - 1; i++)
            {
                int left = i * 2, t = i * 6;
                triangles[t] = left;            // this left
                triangles[t + 1] = left + 2;    // next left
                triangles[t + 2] = left + 1;    // this right
                triangles[t + 3] = left + 2;
                triangles[t + 4] = left + 3;    // next right
                triangles[t + 5] = left + 1;
            }

            var mesh = new Mesh
            {
                name = $"{Kind} road",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,     // a long road has more than 65k vertices
                vertices = vertices,
                uv = uvs,
                triangles = triangles,
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
