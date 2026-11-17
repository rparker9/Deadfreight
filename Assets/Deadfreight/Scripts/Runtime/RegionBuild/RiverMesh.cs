// Scripts/Runtime/RegionBuild/RiverMesh.cs
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A river's water: a flat strip along its line at the water surface, a little wider than the
/// channel so its edges tuck under the banks. u runs across (0-1), v along in metres / 4, so a
/// flowing-water material can scroll along v.
/// </summary>
public static class RiverMesh
{
    const float Lift = 0.02f;           // above the surface height, so it never fights the bank
    const float BankOverlap = 0.5f;     // metres past the channel edge, under the bank
    const float MetresPerV = 4f;

    public static Mesh Build(RiverChannel river)
    {
        var points = river.Points;
        int rows = points.Count;
        var vertices = new Vector3[rows * 2];
        var uvs = new Vector2[rows * 2];
        float along = 0f;

        for (int i = 0; i < rows; i++)
        {
            if (i > 0) along += Vector3.Distance(points[i], points[i - 1]);

            var forward = points[Mathf.Min(i + 1, rows - 1)] - points[Mathf.Max(i - 1, 0)];
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            var right = new Vector3(forward.z, 0f, -forward.x);

            var centre = points[i] + Vector3.up * Lift;
            float halfWidth = river.HalfWidths[i] + BankOverlap;
            vertices[i * 2] = centre - right * halfWidth;
            vertices[i * 2 + 1] = centre + right * halfWidth;
            uvs[i * 2] = new Vector2(0f, along / MetresPerV);
            uvs[i * 2 + 1] = new Vector2(1f, along / MetresPerV);
        }

        // Clockwise seen from above (same winding as the road surface).
        var triangles = new int[(rows - 1) * 6];
        int t = 0;
        for (int i = 0; i < rows - 1; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            triangles[t++] = a; triangles[t++] = c; triangles[t++] = d;
            triangles[t++] = a; triangles[t++] = d; triangles[t++] = b;
        }

        var mesh = new Mesh { name = "River" };
        if (vertices.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
