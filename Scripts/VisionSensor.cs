using UnityEngine;

/// <summary>
/// Vision-based detection sensor for stealth AI.
/// Detects player within field of view with line-of-sight check.
/// Detection meter gradually fills when player visible, decays when not.
/// 
/// Setup:
/// 1. Attach to enemy GameObject
/// 2. Create empty child "Eye" at head height, assign to 'eye' field
/// 3. Assign player Transform to 'player' field
/// 4. Set obstacleMask to layers that block vision (e.g., "Obstacles", "Walls")
/// </summary>
public class VisionSensor : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Transform representing the enemy's eye position (create empty child at head)")]
    public Transform eye;
    
    [Tooltip("Reference to the player's Transform")]
    public Transform player;

    [Header("Vision Settings")]
    [Tooltip("Maximum detection range in units")]
    public float range = 8f;
    
    [Tooltip("Field of view angle in degrees (total cone width)")]
    public float fovAngle = 90f;
    
    [Header("Detection Settings")]
    [Tooltip("How fast detection meter fills when player is visible (per second)")]
    public float detectSpeed = 0.7f;
    
    [Tooltip("How fast detection meter decays when player is not visible (per second)")]
    public float decaySpeed = 0.5f;
    
    [Header("Obstacles")]
    [Tooltip("Layers that block line of sight (walls, obstacles)")]
    public LayerMask obstacleMask;

    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private bool showDebugUI = true;
    [SerializeField] private Color fovColor = new Color(1f, 1f, 0f, 0.3f);
    [SerializeField] private Color visibleColor = Color.red;
    [SerializeField] private Color notVisibleColor = Color.green;

    // Output properties (read-only)
    /// <summary>True if player is currently within FOV and not blocked by obstacles.</summary>
    public bool CanSeePlayer { get; private set; }
    
    /// <summary>Detection level from 0 (unaware) to 1 (fully detected).</summary>
    public float DetectionMeter { get; private set; }
    
    /// <summary>Last position where player was visible. Zero if never seen.</summary>
    public Vector3 LastKnownPlayerPos { get; private set; }

    // Internal state
    private bool isPlayerInRange;
    private bool isPlayerInFOV;
    private bool hasLineOfSight;
    private float angleToPlayer;
    private float distanceToPlayer;
    
    // Events for FSM integration
    public System.Action OnPlayerDetected;      // Fired when meter reaches 1
    public System.Action OnPlayerLost;          // Fired when CanSeePlayer becomes false
    public System.Action<float> OnMeterChanged; // Fired when meter value changes

    private bool wasDetectedLastFrame;
    private bool couldSeeLastFrame;

    private void Start()
    {
        ValidateSetup();
    }

    private void Update()
    {
        if (eye == null || player == null) return;
        
        UpdateVision();
        UpdateDetectionMeter();
        FireEvents();
    }

    /// <summary>
    /// Validates required references and logs warnings if missing.
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
        {
            Debug.LogWarning($"[VisionSensor] {gameObject.name}: obstacleMask not set. Line-of-sight check may not work correctly.", this);
        }
    }

    /// <summary>
    /// Performs all vision checks: range, FOV, line-of-sight.
    /// </summary>
    private void UpdateVision()
    {
        // Reset state
        CanSeePlayer = false;
        isPlayerInRange = false;
        isPlayerInFOV = false;
        hasLineOfSight = false;
        
        // Get direction and distance to player
        Vector3 eyePosition = eye.position;
        Vector3 playerPosition = player.position;
        Vector3 directionToPlayer = playerPosition - eyePosition;
        distanceToPlayer = directionToPlayer.magnitude;
        
        // Step 1: Range check
        if (distanceToPlayer > range)
        {
            return; // Player too far, skip other checks
        }
        isPlayerInRange = true;
        
        // Step 2: FOV check (using Vector3.Angle)
        // Compare angle between forward direction and direction to player
        Vector3 forwardDirection = eye.forward;
        angleToPlayer = Vector3.Angle(forwardDirection, directionToPlayer);
        
        // FOV is total cone width, so check against half-angle
        float halfFOV = fovAngle / 2f;
        if (angleToPlayer > halfFOV)
        {
            return; // Player outside field of view
        }
        isPlayerInFOV = true;
        
        // Step 3: Line-of-sight check (raycast for obstacles)
// Raycast from eye to player, check if anything blocks the view
        Vector3 rayDirection = directionToPlayer.normalized;

// DEBUG: Draw ray in Scene view
        Debug.DrawRay(eyePosition, rayDirection * distanceToPlayer, Color.cyan);

        if (Physics.Raycast(eyePosition, rayDirection, out RaycastHit hit, distanceToPlayer, obstacleMask))
       {
    // DEBUG: Log what we hit
           Debug.Log($"[Vision] BLOCKED by: {hit.collider.name}, Layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}");
    
    hasLineOfSight = false;
    return;
}
else
{
    // DEBUG: Nothing blocked the ray
    Debug.Log($"[Vision] NO OBSTACLE - Can see player! Mask value: {obstacleMask.value}");
}
        
        // All checks passed - can see player!
        hasLineOfSight = true;
        CanSeePlayer = true;
        
        // Update last known position
        LastKnownPlayerPos = playerPosition;
    }

    /// <summary>
    /// Updates detection meter based on visibility.
    /// </summary>
    private void UpdateDetectionMeter()
    {
        float previousMeter = DetectionMeter;
        
        if (CanSeePlayer)
        {
            // Player visible - increase detection
            DetectionMeter += detectSpeed * Time.deltaTime;
        }
        else
        {
            // Player not visible - decay detection
            DetectionMeter -= decaySpeed * Time.deltaTime;
        }
        
        // Clamp to 0-1 range
        DetectionMeter = Mathf.Clamp01(DetectionMeter);
        
        // Fire meter changed event if value changed significantly
        if (Mathf.Abs(DetectionMeter - previousMeter) > 0.001f)
        {
            OnMeterChanged?.Invoke(DetectionMeter);
        }
    }

    /// <summary>
    /// Fires events for FSM integration.
    /// </summary>
    private void FireEvents()
    {
        // Check for full detection (meter reached 1)
        bool isDetected = DetectionMeter >= 1f;
        if (isDetected && !wasDetectedLastFrame)
        {
            OnPlayerDetected?.Invoke();
        }
        wasDetectedLastFrame = isDetected;
        
        // Check for losing sight of player
        if (!CanSeePlayer && couldSeeLastFrame)
        {
            OnPlayerLost?.Invoke();
        }
        couldSeeLastFrame = CanSeePlayer;
    }

    /// <summary>
    /// Resets detection meter to zero. Call when returning to patrol.
    /// </summary>
    public void ResetDetection()
    {
        DetectionMeter = 0f;
        wasDetectedLastFrame = false;
    }

    /// <summary>
    /// Clears last known position. Call after search completes.
    /// </summary>
    public void ClearLastKnownPosition()
    {
        LastKnownPlayerPos = Vector3.zero;
    }

    /// <summary>
    /// Checks if a specific position is within the vision cone (for search behavior).
    /// </summary>
    public bool IsPositionInFOV(Vector3 position)
    {
        if (eye == null) return false;
        
        Vector3 directionToPos = position - eye.position;
        float angle = Vector3.Angle(eye.forward, directionToPos);
        float distance = directionToPos.magnitude;
        
        return distance <= range && angle <= fovAngle / 2f;
    }

    // ==================== DEBUG VISUALIZATION ====================

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;
        
        Transform eyeTransform = eye != null ? eye : transform;
        
        DrawFOVCone(eyeTransform);
        DrawRangeCircle(eyeTransform);
        
        if (Application.isPlaying && player != null)
        {
            DrawLineOfSight(eyeTransform);
        }
    }

    private void DrawFOVCone(Transform eyeTransform)
    {
        Vector3 eyePos = eyeTransform.position;
        Vector3 forward = eyeTransform.forward;
        
        // Calculate left and right edges of FOV
        float halfFOV = fovAngle / 2f;
        Quaternion leftRot = Quaternion.Euler(0, -halfFOV, 0);
        Quaternion rightRot = Quaternion.Euler(0, halfFOV, 0);
        
        Vector3 leftDir = leftRot * forward;
        Vector3 rightDir = rightRot * forward;
        
        // Draw FOV edges
        Gizmos.color = fovColor;
        Gizmos.DrawRay(eyePos, leftDir * range);
        Gizmos.DrawRay(eyePos, rightDir * range);
        
        // Draw arc (approximation with lines)
        int segments = 20;
        Vector3 prevPoint = eyePos + leftDir * range;
        
        for (int i = 1; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = Mathf.Lerp(-halfFOV, halfFOV, t);
            Quaternion rot = Quaternion.Euler(0, angle, 0);
            Vector3 dir = rot * forward;
            Vector3 point = eyePos + dir * range;
            
            Gizmos.DrawLine(prevPoint, point);
            prevPoint = point;
        }
        
        // Draw forward direction
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(eyePos, forward * (range * 0.5f));
    }

    private void DrawRangeCircle(Transform eyeTransform)
    {
        // Draw range indicator
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
        
        // Choose color based on visibility
        if (CanSeePlayer)
        {
            // Red line when player is visible
            Gizmos.color = visibleColor;
            Gizmos.DrawLine(eyePos, playerPos);
            
            // Draw sphere at player position
            Gizmos.DrawWireSphere(playerPos, 0.5f);
        }
        else if (isPlayerInRange)
        {
            // Green line when player in range but not visible
            Gizmos.color = notVisibleColor;
            Gizmos.DrawLine(eyePos, playerPos);
            
            // If blocked by obstacle, show hit point
            if (isPlayerInFOV && !hasLineOfSight)
            {
                Vector3 direction = (playerPos - eyePos).normalized;
                if (Physics.Raycast(eyePos, direction, out RaycastHit hit, distanceToPlayer, obstacleMask))
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawWireSphere(hit.point, 0.2f);
                }
            }
        }
        
        // Draw last known position if we have one
        if (LastKnownPlayerPos != Vector3.zero && !CanSeePlayer)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(LastKnownPlayerPos, 0.3f);
            Gizmos.DrawLine(eyePos, LastKnownPlayerPos);
        }
    }

    // Debug UI overlay
    private void OnGUI()
    {
        if (!showDebugUI || !Application.isPlaying) return;
        if (eye == null) return;
        
        Vector3 screenPos = Camera.main.WorldToScreenPoint(eye.position + Vector3.up * 2.5f);
        if (screenPos.z > 0)
        {
            float barWidth = 80f;
            float barHeight = 10f;
            float x = screenPos.x - barWidth / 2f;
            float y = Screen.height - screenPos.y;
            
            // Background
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(x - 2, y - 2, barWidth + 4, barHeight + 4), Texture2D.whiteTexture);
            
            // Detection meter bar
            GUI.color = Color.Lerp(Color.green, Color.red, DetectionMeter);
            GUI.DrawTexture(new Rect(x, y, barWidth * DetectionMeter, barHeight), Texture2D.whiteTexture);
            
            // Border
            GUI.color = Color.white;
            GUI.Box(new Rect(x - 2, y - 2, barWidth + 4, barHeight + 4), "");
            
            // Text info
            GUI.color = Color.white;
            string status = CanSeePlayer ? "VISIBLE" : "HIDDEN";
            GUI.Label(new Rect(x, y + 15, barWidth, 20), $"{status} ({DetectionMeter:P0})");
        }
    }
}
