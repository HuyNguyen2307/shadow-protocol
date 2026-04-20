using UnityEngine;

/// <summary>
/// Manages a togglable spotlight flashlight attached to the player.
/// </summary>
public class PlayerFlashlight : MonoBehaviour
{
#region Settings

    [Header("Controls")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F;
    [SerializeField] private bool startOn = true;

    [Header("Light Settings")]
    [SerializeField] private float lightRange = 15f;
    [SerializeField] private float spotAngle = 45f;
    [SerializeField] private float innerSpotAngle = 25f;
    [SerializeField] private float intensity = 2f;
    [SerializeField] private Color lightColor = new Color(1f, 0.95f, 0.8f); // Warm white

    [Header("Position")]
    [Tooltip("Offset from player center")]
    [SerializeField] private Vector3 lightOffset = new Vector3(0.3f, 1.2f, 0.3f);
    
    [Header("Smooth Rotation")]
    [SerializeField] private bool smoothRotation = true;
    [SerializeField] private float rotationSpeed = 10f;

    [Header("Shadows")]
    [SerializeField] private bool castShadows = true;
    [SerializeField] private LightShadows shadowType = LightShadows.Soft;

    [Header("Audio (optional)")]
    [SerializeField] private AudioClip toggleSound;
    [SerializeField] private float soundVolume = 0.5f;

    #endregion

#region Private Fields

    private Light flashlight;
    private Transform cameraTransform;
    private AudioSource audioSource;
    private bool isOn;

    #endregion

#region Public Properties

    public bool IsOn => isOn;

    #endregion

#region Unity Lifecycle

    private void Awake()
    {
        CreateFlashlight();
        SetupAudio();
    }

    private void Start()
    {
        // Find camera
        cameraTransform = Camera.main?.transform;
        
        // Initial state
        isOn = startOn;
        flashlight.enabled = isOn;
    }

    private void Update()
    {
        HandleInput();
        UpdateFlashlightPosition();
    }

    #endregion

#region Setup

    private void CreateFlashlight()
    {
        // Check if flashlight already exists
        flashlight = GetComponentInChildren<Light>();
        
        if (flashlight == null)
        {
            // Create new light
            GameObject lightObj = new GameObject("Flashlight");
            lightObj.transform.SetParent(transform);
            lightObj.transform.localPosition = lightOffset;
            
            flashlight = lightObj.AddComponent<Light>();
        }

        // Configure light
        flashlight.type = LightType.Spot;
        flashlight.range = lightRange;
        flashlight.spotAngle = spotAngle;
        flashlight.innerSpotAngle = innerSpotAngle;
        flashlight.intensity = intensity;
        flashlight.color = lightColor;
        
        if (castShadows)
        {
            flashlight.shadows = shadowType;
            flashlight.shadowStrength = 0.8f;
        }
        else
        {
            flashlight.shadows = LightShadows.None;
        }
    }

    private void SetupAudio()
    {
        if (toggleSound != null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f; // 2D sound
        }
    }

    #endregion

#region Input

    private void HandleInput()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleFlashlight();
        }
    }

    #endregion

#region Flashlight Control

    public void ToggleFlashlight()
    {
        isOn = !isOn;
        flashlight.enabled = isOn;
        
        // Play sound
        if (audioSource != null && toggleSound != null)
        {
            audioSource.PlayOneShot(toggleSound, soundVolume);
        }

        Debug.Log($"[Flashlight] {(isOn ? "ON" : "OFF")}");
    }

    public void SetFlashlight(bool on)
    {
        if (isOn != on)
        {
            isOn = on;
            flashlight.enabled = isOn;
        }
    }

    private void UpdateFlashlightPosition()
    {
        if (flashlight == null) return;

        // Position follows player
        flashlight.transform.position = transform.position + transform.TransformDirection(lightOffset);

        // Rotation follows camera or player forward
        Quaternion targetRotation;
        
        if (cameraTransform != null)
        {
            // Follow camera direction (where player is looking)
            targetRotation = Quaternion.LookRotation(cameraTransform.forward);
        }
        else
        {
            // Follow player forward
            targetRotation = transform.rotation;
        }

        if (smoothRotation)
        {
            flashlight.transform.rotation = Quaternion.Slerp(
                flashlight.transform.rotation, 
                targetRotation, 
                Time.deltaTime * rotationSpeed
            );
        }
        else
        {
            flashlight.transform.rotation = targetRotation;
        }
    }

    #endregion

#region Debug

    private void OnDrawGizmosSelected()
    {
        // Draw flashlight cone
        Vector3 pos = transform.position + transform.TransformDirection(lightOffset);
        Vector3 forward = Application.isPlaying && cameraTransform != null 
            ? cameraTransform.forward 
            : transform.forward;

        Gizmos.color = isOn ? Color.yellow : Color.gray;
        Gizmos.DrawRay(pos, forward * lightRange);
        
        // Draw cone outline
        float radius = Mathf.Tan(spotAngle * 0.5f * Mathf.Deg2Rad) * lightRange;
        Gizmos.DrawWireSphere(pos + forward * lightRange, radius * 0.3f);
    }

    #endregion
}
