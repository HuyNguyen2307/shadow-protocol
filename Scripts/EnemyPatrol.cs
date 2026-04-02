using UnityEngine;
using UnityEngine.AI;
using System.Collections;

/// <summary>
/// Enemy patrol behavior using NavMeshAgent.
/// Moves between waypoints in sequence, waiting at each point.
/// 
/// Setup: 
/// 1. Bake NavMesh in scene (Window → AI → Navigation)
/// 2. Attach to enemy with NavMeshAgent component
/// 3. Create empty GameObjects as waypoints
/// 4. Assign waypoints array in Inspector
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyPatrol : MonoBehaviour
{
    [Header("Waypoints")]
    [Tooltip("Array of Transform points to patrol between")]
    public Transform[] waypoints;
    
    [Header("Patrol Settings")]
    [Tooltip("Time to wait at each waypoint before moving to next")]
    public float waitSeconds = 1.0f;
    
    [Tooltip("Distance threshold to consider waypoint reached")]
    [SerializeField] private float arrivalThreshold = 0.5f;
    
    [Header("State (Read-Only)")]
    [Tooltip("Current patrol state for external systems (FSM, UI, etc.)")]
    public string CurrentState = "PATROL";
    
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private Color waypointColor = Color.yellow;
    [SerializeField] private Color pathColor = Color.cyan;

    // Components
    private NavMeshAgent agent;
    
    // Patrol state
    private int currentWaypointIndex = 0;
    private bool isWaiting = false;
    private bool hasValidWaypoints = false;
    private bool hasWarnedOnce = false;
    
    // Public properties for external systems
    public int CurrentWaypointIndex => currentWaypointIndex;
    public bool IsWaiting => isWaiting;
    public Transform CurrentTargetWaypoint => hasValidWaypoints ? waypoints[currentWaypointIndex] : null;
    public float DistanceToWaypoint => hasValidWaypoints ? 
        Vector3.Distance(transform.position, waypoints[currentWaypointIndex].position) : 0f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        ValidateWaypoints();
    }

    private void Start()
    {
        if (hasValidWaypoints)
        {
            // Start patrol by moving to first waypoint
            SetDestinationToCurrentWaypoint();
        }
    }

    private void Update()
    {
        // Skip if no valid waypoints or currently waiting
        if (!hasValidWaypoints || isWaiting) return;
        
        // Check if we've reached the current waypoint
        if (HasReachedDestination())
        {
            StartCoroutine(WaitAtWaypoint());
        }
    }

    /// <summary>
    /// Validates waypoints array. Logs warning once if invalid.
    /// </summary>
    private void ValidateWaypoints()
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            hasValidWaypoints = false;
            
            if (!hasWarnedOnce)
            {
                Debug.LogWarning($"[EnemyPatrol] {gameObject.name}: Waypoints array is null or empty. Patrol disabled.", this);
                hasWarnedOnce = true;
            }
            
            CurrentState = "IDLE";
            return;
        }
        
        // Check for null entries in array
        int validCount = 0;
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] != null)
            {
                validCount++;
            }
            else if (!hasWarnedOnce)
            {
                Debug.LogWarning($"[EnemyPatrol] {gameObject.name}: Waypoint at index {i} is null.", this);
            }
        }
        
        hasValidWaypoints = validCount > 0;
        
        if (!hasValidWaypoints && !hasWarnedOnce)
        {
            Debug.LogWarning($"[EnemyPatrol] {gameObject.name}: All waypoints are null. Patrol disabled.", this);
            hasWarnedOnce = true;
            CurrentState = "IDLE";
        }
    }

    /// <summary>
    /// Checks if agent has reached its destination.
    /// </summary>
    private bool HasReachedDestination()
    {
        // Wait for path to be computed
        if (agent.pathPending) return false;
        
        // Check if close enough to destination
        if (agent.remainingDistance <= arrivalThreshold)
        {
            // Also check velocity to ensure we've actually stopped
            if (agent.velocity.sqrMagnitude < 0.1f)
            {
                return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// Waits at current waypoint, then moves to next.
    /// </summary>
    private IEnumerator WaitAtWaypoint()
    {
        isWaiting = true;
        CurrentState = "WAITING";
        
        // Stop the agent while waiting
        agent.isStopped = true;
        
        yield return new WaitForSeconds(waitSeconds);
        
        // Move to next waypoint
        MoveToNextWaypoint();
        
        agent.isStopped = false;
        isWaiting = false;
        CurrentState = "PATROL";
    }

    /// <summary>
    /// Advances to the next waypoint (wraps around to 0).
    /// </summary>
    private void MoveToNextWaypoint()
    {
        // Increment and wrap around
        currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
        
        // Skip null waypoints
        int attempts = 0;
        while (waypoints[currentWaypointIndex] == null && attempts < waypoints.Length)
        {
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
            attempts++;
        }
        
        SetDestinationToCurrentWaypoint();
    }

    /// <summary>
    /// Sets NavMeshAgent destination to current waypoint.
    /// </summary>
    private void SetDestinationToCurrentWaypoint()
    {
        if (waypoints[currentWaypointIndex] != null)
        {
            agent.SetDestination(waypoints[currentWaypointIndex].position);
        }
    }

    /// <summary>
    /// Pauses patrol (for chase/search states in FSM).
    /// </summary>
    public void PausePatrol()
    {
        StopAllCoroutines();
        agent.isStopped = true;
        isWaiting = false;
        CurrentState = "PAUSED";
    }

    /// <summary>
    /// Resumes patrol from current waypoint.
    /// </summary>
    public void ResumePatrol()
    {
        if (!hasValidWaypoints) return;
        
        agent.isStopped = false;
        isWaiting = false;
        CurrentState = "PATROL";
        SetDestinationToCurrentWaypoint();
    }

    /// <summary>
    /// Resets patrol to first waypoint.
    /// </summary>
    public void ResetPatrol()
    {
        StopAllCoroutines();
        currentWaypointIndex = 0;
        isWaiting = false;
        
        if (hasValidWaypoints)
        {
            agent.isStopped = false;
            CurrentState = "PATROL";
            SetDestinationToCurrentWaypoint();
        }
    }

    // Debug visualization
    private void OnDrawGizmos()
    {
        if (waypoints == null || waypoints.Length == 0) return;
        
        // Draw waypoints and connections
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            
            // Waypoint sphere
            Gizmos.color = (Application.isPlaying && i == currentWaypointIndex) 
                ? Color.green 
                : waypointColor;
            Gizmos.DrawWireSphere(waypoints[i].position, 0.5f);
            
            // Line to next waypoint
            int nextIndex = (i + 1) % waypoints.Length;
            if (waypoints[nextIndex] != null)
            {
                Gizmos.color = pathColor;
                Gizmos.DrawLine(waypoints[i].position, waypoints[nextIndex].position);
            }
        }
        
        // Draw current destination
        if (Application.isPlaying && hasValidWaypoints)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, waypoints[currentWaypointIndex].position);
        }
    }

    // Debug UI
    private void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying) return;
        
        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2);
        if (screenPos.z > 0)
        {
            GUI.Label(new Rect(screenPos.x - 50, Screen.height - screenPos.y, 100, 60),
                $"State: {CurrentState}\n" +
                $"WP: {currentWaypointIndex}/{waypoints?.Length ?? 0}\n" +
                $"Dist: {DistanceToWaypoint:F1}");
        }
    }
}
