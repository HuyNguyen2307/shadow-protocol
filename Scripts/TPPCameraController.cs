using UnityEngine;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// THIRD PERSON CAMERA - Over-the-Shoulder Style (Resident Evil style)
/// ═══════════════════════════════════════════════════════════════════════════════
/// 
/// FEATURES:
/// - Follows player from behind and slightly to the right
/// - Mouse controls camera rotation
/// - Smooth follow and rotation
/// - Collision detection to avoid clipping through walls
/// 
/// SETUP:
/// 1. Attach to Main Camera
/// 2. Assign Player transform
/// 3. Adjust offset for desired view
/// 
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class TPPCameraController : MonoBehaviour
{
    #region ═══════════════════ SETTINGS ═══════════════════

    [Header("═══ TARGET ═══")]
    [SerializeField] private Transform player;
    [SerializeField] private float playerHeight = 1.5f;

    [Header("═══ CAMERA POSITION ═══")]
    [Tooltip("Offset from player (X = right, Y = up, Z = back)")]
    [SerializeField] private Vector3 offset = new Vector3(0.5f, 0.5f, -2.5f);
    
    [Tooltip("Minimum distance from player")]
    [SerializeField] private float minDistance = 1f;
    
    [Tooltip("Maximum distance from player")]
    [SerializeField] private float maxDistance = 4f;

    [Header("═══ ROTATION ═══")]
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float minVerticalAngle = -30f;
    [SerializeField] private float maxVerticalAngle = 60f;

    [Header("═══ SMOOTHING ═══")]
    [SerializeField] private float positionSmoothTime = 0.1f;
    [SerializeField] private float rotationSmoothTime = 0.05f;

    [Header("═══ COLLISION ═══")]
    [SerializeField] private LayerMask collisionLayers;
    [SerializeField] private float collisionRadius = 0.2f;
    [SerializeField] private float collisionOffset = 0.1f;

    [Header("═══ CURSOR ═══")]
    [SerializeField] private bool lockCursor = true;

    #endregion

    #region ═══════════════════ PRIVATE FIELDS ═══════════════════

    private float currentYaw = 0f;
    private float currentPitch = 15f;
    private float currentDistance;
    
    private Vector3 currentVelocity;
    private float rotationVelocityX;
    private float rotationVelocityY;

    #endregion

    #region ═══════════════════ UNITY LIFECYCLE ═══════════════════

    private void Start()
    {
        // Find player if not assigned
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
            }
            else
            {
                Debug.LogError("[TPPCamera] Player not found! Assign manually or tag player as 'Player'");
            }
        }

        // Initialize
        currentDistance = Mathf.Abs(offset.z);
        
        if (player != null)
        {
            currentYaw = player.eulerAngles.y;
        }

        // Lock cursor
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void LateUpdate()
    {
        if (player == null) return;

        HandleInput();
        UpdateCameraPosition();
    }

    #endregion

    #region ═══════════════════ INPUT ═══════════════════

    private void HandleInput()
    {
        // Mouse input
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // Update rotation
        currentYaw += mouseX;
        currentPitch -= mouseY;
        currentPitch = Mathf.Clamp(currentPitch, minVerticalAngle, maxVerticalAngle);

        // Scroll to zoom (optional)
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0)
        {
            currentDistance -= scroll * 2f;
            currentDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);
        }

        // Toggle cursor lock with Escape
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            lockCursor = !lockCursor;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;
        }
    }

    #endregion

    #region ═══════════════════ CAMERA UPDATE ═══════════════════

    private void UpdateCameraPosition()
    {
        // Calculate target position
        Vector3 targetPoint = player.position + Vector3.up * playerHeight;
        
        // Calculate rotation
        Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
        
        // Calculate offset position
        Vector3 rotatedOffset = rotation * new Vector3(offset.x, offset.y, -currentDistance);
        Vector3 targetPosition = targetPoint + rotatedOffset;

        // Check for collision
        targetPosition = HandleCollision(targetPoint, targetPosition);

        // Smooth position
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref currentVelocity, positionSmoothTime);

        // Look at player (slightly above center)
        Vector3 lookTarget = targetPoint + Vector3.up * 0.2f;
        Quaternion targetRotation = Quaternion.LookRotation(lookTarget - transform.position);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime / rotationSmoothTime);
    }

    private Vector3 HandleCollision(Vector3 target, Vector3 cameraPos)
    {
        Vector3 direction = cameraPos - target;
        float distance = direction.magnitude;

        // Raycast to check for obstacles
        if (Physics.SphereCast(target, collisionRadius, direction.normalized, out RaycastHit hit, distance, collisionLayers))
        {
            // Move camera in front of obstacle
            return hit.point + hit.normal * collisionOffset;
        }

        return cameraPos;
    }

    #endregion

    #region ═══════════════════ PUBLIC METHODS ═══════════════════

    /// <summary>
    /// Get the forward direction of the camera (for player movement)
    /// </summary>
    public Vector3 GetCameraForward()
    {
        Vector3 forward = transform.forward;
        forward.y = 0;
        return forward.normalized;
    }

    /// <summary>
    /// Get the right direction of the camera
    /// </summary>
    public Vector3 GetCameraRight()
    {
        Vector3 right = transform.right;
        right.y = 0;
        return right.normalized;
    }

    /// <summary>
    /// Get current yaw rotation (for player rotation sync)
    /// </summary>
    public float GetYaw()
    {
        return currentYaw;
    }

    #endregion
}
