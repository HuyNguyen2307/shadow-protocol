using UnityEngine;
using UnityEngine.AI;
using System.Collections;

/// <summary>
/// Enemy AI using Finite State Machine with PATROL, CHASE, SEARCH states.
/// Integrates with VisionSensor for player detection.
/// 
/// This script REPLACES EnemyPatrol.cs - remove/disable EnemyPatrol if present.
/// 
/// Setup:
/// 1. Attach to Enemy with NavMeshAgent and VisionSensor
/// 2. Assign player, sensor, and waypoints in Inspector
/// 3. Remove or disable EnemyPatrol component
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(VisionSensor))]
public class EnemyAI : MonoBehaviour
{
    // ==================== FSM STATE ====================
    public enum AIState
    {
        PATROL,
        CHASE,
        SEARCH
    }

    [Header("Current State (Read-Only)")]
    [Tooltip("Current FSM state - exposed for UI/debugging")]
    public string CurrentState = "PATROL";
    
    [SerializeField] private AIState state = AIState.PATROL;

    // ==================== REFERENCES ====================
    [Header("References")]
    [Tooltip("VisionSensor component (auto-assigned if on same object)")]
    public VisionSensor sensor;
    
    [Tooltip("Player transform for chase targeting")]
    public Transform player;
    
    [Tooltip("Waypoints for patrol route")]
    public Transform[] waypoints;

    // ==================== PATROL SETTINGS ====================
    [Header("Patrol Settings")]
    [Tooltip("Time to wait at each waypoint")]
    public float waitSeconds = 1.0f;
    
    [Tooltip("Walking speed during patrol")]
    public float patrolSpeed = 2.5f;

    // ==================== CHASE SETTINGS ====================
    [Header("Chase Settings")]
    [Tooltip("Running speed when chasing player")]
    public float chaseSpeed = 5.0f;
    
    [Tooltip("Seconds without seeing player before switching to SEARCH")]
    public float loseSightSeconds = 2.0f;
    
    [Tooltip("Distance at which enemy catches player (game over)")]
    public float catchDistance = 1.5f;

    // ==================== SEARCH SETTINGS ====================
    [Header("Search Settings")]
    [Tooltip("How long to search at last known position")]
    public float searchDuration = 5.0f;
    
    [Tooltip("Rotation speed while scanning (degrees/second)")]
    public float searchRotationSpeed = 120f;
    
    [Tooltip("Speed when moving to search location")]
    public float searchSpeed = 3.5f;

    // ==================== DEBUG ====================
    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private bool showDebugUI = true;
    [SerializeField] private Color patrolColor = Color.green;
    [SerializeField] private Color chaseColor = Color.red;
    [SerializeField] private Color searchColor = Color.yellow;

    // ==================== COMPONENTS ====================
    private NavMeshAgent agent;

    // ==================== PATROL STATE ====================
    private int currentWaypointIndex = 0;
    private bool isWaitingAtWaypoint = false;

    // ==================== CHASE STATE ====================
    private float lastSeenTime;
    private float timeSinceLastSeen => Time.time - lastSeenTime;

    // ==================== SEARCH STATE ====================
    private Vector3 searchPosition;
    private float searchStartTime;
    private bool hasReachedSearchPosition = false;
    private float searchRotationProgress = 0f;

    // ==================== EVENTS (for external systems) ====================
    public System.Action<AIState> OnStateChanged;
    public System.Action OnPlayerCaught;

    // ==================== PUBLIC PROPERTIES ====================
    public AIState State => state;
    public int CurrentWaypointIndex => currentWaypointIndex;
    public Vector3 LastKnownPosition => sensor != null ? sensor.LastKnownPlayerPos : Vector3.zero;
    public float DetectionLevel => sensor != null ? sensor.DetectionMeter : 0f;

    // ==================== INITIALIZATION ====================
    
    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        
        // Auto-assign sensor if not set
        if (sensor == null)
        {
            sensor = GetComponent<VisionSensor>();
        }
    }

    private void Start()
    {
        ValidateSetup();
        
        // Subscribe to sensor events
        if (sensor != null)
        {
            sensor.OnPlayerDetected += HandlePlayerDetected;
        }
        
        // Initialize patrol
        if (waypoints != null && waypoints.Length > 0)
        {
            EnterPatrolState();
        }
        else
        {
            Debug.LogWarning($"[EnemyAI] {gameObject.name}: No waypoints assigned. Enemy will idle.", this);
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe from events
        if (sensor != null)
        {
            sensor.OnPlayerDetected -= HandlePlayerDetected;
        }
    }

    private void ValidateSetup()
    {
        if (sensor == null)
        {
            Debug.LogError($"[EnemyAI] {gameObject.name}: VisionSensor not found!", this);
        }
        
        if (player == null)
        {
            // Try to find player by tag
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
                Debug.Log($"[EnemyAI] Auto-assigned player: {playerObj.name}", this);
            }
            else
            {
                Debug.LogError($"[EnemyAI] {gameObject.name}: Player not assigned and not found by tag!", this);
            }
        }
        
        if (waypoints == null || waypoints.Length == 0)
        {
            Debug.LogWarning($"[EnemyAI] {gameObject.name}: No waypoints assigned.", this);
        }
    }

    // ==================== UPDATE LOOP ====================
    
    private void Update()
    {
        // Update current state string for Inspector
        CurrentState = state.ToString();
        
        // Run state-specific logic
        switch (state)
        {
            case AIState.PATROL:
                UpdatePatrolState();
                break;
            case AIState.CHASE:
                UpdateChaseState();
                break;
            case AIState.SEARCH:
                UpdateSearchState();
                break;
        }
        
        // Check for player catch (game over condition)
        CheckPlayerCatch();
    }

    // ==================== STATE: PATROL ====================
    
    private void EnterPatrolState()
    {
        state = AIState.PATROL;
        CurrentState = "PATROL";
        
        agent.speed = patrolSpeed;
        agent.isStopped = false;
        isWaitingAtWaypoint = false;
        
        // Reset detection
        if (sensor != null)
        {
            sensor.ResetDetection();
        }
        
        // Set destination to current waypoint
        SetDestinationToWaypoint();
        
        OnStateChanged?.Invoke(state);
        Debug.Log($"[EnemyAI] Entering PATROL state, waypoint {currentWaypointIndex}", this);
    }

    private void UpdatePatrolState()
    {
        // Check for state transition: detection meter full
        if (sensor != null && sensor.DetectionMeter >= 1.0f)
        {
            EnterChaseState();
            return;
        }
        
        // Skip patrol logic if no waypoints or waiting
        if (waypoints == null || waypoints.Length == 0) return;
        if (isWaitingAtWaypoint) return;
        
        // Check if reached current waypoint
        if (HasReachedDestination())
        {
            StartCoroutine(WaitAtWaypoint());
        }
    }

    private void SetDestinationToWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0) return;
        if (waypoints[currentWaypointIndex] == null) return;
        
        agent.SetDestination(waypoints[currentWaypointIndex].position);
    }

    private IEnumerator WaitAtWaypoint()
    {
        isWaitingAtWaypoint = true;
        agent.isStopped = true;
        
        yield return new WaitForSeconds(waitSeconds);
        
        // Check if state changed during wait
        if (state != AIState.PATROL)
        {
            yield break;
        }
        
        // Move to next waypoint
        currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
        
        // Skip null waypoints
        int attempts = 0;
        while (waypoints[currentWaypointIndex] == null && attempts < waypoints.Length)
        {
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
            attempts++;
        }
        
        agent.isStopped = false;
        isWaitingAtWaypoint = false;
        SetDestinationToWaypoint();
    }

    // ==================== STATE: CHASE ====================
    
    private void EnterChaseState()
    {
        state = AIState.CHASE;
        CurrentState = "CHASE";
        
        // Stop any patrol coroutines
        StopAllCoroutines();
        isWaitingAtWaypoint = false;
        
        agent.speed = chaseSpeed;
        agent.isStopped = false;
        
        // Record when we last saw the player
        lastSeenTime = Time.time;
        
        OnStateChanged?.Invoke(state);
        Debug.Log($"[EnemyAI] Entering CHASE state!", this);
    }

    private void UpdateChaseState()
    {
        if (player == null) return;
        
        // Update destination to player position
        agent.SetDestination(player.position);
        
        // Update last seen time if player is visible
        if (sensor != null && sensor.CanSeePlayer)
        {
            lastSeenTime = Time.time;
        }
        else
        {
            // Player not visible - check if we should transition to SEARCH
            if (timeSinceLastSeen > loseSightSeconds)
            {
                EnterSearchState();
                return;
            }
        }
    }

    private void HandlePlayerDetected()
    {
        // Called by VisionSensor when detection meter reaches 1.0
        if (state == AIState.PATROL)
        {
            EnterChaseState();
        }
        else if (state == AIState.SEARCH)
        {
            EnterChaseState();
        }
    }

    // ==================== STATE: SEARCH ====================
    
    private void EnterSearchState()
    {
        state = AIState.SEARCH;
        CurrentState = "SEARCH";
        
        agent.speed = searchSpeed;
        agent.isStopped = false;
        hasReachedSearchPosition = false;
        searchRotationProgress = 0f;
        
        // Get last known position from sensor
        if (sensor != null && sensor.LastKnownPlayerPos != Vector3.zero)
        {
            searchPosition = sensor.LastKnownPlayerPos;
        }
        else
        {
            // Fallback: use current position
            searchPosition = transform.position;
        }
        
        agent.SetDestination(searchPosition);
        searchStartTime = Time.time;
        
        OnStateChanged?.Invoke(state);
        Debug.Log($"[EnemyAI] Entering SEARCH state at {searchPosition}", this);
    }

    private void UpdateSearchState()
    {
        // Check if player becomes visible during search
        if (sensor != null && sensor.CanSeePlayer)
        {
            EnterChaseState();
            return;
        }
        
        // Check if search duration exceeded
        float timeSearching = Time.time - searchStartTime;
        if (timeSearching > searchDuration)
        {
            // Search complete, return to patrol
            FindNearestWaypointAndPatrol();
            return;
        }
        
        // If not yet at search position, wait for arrival
        if (!hasReachedSearchPosition)
        {
            if (HasReachedDestination())
            {
                hasReachedSearchPosition = true;
                agent.isStopped = true;
                searchRotationProgress = 0f;
            }
        }
        else
        {
            // At search position: rotate to scan area
            PerformSearchRotation();
        }
    }

    private void PerformSearchRotation()
    {
        // Rotate 360 degrees while searching
        float rotationAmount = searchRotationSpeed * Time.deltaTime;
        transform.Rotate(0, rotationAmount, 0);
        
        searchRotationProgress += rotationAmount;
        
        // Optional: check for player during rotation
        // The VisionSensor will automatically detect if player enters FOV
    }

    private void FindNearestWaypointAndPatrol()
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            // No waypoints, just enter patrol state at current position
            EnterPatrolState();
            return;
        }
        
        // Find nearest waypoint
        float nearestDistance = float.MaxValue;
        int nearestIndex = 0;
        
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            
            float distance = Vector3.Distance(transform.position, waypoints[i].position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }
        
        currentWaypointIndex = nearestIndex;
        Debug.Log($"[EnemyAI] Search complete. Resuming patrol at waypoint {nearestIndex}", this);
        
        EnterPatrolState();
    }

    // ==================== UTILITY METHODS ====================
    
    private bool HasReachedDestination()
    {
        if (agent.pathPending) return false;
        if (agent.remainingDistance > agent.stoppingDistance + 0.1f) return false;
        if (agent.velocity.sqrMagnitude > 0.1f) return false;
        
        return true;
    }

    private void CheckPlayerCatch()
    {
        if (player == null) return;
        
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        
        if (distanceToPlayer <= catchDistance && state == AIState.CHASE)
        {
            Debug.Log($"[EnemyAI] PLAYER CAUGHT!", this);
            OnPlayerCaught?.Invoke();
            
            // Optional: stop enemy
            agent.isStopped = true;
        }
    }

    // ==================== PUBLIC CONTROL METHODS ====================
    
    /// <summary>
    /// Force enemy back to patrol state.
    /// </summary>
    public void ForcePatrolState()
    {
        StopAllCoroutines();
        EnterPatrolState();
    }

    /// <summary>
    /// Alert enemy to a position (simulate noise/distraction).
    /// </summary>
    public void AlertToPosition(Vector3 position)
    {
        if (state == AIState.PATROL)
        {
            searchPosition = position;
            EnterSearchState();
        }
    }

    /// <summary>
    /// Pause all AI behavior.
    /// </summary>
    public void PauseAI()
    {
        StopAllCoroutines();
        agent.isStopped = true;
        CurrentState = "PAUSED";
    }

    /// <summary>
    /// Resume AI from current state.
    /// </summary>
    public void ResumeAI()
    {
        agent.isStopped = false;
        CurrentState = state.ToString();
        
        switch (state)
        {
            case AIState.PATROL:
                SetDestinationToWaypoint();
                break;
            case AIState.CHASE:
                if (player != null)
                    agent.SetDestination(player.position);
                break;
            case AIState.SEARCH:
                agent.SetDestination(searchPosition);
                break;
        }
    }

    // ==================== DEBUG VISUALIZATION ====================
    
    private void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;
        
        // Draw waypoints and patrol path
        DrawPatrolPath();
        
        // Draw state-specific visuals
        if (Application.isPlaying)
        {
            DrawCurrentStateGizmos();
        }
    }

    private void DrawPatrolPath()
    {
        if (waypoints == null || waypoints.Length == 0) return;
        
        Gizmos.color = patrolColor;
        
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            
            // Draw waypoint
            bool isCurrent = Application.isPlaying && i == currentWaypointIndex && state == AIState.PATROL;
            Gizmos.color = isCurrent ? Color.cyan : patrolColor;
            Gizmos.DrawWireSphere(waypoints[i].position, isCurrent ? 0.7f : 0.5f);
            
            // Draw line to next waypoint
            int nextIndex = (i + 1) % waypoints.Length;
            if (waypoints[nextIndex] != null)
            {
                Gizmos.color = patrolColor;
                Gizmos.DrawLine(waypoints[i].position, waypoints[nextIndex].position);
            }
        }
    }

    private void DrawCurrentStateGizmos()
    {
        switch (state)
        {
            case AIState.PATROL:
                Gizmos.color = patrolColor;
                break;
                
            case AIState.CHASE:
                Gizmos.color = chaseColor;
                if (player != null)
                {
                    Gizmos.DrawLine(transform.position, player.position);
                    Gizmos.DrawWireSphere(player.position, 0.5f);
                }
                break;
                
            case AIState.SEARCH:
                Gizmos.color = searchColor;
                // Draw search position
                Gizmos.DrawWireSphere(searchPosition, 0.8f);
                Gizmos.DrawLine(transform.position, searchPosition);
                
                // Draw search radius
                Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
                Gizmos.DrawWireSphere(searchPosition, 2f);
                break;
        }
        
        // Draw last known position if available
        if (sensor != null && sensor.LastKnownPlayerPos != Vector3.zero)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(sensor.LastKnownPlayerPos, Vector3.one * 0.5f);
        }
    }

    // Debug UI
    private void OnGUI()
    {
        if (!showDebugUI || !Application.isPlaying) return;
        
        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 3f);
        if (screenPos.z > 0)
        {
            float x = screenPos.x - 60;
            float y = Screen.height - screenPos.y;
            
            // Background
            GUI.color = GetStateColor();
            GUI.Box(new Rect(x - 5, y - 5, 130, 75), "");
            
            // State info
            GUI.color = Color.white;
            GUI.Label(new Rect(x, y, 120, 20), $"State: {CurrentState}");
            
            // State-specific info
            switch (state)
            {
                case AIState.PATROL:
                    GUI.Label(new Rect(x, y + 20, 120, 20), $"Waypoint: {currentWaypointIndex}");
                    float detection = sensor != null ? sensor.DetectionMeter : 0f;
                    GUI.Label(new Rect(x, y + 40, 120, 20), $"Detection: {detection:P0}");
                    break;
                    
                case AIState.CHASE:
                    GUI.Label(new Rect(x, y + 20, 120, 20), $"Last seen: {timeSinceLastSeen:F1}s");
                    GUI.Label(new Rect(x, y + 40, 120, 20), sensor?.CanSeePlayer == true ? "VISIBLE" : "LOST");
                    break;
                    
                case AIState.SEARCH:
                    float timeLeft = searchDuration - (Time.time - searchStartTime);
                    GUI.Label(new Rect(x, y + 20, 120, 20), $"Time left: {timeLeft:F1}s");
                    GUI.Label(new Rect(x, y + 40, 120, 20), hasReachedSearchPosition ? "SCANNING" : "MOVING");
                    break;
            }
        }
    }

    private Color GetStateColor()
    {
        switch (state)
        {
            case AIState.PATROL: return new Color(0f, 0.5f, 0f, 0.7f);
            case AIState.CHASE: return new Color(0.5f, 0f, 0f, 0.7f);
            case AIState.SEARCH: return new Color(0.5f, 0.5f, 0f, 0.7f);
            default: return new Color(0.3f, 0.3f, 0.3f, 0.7f);
        }
    }
}
