using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// On-screen debug readout for a vehicle. Toggle with F3.
/// Shows the vehicle you're sitting in, otherwise the one nearest your camera.
///
/// Host: reads the vehicle directly. Client: subscribes to server snapshots (5/sec),
/// so every machine sees the authoritative values. Put one anywhere in the scene.
/// </summary>
public sealed class VehicleDebugOverlay : MonoBehaviour
{
    [SerializeField] private Key toggleKey = Key.F3;
    [SerializeField] private bool visibleOnStart = false;
    [Tooltip("UI scale (1 = 1080p-ish)")]
    [SerializeField] private float scale = 1f;

    private bool _visible;
    private Vehicle _target;
    private Vehicle _subscribedTo;
    private float _nextRetarget;

    private GUIStyle _box, _label, _header, _cell;
    private const float LineHeight = 18f;

    private void Start() => _visible = visibleOnStart;

    private void OnDisable() => Subscribe(null);

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb[toggleKey].wasPressedThisFrame)
            _visible = !_visible;

        if (!_visible)
        {
            Subscribe(null);
            return;
        }

        if (Time.unscaledTime >= _nextRetarget)
        {
            _nextRetarget = Time.unscaledTime + 0.5f;
            _target = PickTarget();
        }

        Subscribe(_target);
    }

    // ============================================================
    // TARGET
    // ============================================================

    private static Vehicle PickTarget()
    {
        // The vehicle the local player is sitting in.
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.LocalClient?.PlayerObject != null)
        {
            var seating = nm.LocalClient.PlayerObject.GetComponent<PlayerSeating>();
            if (seating != null && seating.CurrentSeat != null)
            {
                var v = seating.CurrentSeat.GetComponentInParent<Vehicle>();
                if (v != null) return v;
            }
        }

        // Otherwise the nearest to the camera.
        Camera cam = Camera.main;
        Vector3 from = cam != null ? cam.transform.position : Vector3.zero;

        Vehicle best = null;
        float bestDist = float.MaxValue;
        foreach (var v in FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
        {
            if (!v.IsSpawned) continue;
            float d = (v.transform.position - from).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = v; }
        }
        return best;
    }

    private void Subscribe(Vehicle vehicle)
    {
        if (_subscribedTo == vehicle) return;

        if (_subscribedTo != null && _subscribedTo.IsSpawned) _subscribedTo.SetDebugSubscribed(false);
        _subscribedTo = vehicle;
        if (_subscribedTo != null) _subscribedTo.SetDebugSubscribed(true);
    }

    // ============================================================
    // DRAWING
    // ============================================================

    private void OnGUI()
    {
        if (!_visible) return;
        EnsureStyles();

        Matrix4x4 old = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        GUILayout.BeginArea(new Rect(10, 10, 700, 600));
        GUILayout.BeginVertical(_box);

        if (_target == null || !_target.IsSpawned)
        {
            GUILayout.Label("VEHICLE DEBUG  (F3)   no vehicle found", _header);
            End(old);
            return;
        }

        VehicleDebugSnapshot? snap;
        string source;
        if (_target.IsServer)
        {
            snap = _target.BuildDebugSnapshot();
            source = "host (live)";
        }
        else
        {
            snap = _target.LastDebugSnapshot;
            float age = Time.unscaledTime - _target.LastDebugReceiveTime;
            source = snap.HasValue ? $"from host, {age:0.0}s ago" : "waiting for host...";
        }

        GUILayout.Label($"VEHICLE DEBUG  (F3)   {_target.name}   [{source}]", _header);

        if (snap.HasValue)
            DrawSnapshot(snap.Value);

        End(old);
    }

    private void End(Matrix4x4 old)
    {
        GUILayout.EndVertical();
        GUILayout.EndArea();
        GUI.matrix = old;
    }

    private void DrawSnapshot(in VehicleDebugSnapshot s)
    {
        float kmh = s.ForwardSpeed * 3.6f;

        Section("Control");
        Line($"Source  {s.ControlSource}");
        Line($"Throttle {s.Controls.Throttle,6:0.00}   Steer {s.Controls.Steer,6:0.00}   Brake {s.Controls.Brake,5:0.00}");
        Line($"Axle angle {s.State.SteerAngle,6:0.0}°   Brake applied {s.State.Brake:0.00}");

        Section("Body");
        Line($"Speed {s.ForwardSpeed,6:0.00} m/s ({kmh:0} km/h)   |v| {s.Velocity.magnitude:0.00} m/s   Mass {s.Mass:0} kg");
        Line($"Tilt {s.TiltAngle,5:0.0}°   Wheels grounded {s.WheelsGrounded}/{(s.Wheels?.Length ?? 0)}   "
           + $"{Flag("TIPPED", s.Tipped)}  {Flag("STUCK", s.IsStuck)}");

        Section("Last impact");
        if (s.HasImpact)
            Line($"{s.ImpactSpeed:0.0} m/s   {s.ImpactImpulse:0} N·s   with {s.ImpactWith}   {s.ServerTime - s.ImpactTime:0.0}s ago");
        else
            Line("none yet");

        Section("Wheels");
        if (s.Wheels == null || s.Wheels.Length == 0) { Line("no wheels"); return; }

        Row(_header, "wheel", "ground", "load N", "surface", "speed", "slip", "drive N", "roll N", "side N", "brake");
        foreach (var w in s.Wheels)
        {
            string tags = (w.Driven ? "D" : "") + (w.Braked ? "B" : "");
            Row(_cell,
                $"{w.Name} {tags}",
                w.Grounded ? "yes" : "<color=#ff6060>air</color>",
                $"{w.Load:0}",
                $"{w.Surface}",
                $"{w.ContactSpeed:0.00}",
                Mathf.Abs(w.SlipSpeed) > 0.3f ? $"<color=#ffb040>{w.SlipSpeed:0.00}</color>" : $"{w.SlipSpeed:0.00}",
                $"{w.DriveForce:0}",
                $"{w.RollForce:0}",
                $"{w.SideForce:0}",
                $"{w.BrakeInput:0.00}");
        }
        Line("<size=11>D = driven, B = has brake. Slip in orange = sliding sideways.</size>");
    }

    // ---- Layout helpers ----

    private void Section(string title)
    {
        GUILayout.Space(4);
        GUILayout.Label($"<b>{title}</b>", _label);
    }

    private void Line(string text) => GUILayout.Label(text, _label);

    private static string Flag(string name, bool on) =>
        on ? $"<color=#ff6060><b>{name}</b></color>" : $"<color=#808080>{name.ToLower()}</color>";

    private static readonly float[] ColumnWidths = { 110, 55, 60, 80, 55, 55, 65, 60, 60, 50 };

    private void Row(GUIStyle style, params string[] cells)
    {
        GUILayout.BeginHorizontal();
        for (int i = 0; i < cells.Length; i++)
            GUILayout.Label(cells[i], style, GUILayout.Width(ColumnWidths[i]), GUILayout.Height(LineHeight));
        GUILayout.EndHorizontal();
    }

    private void EnsureStyles()
    {
        if (_box != null) return;

        var bg = new Texture2D(1, 1);
        bg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.75f));
        bg.Apply();

        _box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8) };
        _box.normal.background = bg;

        _label = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 13, margin = new RectOffset(0, 0, 0, 0) };
        _label.normal.textColor = Color.white;

        _header = new GUIStyle(_label) { fontStyle = FontStyle.Bold };
        _header.normal.textColor = new Color(1f, 0.85f, 0.4f);

        _cell = new GUIStyle(_label) { fontSize = 12 };
    }
}