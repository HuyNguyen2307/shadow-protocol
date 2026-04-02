using UnityEngine;
using System;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// HEARING SENSOR - AI Sound Detection
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class HearingSensor : MonoBehaviour, ISoundListener
{
    #region ═══════════════════ SERIALIZED FIELDS ═══════════════════

    [Header("═══ HEARING RANGE ═══")]
    [SerializeField] private float maxHearingRange = 15f;
    [SerializeField] private float minHearingRange = 3f;

    [Header("═══ SENSITIVITY ═══")]
    [Range(0.1f, 3f)]
    [SerializeField] private float hearingSensitivity = 1.0f;
    [Tooltip("How much sound passes through walls (0.3 = 30% passes through)")]
    [Range(0f, 1f)]
    [SerializeField] private float wallSoundReduction = 0.5f;  // Increased from 0.3
    [Tooltip("Layer mask for WALLS ONLY - do not include floor/cover!")]
    [SerializeField] private LayerMask obstacleMask;

    [Header("═══ SUSPICION METER ═══")]
    [SerializeField] private float suspicionBuildRate = 1.5f;  // Increased from 0.5
    [SerializeField] private float suspicionDecayRate = 0.1f;  // Decreased from 0.2
    [Range(0f, 1f)]
    [SerializeField] private float investigationThreshold = 0.3f;  // Decreased from 0.4
    [Range(0f, 1f)]
    [SerializeField] private float alertThreshold = 0.7f;  // Decreased from 0.8

    [Header("═══ MEMORY ═══")]
    [SerializeField] private float soundMemoryDuration = 5f;

    [Header("═══ DEBUG ═══")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private bool logHearingEvents = false;

    #endregion

    #region ═══════════════════ PRIVATE FIELDS ═══════════════════

    private float suspicionMeter = 0f;
    private Vector3 lastHeardPosition = Vector3.zero;
    private float lastHeardTime = -100f;
    private bool isHearingSound = false;
    private float lastHeardSoundIntensity = 0f;
    private bool isRegistered = false;
    private float lastSoundProcessedTime = -100f;
    private const float HEARING_GRACE_PERIOD = 0.5f; // Don't decay for 0.5s after hearing
    
    private HearingState currentState = HearingState.Idle;
    private HearingState previousState = HearingState.Idle;

    #endregion

    #region ═══════════════════ ENUMS ═══════════════════

    public enum HearingState
    {
        Idle,
        Hearing,
        Suspicious,
        Alert
    }

    #endregion

    #region ═══════════════════ PUBLIC PROPERTIES ═══════════════════

    public float SuspicionMeter => suspicionMeter;
    public Vector3 LastHeardPosition => lastHeardPosition;
    public bool IsHearingSound => isHearingSound;
    public HearingState CurrentState => currentState;
    public float MaxHearingRange => maxHearingRange;
    
    public bool ShouldInvestigate => suspicionMeter >= investigationThreshold && 
                                      Time.time - lastHeardTime < soundMemoryDuration;
    
    public bool IsAlert => suspicionMeter >= alertThreshold;
    
    public bool HasSoundMemory => Time.time - lastHeardTime < soundMemoryDuration && 
                                   lastHeardPosition != Vector3.zero;

    #endregion

    #region ═══════════════════ EVENTS ═══════════════════

    public event Action<Vector3> OnSoundInvestigate;
    public event Action OnSoundAlert;
    public event Action<HearingState> OnHearingStateChanged;
    public event Action<Vector3, float> OnSoundHeard;

    #endregion

    #region ═══════════════════ UNITY LIFECYCLE ═══════════════════

    private void Start()
    {
        TryRegister();
    }

    private void Update()
    {
        // Keep trying to register if not yet registered
        if (!isRegistered)
        {
            TryRegister();
        }
        
        UpdateSuspicion();
        UpdateState();
        
        // Reset hearing flag each frame
        isHearingSound = false;
    }

    private void TryRegister()
    {
        if (PlayerNoiseSystem.Instance != null && !isRegistered)
        {
            PlayerNoiseSystem.Instance.RegisterListener(this);
            isRegistered = true;
            Debug.Log($"[HearingSensor] {gameObject.name} registered with PlayerNoiseSystem!");
        }
    }

    private void OnDestroy()
    {
        if (PlayerNoiseSystem.Instance != null)
        {
            PlayerNoiseSystem.Instance.UnregisterListener(this);
        }
    }

    #endregion

    #region ═══════════════════ ISOUNDLISTENER IMPLEMENTATION ═══════════════════

    public void HearSound(Vector3 position, float radius, PlayerNoiseSystem.NoiseType type)
    {
        // Use head height for both positions to avoid hitting floor
        Vector3 earPosition = transform.position + Vector3.up * 1.5f;
        Vector3 soundPosition = position + Vector3.up * 1.0f;
        
        float distance = Vector3.Distance(transform.position, position);
        
        float effectiveRange = Mathf.Min(radius, maxHearingRange) * hearingSensitivity;
        
        if (distance > effectiveRange && distance > minHearingRange)
        {
            return; // Too far
        }
        
        float intensity = 1f - (distance / effectiveRange);
        intensity = Mathf.Clamp01(intensity);
        
        // Check for WALLS ONLY (not floor/cover) using elevated positions
        if (Physics.Linecast(earPosition, soundPosition, obstacleMask))
        {
            // Wall blocks some sound but not all
            intensity *= wallSoundReduction;
            
            if (logHearingEvents)
            {
                Debug.Log($"[HearingSensor] {gameObject.name} Sound blocked by obstacle, reduced to {intensity:F2}");
            }
        }
        
        intensity *= GetTypeModifier(type);
        
        // Lower threshold - process even quiet sounds
        if (intensity > 0.05f)
        {
            ProcessSound(position, intensity, type);
        }
    }

    private float GetTypeModifier(PlayerNoiseSystem.NoiseType type)
    {
        switch (type)
        {
            case PlayerNoiseSystem.NoiseType.Footstep: return 1.0f;
            case PlayerNoiseSystem.NoiseType.Landing: return 1.5f;
            case PlayerNoiseSystem.NoiseType.Interaction: return 1.2f;
            case PlayerNoiseSystem.NoiseType.Distraction: return 1.8f;
            default: return 1.0f;
        }
    }

    private void ProcessSound(Vector3 position, float intensity, PlayerNoiseSystem.NoiseType type)
    {
        isHearingSound = true;
        lastHeardSoundIntensity = intensity;
        lastSoundProcessedTime = Time.time;
        
        lastHeardPosition = position;
        lastHeardTime = Time.time;
        
        // INCREASED: Build suspicion faster
        suspicionMeter += intensity * suspicionBuildRate * 0.3f; // Per sound event, not per frame
        suspicionMeter = Mathf.Clamp01(suspicionMeter);
        
        OnSoundHeard?.Invoke(position, intensity);
        
        if (logHearingEvents)
        {
            Debug.Log($"[HearingSensor] {gameObject.name} heard {type} at {position}, intensity: {intensity:F2}, suspicion: {suspicionMeter:F2}", this);
        }
    }

    #endregion

    #region ═══════════════════ SUSPICION MANAGEMENT ═══════════════════

    private void UpdateSuspicion()
    {
        // Only decay if we haven't heard a sound recently (grace period)
        bool recentlyHeard = (Time.time - lastSoundProcessedTime) < HEARING_GRACE_PERIOD;
        
        if (!recentlyHeard)
        {
            suspicionMeter -= suspicionDecayRate * Time.deltaTime;
            suspicionMeter = Mathf.Max(0f, suspicionMeter);
        }
        
        if (Time.time - lastHeardTime > soundMemoryDuration)
        {
            lastHeardPosition = Vector3.zero;
        }
    }

    private void UpdateState()
    {
        previousState = currentState;
        
        if (suspicionMeter >= alertThreshold)
        {
            currentState = HearingState.Alert;
        }
        else if (suspicionMeter >= investigationThreshold)
        {
            currentState = HearingState.Suspicious;
        }
        else if (isHearingSound)
        {
            currentState = HearingState.Hearing;
        }
        else
        {
            currentState = HearingState.Idle;
        }
        
        if (currentState != previousState)
        {
            OnHearingStateChanged?.Invoke(currentState);
            
            // Fire investigate when reaching Suspicious from ANY lower state
            if (currentState == HearingState.Suspicious && 
                (previousState == HearingState.Hearing || previousState == HearingState.Idle))
            {
                Debug.Log($"[HearingSensor] {gameObject.name} >>> INVESTIGATE EVENT FIRED! Pos: {lastHeardPosition}");
                OnSoundInvestigate?.Invoke(lastHeardPosition);
            }
            
            if (currentState == HearingState.Alert && previousState != HearingState.Alert)
            {
                Debug.Log($"[HearingSensor] {gameObject.name} >>> ALERT EVENT FIRED!");
                OnSoundAlert?.Invoke();
            }
        }
    }

    #endregion

    #region ═══════════════════ PUBLIC METHODS ═══════════════════

    public void ResetHearing()
    {
        suspicionMeter = 0f;
        lastHeardPosition = Vector3.zero;
        lastHeardTime = -100f;
        currentState = HearingState.Idle;
    }

    public void AddSuspicion(float amount)
    {
        suspicionMeter += amount;
        suspicionMeter = Mathf.Clamp01(suspicionMeter);
    }

    public bool IsInHearingRange(Vector3 position)
    {
        return Vector3.Distance(transform.position, position) <= maxHearingRange;
    }

    #endregion

    #region ═══════════════════ DEBUG ═══════════════════

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;
        
        Gizmos.color = new Color(0f, 0.5f, 1f, 0.1f);
        Gizmos.DrawSphere(transform.position, maxHearingRange);
        
        Gizmos.color = new Color(0f, 0.5f, 1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, maxHearingRange);
        
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, minHearingRange);
        
        if (Application.isPlaying && HasSoundMemory)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
            Gizmos.DrawLine(transform.position, lastHeardPosition);
            Gizmos.DrawWireSphere(lastHeardPosition, 0.5f);
        }
    }

    private void OnGUI()
    {
        if (!showDebugGizmos || !Application.isPlaying) return;
        
        Camera cam = Camera.main;
        if (cam == null) return;
        
        Vector3 screenPos = cam.WorldToScreenPoint(transform.position + Vector3.up * 3.2f);
        if (screenPos.z < 0) return;
        
        float x = screenPos.x - 60;
        float y = Screen.height - screenPos.y;
        
        float barWidth = 50f;
        float barHeight = 5f;
        
        GUI.color = new Color(0, 0, 0, 0.5f);
        GUI.DrawTexture(new Rect(x, y, barWidth + 2, barHeight + 2), Texture2D.whiteTexture);
        
        GUI.color = suspicionMeter < investigationThreshold ? Color.blue :
                    suspicionMeter < alertThreshold ? Color.yellow : Color.red;
        GUI.DrawTexture(new Rect(x + 1, y + 1, barWidth * suspicionMeter, barHeight), Texture2D.whiteTexture);
        
        if (currentState != HearingState.Idle)
        {
            GUI.color = Color.white;
            GUI.Label(new Rect(x + barWidth + 5, y - 3, 80, 15), currentState.ToString());
        }
    }

    #endregion
}
