using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// PLAYER NOISE SYSTEM - Auto-detects movement (no PlayerController required!)
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class PlayerNoiseSystem : MonoBehaviour
{
    #region ═══════════════════ SINGLETON ═══════════════════
    
    public static PlayerNoiseSystem Instance { get; private set; }
    
    #endregion

    #region ═══════════════════ NOISE SETTINGS ═══════════════════

    [Header("═══ NOISE LEVELS ═══")]
    [SerializeField] private float idleNoiseRadius = 0f;
    [SerializeField] private float crouchNoiseRadius = 2f;
    [SerializeField] private float walkNoiseRadius = 5f;
    [SerializeField] private float sprintNoiseRadius = 12f;

    [Header("═══ SPEED THRESHOLDS ═══")]
    [Tooltip("Speed below this = idle")]
    [SerializeField] private float idleSpeedThreshold = 0.1f;
    [Tooltip("Speed above this = sprinting")]
    [SerializeField] private float sprintSpeedThreshold = 5f;

    [Header("═══ SURFACE MULTIPLIERS ═══")]
    [SerializeField] private float normalSurfaceMultiplier = 1.0f;
    [SerializeField] private float metalSurfaceMultiplier = 1.5f;
    [SerializeField] private float softSurfaceMultiplier = 0.5f;
    [SerializeField] private float waterSurfaceMultiplier = 2.0f;

    [Header("═══ NOISE GENERATION ═══")]
    [SerializeField] private float noiseInterval = 0.3f;
    [SerializeField] private LayerMask groundLayerMask = ~0;

    [Header("═══ DEBUG ═══")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private bool logNoiseEvents = false;

    #endregion

    #region ═══════════════════ REFERENCES ═══════════════════

    [Header("═══ REFERENCES ═══")]
    [SerializeField] private Transform player;

    #endregion

    #region ═══════════════════ PRIVATE FIELDS ═══════════════════

    private float lastNoiseTime;
    private float currentNoiseRadius;
    private SurfaceType currentSurface = SurfaceType.Normal;
    
    // Auto-detect movement
    private Vector3 lastPosition;
    private float currentSpeed;
    private bool isMoving;
    private bool isSprinting;
    
    // Registered listeners
    private List<ISoundListener> listeners = new List<ISoundListener>();

    #endregion

    #region ═══════════════════ ENUMS ═══════════════════

    public enum SurfaceType { Normal, Metal, Soft, Water }
    public enum NoiseType { Footstep, Landing, Interaction, Distraction }

    #endregion

    #region ═══════════════════ PUBLIC PROPERTIES ═══════════════════

    public float CurrentNoiseRadius => currentNoiseRadius;
    public SurfaceType CurrentSurface => currentSurface;
    public bool IsMoving => isMoving;
    public bool IsSprinting => isSprinting;
    public float CurrentSpeed => currentSpeed;

    #endregion

    #region ═══════════════════ EVENTS ═══════════════════

    public event Action<Vector3, float, NoiseType> OnNoiseGenerated;

    #endregion

    #region ═══════════════════ UNITY LIFECYCLE ═══════════════════

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        FindReferences();
        if (player != null)
        {
            lastPosition = player.position;
        }
    }

    private void Update()
    {
        if (player == null)
        {
            FindReferences();
            return;
        }
        
        UpdateMovementDetection();
        UpdateCurrentSurface();
        UpdateNoiseGeneration();
    }

    private void FindReferences()
    {
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
                lastPosition = player.position;
                Debug.Log("[PlayerNoiseSystem] Found player by tag!");
            }
        }
    }

    #endregion

    #region ═══════════════════ MOVEMENT DETECTION ═══════════════════

    private void UpdateMovementDetection()
    {
        // Calculate speed from position change
        Vector3 movement = player.position - lastPosition;
        movement.y = 0; // Ignore vertical movement
        
        currentSpeed = movement.magnitude / Time.deltaTime;
        lastPosition = player.position;
        
        // Determine movement state
        isMoving = currentSpeed > idleSpeedThreshold;
        isSprinting = currentSpeed > sprintSpeedThreshold;
    }

    #endregion

    #region ═══════════════════ SURFACE DETECTION ═══════════════════

    private void UpdateCurrentSurface()
    {
        if (player == null) return;
        
        RaycastHit hit;
        if (Physics.Raycast(player.position + Vector3.up * 0.1f, Vector3.down, out hit, 1f, groundLayerMask))
        {
            string tag = hit.collider.tag;
            
            if (tag == "Metal" || hit.collider.gameObject.layer == LayerMask.NameToLayer("Metal"))
            {
                currentSurface = SurfaceType.Metal;
            }
            else if (tag == "Grass" || tag == "Carpet" || tag == "Soft")
            {
                currentSurface = SurfaceType.Soft;
            }
            else if (tag == "Water")
            {
                currentSurface = SurfaceType.Water;
            }
            else
            {
                currentSurface = SurfaceType.Normal;
            }
        }
    }

    private float GetSurfaceMultiplier()
    {
        switch (currentSurface)
        {
            case SurfaceType.Metal: return metalSurfaceMultiplier;
            case SurfaceType.Soft: return softSurfaceMultiplier;
            case SurfaceType.Water: return waterSurfaceMultiplier;
            default: return normalSurfaceMultiplier;
        }
    }

    #endregion

    #region ═══════════════════ NOISE GENERATION ═══════════════════

    private void UpdateNoiseGeneration()
    {
        // Calculate base noise radius based on speed
        float baseRadius = CalculateBaseNoiseRadius();
        
        // Apply surface multiplier
        currentNoiseRadius = baseRadius * GetSurfaceMultiplier();
        
        // Generate noise event at interval
        if (currentNoiseRadius > 0 && Time.time - lastNoiseTime >= noiseInterval)
        {
            GenerateNoiseEvent(player.position, currentNoiseRadius, NoiseType.Footstep);
            lastNoiseTime = Time.time;
        }
    }

    private float CalculateBaseNoiseRadius()
    {
        if (!isMoving)
        {
            return idleNoiseRadius;
        }
        
        if (isSprinting)
        {
            return sprintNoiseRadius;
        }
        
        return walkNoiseRadius;
    }

    public void GenerateNoiseEvent(Vector3 position, float radius, NoiseType type)
    {
        if (radius <= 0) return;
        
        OnNoiseGenerated?.Invoke(position, radius, type);
        
        // Notify all registered listeners
        foreach (var listener in listeners)
        {
            if (listener != null)
            {
                listener.HearSound(position, radius, type);
            }
        }
        
        if (logNoiseEvents)
        {
            Debug.Log($"[Noise] {type} at {position}, radius: {radius:F1}, surface: {currentSurface}, listeners: {listeners.Count}");
        }
    }

    #endregion

    #region ═══════════════════ LISTENER MANAGEMENT ═══════════════════

    public void RegisterListener(ISoundListener listener)
    {
        if (!listeners.Contains(listener))
        {
            listeners.Add(listener);
            Debug.Log($"[PlayerNoiseSystem] Registered listener. Total: {listeners.Count}");
        }
    }

    public void UnregisterListener(ISoundListener listener)
    {
        listeners.Remove(listener);
    }

    #endregion

    #region ═══════════════════ DEBUG ═══════════════════

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || player == null) return;
        
        if (currentNoiseRadius > 0)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.15f);
            Gizmos.DrawSphere(player.position, currentNoiseRadius);
            
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            Gizmos.DrawWireSphere(player.position, currentNoiseRadius);
        }
    }

    private void OnGUI()
    {
        if (!showDebugGizmos || !Application.isPlaying) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 200, 120));
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, 200, 120), Texture2D.whiteTexture);
        
        GUI.color = Color.white;
        GUILayout.Label($"Speed: {currentSpeed:F1}");
        GUILayout.Label($"Moving: {isMoving}");
        GUILayout.Label($"Sprinting: {isSprinting}");
        GUILayout.Label($"Noise: {currentNoiseRadius:F1}m ({currentSurface})");
        GUILayout.Label($"Listeners: {listeners.Count}");
        GUILayout.EndArea();
    }

    #endregion
}

/// <summary>
/// Interface for objects that can hear sounds.
/// </summary>
public interface ISoundListener
{
    void HearSound(Vector3 position, float radius, PlayerNoiseSystem.NoiseType type);
}
