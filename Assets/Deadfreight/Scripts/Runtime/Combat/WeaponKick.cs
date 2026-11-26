using UnityEngine;

/// <summary>
/// Recoil kick for the gun views: each shot adds to a target that fades back to zero,
/// and the current value eases toward the target (same easing as the camera recoil).
/// Back = metres the gun slides back, Up = degrees it tips up.
/// </summary>
public sealed class WeaponKick
{
    private Vector2 _target;
    private Vector2 _current;

    public float Back => _current.x;
    public float Up => _current.y;

    public void Add(WeaponDefinition def) => _target += new Vector2(def.kickBack, def.kickUp);

    public void Update(WeaponDefinition def, float dt)
    {
        if (_target == Vector2.zero && _current == Vector2.zero) return;

        _target *= Mathf.Exp(-def.kickReturnSpeed * dt);
        _current = Vector2.Lerp(_current, _target, 1f - Mathf.Exp(-def.kickSnappiness * dt));

        if (_target.sqrMagnitude < 1e-8f && _current.sqrMagnitude < 1e-8f)
            _target = _current = Vector2.zero;
    }
}
