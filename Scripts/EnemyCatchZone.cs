using UnityEngine;

/// <summary>
/// Optional: Attach to Enemy for trigger-based catch detection.
/// Alternative to distance-based checking in GameManager.
/// 
/// Setup:
/// 1. Select Enemy object
/// 2. Add Component → Sphere Collider
/// 3. Set Radius to catch distance (e.g., 1.5)
/// 4. Check "Is Trigger"
/// 5. Attach this script
/// 6. In GameManager, set useDistanceCheck = false (optional)
/// 
/// Note: Can work alongside distance check for redundancy.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class EnemyCatchZone : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Tag to check for (default: Player)")]
    public string playerTag = "Player";
    
    [Tooltip("Custom lose message")]
    public string loseMessage = "CAUGHT!";
    
    [Tooltip("Only trigger catch during CHASE state")]
    public bool onlyDuringChase = true;
    
    [Header("Debug")]
    public bool showDebugMessages = true;
    [SerializeField] private Color gizmoColor = new Color(1, 0, 0, 0.2f);

    private SphereCollider catchCollider;
    private EnemyAI enemyAI;
    private bool hasTriggered = false;

    private void Start()
    {
        catchCollider = GetComponent<SphereCollider>();
        enemyAI = GetComponentInParent<EnemyAI>();
        
        // Ensure it's a trigger
        if (!catchCollider.isTrigger)
        {
            Debug.LogWarning($"[EnemyCatchZone] {gameObject.name}: Collider is not set as Trigger. Enabling trigger.", this);
            catchCollider.isTrigger = true;
        }
        
        if (showDebugMessages)
        {
            Debug.Log($"[EnemyCatchZone] Initialized with radius {catchCollider.radius}", this);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Prevent multiple triggers
        if (hasTriggered) return;
        
        // Check if it's the player
        if (!other.CompareTag(playerTag)) return;
        
        // Optionally only catch during CHASE state
        if (onlyDuringChase && enemyAI != null)
        {
            if (enemyAI.State != EnemyAI.AIState.CHASE)
            {
                if (showDebugMessages)
                {
                    Debug.Log($"[EnemyCatchZone] Player in range but enemy not chasing (state: {enemyAI.State})", this);
                }
                return;
            }
        }
        
        hasTriggered = true;
        
        if (showDebugMessages)
        {
            Debug.Log($"[EnemyCatchZone] Player caught by trigger!", this);
        }
        
        // Notify GameManager
        if (GameManager.Instance != null)
        {
            GameManager.Instance.TriggerLose(loseMessage);
        }
        else
        {
            Debug.LogWarning("[EnemyCatchZone] GameManager not found!", this);
        }
    }

    private void OnTriggerStay(Collider other)
    {
        // Backup check in case OnTriggerEnter missed due to state
        if (hasTriggered) return;
        if (!other.CompareTag(playerTag)) return;
        
        // Re-check during CHASE
        if (onlyDuringChase && enemyAI != null && enemyAI.State == EnemyAI.AIState.CHASE)
        {
            OnTriggerEnter(other);
        }
    }

    /// <summary>
    /// Reset trigger (called on game restart if needed)
    /// </summary>
    public void ResetTrigger()
    {
        hasTriggered = false;
    }

    private void OnDrawGizmos()
    {
        SphereCollider col = GetComponent<SphereCollider>();
        if (col == null) return;
        
        Gizmos.color = gizmoColor;
        
        // Account for collider center offset and scale
        Vector3 center = transform.TransformPoint(col.center);
        float radius = col.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
        
        Gizmos.DrawWireSphere(center, radius);
        
        // Solid sphere (more visible)
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.1f);
        Gizmos.DrawSphere(center, radius);
    }
}
