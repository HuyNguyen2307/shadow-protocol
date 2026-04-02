using UnityEngine;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// PLAYER CONTROLLER - Third Person Movement
/// ═══════════════════════════════════════════════════════════════════════════════
/// 
/// FEATURES:
/// - WASD movement relative to camera direction
/// - Sprint with Shift
/// - Smooth rotation to face movement direction
/// - Works with TPPCameraController
/// 
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerControllerTPP : MonoBehaviour
{
    #region ═══════════════════ SETTINGS ═══════════════════

    [Header("═══ MOVEMENT ═══")]
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float sprintSpeed = 7f;
    [SerializeField] private float rotationSpeed = 10f;
    
    [Header("═══ GRAVITY ═══")]
    [SerializeField] private float gravity = -15f;
    [SerializeField] private float groundCheckDistance = 0.2f;
    [SerializeField] private LayerMask groundMask;

    [Header("═══ CONTROLS ═══")]
    [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;

    #endregion

    #region ═══════════════════ PRIVATE FIELDS ═══════════════════

    private CharacterController controller;
    private Transform cameraTransform;
    
    private Vector3 velocity;
    private bool isGrounded;
    private float currentSpeed;

    #endregion

    #region ═══════════════════ PUBLIC PROPERTIES ═══════════════════

    public bool IsMoving { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsGrounded => isGrounded;
    public float CurrentSpeed => currentSpeed;

    #endregion

    #region ═══════════════════ UNITY LIFECYCLE ═══════════════════

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void Start()
    {
        cameraTransform = Camera.main?.transform;
        
        if (cameraTransform == null)
        {
            Debug.LogWarning("[PlayerControllerTPP] Main Camera not found!");
        }
    }

    private void Update()
    {
        GroundCheck();
        HandleMovement();
        ApplyGravity();
    }

    #endregion

    #region ═══════════════════ MOVEMENT ═══════════════════

    private void GroundCheck()
    {
        isGrounded = Physics.CheckSphere(
            transform.position + Vector3.down * 0.1f, 
            groundCheckDistance, 
            groundMask
        );

        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }
    }

    private void HandleMovement()
    {
        // Get input
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        
        Vector3 inputDir = new Vector3(horizontal, 0f, vertical).normalized;

        IsMoving = inputDir.magnitude > 0.1f;
        IsSprinting = Input.GetKey(sprintKey) && IsMoving;

        if (IsMoving)
        {
            // Calculate movement direction relative to camera
            Vector3 moveDir = Vector3.zero;
            
            if (cameraTransform != null)
            {
                Vector3 camForward = cameraTransform.forward;
                Vector3 camRight = cameraTransform.right;
                
                camForward.y = 0f;
                camRight.y = 0f;
                camForward.Normalize();
                camRight.Normalize();

                moveDir = camForward * inputDir.z + camRight * inputDir.x;
            }
            else
            {
                moveDir = inputDir;
            }

            // Rotate player to face movement direction
            if (moveDir != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
            }

            // Calculate speed
            currentSpeed = IsSprinting ? sprintSpeed : walkSpeed;

            // Move
            controller.Move(moveDir * currentSpeed * Time.deltaTime);
        }
        else
        {
            currentSpeed = 0f;
        }
    }

    private void ApplyGravity()
    {
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    #endregion

    #region ═══════════════════ DEBUG ═══════════════════

    private void OnDrawGizmosSelected()
    {
        // Ground check sphere
        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position + Vector3.down * 0.1f, groundCheckDistance);
    }

    #endregion
}
