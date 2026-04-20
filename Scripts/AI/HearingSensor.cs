using UnityEngine;
using System;

/// <summary>
/// Detects player-generated sounds within range, attenuates them by distance and wall occlusion,
/// and drives a suspicion meter that triggers investigation or full-alert state transitions.
/// Implements <see cref="ISoundListener"/> so it can be registered with <see cref="PlayerNoiseSystem"/>.
/// </summary>
public class HearingSensor : MonoBehaviour, ISoundListener
{
    #region Inspector Settings

    [Header("Hearing Range")]
    [SerializeField] private float _maxRange = 15f;
    [SerializeField] private float _minHearingRange = 3f;

    [Header("Sensitivity")]
    [Range(0.1f, 3f)]
    [SerializeField] private float _sensitivityMultiplier = 1.0f;

    // Raised from 0.3: playtesting showed 0.3 let guards hear detailed footsteps through
    // thick concrete walls in the warehouse level, breaking stealth consistency.
    [Tooltip("Fraction of sound intensity that passes through walls (e.g. 0.5 = 50% passes through).")]
    [Range(0f, 1f)]
    [SerializeField] private float _wallSoundReduction = 0.5f;

    // Layer mask intentionally named to match VisionSensor convention so designers
    // configure both sensors from the same mental model in the Inspector.
    [Tooltip("Layer mask for WALLS ONLY — do not include floor or cover geometry.")]
    [SerializeField] private LayerMask _obstacleMask;

    [Header("Suspicion Meter")]

    // Raised from 0.5: the slower original rate meant guards could hear a full sprint
    // across an open room and barely react before the player reached cover.
    [SerializeField] private float _suspicionBuildRate = 1.5f;

    // Lowered from 0.2: faster decay at 0.2 collapsed suspicion so quickly that
    // crouching behind cover for two seconds fully reset the guard — too forgiving.
    [SerializeField] private float _suspicionDecayRate = 0.1f;

    // Lowered from 0.4: the higher threshold meant guards ignored faint but clearly
    // audible sounds (e.g. distant Interaction events) during early playtests.
    [Range(0f, 1f)]
    [SerializeField] private float _investigationThreshold = 0.3f;

    // Lowered from 0.8: 0.8 required nearly continuous loud noise to trigger full alert,
    // which made the alarm state feel unachievable in practice.
    [Range(0f, 1f)]
    [SerializeField] private float _alertThreshold = 0.7f;

    [Header("Memory")]
    [SerializeField] private float _soundMemoryDuration = 5f;

    [Header("Debug")]
    [SerializeField] private bool _showDebugGizmos = true;
    [SerializeField] private bool _logHearingEvents = false;

    #endregion

    #region Public State

    /// <summary>Gets the normalised suspicion level in the range [0, 1].</summary>
    public float SuspicionLevel => _suspicionLevel;

    /// <summary>Gets the world-space position of the most recently heard sound.</summary>
    public Vector3 LastHeardPosition => _lastHeardPosition;

    /// <summary>Gets whether a sound was processed this frame.</summary>
    public bool IsHearingSound => _isHearingSound;

    /// <summary>Gets the current hearing state of this sensor.</summary>
    public HearingState CurrentState => _currentState;

    /// <summary>Gets the maximum hearing range in world units.</summary>
    public float MaxHearingRange => _maxRange;

    /// <summary>
    /// Gets whether the guard should move to investigate the last heard position.
    /// Sound memory expires after <see cref="_soundMemoryDuration"/> seconds so the guard
    /// does not chase a ghost indefinitely.
    /// </summary>
    public bool ShouldInvestigate =>
        _suspicionLevel >= _investigationThreshold &&
        Time.time - _lastHeardTime < _soundMemoryDuration;

    /// <summary>Gets whether the guard has reached full alert from sound alone.</summary>
    public bool IsAlert => _suspicionLevel >= _alertThreshold;

    /// <summary>
    /// Gets whether a recent-enough heard position is still stored in memory.
    /// Guards use this to decide whether returning to patrol makes sense.
    /// </summary>
    public bool HasSoundMemory =>
        Time.time - _lastHeardTime < _soundMemoryDuration &&
        _lastHeardPosition != Vector3.zero;

    #endregion

    #region Private Fields

    // Suspicion decays over time rather than being binary so the player can duck behind
    // cover after making noise and avoid triggering an investigation — the core stealth loop.
    private float _suspicionLevel = 0f;

    private Vector3 _lastHeardPosition = Vector3.zero;
    private float _lastHeardTime = -100f;
    private bool _isHearingSound = false;
    private float _lastHeardSoundIntensity = 0f;
    private bool _isRegistered = false;
    private float _lastSoundProcessedTime = -100f;

    // Grace period prevents the suspicion meter from beginning to decay in the same
    // frame a sound is processed, which would cause micro-fluctuations at threshold edges.
    private const float HEARING_GRACE_PERIOD = 0.5f;

    private HearingState _currentState = HearingState.Idle;
    private HearingState _previousState = HearingState.Idle;

    #endregion

    #region Enums

    /// <summary>Represents the escalating awareness states driven by heard sounds.</summary>
    public enum HearingState
    {
        Idle,
        Hearing,
        Suspicious,
        Alert
    }

    #endregion

    #region Events

    /// <summary>Fired when suspicion crosses <see cref="_investigationThreshold"/>; carries the position to investigate.</summary>
    public event Action<Vector3> OnSoundInvestigate;

    /// <summary>Fired when suspicion crosses <see cref="_alertThreshold"/>.</summary>
    public event Action OnSoundAlert;

    /// <summary>Fired whenever the hearing state transitions to a new value.</summary>
    public event Action<HearingState> OnHearingStateChanged;

    /// <summary>Fired for every sound event that passes range and occlusion checks; carries position and final intensity.</summary>
    public event Action<Vector3, float> OnSoundHeard;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        TryRegister();
    }

    private void Update()
    {
        // Retry registration each frame until PlayerNoiseSystem is available at runtime startup.
        if (!_isRegistered)
        {
            TryRegister();
        }

        UpdateSuspicion();
        UpdateState();

        // Reset per-frame hearing flag so IsHearingSound only reflects the current frame.
        _isHearingSound = false;
    }

    private void OnDestroy()
    {
        if (PlayerNoiseSystem.Instance != null)
        {
            PlayerNoiseSystem.Instance.UnregisterListener(this);
        }
    }

    #endregion

    #region Sound Processing

    /// <summary>
    /// Called by <see cref="PlayerNoiseSystem"/> when the player emits a sound.
    /// This is the <see cref="ISoundListener"/> contract entry point — do not rename.
    /// </summary>
    /// <param name="position">World-space origin of the sound.</param>
    /// <param name="radius">Propagation radius reported by the noise system.</param>
    /// <param name="type">Semantic category used to apply per-type intensity modifiers.</param>
    public void HearSound(Vector3 position, float radius, PlayerNoiseSystem.NoiseType type)
    {
        float distance = Vector3.Distance(transform.position, position);
        float effectiveRange = Mathf.Min(radius, _maxRange) * _sensitivityMultiplier;

        // Guard clause: ignore sounds beyond effective range and below the guaranteed
        // minimum perception radius that exists regardless of the noise system's radius.
        if (distance > effectiveRange && distance > _minHearingRange)
            return;

        float intensity = CalculateHearingStrength(distance, effectiveRange);

        // Elevate both endpoints to head height to avoid the Linecast striking the floor
        // or low-lying geometry that should not block sound.
        Vector3 earPosition = transform.position + Vector3.up * 1.5f;
        Vector3 soundPosition = position + Vector3.up * 1.0f;

        // Linecast is used instead of Raycast because "from A to B" semantics are
        // clearer here than a direction+distance pair — intent reads at a glance.
        if (IsBlockedByWalls(earPosition, soundPosition))
        {
            intensity *= _wallSoundReduction;

            if (_logHearingEvents)
                Debug.Log($"[HearingSensor] {gameObject.name} sound blocked by obstacle, reduced to {intensity:F2}");
        }

        intensity *= GetTypeModifier(type);

        // Accept very faint sounds so that nearby quiet events (e.g. crouched footsteps)
        // still register a small suspicion increment rather than being silently dropped.
        if (intensity > 0.05f)
            ProcessSound(position, intensity, type);
    }

    private void ProcessSound(Vector3 position, float intensity, PlayerNoiseSystem.NoiseType type)
    {
        _isHearingSound = true;
        _lastHeardSoundIntensity = intensity;
        _lastSoundProcessedTime = Time.time;
        _lastHeardPosition = position;
        _lastHeardTime = Time.time;

        // Suspicion is accumulated per sound event, not per frame, so rapid-fire
        // identical sounds cannot trivially spike the meter in a single Update tick.
        _suspicionLevel += intensity * _suspicionBuildRate * 0.3f;
        _suspicionLevel = Mathf.Clamp01(_suspicionLevel);

        OnSoundHeard?.Invoke(position, intensity);

        if (_logHearingEvents)
            Debug.Log($"[HearingSensor] {gameObject.name} heard {type} at {position}, intensity: {intensity:F2}, suspicion: {_suspicionLevel:F2}", this);
    }

    private float GetTypeModifier(PlayerNoiseSystem.NoiseType type)
    {
        switch (type)
        {
            case PlayerNoiseSystem.NoiseType.Footstep:    return 1.0f;
            case PlayerNoiseSystem.NoiseType.Landing:     return 1.5f;
            case PlayerNoiseSystem.NoiseType.Interaction: return 1.2f;
            case PlayerNoiseSystem.NoiseType.Distraction: return 1.8f;
            default:                                      return 1.0f;
        }
    }

    #endregion

    #region Suspicion

    private void UpdateSuspicion()
    {
        bool recentlyHeard = (Time.time - _lastSoundProcessedTime) < HEARING_GRACE_PERIOD;

        // Decay only outside the grace window so a guard who just heard a sound does not
        // immediately begin losing suspicion in the same Update cycle.
        if (!recentlyHeard)
        {
            _suspicionLevel -= _suspicionDecayRate * Time.deltaTime;
            _suspicionLevel = Mathf.Max(0f, _suspicionLevel);
        }

        if (Time.time - _lastHeardTime > _soundMemoryDuration)
            _lastHeardPosition = Vector3.zero;
    }

    private void UpdateState()
    {
        _previousState = _currentState;

        if (_suspicionLevel >= _alertThreshold)
            _currentState = HearingState.Alert;
        else if (_suspicionLevel >= _investigationThreshold)
            _currentState = HearingState.Suspicious;
        else if (_isHearingSound)
            _currentState = HearingState.Hearing;
        else
            _currentState = HearingState.Idle;

        if (_currentState == _previousState)
            return;

        OnHearingStateChanged?.Invoke(_currentState);

        // Investigation is triggered only on the first crossing into Suspicious from a
        // lower state, preventing repeated event floods if suspicion hovers at the threshold.
        if (_currentState == HearingState.Suspicious &&
            (_previousState == HearingState.Hearing || _previousState == HearingState.Idle))
        {
            Debug.Log($"[HearingSensor] {gameObject.name} >>> INVESTIGATE EVENT FIRED! Pos: {_lastHeardPosition}");
            OnSoundInvestigate?.Invoke(_lastHeardPosition);
        }

        if (_currentState == HearingState.Alert && _previousState != HearingState.Alert)
        {
            Debug.Log($"[HearingSensor] {gameObject.name} >>> ALERT EVENT FIRED!");
            OnSoundAlert?.Invoke();
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Computes a normalised hearing strength in [0, 1] based on distance to the sound.
    /// Attenuation is linear with distance rather than inverse-square because we are
    /// working in a discretised game world where pure inverse-square would make mid-range
    /// sounds inaudible — linear gives designers a predictable falloff curve to tune.
    /// </summary>
    /// <param name="distance">Actual distance from this sensor to the sound origin.</param>
    /// <param name="maxRange">Effective range beyond which strength reaches zero.</param>
    private float CalculateHearingStrength(float distance, float maxRange)
    {
        return Mathf.Clamp01(1f - (distance / maxRange));
    }

    /// <summary>
    /// Returns true if solid wall geometry on <see cref="_obstacleMask"/> fully or partially
    /// occludes the path from <paramref name="from"/> to <paramref name="to"/>.
    /// Linecast is preferred over Raycast here because it expresses "does anything block the
    /// path between these two known points" without requiring a direction vector calculation.
    /// </summary>
    /// <param name="from">Start point (typically the guard's ear position).</param>
    /// <param name="to">End point (typically the elevated sound origin).</param>
    private bool IsBlockedByWalls(Vector3 from, Vector3 to)
    {
        return Physics.Linecast(from, to, _obstacleMask);
    }

    private void TryRegister()
    {
        if (PlayerNoiseSystem.Instance == null || _isRegistered)
            return;

        PlayerNoiseSystem.Instance.RegisterListener(this);
        _isRegistered = true;
        Debug.Log($"[HearingSensor] {gameObject.name} registered with PlayerNoiseSystem!");
    }

    #endregion

    #region Public Methods

    /// <summary>Resets suspicion, sound memory, and state back to idle defaults.</summary>
    public void ResetHearing()
    {
        _suspicionLevel = 0f;
        _lastHeardPosition = Vector3.zero;
        _lastHeardTime = -100f;
        _currentState = HearingState.Idle;
    }

    /// <summary>
    /// Adds a direct suspicion increment from an external system (e.g. a scripted event).
    /// Clamps the result to [0, 1] to maintain meter integrity.
    /// </summary>
    /// <param name="amount">Suspicion amount to add, typically in the range (0, 1].</param>
    public void AddSuspicion(float amount)
    {
        _suspicionLevel = Mathf.Clamp01(_suspicionLevel + amount);
    }

    /// <summary>Returns true if <paramref name="position"/> falls within the maximum hearing range.</summary>
    /// <param name="position">World-space position to test.</param>
    public bool IsInHearingRange(Vector3 position)
    {
        return Vector3.Distance(transform.position, position) <= _maxRange;
    }

    #endregion

    #region Debug

    private void OnDrawGizmos()
    {
        if (!_showDebugGizmos) return;

        Gizmos.color = new Color(0f, 0.5f, 1f, 0.1f);
        Gizmos.DrawSphere(transform.position, _maxRange);

        Gizmos.color = new Color(0f, 0.5f, 1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, _maxRange);

        Gizmos.color = new Color(0f, 0.8f, 1f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, _minHearingRange);

        if (Application.isPlaying && HasSoundMemory)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
            Gizmos.DrawLine(transform.position, _lastHeardPosition);
            Gizmos.DrawWireSphere(_lastHeardPosition, 0.5f);
        }
    }

    private void OnGUI()
    {
        if (!_showDebugGizmos || !Application.isPlaying) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 screenPos = cam.WorldToScreenPoint(transform.position + Vector3.up * 3.2f);
        if (screenPos.z < 0) return;

        float x = screenPos.x - 60;
        float y = Screen.height - screenPos.y;

        const float barWidth  = 50f;
        const float barHeight = 5f;

        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(new Rect(x, y, barWidth + 2, barHeight + 2), Texture2D.whiteTexture);

        GUI.color = _suspicionLevel < _investigationThreshold ? Color.blue :
                    _suspicionLevel < _alertThreshold         ? Color.yellow : Color.red;
        GUI.DrawTexture(new Rect(x + 1, y + 1, barWidth * _suspicionLevel, barHeight), Texture2D.whiteTexture);

        if (_currentState != HearingState.Idle)
        {
            GUI.color = Color.white;
            GUI.Label(new Rect(x + barWidth + 5, y - 3, 80, 15), _currentState.ToString());
        }
    }

    #endregion
}
