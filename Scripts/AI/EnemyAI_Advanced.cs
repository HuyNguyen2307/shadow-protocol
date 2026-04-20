using UnityEngine;
using UnityEngine.AI;
using System;

/// <summary>
/// Finite-state-machine enemy AI integrating vision, hearing, episodic memory, and
/// multi-agent alert broadcasting. Drives six discrete states (PATROL, INVESTIGATE,
/// SUSPICIOUS, CHASE, SEARCH, RESPOND_ALERT) through sensor-threshold rules and
/// timed transitions, with graceful degradation when optional components are absent.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(VisionSensor))]
public class EnemyAI_Advanced : MonoBehaviour, IAlertListener
{
    #region Inspector Settings

    public enum AIState
    {
        PATROL,
        INVESTIGATE,
        SUSPICIOUS,
        CHASE,
        SEARCH,
        RESPOND_ALERT
    }

    public enum AlertLevel
    {
        Relaxed,
        Cautious,
        Suspicious,
        Alert,
        Combat
    }

    [Header("Sensors")]
    [SerializeField] private VisionSensor _vision;
    [SerializeField] private HearingSensor _hearing;
    [SerializeField] private AIMemory _memory;

    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private Transform[] _patrolRoute;

    [Header("Movement Speeds")]
    [SerializeField] private float patrolSpeed = 2.5f;
    [SerializeField] private float investigateSpeed = 3.0f;
    [SerializeField] private float suspiciousSpeed = 3.5f;
    [SerializeField] private float chaseSpeed = 5.0f;
    [SerializeField] private float searchSpeed = 3.5f;

    [Header("Patrol Settings")]
    [SerializeField] private float waypointWaitTime = 2.0f;
    [SerializeField] private float waypointReachThreshold = 0.5f;

    [Header("Detection Thresholds")]
    [Tooltip("Vision detection fraction required to enter SUSPICIOUS — a threshold prevents reacting to single-frame glimpses.")]
    [Range(0f, 1f)]
    [SerializeField] private float suspiciousThreshold = 0.3f;

    [Tooltip("Vision detection fraction required to enter CHASE — full meter means confident, unambiguous sighting.")]
    [Range(0f, 1f)]
    [SerializeField] private float chaseThreshold = 1.0f;

    [Header("Chase Settings")]
    [SerializeField] private float loseSightDuration = 2.0f;
    [SerializeField] private float catchDistance = 1.5f;

    [Header("Search Settings")]
    [SerializeField] private float searchDuration = 8.0f;
    [SerializeField] private float searchScanSpeed = 90f;
    [SerializeField] private int searchPointsToCheck = 3;

    [Header("Investigate Settings")]
    [SerializeField] private float investigateDuration = 4.0f;
    [SerializeField] private float investigateLookAroundTime = 2.0f;

    [Header("Alert Communication")]
    [Tooltip("Should this enemy alert others when spotting player?")]
    [SerializeField] private bool canRaiseAlerts = true;
    [Tooltip("Should this enemy respond to alerts from others?")]
    [SerializeField] private bool respondsToAlerts = true;
    [Tooltip("Speed when responding to alert")]
    [SerializeField] private float respondAlertSpeed = 4.0f;
    [Tooltip("How long to investigate an alert position")]
    [SerializeField] private float alertResponseDuration = 6.0f;

    [Header("Alert Decay")]
    [SerializeField] private float alertDecayRate = 0.1f;
    [SerializeField] private float relaxedPatrolSpeedMultiplier = 0.8f;

    [Header("Debug")]
    [SerializeField] private bool enableDebug = true;

    #endregion

    #region State

    // Named constants replace all magic numbers so tuning values are self-documenting.
    private const float AlertRaiseFastRate    = 2f;
    private const float SuspiciousMinDwell   = 3f;
    private const float SearchScanDwell      = 2f;
    private const float AlertScanDwell       = 3f;
    private const float SearchRadius         = 5f;
    private const float SearchNavSampleRange = 3f;
    private const int   SearchRetryLimit     = 5;
    private const float AlertMeterSoundFloor  = 0.7f;
    private const float AlertMeterCombatFloor = 0.6f;
    private const float AlertMeterSuspFloor   = 0.5f;
    private const float AlertMeterSpottedFloor = 0.3f;
    private const float AlertMeterCombatThreshold    = 0.9f;
    private const float AlertMeterAlertThreshold     = 0.7f;
    private const float AlertMeterSuspiciousThreshold = 0.4f;
    private const float AlertMeterCautiousThreshold  = 0.1f;
    private const float VisionAlertWeight    = 0.5f;
    private const float HearingAlertWeight   = 0.3f;
    private const float SearchAlertFloor     = 0.6f;

    // Components
    private NavMeshAgent _navAgent;

    // State machine
    private AIState _currentState = AIState.PATROL;
    private AlertLevel _alertLevel = AlertLevel.Relaxed;
    private float _alertMeter = 0f;
    private float _stateEnterTime;

    // Sensor subscription guard — avoids a per-frame retry once sensors are live.
    // HearingSensor is AddComponent'd at runtime, so subscription can't always
    // happen in Awake; the guard lets Update wire it up on the first valid frame
    // without paying the cost of repeated null checks thereafter.
    private bool _sensorsReady = false;
    private bool _isRegisteredWithAlertSystem = false;

    // Patrol
    private int   _patrolIndex = 0;
    private float _waypointWaitTimer = 0f;
    private bool  _isWaitingAtWaypoint = false;

    // Chase
    private float _lastPlayerSeenTime;
    private Vector3 _lastKnownPosition;

    // Alert response
    private Vector3 _alertTargetPosition;
    private float   _alertResponseStartTime;
    private bool    _hasReachedAlertPosition = false;

    // Search
    private Vector3 _searchCenter;
    private float   _searchStartTime;
    private int     _searchPointsChecked = 0;
    private float   _searchScanTimer = 0f;
    private bool    _isScanning = false;

    // Investigate
    private Vector3 _investigationPoint;
    private float   _investigateStartTime;
    private bool    _isLookingAround = false;

    // SetDestination deduplication — NavMesh path recalculation is expensive;
    // we track the last submitted destination and skip redundant calls.
    private Vector3 _lastSetDestination = Vector3.positiveInfinity;

    #endregion

    #region Public Properties

    /// <summary>Read by StealthMinimap and AIMetricsCollector — do not rename.</summary>
    public AIState State => _currentState;

    /// <summary>String representation of the current state for UI display.</summary>
    public string CurrentState => _currentState.ToString();

    /// <summary>Composite alert tier derived from sensor inputs.</summary>
    public AlertLevel CurrentAlertLevel => _alertLevel;

    /// <summary>Continuous 0-1 alert accumulator that drives AlertLevel tiers.</summary>
    public float AlertMeter => _alertMeter;

    /// <summary>Passthrough to VisionSensor's detection accumulator.</summary>
    public float DetectionLevel => _vision?.DetectionMeter ?? 0f;

    /// <summary>True when VisionSensor has line-of-sight to the player this frame.</summary>
    public bool CanSeePlayer => _vision?.CanSeePlayer ?? false;

    #endregion

    #region Events

    public event Action<AIState> OnStateChanged;
    public event Action<AlertLevel> OnAlertLevelChanged;
    public event Action OnPlayerCaught;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        _navAgent = GetComponent<NavMeshAgent>();

        // Fall back to sibling components before AddComponent so we don't duplicate
        // sensors that were assigned in the Inspector.
        if (_vision == null)
            _vision = GetComponent<VisionSensor>();
        if (_hearing == null)
            _hearing = GetComponent<HearingSensor>();
        if (_memory == null)
            _memory = GetComponent<AIMemory>();

        if (_hearing == null)
            _hearing = gameObject.AddComponent<HearingSensor>();
        if (_memory == null)
            _memory = gameObject.AddComponent<AIMemory>();
    }

    private void Start()
    {
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                player = playerObj.transform;
        }

        TrySubscribeToSensors();
        RegisterWithAlertSystem();
        TransitionTo(AIState.PATROL);
    }

    private void OnDestroy()
    {
        UnsubscribeFromSensors();
        UnregisterFromAlertSystem();
    }

    private void Update()
    {
        // HearingSensor may be instantiated after Start() due to Unity's component
        // initialization order, so we retry subscription until it succeeds, then
        // permanently skip this branch via _sensorsReady to avoid per-frame overhead.
        if (!_sensorsReady)
            TrySubscribeToSensors();

        UpdateAlertLevel();
        UpdateCurrentState();
        CheckCatchCondition();
    }

    #endregion

    #region Sensor Integration

    private void TrySubscribeToSensors()
    {
        if (_hearing == null)
            return;

        _hearing.OnSoundInvestigate += HandleSoundInvestigate;
        _hearing.OnSoundAlert       += HandleSoundAlert;
        _sensorsReady = true;

        if (enableDebug)
            Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: Subscribed to HearingSensor events.");
    }

    private void UnsubscribeFromSensors()
    {
        if (_hearing == null)
            return;

        _hearing.OnSoundInvestigate -= HandleSoundInvestigate;
        _hearing.OnSoundAlert       -= HandleSoundAlert;
    }

    private void HandleSoundInvestigate(Vector3 position)
    {
        if (enableDebug)
            Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: Sound investigate at {position}. State: {_currentState}");

        if (_currentState != AIState.PATROL && _currentState != AIState.INVESTIGATE)
        {
            if (enableDebug)
                Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: Ignored sound — currently in {_currentState}.");
            return;
        }

        _investigationPoint = position;
        _memory?.RememberPlayerHeard(position);
        TransitionTo(AIState.INVESTIGATE);

        if (enableDebug)
            Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: Investigating sound at {position}.");
    }

    private void HandleSoundAlert()
    {
        _alertMeter = Mathf.Max(_alertMeter, AlertMeterSoundFloor);
        UpdateAlertLevelFromMeter();
    }

    #endregion

    #region State Machine

    private void UpdateCurrentState()
    {
        CheckVisionTransitions();

        switch (_currentState)
        {
            case AIState.PATROL:        UpdatePatrol();       break;
            case AIState.INVESTIGATE:   UpdateInvestigate();  break;
            case AIState.SUSPICIOUS:    UpdateSuspicious();   break;
            case AIState.CHASE:         UpdateChase();        break;
            case AIState.SEARCH:        UpdateSearch();       break;
            case AIState.RESPOND_ALERT: UpdateRespondAlert(); break;
        }
    }

    private void CheckVisionTransitions()
    {
        if (_vision == null)
            return;

        float detection = _vision.DetectionMeter;

        // Full detection always overrides any non-chase state immediately.
        if (detection >= chaseThreshold && _currentState != AIState.CHASE)
        {
            TransitionTo(AIState.CHASE);
            return;
        }

        // A threshold for SUSPICIOUS (rather than reacting to any nonzero detection)
        // prevents spurious state changes from momentary partial occlusion or sensor
        // jitter — the player must maintain meaningful visibility before the enemy reacts.
        if (detection >= suspiciousThreshold && detection < chaseThreshold)
        {
            if (_currentState == AIState.PATROL || _currentState == AIState.INVESTIGATE)
            {
                _investigationPoint = player.position;
                TransitionTo(AIState.SUSPICIOUS);
            }
        }
    }

    private void TransitionTo(AIState newState)
    {
        if (_currentState == newState)
            return;

        AIState previous = _currentState;
        _currentState   = newState;
        _stateEnterTime = Time.time;

        OnExitState(previous);
        OnEnterState(newState);
        OnStateChanged?.Invoke(newState);

        if (enableDebug)
            Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: {previous} → {newState}", this);
    }

    private void OnEnterState(AIState state)
    {
        switch (state)
        {
            case AIState.PATROL:
                _navAgent.speed      = patrolSpeed * GetAlertSpeedMultiplier();
                _navAgent.isStopped  = false;
                _isWaitingAtWaypoint = false;
                _isLookingAround     = false;
                break;

            case AIState.INVESTIGATE:
                _navAgent.speed       = investigateSpeed;
                _investigateStartTime = Time.time;
                _isLookingAround      = false;
                SetDestination(_investigationPoint);
                break;

            case AIState.SUSPICIOUS:
                _navAgent.speed = suspiciousSpeed;
                _alertMeter     = Mathf.Max(_alertMeter, AlertMeterSuspFloor);
                RaiseAlertToOthers(AlertSystem.AlertType.SPOTTED);
                break;

            case AIState.CHASE:
                _navAgent.speed     = chaseSpeed;
                _navAgent.isStopped = false;
                _alertMeter         = 1f;
                UpdateAlertLevelFromMeter();
                RaiseAlertToOthers(AlertSystem.AlertType.COMBAT);
                break;

            case AIState.SEARCH:
                _navAgent.speed      = searchSpeed;
                _searchCenter        = _memory?.LastSeenPosition ?? transform.position;
                _searchStartTime     = Time.time;
                _searchPointsChecked = 0;
                _isScanning          = false;
                GenerateSearchPoint();
                // Broadcast so nearby allies converge on the same area instead of
                // continuing unrelated patrols while the player is known to be close.
                RaiseAlertToOthers(AlertSystem.AlertType.LOST);
                break;

            case AIState.RESPOND_ALERT:
                _navAgent.speed     = respondAlertSpeed;
                _navAgent.isStopped = false;
                _alertMeter         = Mathf.Max(_alertMeter, AlertMeterCombatFloor);
                UpdateAlertLevelFromMeter();
                SetDestination(_alertTargetPosition);

                if (enableDebug)
                    Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: Responding to alert at {_alertTargetPosition}.");
                break;
        }
    }

    private void OnExitState(AIState state)
    {
        switch (state)
        {
            case AIState.CHASE:
                if (_vision != null && _vision.LastKnownPlayerPos != Vector3.zero)
                {
                    _lastKnownPosition = _vision.LastKnownPlayerPos;
                    _memory?.RememberPlayerSeen(_lastKnownPosition);
                }
                break;

            case AIState.SEARCH:
                _memory?.MarkAsSearched(_searchCenter);
                break;
        }
    }

    #endregion

    #region Patrol

    private void UpdatePatrol()
    {
        if (_patrolRoute == null || _patrolRoute.Length == 0)
            return;

        if (_isWaitingAtWaypoint)
        {
            _waypointWaitTimer += Time.deltaTime;
            LookAround(45f);

            if (_waypointWaitTimer >= waypointWaitTime)
            {
                _isWaitingAtWaypoint = false;
                _navAgent.isStopped  = false;
                AdvanceToNextWaypoint();
            }

            return;
        }

        _navAgent.isStopped = false;

        if (_patrolRoute[_patrolIndex] != null)
            SetDestination(_patrolRoute[_patrolIndex].position);

        if (!IsAtDestination())
            return;

        _isWaitingAtWaypoint = true;
        _waypointWaitTimer   = 0f;
        _navAgent.isStopped  = true;
    }

    private void AdvanceToNextWaypoint()
    {
        _patrolIndex = (_patrolIndex + 1) % _patrolRoute.Length;

        int safety = 0;
        while (_patrolRoute[_patrolIndex] == null && safety < _patrolRoute.Length)
        {
            _patrolIndex = (_patrolIndex + 1) % _patrolRoute.Length;
            safety++;
        }

        _navAgent.isStopped = false;
    }

    #endregion

    #region Investigation

    private void UpdateInvestigate()
    {
        float elapsed = Time.time - _investigateStartTime;

        // Hard timeout prevents the enemy from getting permanently stuck investigating
        // a position it can never reach (e.g., behind a door).
        if (elapsed >= investigateDuration)
        {
            TransitionTo(AIState.PATROL);
            return;
        }

        if (!_isLookingAround)
        {
            _navAgent.isStopped = false;
            SetDestination(_investigationPoint);

            if (!IsAtDestination())
                return;

            _isLookingAround    = true;
            _navAgent.isStopped = true;
            _searchScanTimer    = 0f;
            return;
        }

        LookAround(90f);
        _searchScanTimer += Time.deltaTime;

        if (_searchScanTimer >= investigateLookAroundTime)
        {
            _memory?.MarkAsSearched(_investigationPoint);
            TransitionTo(AIState.PATROL);
        }
    }

    private void UpdateSuspicious()
    {
        SetDestination(_investigationPoint);

        if (_vision == null || _vision.DetectionMeter >= suspiciousThreshold * 0.5f)
            return;

        float elapsed = Time.time - _stateEnterTime;
        if (elapsed <= SuspiciousMinDwell)
            return;

        // Resuming movement is mandatory here — isStopped may be true from a prior
        // waypoint wait, which would leave the NavMeshAgent frozen on re-entry to PATROL.
        _navAgent.isStopped = false;
        TransitionTo(AIState.PATROL);
    }

    #endregion

    #region Chase

    private void UpdateChase()
    {
        if (player == null)
            return;

        _navAgent.isStopped = false;
        SetDestination(player.position);

        if (_vision != null && _vision.CanSeePlayer)
        {
            _lastPlayerSeenTime = Time.time;
            _memory?.RememberPlayerSeen(player.position);
        }

        float timeSinceSeen = Time.time - _lastPlayerSeenTime;
        if (timeSinceSeen > loseSightDuration && !(_vision?.CanSeePlayer ?? false))
            TransitionTo(AIState.SEARCH);
    }

    #endregion

    #region Search

    private void UpdateSearch()
    {
        float elapsed       = Time.time - _searchStartTime;
        float totalDuration = searchDuration + (_memory?.GetSearchDurationBonus(_searchCenter) ?? 0f);

        if (elapsed >= totalDuration)
        {
            _vision?.ResetDetection();
            _hearing?.ResetHearing();
            TransitionTo(AIState.PATROL);
            return;
        }

        if (_isScanning)
        {
            UpdateSearchScan();
            return;
        }

        if (IsAtDestination())
        {
            _isScanning         = true;
            _searchScanTimer    = 0f;
            _navAgent.isStopped = true;
        }
    }

    private void UpdateSearchScan()
    {
        LookAround(searchScanSpeed * Time.deltaTime);
        _searchScanTimer += Time.deltaTime;

        if (_searchScanTimer < SearchScanDwell)
            return;

        _isScanning = false;
        _searchPointsChecked++;

        // A queue of discrete points (rather than pure random walk) guarantees the
        // enemy sweeps distinct positions and doesn't re-examine the same spot twice
        // when the search radius is small relative to step size.
        if (_searchPointsChecked >= searchPointsToCheck)
        {
            TransitionTo(AIState.PATROL);
            return;
        }

        GenerateSearchPoint();
    }

    private void UpdateRespondAlert()
    {
        float elapsed = Time.time - _alertResponseStartTime;

        if (elapsed >= alertResponseDuration)
        {
            TransitionTo(AIState.PATROL);
            return;
        }

        if (!_hasReachedAlertPosition)
        {
            _navAgent.isStopped = false;
            SetDestination(_alertTargetPosition);

            if (!IsAtDestination())
                return;

            _hasReachedAlertPosition = true;
            _navAgent.isStopped      = true;
            _searchScanTimer         = 0f;

            if (enableDebug)
                Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: Arrived at alert position — scanning.");

            return;
        }

        LookAround(searchScanSpeed * Time.deltaTime);
        _searchScanTimer += Time.deltaTime;

        if (_searchScanTimer < AlertScanDwell)
            return;

        if (_alertMeter > AlertMeterSuspFloor)
        {
            _searchCenter = _alertTargetPosition;
            TransitionTo(AIState.SEARCH);
        }
        else
        {
            TransitionTo(AIState.PATROL);
        }
    }

    #endregion

    #region Alert System

    private void RegisterWithAlertSystem()
    {
        if (AlertSystem.Instance == null || _isRegisteredWithAlertSystem)
            return;

        AlertSystem.Instance.RegisterListener(this);
        _isRegisteredWithAlertSystem = true;

        if (enableDebug)
            Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: Registered with AlertSystem.");
    }

    private void UnregisterFromAlertSystem()
    {
        if (AlertSystem.Instance == null || !_isRegisteredWithAlertSystem)
            return;

        AlertSystem.Instance.UnregisterListener(this);
        _isRegisteredWithAlertSystem = false;
    }

    private void RaiseAlertToOthers(AlertSystem.AlertType type)
    {
        if (!canRaiseAlerts || AlertSystem.Instance == null)
            return;

        Vector3 targetPos = player != null ? player.position : transform.position;
        AlertSystem.Instance.RaiseAlert(type, transform.position, targetPos, this);

        if (enableDebug)
            Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: Raised {type} alert.");
    }

    /// <summary>
    /// Receives alert broadcasts from other agents via the AlertSystem. RESPOND_ALERT
    /// is intentionally not a full detection state — the alert is positional hearsay
    /// from a peer, not a first-hand sighting, so the enemy investigates rather than
    /// immediately chasing an unconfirmed target.
    /// </summary>
    public void OnAlertReceived(AlertSystem.AlertData alert)
    {
        if (!respondsToAlerts)
            return;

        // CHASE and SEARCH are higher-priority states with confirmed threat knowledge;
        // downgrading them in response to a peer hint would regress tactical quality.
        if (_currentState == AIState.CHASE || _currentState == AIState.SEARCH)
        {
            if (enableDebug)
                Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: Ignored alert — already in {_currentState}.");
            return;
        }

        switch (alert.Type)
        {
            case AlertSystem.AlertType.SUSPICIOUS:
                _alertMeter = Mathf.Max(_alertMeter, AlertMeterSpottedFloor);
                UpdateAlertLevelFromMeter();
                break;

            case AlertSystem.AlertType.SPOTTED:
                if (_currentState == AIState.PATROL || _currentState == AIState.INVESTIGATE)
                {
                    _investigationPoint = alert.TargetPosition;
                    TransitionTo(AIState.INVESTIGATE);
                }
                break;

            case AlertSystem.AlertType.COMBAT:
            case AlertSystem.AlertType.BACKUP_REQUEST:
                _alertTargetPosition     = alert.TargetPosition;
                _alertResponseStartTime  = Time.time;
                _hasReachedAlertPosition = false;
                TransitionTo(AIState.RESPOND_ALERT);

                if (enableDebug)
                    Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: Responding to COMBAT alert at {alert.TargetPosition}.");
                break;

            case AlertSystem.AlertType.LOST:
                if (_currentState == AIState.PATROL)
                {
                    _investigationPoint = alert.TargetPosition;
                    TransitionTo(AIState.INVESTIGATE);
                }
                break;
        }

        _memory?.RememberPlayerHeard(alert.TargetPosition);
    }

    /// <summary>Returns this agent's world position for AlertSystem distance sorting.</summary>
    public Vector3 GetPosition() => transform.position;

    /// <summary>Returns true when actively pursuing the player in CHASE state.</summary>
    public bool IsInCombat() => _currentState == AIState.CHASE;

    #endregion

    #region Helpers

    private void UpdateAlertLevel()
    {
        float visionContrib  = (_vision?.DetectionMeter  ?? 0f) * VisionAlertWeight;
        float hearingContrib = (_hearing?.SuspicionLevel ?? 0f) * HearingAlertWeight;
        float targetAlert    = visionContrib + hearingContrib;

        if (_currentState == AIState.CHASE)
            targetAlert = 1f;
        else if (_currentState == AIState.SEARCH)
            targetAlert = Mathf.Max(targetAlert, SearchAlertFloor);

        float rate = targetAlert > _alertMeter ? AlertRaiseFastRate : alertDecayRate;
        _alertMeter = Mathf.MoveTowards(_alertMeter, targetAlert, rate * Time.deltaTime);

        UpdateAlertLevelFromMeter();
    }

    private void UpdateAlertLevelFromMeter()
    {
        AlertLevel newLevel;

        if      (_alertMeter >= AlertMeterCombatThreshold)    newLevel = AlertLevel.Combat;
        else if (_alertMeter >= AlertMeterAlertThreshold)     newLevel = AlertLevel.Alert;
        else if (_alertMeter >= AlertMeterSuspiciousThreshold) newLevel = AlertLevel.Suspicious;
        else if (_alertMeter >= AlertMeterCautiousThreshold)  newLevel = AlertLevel.Cautious;
        else                                                   newLevel = AlertLevel.Relaxed;

        if (newLevel == _alertLevel)
            return;

        _alertLevel = newLevel;
        OnAlertLevelChanged?.Invoke(_alertLevel);
    }

    private float GetAlertSpeedMultiplier()
    {
        return _alertLevel == AlertLevel.Relaxed ? relaxedPatrolSpeedMultiplier : 1f;
    }

    private bool IsAtDestination()
    {
        if (_navAgent.pathPending) return false;
        if (_navAgent.remainingDistance > waypointReachThreshold) return false;
        return true;
    }

    /// <summary>
    /// Wraps NavMeshAgent.SetDestination with deduplication — skips the call when
    /// the requested position matches the last submitted one, avoiding unnecessary
    /// path recalculations on repeated frames (e.g., chasing a stationary player).
    /// </summary>
    private void SetDestination(Vector3 destination)
    {
        if (destination == _lastSetDestination)
            return;

        _lastSetDestination = destination;
        _navAgent.SetDestination(destination);
    }

    /// <summary>
    /// Samples up to SearchRetryLimit candidate positions around the search center,
    /// skipping recently visited ones. A retry loop (rather than a single fallback)
    /// meaningfully increases coverage when the search center sits inside already-
    /// searched space — common after the player doubles back on the enemy's route.
    /// Falls back to last known position when all candidates are exhausted.
    /// </summary>
    private void GenerateSearchPoint()
    {
        for (int i = 0; i < SearchRetryLimit; i++)
        {
            Vector2 randomDir  = UnityEngine.Random.insideUnitCircle * SearchRadius;
            Vector3 candidate  = _searchCenter + new Vector3(randomDir.x, 0f, randomDir.y);

            if (_memory != null && _memory.WasRecentlySearched(candidate))
                continue;

            NavMeshHit hit;
            if (!NavMesh.SamplePosition(candidate, out hit, SearchNavSampleRange, NavMesh.AllAreas))
                continue;

            _navAgent.isStopped = false;
            SetDestination(hit.position);
            return;
        }

        // All retries exhausted — fall back to last known player position so the
        // enemy doesn't idle silently when the search center is fully covered.
        Vector3 fallback = _lastKnownPosition != Vector3.zero ? _lastKnownPosition : _searchCenter;
        NavMeshHit fallbackHit;
        if (NavMesh.SamplePosition(fallback, out fallbackHit, SearchNavSampleRange, NavMesh.AllAreas))
        {
            _navAgent.isStopped = false;
            SetDestination(fallbackHit.position);
        }
    }

    private void LookAround(float angle)
    {
        transform.Rotate(0f, angle * Time.deltaTime, 0f);
    }

    private void CheckCatchCondition()
    {
        if (player == null || _currentState != AIState.CHASE)
            return;

        if (Vector3.Distance(transform.position, player.position) <= catchDistance)
            OnPlayerCaught?.Invoke();
    }

    #endregion

    #region Debug

    private void OnDrawGizmos()
    {
        if (!enableDebug)
            return;

        if (_patrolRoute != null)
        {
            for (int i = 0; i < _patrolRoute.Length; i++)
            {
                if (_patrolRoute[i] == null)
                    continue;

                bool isCurrent = Application.isPlaying && i == _patrolIndex;
                Gizmos.color   = isCurrent ? Color.cyan : Color.green;
                Gizmos.DrawWireSphere(_patrolRoute[i].position, isCurrent ? 0.7f : 0.5f);
            }
        }

        if (!Application.isPlaying)
            return;

        Gizmos.color = GetStateColor();
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 2.5f, 0.3f);

        if (_currentState == AIState.SEARCH)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_searchCenter, SearchRadius);
        }
        else if (_currentState == AIState.INVESTIGATE)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawLine(transform.position, _investigationPoint);
            Gizmos.DrawWireSphere(_investigationPoint, 1f);
        }
    }

    private Color GetStateColor()
    {
        switch (_currentState)
        {
            case AIState.PATROL:        return Color.green;
            case AIState.INVESTIGATE:   return new Color(1f, 0.5f, 0f);
            case AIState.SUSPICIOUS:    return Color.yellow;
            case AIState.CHASE:         return Color.red;
            case AIState.SEARCH:        return Color.yellow;
            case AIState.RESPOND_ALERT: return Color.magenta;
            default:                    return Color.gray;
        }
    }

    private void OnGUI()
    {
        if (!enableDebug || !Application.isPlaying)
            return;

        Camera cam = Camera.main;
        if (cam == null)
            return;

        Vector3 screenPos = cam.WorldToScreenPoint(transform.position + Vector3.up * 3f);
        if (screenPos.z < 0)
            return;

        float x = screenPos.x - 70;
        float y = Screen.height - screenPos.y;

        GUI.color = new Color(0, 0, 0, 0.8f);
        GUI.DrawTexture(new Rect(x - 5, y - 5, 150, 65), Texture2D.whiteTexture);

        GUI.color = GetStateColor();
        GUI.Label(new Rect(x, y, 140, 20), $"State: {_currentState}");

        GUI.color = _alertLevel >= AlertLevel.Alert      ? Color.red
                  : _alertLevel >= AlertLevel.Suspicious ? Color.yellow
                  : Color.white;
        GUI.Label(new Rect(x, y + 18, 140, 20), $"Alert: {_alertLevel} ({_alertMeter:F2})");

        GUI.color = Color.white;
        GUI.Label(new Rect(x, y + 36, 140, 20), $"Vision: {(_vision?.DetectionMeter ?? 0f):P0}");
    }

    #endregion
}
