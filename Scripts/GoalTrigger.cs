using UnityEngine;

/// <summary>
/// Attach to Goal object with trigger collider.
/// Notifies GameManager when player enters.
/// 
/// Setup:
/// 1. Create Goal object (cube, empty, etc.)
/// 2. Add Collider (Box, Sphere, etc.)
/// 3. Check "Is Trigger" on collider
/// 4. Tag object as "Goal"
/// 5. Attach this script
/// </summary>
[RequireComponent(typeof(Collider))]
public class GoalTrigger : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Tag to check for (default: Player)")]
    public string playerTag = "Player";
    
    [Tooltip("Custom win message")]
    public string winMessage = "YOU WIN!";
    
    [Header("Visual (Optional)")]
    [Tooltip("Disable renderer when game starts (invisible goal)")]
    public bool hideOnStart = false;
    
    [Header("Debug")]
    public bool showDebugMessages = true;

    private Collider triggerCollider;
    private bool hasTriggered = false;

    private void Start()
    {
        triggerCollider = GetComponent<Collider>();
        
        // Ensure it's a trigger
        if (!triggerCollider.isTrigger)
        {
            Debug.LogWarning($"[GoalTrigger] {gameObject.name}: Collider is not set as Trigger. Enabling trigger.", this);
            triggerCollider.isTrigger = true;
        }
        
        // Hide visual if requested
        if (hideOnStart)
        {
            Renderer rend = GetComponent<Renderer>();
            if (rend != null) rend.enabled = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Prevent multiple triggers
        if (hasTriggered) return;
        
        // Check if it's the player
        if (other.CompareTag(playerTag))
        {
            hasTriggered = true;
            
            if (showDebugMessages)
            {
                Debug.Log($"[GoalTrigger] Player reached goal!", this);
            }
            
            // Notify GameManager
            if (GameManager.Instance != null)
            {
                GameManager.Instance.TriggerWin(winMessage);
            }
            else
            {
                Debug.LogWarning("[GoalTrigger] GameManager not found!", this);
            }
        }
    }

    private void OnDrawGizmos()
    {
        // Draw goal indicator
        Gizmos.color = new Color(0, 1, 0, 0.4f);
        
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        }
        else
        {
            Gizmos.DrawCube(transform.position, Vector3.one);
        }
        
        // Draw flag icon
        Gizmos.color = Color.green;
        Vector3 flagPos = transform.position + Vector3.up * 2;
        Gizmos.DrawLine(transform.position, flagPos);
        Gizmos.DrawWireSphere(flagPos, 0.3f);
    }
}
