using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// FOOTSTEP SOUND SYSTEM - Surface-based footstep audio
/// ═══════════════════════════════════════════════════════════════════════════════
/// 
/// FEATURES:
/// - Different sounds for different surfaces (concrete, metal, grass, etc.)
/// - Volume/pitch varies with movement speed
/// - Integrates with PlayerNoiseSystem for AI detection
/// 
/// SETUP:
/// 1. Attach to Player
/// 2. Assign AudioClips for each surface type
/// 3. Tag ground objects with surface type (optional)
/// 
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class FootstepSystem : MonoBehaviour
{
    #region ═══════════════════ ENUMS ═══════════════════

    public enum SurfaceType
    {
        Concrete,
        Metal,
        Wood,
        Grass,
        Water,
        Gravel,
        Carpet
    }

    #endregion

    #region ═══════════════════ SETTINGS ═══════════════════

    [Header("═══ DEBUG ═══")]
    [Tooltip("Show debug logs when sounds would play (even without audio files)")]
    [SerializeField] private bool debugMode = true;

    [Header("═══ FOOTSTEP SOUNDS ═══")]
    [SerializeField] private AudioClip[] concreteSteps;
    [SerializeField] private AudioClip[] metalSteps;
    [SerializeField] private AudioClip[] woodSteps;
    [SerializeField] private AudioClip[] grassSteps;
    [SerializeField] private AudioClip[] waterSteps;
    [SerializeField] private AudioClip[] gravelSteps;
    [SerializeField] private AudioClip[] carpetSteps;

    [Header("═══ TIMING ═══")]
    [Tooltip("Time between footsteps when walking")]
    [SerializeField] private float walkStepInterval = 0.5f;
    [Tooltip("Time between footsteps when sprinting")]
    [SerializeField] private float sprintStepInterval = 0.3f;
    [Tooltip("Time between footsteps when crouching")]
    [SerializeField] private float crouchStepInterval = 0.7f;

    [Header("═══ VOLUME ═══")]
    [SerializeField] private float walkVolume = 0.4f;
    [SerializeField] private float sprintVolume = 0.8f;
    [SerializeField] private float crouchVolume = 0.15f;
    [SerializeField] private float landingVolume = 0.6f;

    [Header("═══ PITCH VARIATION ═══")]
    [SerializeField] private float minPitch = 0.9f;
    [SerializeField] private float maxPitch = 1.1f;

    [Header("═══ SURFACE DETECTION ═══")]
    [SerializeField] private LayerMask groundLayers;
    [SerializeField] private float raycastDistance = 0.3f;
    [SerializeField] private SurfaceType defaultSurface = SurfaceType.Concrete;

    [Header("═══ LANDING SOUNDS ═══")]
    [SerializeField] private AudioClip[] landingSounds;
    [SerializeField] private float landingThreshold = 0.5f;

    #endregion

    #region ═══════════════════ PRIVATE FIELDS ═══════════════════

    private AudioSource audioSource;
    private float stepTimer;
    private float lastYVelocity;
    private bool wasGrounded = true;
    private SurfaceType currentSurface;
    
    // Movement detection
    private Vector3 lastPosition;
    private float currentSpeed;
    private bool isMoving;
    private bool isSprinting;
    private bool isCrouching;

    // Cache
    private CharacterController characterController;
    private Dictionary<SurfaceType, AudioClip[]> surfaceSounds;

    #endregion

    #region ═══════════════════ UNITY LIFECYCLE ═══════════════════

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f; // 3D sound
        
        characterController = GetComponent<CharacterController>();
        
        InitializeSurfaceSounds();
    }

    private void Start()
    {
        lastPosition = transform.position;
    }

    private void Update()
    {
        DetectMovement();
        DetectSurface();
        HandleFootsteps();
        HandleLanding();
    }

    #endregion

    #region ═══════════════════ INITIALIZATION ═══════════════════

    private void InitializeSurfaceSounds()
    {
        surfaceSounds = new Dictionary<SurfaceType, AudioClip[]>
        {
            { SurfaceType.Concrete, concreteSteps },
            { SurfaceType.Metal, metalSteps },
            { SurfaceType.Wood, woodSteps },
            { SurfaceType.Grass, grassSteps },
            { SurfaceType.Water, waterSteps },
            { SurfaceType.Gravel, gravelSteps },
            { SurfaceType.Carpet, carpetSteps }
        };
    }

    #endregion

    #region ═══════════════════ MOVEMENT DETECTION ═══════════════════

    private void DetectMovement()
    {
        // Calculate speed from position change
        Vector3 movement = transform.position - lastPosition;
        movement.y = 0; // Ignore vertical movement
        currentSpeed = movement.magnitude / Time.deltaTime;
        lastPosition = transform.position;

        // Determine movement state
        isMoving = currentSpeed > 0.5f;
        isSprinting = Input.GetKey(KeyCode.LeftShift) && currentSpeed > 4f;
        isCrouching = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
    }

    #endregion

    #region ═══════════════════ SURFACE DETECTION ═══════════════════

    private void DetectSurface()
    {
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, raycastDistance, groundLayers))
        {
            // Check for SurfaceTag component
            SurfaceTag surfaceTag = hit.collider.GetComponent<SurfaceTag>();
            if (surfaceTag != null)
            {
                currentSurface = surfaceTag.Surface;
                return;
            }

            // Check for tag-based surface
            currentSurface = GetSurfaceFromTag(hit.collider.tag);
        }
        else
        {
            currentSurface = defaultSurface;
        }
    }

    private SurfaceType GetSurfaceFromTag(string tag)
    {
        switch (tag.ToLower())
        {
            case "metal":
                return SurfaceType.Metal;
            case "wood":
                return SurfaceType.Wood;
            case "grass":
                return SurfaceType.Grass;
            case "water":
                return SurfaceType.Water;
            case "gravel":
                return SurfaceType.Gravel;
            case "carpet":
                return SurfaceType.Carpet;
            default:
                return defaultSurface;
        }
    }

    #endregion

    #region ═══════════════════ FOOTSTEP PLAYBACK ═══════════════════

    private void HandleFootsteps()
    {
        if (!isMoving) 
        {
            stepTimer = 0f;
            return;
        }

        // Check if grounded
        bool isGrounded = characterController != null ? characterController.isGrounded : IsGrounded();
        if (!isGrounded) return;

        // Get step interval based on movement type
        float interval = GetStepInterval();
        
        stepTimer += Time.deltaTime;
        
        if (stepTimer >= interval)
        {
            PlayFootstep();
            stepTimer = 0f;
        }
    }

    private float GetStepInterval()
    {
        if (isCrouching) return crouchStepInterval;
        if (isSprinting) return sprintStepInterval;
        return walkStepInterval;
    }

    private float GetVolume()
    {
        if (isCrouching) return crouchVolume;
        if (isSprinting) return sprintVolume;
        return walkVolume;
    }

    private void PlayFootstep()
    {
        AudioClip[] clips = GetSurfaceClips(currentSurface);
        
        if (clips == null || clips.Length == 0)
        {
            // Fallback to concrete
            clips = concreteSteps;
        }
        
        // DEBUG: Log even without audio files
        if (debugMode)
        {
            string moveType = isCrouching ? "CROUCH" : (isSprinting ? "SPRINT" : "WALK");
            float volume = GetVolume();
            Debug.Log($"[FootstepSystem] 🔊 STEP! Surface: {currentSurface}, Type: {moveType}, Volume: {volume:F2}");
        }
        
        if (clips == null || clips.Length == 0) 
        {
            if (debugMode)
            {
                Debug.LogWarning($"[FootstepSystem] ⚠️ No audio clips assigned for {currentSurface}! Assign clips in Inspector.");
            }
            return;
        }

        // Random clip
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        
        // Random pitch
        audioSource.pitch = Random.Range(minPitch, maxPitch);
        
        // Play with appropriate volume
        audioSource.PlayOneShot(clip, GetVolume());
    }

    private AudioClip[] GetSurfaceClips(SurfaceType surface)
    {
        if (surfaceSounds.TryGetValue(surface, out AudioClip[] clips))
        {
            return clips;
        }
        return concreteSteps;
    }

    #endregion

    #region ═══════════════════ LANDING ═══════════════════

    private void HandleLanding()
    {
        bool isGrounded = characterController != null ? characterController.isGrounded : IsGrounded();
        
        // Detect landing
        if (isGrounded && !wasGrounded)
        {
            // Check fall velocity
            float fallSpeed = Mathf.Abs(lastYVelocity);
            
            if (fallSpeed > landingThreshold)
            {
                PlayLandingSound(fallSpeed);
            }
        }

        wasGrounded = isGrounded;
        
        if (characterController != null)
        {
            lastYVelocity = characterController.velocity.y;
        }
    }

    private void PlayLandingSound(float fallSpeed)
    {
        AudioClip[] clips = landingSounds;
        
        if (clips == null || clips.Length == 0)
        {
            clips = GetSurfaceClips(currentSurface);
        }
        
        if (clips == null || clips.Length == 0) return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        
        // Volume based on fall speed
        float volume = Mathf.Clamp(fallSpeed / 5f, 0.3f, 1f) * landingVolume;
        
        audioSource.pitch = Random.Range(0.85f, 0.95f); // Lower pitch for landing
        audioSource.PlayOneShot(clip, volume);
    }

    private bool IsGrounded()
    {
        return Physics.Raycast(transform.position, Vector3.down, 0.2f, groundLayers);
    }

    #endregion

    #region ═══════════════════ PUBLIC METHODS ═══════════════════

    /// <summary>
    /// Force play a footstep sound
    /// </summary>
    public void PlayFootstepNow()
    {
        PlayFootstep();
    }

    /// <summary>
    /// Play a footstep on a specific surface
    /// </summary>
    public void PlayFootstepOnSurface(SurfaceType surface)
    {
        SurfaceType temp = currentSurface;
        currentSurface = surface;
        PlayFootstep();
        currentSurface = temp;
    }

    #endregion
}

/// <summary>
/// Helper component to tag surfaces
/// </summary>
public class SurfaceTag : MonoBehaviour
{
    public FootstepSystem.SurfaceType Surface = FootstepSystem.SurfaceType.Concrete;
}
