using UnityEngine;

/// <summary>
/// Vision-based detection sensor for stealth AI.
/// Detects the player within a cone-shaped field of view using a three-stage pipeline:
/// range check, angle check, then line-of-sight raycast.
///
/// Detection is graduated via a meter rather than binary so that brief glimpses
/// do not instantly trigger a full alert — this is central to stealth game feel,
/// giving the player time to react and break line-of-sight before full detection.
///
/// Setup:
/// 1. Attach to an enemy GameObject.
/// 2. Create an empty child at head height; assign it to the <c>eye</c> field.
/// 3. Assign the player Transform to the <c>player</c> field.
/// 4. Set <c>obstacleMask</c> to every layer that should block vision (e.g. Obstacles, Walls).
/// </summary>
public class VisionSensor : MonoBehaviour
{
    #region Inspector Settings

    [Header("References")]
    [Tooltip("Transform representing the enemy's eye position (create an empty child at head height).")]
    public Transform eye;

    [Tooltip("Reference to the player's Transform.")]
    public Transform player;

    [Header("Vision Settings")]
    [Tooltip("Maximum detection range in world units.")]
    public float range = 8f;

    [Tooltip("Field of view angle in degrees — this is the total cone width, not the half-angle.")]
    public float fovAngle = 90f;

    [Header("Detection Settings")]
    [Tooltip("Rate at which the detection meter fills while the player is visible (units per second).")]
    public float _fillRate = 0.7f;

    [Tooltip("Rate at which the detection meter drains while the player is not visible (units per second).")]
    public float _drainRate = 0.5f;

    [Header("Obstacles")]
    [Tooltip("Layers that block line of sight (walls, obstacles, etc.).")]
    public LayerMask obstacleMask;

    [Header("Debug")]
    [SerializeField] private bool _showGizmos = true;
    [SerializeField] private bool _showDebugOverlay = true;
    [SerializeField] private Color fovColor = new Color(1f, 1f, 0f, 0.3f);
    [SerializeField] private Color visibleColor = Color.red;
    [SerializeField] private Color notVisibleColor = Color.green;

    #endregion

    #region Public State

    /// <summary>
    /// True when the player is currently within FOV and not occluded by an obstacle.
    /// Resets to false every frame before vision checks run.
    /// </summary>
    public bool CanSeePlayer { get; private set; }

    /// <summary>
    /// Normalized detection level in [0, 1]. Rises at <c>_fillRate</c> per second
    /// while the player is visible and falls at <c>_drainRate</c> when not.
    /// A graduated meter reduces false positives from brief glimpses.
    /// </summary>
    public float DetectionMeter { get; private set; }

    /// <summary>
    /// World-space position where the player was last confirmed visible.
    /// Not cleared when sight is lost because the FSM uses this position as the
    /// search destination during the investigate/hunt state after detection lapses.
    /// Call <see cref="ClearLastKnownPosition"/> explicitly once the search concludes.
    /// </summary>
    public Vector3 LastKnownPlayerPos { get; private set; }

    #endregion

    #region Events

    /// <summary>
    /// Fired once when <see cref="DetectionMeter"/> first reaches 1 (full alert).
    /// Events fire on threshold crossing rather than every frame to prevent the
    /// FSM from spam-transitioning while visibility flickers.
    /// </summary>
    public System.Action OnPlayerDetected;

    /// <summary>
    /// Fired once when <see cref="CanSeePlayer"/> transitions from true to false.
    /// Same edge-trigger rationale as <see cref="OnPlayerDetected"/>.
    /// </summary>
    public System.Action OnPlayerLost;

    /// <summary>
    /// Fired each frame the meter value changes by more than a small epsilon.
    /// Passes the new meter value so listeners can drive UI or audio without polling.
    /// </summary>
    public System.Action<float> OnMeterChanged;

    #endregion

    #region Private Fields

    // Per-frame detection pipeline state — reset at the start of every UpdateVision call.
    private bool _inRange;
    private bool _inFov;
    private bool _hasLos;
    private float _angleToTarget;
    private float _distanceToTarget;

    // Edge-detection flags for one-shot event firing.
    private bool _wasFullyDetected;
    private bool _hadVisibilityLastFrame;

    // Cached to avoid per-frame Camera.main property lookup (which calls FindObjectWithTag internally).
    private Camera _camera;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        // FindGameObjectWithTag is slow but ValidateSetup is called only once, so it is acceptable here.
        ValidateSetup();
        _camera = Camera.main;
    }

    private void Update()
    {
        if (eye == null || player == null) return;

        UpdateVision();
        UpdateDetectionMeter();
        FireEvents();
    }

    #endregion

    #region Detection Pipeline

    /// <summary>
    /// Runs the three-stage detection pipeline and updates <see cref="CanSeePlayer"/>
    /// and <see cref="LastKnownPlayerPos"/>.
    /// </summary>
    private void UpdateVision()
    {
        CanSeePlayer = false;
        _inRange = false;
        _inFov = false;
        _hasLos = false;

        Vector3 eyePosition = eye.position;
        Vector3 playerPosition = player.position;
        Vector3 directionToPlayer = playerPosition - eyePosition;
        _distanceToTarget = directionToPlayer.magnitude;

        if (!IsInRange(_distanceToTarget)) return;
        _inRange = true;

        _angleToTarget = Vector3.Angle(eye.forward, directionToPlayer);
        if (!IsInFieldOfView(_angleToTarget)) return;
        _inFov = true;

        if (!HasLineOfSight(eyePosition, directionToPlayer.normalized, _distanceToTarget)) return;
        _hasLos = true;

        CanSeePlayer = true;
        LastKnownPlayerPos = playerPosition;
    }

    /// <summary>Returns true when <paramref name="distance"/> is within the configured range.</summary>
    private bool IsInRange(float distance)
    {
        return distance <= range;
    }

    /// <summary>
    /// Returns true when <paramref name="angle"/> falls within the FOV cone.
    /// <c>fovAngle</c> is the total cone width, so the comparison uses its half-value —
    /// the maximum permitted offset from the eye's forward direction.
    /// </summary>
    private bool IsInFieldOfView(float angle)
    {
        return angle <= fovAngle / 2f;
    }

    /// <summary>
    /// Returns true when no obstacle on <see cref="obstacleMask"/> interrupts the
    /// ray from <paramref name="origin"/> toward the player.
    /// </summary>
    private bool HasLineOfSight(Vector3 origin, Vector3 direction, float distance)
    {
        return !Physics.Raycast(origin, direction, distance, obstacleMask);
    }

    /// <summary>
    /// Advances the detection meter toward 1 while the player is visible and
    /// toward 0 while not. Fires <see cref="OnMeterChanged"/> on any meaningful change.
    /// </summary>
    private void UpdateDetectionMeter()
    {
        float previousMeter = DetectionMeter;

        if (CanSeePlayer)
            DetectionMeter += _fillRate * Time.deltaTime;
        else
            DetectionMeter -= _drainRate * Time.deltaTime;

        DetectionMeter = Mathf.Clamp01(DetectionMeter);

        if (Mathf.Abs(DetectionMeter - previousMeter) > 0.001f)
            OnMeterChanged?.Invoke(DetectionMeter);
    }

    /// <summary>
    /// Fires <see cref="OnPlayerDetected"/> and <see cref="OnPlayerLost"/> on the
    /// frame their respective thresholds are crossed. Edge-trigger logic prevents
    /// the FSM from receiving repeated events while the condition persists.
    /// </summary>
    private void FireEvents()
    {
        bool isDetected = DetectionMeter >= 1f;
        if (isDetected && !_wasFullyDetected)
            OnPlayerDetected?.Invoke();
        _wasFullyDetected = isDetected;

        if (!CanSeePlayer && _hadVisibilityLastFrame)
            OnPlayerLost?.Invoke();
        _hadVisibilityLastFrame = CanSeePlayer;
    }

    /// <summary>
    /// Validates required references at startup and falls back gracefully.
    /// </summary>
    private void ValidateSetup()
    {
        if (eye == null)
        {
            Debug.LogWarning($"[VisionSensor] {gameObject.name}: 'eye' Transform not assigned. Using this transform.", this);
            eye = transform;
        }

        if (player == null)
        {
            Debug.LogWarning($"[VisionSensor] {gameObject.name}: 'player' Transform not assigned. Searching for 'Player' tag.", this);
            // FindGameObjectWithTag is called only once here — performance cost is acceptable.
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
                Debug.Log($"[VisionSensor] Found player: {playerObj.name}", this);
            }
            else
            {
                Debug.LogError($"[VisionSensor] {gameObject.name}: Could not find player. Detection disabled.", this);
            }
        }

        if (obstacleMask == 0)
            Debug.LogWarning($"[VisionSensor] {gameObject.name}: obstacleMask not set. Line-of-sight check may not work correctly.", this);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Resets <see cref="DetectionMeter"/> to zero and clears the full-detection flag.
    /// Call this when the enemy returns to patrol so a new detection cycle starts clean.
    /// </summary>
    public void ResetDetection()
    {
        DetectionMeter = 0f;
        _wasFullyDetected = false;
    }

    /// <summary>
    /// Sets <see cref="LastKnownPlayerPos"/> to <see cref="Vector3.zero"/>.
    /// Call this after the FSM's investigate/search state concludes so stale
    /// data does not influence the next detection cycle.
    /// </summary>
    public void ClearLastKnownPosition()
    {
        LastKnownPlayerPos = Vector3.zero;
    }

    /// <summary>
    /// Returns true when <paramref name="position"/> is within the vision cone,
    /// ignoring line-of-sight. Used by search/investigate behaviors to test
    /// whether a candidate point is plausibly within the enemy's view direction.
    /// </summary>
    public bool IsPositionInFOV(Vector3 position)
    {
        if (eye == null) return false;

        Vector3 directionToPos = position - eye.position;
        float distance = directionToPos.magnitude;
        float angle = Vector3.Angle(eye.forward, directionToPos);

        return IsInRange(distance) && IsInFieldOfView(angle);
    }

    #endregion

    #region Debug Visualization

    private void OnDrawGizmos()
    {
        if (!_showGizmos) return;

        Transform eyeTransform = eye != null ? eye : transform;

        DrawFOVCone(eyeTransform);
        DrawRangeCircle(eyeTransform);

        if (Application.isPlaying && player != null)
            DrawLineOfSight(eyeTransform);
    }

    private void DrawFOVCone(Transform eyeTransform)
    {
        Vector3 eyePos = eyeTransform.position;
        Vector3 forward = eyeTransform.forward;

        float halfFov = fovAngle / 2f;
        Quaternion leftRot = Quaternion.Euler(0, -halfFov, 0);
        Quaternion rightRot = Quaternion.Euler(0, halfFov, 0);

        Vector3 leftDir = leftRot * forward;
        Vector3 rightDir = rightRot * forward;

        Gizmos.color = fovColor;
        Gizmos.DrawRay(eyePos, leftDir * range);
        Gizmos.DrawRay(eyePos, rightDir * range);

        int segments = 20;
        Vector3 prevPoint = eyePos + leftDir * range;

        for (int i = 1; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = Mathf.Lerp(-halfFov, halfFov, t);
            Vector3 dir = Quaternion.Euler(0, angle, 0) * forward;
            Vector3 point = eyePos + dir * range;
            Gizmos.DrawLine(prevPoint, point);
            prevPoint = point;
        }

        Gizmos.color = Color.blue;
        Gizmos.DrawRay(eyePos, forward * (range * 0.5f));
    }

    private void DrawRangeCircle(Transform eyeTransform)
    {
        Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);

        int segments = 32;
        float angleStep = 360f / segments;
        Vector3 center = eyeTransform.position;

        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * angleStep * Mathf.Deg2Rad;
            float angle2 = (i + 1) * angleStep * Mathf.Deg2Rad;
            Vector3 point1 = center + new Vector3(Mathf.Sin(angle1), 0, Mathf.Cos(angle1)) * range;
            Vector3 point2 = center + new Vector3(Mathf.Sin(angle2), 0, Mathf.Cos(angle2)) * range;
            Gizmos.DrawLine(point1, point2);
        }
    }

    private void DrawLineOfSight(Transform eyeTransform)
    {
        Vector3 eyePos = eyeTransform.position;
        Vector3 playerPos = player.position;

        if (CanSeePlayer)
        {
            Gizmos.color = visibleColor;
            Gizmos.DrawLine(eyePos, playerPos);
            Gizmos.DrawWireSphere(playerPos, 0.5f);
        }
        else if (_inRange)
        {
            Gizmos.color = notVisibleColor;
            Gizmos.DrawLine(eyePos, playerPos);

            if (_inFov && !_hasLos)
            {
                Vector3 direction = (playerPos - eyePos).normalized;
                if (Physics.Raycast(eyePos, direction, out RaycastHit hit, _distanceToTarget, obstacleMask))
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawWireSphere(hit.point, 0.2f);
                }
            }
        }

        if (LastKnownPlayerPos != Vector3.zero && !CanSeePlayer)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(LastKnownPlayerPos, 0.3f);
            Gizmos.DrawLine(eyePos, LastKnownPlayerPos);
        }
    }

    private void OnGUI()
    {
        if (!_showDebugOverlay || !Application.isPlaying) return;
        if (eye == null || _camera == null) return;

        Vector3 screenPos = _camera.WorldToScreenPoint(eye.position + Vector3.up * 2.5f);
        if (screenPos.z <= 0) return;

        float barWidth = 80f;
        float barHeight = 10f;
        float x = screenPos.x - barWidth / 2f;
        float y = Screen.height - screenPos.y;

        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(x - 2, y - 2, barWidth + 4, barHeight + 4), Texture2D.whiteTexture);

        GUI.color = Color.Lerp(Color.green, Color.red, DetectionMeter);
        GUI.DrawTexture(new Rect(x, y, barWidth * DetectionMeter, barHeight), Texture2D.whiteTexture);

        GUI.color = Color.white;
        GUI.Box(new Rect(x - 2, y - 2, barWidth + 4, barHeight + 4), "");

        GUI.color = Color.white;
        string status = CanSeePlayer ? "VISIBLE" : "HIDDEN";
        GUI.Label(new Rect(x, y + 15, barWidth, 20), $"{status} ({DetectionMeter:P0})");
    }

    #endregion
}
