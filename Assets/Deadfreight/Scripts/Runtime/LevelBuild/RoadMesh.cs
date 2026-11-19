// Scripts/Runtime/LevelBuild/RoadMesh.cs
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A road surface along a RoadPath: a flat strip at the road's width, with a skirt folding down
/// each edge to hide any gap with the terrain. Each row across the road has six vertices, so the
/// surface and skirts don't share normals (sharing would shade the surface edges as if tilted):
///
///     1 ──── 2  3 ──── 4      (seen end-on: 0-1 left skirt, 2-3 surface, 4-5 right skirt)
///     |                 |
///     0                 5
/// </summary>
public static class RoadMesh
{
    const int RowSize = 6;
    const float SkirtDepth = 0.6f;
    static readonly int[] StripStarts = { 0, 2, 4 };   // first column of each strip

    public static Mesh Build(RoadPath road, float lift)
    {
        var points = road.Points;
        int rows = points.Count;
        var vertices = new Vector3[rows * RowSize];
        var uvs = new Vector2[rows * RowSize];
        float halfWidth = road.HalfWidth;
        float along = 0f;

        for (int i = 0; i < rows; i++)
        {
            if (i > 0) along += Vector3.Distance(points[i], points[i - 1]);

            // Right of the direction of travel, kept level so the surface is flat across.
            var forward = points[Mathf.Min(i + 1, rows - 1)] - points[Mathf.Max(i - 1, 0)];
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            var right = new Vector3(forward.z, 0f, -forward.x);

            var centre = points[i] + Vector3.up * lift;
            var left = centre - right * halfWidth;
            var rightEdge = centre + right * halfWidth;
            var down = Vector3.down * SkirtDepth;

            int v = i * RowSize;
            vertices[v + 0] = left + down;
            vertices[v + 1] = left;
            vertices[v + 2] = left;
            vertices[v + 3] = rightEdge;
            vertices[v + 4] = rightEdge;
            vertices[v + 5] = rightEdge + down;

            // u across the surface (0-1), v along it in road widths so textures keep their shape.
            float vCoord = along / (halfWidth * 2f);
            uvs[v + 0] = new Vector2(0f, vCoord);
            uvs[v + 1] = new Vector2(0f, vCoord);
            uvs[v + 2] = new Vector2(0f, vCoord);
            uvs[v + 3] = new Vector2(1f, vCoord);
            uvs[v + 4] = new Vector2(1f, vCoord);
            uvs[v + 5] = new Vector2(1f, vCoord);
        }

        // Three strips per row: left skirt (0-1), surface (2-3), right skirt (4-5).
        var triangles = new int[(rows - 1) * 3 * 6];
        int t = 0;
        for (int i = 0; i < rows - 1; i++)
        {
            foreach (int column in StripStarts)
            {
                int a = i * RowSize + column, b = a + 1;              // this row
                int c = a + RowSize, d = b + RowSize;                 // next row
                // Clockwise seen from outside (above for the surface, beside for the skirts).
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = d;
                triangles[t++] = a; triangles[t++] = d; triangles[t++] = b;
            }
        }

        var mesh = new Mesh { name = $"{road.Class} road" };
        if (vertices.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
