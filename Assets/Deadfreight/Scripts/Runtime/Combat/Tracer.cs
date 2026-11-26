using UnityEngine;

/// <summary>Minimal bullet tracer: a two-point LineRenderer that destroys itself.</summary>
[RequireComponent(typeof(LineRenderer))]
public sealed class Tracer : MonoBehaviour
{
    [Unit("s")] [SerializeField, Min(0.01f)] private float duration = 0.05f;

    public void Init(Vector3 from, Vector3 to)
    {
        var line = GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        Destroy(gameObject, duration);
    }
}
