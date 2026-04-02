using UnityEngine;

/// <summary>
/// Top-down player controller for stealth prototype.
/// Uses CharacterController for stable, physics-independent movement.
/// 
/// Setup: Attach to player GameObject with CharacterController component.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("Base movement speed in units per second")]
    [SerializeField] private float walkSpeed = 5f;
    
    [Tooltip("Sprint speed multiplier")]
    [SerializeField] private float sprintMultiplier = 1.8f;
    
    [Tooltip("How quickly the player rotates to face movement direction")]
    [SerializeField] private float rotationSpeed = 720f;
    
    [Tooltip("Smoothing for movement acceleration/deceleration")]
    [SerializeField] private float movementSmoothing = 0.1f;

    [Header("Ground Check")]
    [Tooltip("Constant downward force to keep grounded")]
    [SerializeField] private float gravity = -9.81f;
    
    [Header("Debug")]
    [Tooltip("Show movement direction gizmo in Scene view")]
    [SerializeField] private bool showDebugGizmos = true;

    // Components
    private CharacterController controller;
    
    // Movement state
    private Vector3 currentVelocity;
    private Vector3 smoothVelocity;
    private float verticalVelocity;
    private bool isSprinting;
    
    // Input cache
    private Vector2 inputDirection;
    
    // Public properties for other systems (AI detection, UI, etc.)
    public bool IsMoving => inputDirection.magnitude > 0.1f;
    public bool IsSprinting => isSprinting && IsMoving;
    public float CurrentSpeed => controller.velocity.magnitude;
    public Vector3 Velocity => controller.velocity;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        
        // Validate component
        if (controller == null)
        {
            Debug.LogError("[PlayerController] CharacterController component missing!", this);
            enabled = false;
        }
    }

    private void Update()
    {
        HandleInput();
        HandleMovement();
        HandleRotation();
    }

    /// <summary>
    /// Reads input from Unity's Input system (old/legacy).
    /// Easy to swap out for new Input System if needed.
    /// </summary>
    private void HandleInput()
    {
        // WASD / Arrow keys - GetAxisRaw for instant response
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        
        inputDirection = new Vector2(horizontal, vertical);
        
        // Normalize to prevent faster diagonal movement
        if (inputDirection.magnitude > 1f)
        {
            inputDirection.Normalize();
        }
        
        // Sprint toggle (hold Left Shift)
        isSprinting = Input.GetKey(KeyCode.LeftShift);
    }

    /// <summary>
    /// Handles smooth movement using CharacterController.
    /// Top-down games typically move on X/Z plane.
    /// </summary>
    private void HandleMovement()
    {
        // Calculate target velocity on X/Z plane
        float currentSpeed = isSprinting ? walkSpeed * sprintMultiplier : walkSpeed;
        Vector3 targetVelocity = new Vector3(inputDirection.x, 0f, inputDirection.y) * currentSpeed;
        
        // Smooth the velocity change for natural acceleration/deceleration
        currentVelocity = Vector3.SmoothDamp(
            currentVelocity, 
            targetVelocity, 
            ref smoothVelocity, 
            movementSmoothing
        );
        
        // Apply gravity (keeps player grounded)
        if (controller.isGrounded)
        {
            verticalVelocity = -2f; // Small downward force to stay grounded
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }
        
        // Combine horizontal movement with vertical (gravity)
        Vector3 finalMovement = currentVelocity + Vector3.up * verticalVelocity;
        
        // Move the character
        controller.Move(finalMovement * Time.deltaTime);
    }

    /// <summary>
    /// Smoothly rotates player to face movement direction.
    /// Only rotates when there's actual movement input.
    /// </summary>
    private void HandleRotation()
    {
        // Only rotate if we have movement input
        if (inputDirection.magnitude < 0.1f) return;
        
        // Calculate target rotation from input direction
        // In top-down: X input = X world, Y input = Z world
        Vector3 targetDirection = new Vector3(inputDirection.x, 0f, inputDirection.y);
        
        if (targetDirection != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(targetDirection, Vector3.up);
            
            // Smooth rotation
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }
    }

    /// <summary>
    /// Teleports player to position. Useful for respawning.
    /// </summary>
    public void TeleportTo(Vector3 position)
    {
        controller.enabled = false;
        transform.position = position;
        controller.enabled = true;
        
        // Reset velocities
        currentVelocity = Vector3.zero;
        smoothVelocity = Vector3.zero;
        verticalVelocity = 0f;
    }

    /// <summary>
    /// Stops all movement. Useful for cutscenes, menus, etc.
    /// </summary>
    public void StopMovement()
    {
        currentVelocity = Vector3.zero;
        smoothVelocity = Vector3.zero;
        inputDirection = Vector2.zero;
    }

    // Debug visualization
    private void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;
        
        // Movement direction indicator
        if (Application.isPlaying && IsMoving)
        {
            Gizmos.color = IsSprinting ? Color.yellow : Color.green;
            Vector3 direction = new Vector3(inputDirection.x, 0f, inputDirection.y).normalized;
            Gizmos.DrawRay(transform.position + Vector3.up * 0.1f, direction * 2f);
        }
        
        // Player position indicator
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.3f);
    }

    // Debug info in Inspector
    private void OnGUI()
    {
        if (!showDebugGizmos || !Application.isPlaying) return;

    const float pad = 8f;
    Rect box = new Rect(10, 10, 260, 110);

    // Background (semi-transparent black)
    Color old = GUI.color;
    GUI.color = new Color(0f, 0f, 0f, 0.60f);
    GUI.Box(box, GUIContent.none);
    GUI.color = old;

    // Text style (bright yellow)
    GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
    {
        fontSize = 14,
        normal = { textColor = new Color(1f, 0.92f, 0.2f, 1f) } // vàng sáng
    };

    GUILayout.BeginArea(new Rect(box.x + pad, box.y + pad, box.width - pad * 2, box.height - pad * 2));
    GUILayout.Label($"Speed: {CurrentSpeed:F1}", labelStyle);
    GUILayout.Label($"Moving: {IsMoving}", labelStyle);
    GUILayout.Label($"Sprinting: {IsSprinting}", labelStyle);
    GUILayout.Label($"Grounded: {controller.isGrounded}", labelStyle);
    GUILayout.EndArea();
    }
}
