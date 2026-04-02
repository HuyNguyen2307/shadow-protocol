using UnityEngine;
using UnityEngine.AI;
using System;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// ENEMY AI ADVANCED - Multi-Sensory AI with Memory + Communication
/// ═══════════════════════════════════════════════════════════════════════════════
/// 
/// Extends basic FSM with:
/// - Vision Detection (VisionSensor)
/// - Sound Detection (HearingSensor)  
/// - Memory System (AIMemory)
/// - Alert Levels (Relaxed → Suspicious → Alert → Combat)
/// - Multi-AI Communication (AlertSystem)
/// 
/// STATE MACHINE:
/// ┌─────────────────────────────────────────────────────────────────────────────┐
/// │ PATROL ──────────────────────────────────────────────────────────────────── │
/// │   ↓ (hear sound)           ↓ (see glimpse)           ↓ (full detection)    │
/// │ INVESTIGATE ──────────── SUSPICIOUS ──────────────── CHASE                 │
/// │   ↓ (nothing found)        ↓ (nothing found)          ↓ (lose sight)       │
/// │ PATROL ◄───────────────── PATROL ◄─────────────────── SEARCH              │
/// │                                                        ↓ (timeout)         │
/// │                           ↑ (receive alert)           PATROL ◄──────────── │
/// │                         RESPOND TO ALERT                                    │
/// └─────────────────────────────────────────────────────────────────────────────┘
/// 
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(VisionSensor))]
public class EnemyAI_Advanced : MonoBehaviour, IAlertListener
{
    #region ═══════════════════ ENUMS ═══════════════════

    public enum AIState
    {
        PATROL,         // Normal patrol
        INVESTIGATE,    // Heard something, checking it out
        SUSPICIOUS,     // Saw something brief, on guard
        CHASE,          // Full pursuit
        SEARCH,         // Lost target, searching area
        RESPOND_ALERT   // Responding to another enemy's alert
    }

    public enum AlertLevel
    {
        Relaxed,        // Normal patrol, not alert
        Cautious,       // Heard something recently
        Suspicious,     // Saw glimpse or multiple sounds
        Alert,          // Actively searching/chasing
        Combat          // In direct pursuit
    }

    #endregion

    #region ═══════════════════ SERIALIZED FIELDS ═══════════════════

    [Header("═══ SENSORS ═══")]
    [SerializeField] private VisionSensor visionSensor;
    [SerializeField] private HearingSensor hearingSensor;
    [SerializeField] private AIMemory aiMemory;

    [Header("═══ REFERENCES ═══")]
    [SerializeField] private Transform player;
    [SerializeField] private Transform[] waypoints;

    [Header("═══ MOVEMENT SPEEDS ═══")]
    [SerializeField] private float patrolSpeed = 2.5f;
    [SerializeField] private float investigateSpeed = 3.0f;
    [SerializeField] private float suspiciousSpeed = 3.5f;
    [SerializeField] private float chaseSpeed = 5.0f;
    [SerializeField] private float searchSpeed = 3.5f;

    [Header("═══ PATROL SETTINGS ═══")]
    [SerializeField] private float waypointWaitTime = 2.0f;
    [SerializeField] private float waypointReachThreshold = 0.5f;

    [Header("═══ DETECTION THRESHOLDS ═══")]
    [Tooltip("Vision detection to trigger suspicious state")]
    [Range(0f, 1f)]
    [SerializeField] private float suspiciousThreshold = 0.3f;
    
    [Tooltip("Vision detection to trigger chase")]
    [Range(0f, 1f)]
    [SerializeField] private float chaseThreshold = 1.0f;

    [Header("═══ CHASE SETTINGS ═══")]
    [SerializeField] private float loseSightDuration = 2.0f;
    [SerializeField] private float catchDistance = 1.5f;

    [Header("═══ SEARCH SETTINGS ═══")]
    [SerializeField] private float searchDuration = 8.0f;
    [SerializeField] private float searchScanSpeed = 90f;
    [SerializeField] private int searchPointsToCheck = 3;

    [Header("═══ INVESTIGATE SETTINGS ═══")]
    [SerializeField] private float investigateDuration = 4.0f;
    [SerializeField] private float investigateLookAroundTime = 2.0f;

    [Header("═══ ALERT COMMUNICATION ═══")]
    [Tooltip("Should this enemy alert others when spotting player?")]
    [SerializeField] private bool canRaiseAlerts = true;
    [Tooltip("Should this enemy respond to alerts from others?")]
    [SerializeField] private bool respondsToAlerts = true;
    [Tooltip("Speed when responding to alert")]
    [SerializeField] private float respondAlertSpeed = 4.0f;
    [Tooltip("How long to investigate an alert position")]
    [SerializeField] private float alertResponseDuration = 6.0f;

    [Header("═══ ALERT DECAY ═══")]
    [SerializeField] private float alertDecayRate = 0.1f;
    [SerializeField] private float relaxedPatrolSpeedMultiplier = 0.8f;

    [Header("═══ DEBUG ═══")]
    [SerializeField] private bool enableDebug = true;

    #endregion

    #region ═══════════════════ PRIVATE FIELDS ═══════════════════

    // Components
    private NavMeshAgent navAgent;
    
    // State
    private AIState currentState = AIState.PATROL;
    private AlertLevel alertLevel = AlertLevel.Relaxed;
    private float alertMeter = 0f; // 0-1, affects behavior
    
    // Patrol
    private int currentWaypointIndex = 0;
    private float waypointWaitTimer = 0f;
    private bool isWaitingAtWaypoint = false;
    
    // Chase
    private float lastPlayerSeenTime;
    
    // Alert Response
    private Vector3 alertTargetPosition;
    private float alertResponseStartTime;
    private bool hasReachedAlertPosition = false;
    
    // Search
    private Vector3 searchCenter;
    private float searchStartTime;
    private int searchPointsChecked = 0;
    private float searchScanTimer = 0f;
    private bool isScanning = false;
    
    // Investigate
    private Vector3 investigateTarget;
    private float investigateStartTime;
    private bool isLookingAround = false;
    
    // Metrics
    private float stateEnterTime;

    #endregion

    #region ═══════════════════ PUBLIC PROPERTIES ═══════════════════

    public AIState State => currentState;
    public string CurrentState => currentState.ToString();
    public AlertLevel CurrentAlertLevel => alertLevel;
    public float AlertMeter => alertMeter;
    public float DetectionLevel => visionSensor?.DetectionMeter ?? 0f;
    public bool CanSeePlayer => visionSensor?.CanSeePlayer ?? false;

    #endregion

    #region ═══════════════════ EVENTS ═══════════════════

    public event Action<AIState> OnStateChanged;
    public event Action<AlertLevel> OnAlertLevelChanged;
    public event Action OnPlayerCaught;

    #endregion

    #region ═══════════════════ UNITY LIFECYCLE ═══════════════════

    private void Awake()
    {
        navAgent = GetComponent<NavMeshAgent>();
        
        // Auto-find sensors
        if (visionSensor == null)
            visionSensor = GetComponent<VisionSensor>();
        if (hearingSensor == null)
            hearingSensor = GetComponent<HearingSensor>();
        if (aiMemory == null)
            aiMemory = GetComponent<AIMemory>();
        
        // Add missing components
        if (hearingSensor == null)
            hearingSensor = gameObject.AddComponent<HearingSensor>();
        if (aiMemory == null)
            aiMemory = gameObject.AddComponent<AIMemory>();
    }

    private void Start()
    {
        // Find player
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                player = playerObj.transform;
        }
        
        // Subscribe to sensor events
        SubscribeToEvents();
        
        // Register with AlertSystem for multi-AI communication
        RegisterWithAlertSystem();
        
        // Start patrol
        ChangeState(AIState.PATROL);
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
        UnregisterFromAlertSystem();
    }

    private void Update()
    {
        // Retry subscription if not yet subscribed
        if (!isSubscribed && hearingSensor != null)
        {
            SubscribeToEvents();
        }
        
        UpdateAlertLevel();
        UpdateCurrentState();
        CheckCatchCondition();
    }

    #endregion

    #region ═══════════════════ EVENT SUBSCRIPTIONS ═══════════════════

    private bool isSubscribed = false;
    
    private void SubscribeToEvents()
    {
        if (hearingSensor != null && !isSubscribed)
        {
            hearingSensor.OnSoundInvestigate += HandleSoundInvestigate;
            hearingSensor.OnSoundAlert += HandleSoundAlert;
            isSubscribed = true;
            Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: Subscribed to HearingSensor events!");
        }
        else if (hearingSensor == null)
        {
            Debug.LogWarning($"[EnemyAI_Advanced] {gameObject.name}: HearingSensor is NULL - cannot subscribe!");
        }
    }

    private void UnsubscribeFromEvents()
    {
        if (hearingSensor != null)
        {
            hearingSensor.OnSoundInvestigate -= HandleSoundInvestigate;
            hearingSensor.OnSoundAlert -= HandleSoundAlert;
        }
    }

    private void HandleSoundInvestigate(Vector3 position)
    {
        Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: HandleSoundInvestigate CALLED! State: {currentState}, Position: {position}");
        
        if (currentState == AIState.PATROL || currentState == AIState.INVESTIGATE)
        {
            investigateTarget = position;
            ChangeState(AIState.INVESTIGATE);
            
            // Remember heard position
            aiMemory?.RememberPlayerHeard(position);
            
            Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: >>> NOW INVESTIGATING at {position}!");
        }
        else
        {
            Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: Ignored sound - currently in {currentState}");
        }
    }

    private void HandleSoundAlert()
    {
        alertMeter = Mathf.Max(alertMeter, 0.7f);
        UpdateAlertLevelFromMeter();
    }

    #endregion

    #region ═══════════════════ ALERT SYSTEM ═══════════════════

    private bool isRegisteredWithAlertSystem = false;

    private void RegisterWithAlertSystem()
    {
        if (AlertSystem.Instance != null && !isRegisteredWithAlertSystem)
        {
            AlertSystem.Instance.RegisterListener(this);
            isRegisteredWithAlertSystem = true;
            
            if (enableDebug)
            {
                Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: Registered with AlertSystem");
            }
        }
    }

    private void UnregisterFromAlertSystem()
    {
        if (AlertSystem.Instance != null && isRegisteredWithAlertSystem)
        {
            AlertSystem.Instance.UnregisterListener(this);
            isRegisteredWithAlertSystem = false;
        }
    }

    /// <summary>
    /// Raise alert to notify other enemies
    /// </summary>
    private void RaiseAlertToOthers(AlertSystem.AlertType type)
    {
        if (!canRaiseAlerts || AlertSystem.Instance == null) return;
        
        Vector3 targetPos = player != null ? player.position : transform.position;
        AlertSystem.Instance.RaiseAlert(type, transform.position, targetPos, this);
        
        if (enableDebug)
        {
            Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: Raised {type} alert!");
        }
    }

    // ═══════════════════ IAlertListener Implementation ═══════════════════

    /// <summary>
    /// Called when another enemy raises an alert
    /// </summary>
    public void OnAlertReceived(AlertSystem.AlertData alert)
    {
        if (!respondsToAlerts) return;
        
        // Don't respond if already in high-priority state
        if (currentState == AIState.CHASE || currentState == AIState.SEARCH)
        {
            if (enableDebug)
            {
                Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: Ignored alert - already in {currentState}");
            }
            return;
        }
        
        // Respond based on alert type
        switch (alert.Type)
        {
            case AlertSystem.AlertType.SUSPICIOUS:
                // Become more alert but continue patrol
                alertMeter = Mathf.Max(alertMeter, 0.3f);
                UpdateAlertLevelFromMeter();
                break;
                
            case AlertSystem.AlertType.SPOTTED:
                // Investigate the location
                if (currentState == AIState.PATROL || currentState == AIState.INVESTIGATE)
                {
                    investigateTarget = alert.TargetPosition;
                    ChangeState(AIState.INVESTIGATE);
                }
                break;
                
            case AlertSystem.AlertType.COMBAT:
            case AlertSystem.AlertType.BACKUP_REQUEST:
                // Rush to help!
                alertTargetPosition = alert.TargetPosition;
                alertResponseStartTime = Time.time;
                hasReachedAlertPosition = false;
                ChangeState(AIState.RESPOND_ALERT);
                
                if (enableDebug)
                {
                    Debug.Log($"[EnemyAI_Advanced] {gameObject.name}: Responding to COMBAT alert at {alert.TargetPosition}!");
                }
                break;
                
            case AlertSystem.AlertType.LOST:
                // Help search the area
                if (currentState == AIState.PATROL)
                {
                    investigateTarget = alert.TargetPosition;
                    ChangeState(AIState.INVESTIGATE);
                }
                break;
        }
        
        // Remember alert position
        aiMemory?.RememberPlayerHeard(alert.TargetPosition);
    }

    /// <summary>
    /// Get this enemy's position for distance calculations
    /// </summary>
    public Vector3 GetPosition()
    {
        return transform.position;
    }

    /// <summary>
    /// Check if this enemy is currently in combat
    /// </summary>
    public bool IsInCombat()
    {
        return currentState == AIState.CHASE;
    }

    #endregion

    #region ═══════════════════ STATE MACHINE ═══════════════════

    private void UpdateCurrentState()
    {
        // Check for vision-based state transitions first
        CheckVisionTransitions();
        
        // Then update current state
        switch (currentState)
        {
            case AIState.PATROL:
                UpdatePatrol();
                break;
            case AIState.INVESTIGATE:
                UpdateInvestigate();
                break;
            case AIState.SUSPICIOUS:
                UpdateSuspicious();
                break;
            case AIState.CHASE:
                UpdateChase();
                break;
            case AIState.SEARCH:
                UpdateSearch();
                break;
            case AIState.RESPOND_ALERT:
                UpdateRespondAlert();
                break;
        }
    }

    private void CheckVisionTransitions()
    {
        if (visionSensor == null) return;
        
        float detection = visionSensor.DetectionMeter;
        
        // Full detection → Chase
        if (detection >= chaseThreshold && currentState != AIState.CHASE)
        {
            ChangeState(AIState.CHASE);
            return;
        }
        
        // Partial detection → Suspicious (unless already chasing)
        if (detection >= suspiciousThreshold && detection < chaseThreshold)
        {
            if (currentState == AIState.PATROL || currentState == AIState.INVESTIGATE)
            {
                investigateTarget = player.position;
                ChangeState(AIState.SUSPICIOUS);
            }
        }
    }

    private void ChangeState(AIState newState)
    {
        if (currentState == newState) return;
        
        AIState previousState = currentState;
        currentState = newState;
        stateEnterTime = Time.time;
        
        // Exit current state
        OnExitState(previousState);
        
        // Enter new state
        OnEnterState(newState);
        
        OnStateChanged?.Invoke(newState);
        
        if (enableDebug)
        {
            Debug.Log($"[EnemyAI_Adv] {gameObject.name}: {previousState} → {newState}", this);
        }
    }

    private void OnEnterState(AIState state)
    {
        switch (state)
        {
            case AIState.PATROL:
                navAgent.speed = patrolSpeed * GetAlertSpeedMultiplier();
                navAgent.isStopped = false;  // IMPORTANT: Restart movement!
                isWaitingAtWaypoint = false;
                isLookingAround = false;
                break;
                
            case AIState.INVESTIGATE:
                navAgent.speed = investigateSpeed;
                investigateStartTime = Time.time;
                isLookingAround = false;
                navAgent.SetDestination(investigateTarget);
                break;
                
            case AIState.SUSPICIOUS:
                navAgent.speed = suspiciousSpeed;
                alertMeter = Mathf.Max(alertMeter, 0.5f);
                // Alert nearby enemies
                RaiseAlertToOthers(AlertSystem.AlertType.SPOTTED);
                break;
                
            case AIState.CHASE:
                navAgent.speed = chaseSpeed;
                navAgent.isStopped = false;
                alertMeter = 1f;
                UpdateAlertLevelFromMeter();
                // ALERT OTHER ENEMIES!
                RaiseAlertToOthers(AlertSystem.AlertType.COMBAT);
                break;
                
            case AIState.SEARCH:
                navAgent.speed = searchSpeed;
                searchCenter = aiMemory?.LastSeenPosition ?? transform.position;
                searchStartTime = Time.time;
                searchPointsChecked = 0;
                isScanning = false;
                GenerateSearchPoint();
                // Tell others we lost the target
                RaiseAlertToOthers(AlertSystem.AlertType.LOST);
                break;
                
            case AIState.RESPOND_ALERT:
                navAgent.speed = respondAlertSpeed;
                navAgent.isStopped = false;
                alertMeter = Mathf.Max(alertMeter, 0.6f);
                UpdateAlertLevelFromMeter();
                navAgent.SetDestination(alertTargetPosition);
                
                if (enableDebug)
                {
                    Debug.Log($"[EnemyAI_Adv] {gameObject.name}: Responding to alert at {alertTargetPosition}!");
                }
                break;
        }
    }

    private void OnExitState(AIState state)
    {
        switch (state)
        {
            case AIState.CHASE:
                // Remember where player was last seen
                if (visionSensor != null && visionSensor.LastKnownPlayerPos != Vector3.zero)
                {
                    aiMemory?.RememberPlayerSeen(visionSensor.LastKnownPlayerPos);
                }
                break;
                
            case AIState.SEARCH:
                // Mark searched areas
                aiMemory?.MarkAsSearched(searchCenter);
                break;
        }
    }

    #endregion

    #region ═══════════════════ STATE UPDATES ═══════════════════

    private void UpdatePatrol()
    {
        if (waypoints == null || waypoints.Length == 0) return;
        
        if (isWaitingAtWaypoint)
        {
            waypointWaitTimer += Time.deltaTime;
            
            // Look around while waiting
            LookAround(45f);
            
            if (waypointWaitTimer >= waypointWaitTime)
            {
                isWaitingAtWaypoint = false;
                navAgent.isStopped = false;  // Resume movement
                AdvanceToNextWaypoint();
            }
        }
        else
        {
            // Ensure we're moving
            navAgent.isStopped = false;
            
            // Move to current waypoint
            if (waypoints[currentWaypointIndex] != null)
            {
                navAgent.SetDestination(waypoints[currentWaypointIndex].position);
            }
            
            // Check if arrived
            if (HasReachedDestination())
            {
                isWaitingAtWaypoint = true;
                waypointWaitTimer = 0f;
                navAgent.isStopped = true;
            }
        }
    }

    private void UpdateInvestigate()
    {
        float elapsed = Time.time - investigateStartTime;
        
        if (!isLookingAround)
        {
            // Move to investigate position
            navAgent.isStopped = false;  // Ensure we're moving
            navAgent.SetDestination(investigateTarget);
            
            if (HasReachedDestination())
            {
                isLookingAround = true;
                navAgent.isStopped = true;
                searchScanTimer = 0f;
            }
        }
        else
        {
            // Look around at investigate position
            LookAround(90f);
            searchScanTimer += Time.deltaTime;
            
            if (searchScanTimer >= investigateLookAroundTime)
            {
                // Nothing found, return to patrol
                aiMemory?.MarkAsSearched(investigateTarget);
                ChangeState(AIState.PATROL);
            }
        }
        
        // Timeout
        if (elapsed >= investigateDuration)
        {
            ChangeState(AIState.PATROL);
        }
    }

    private void UpdateSuspicious()
    {
        // Move toward suspicious position
        navAgent.SetDestination(investigateTarget);
        
        // If detection drops, go back to patrol
        if (visionSensor != null && visionSensor.DetectionMeter < suspiciousThreshold * 0.5f)
        {
            float elapsed = Time.time - stateEnterTime;
            if (elapsed > 3f) // Stay suspicious for at least 3 seconds
            {
                ChangeState(AIState.PATROL);
            }
        }
    }

    private void UpdateChase()
    {
        if (player == null) return;
        
        // Update destination to player
        navAgent.isStopped = false;
        navAgent.SetDestination(player.position);
        
        // Track when we last saw player
        if (visionSensor != null && visionSensor.CanSeePlayer)
        {
            lastPlayerSeenTime = Time.time;
            aiMemory?.RememberPlayerSeen(player.position);
        }
        
        // Check if lost sight
        float timeSinceSeen = Time.time - lastPlayerSeenTime;
        if (timeSinceSeen > loseSightDuration && !visionSensor.CanSeePlayer)
        {
            ChangeState(AIState.SEARCH);
        }
    }

    private void UpdateSearch()
    {
        float elapsed = Time.time - searchStartTime;
        float totalDuration = searchDuration + (aiMemory?.GetSearchDurationBonus(searchCenter) ?? 0f);
        
        if (elapsed >= totalDuration)
        {
            // Search timeout
            visionSensor?.ResetDetection();
            hearingSensor?.ResetHearing();
            ChangeState(AIState.PATROL);
            return;
        }
        
        if (isScanning)
        {
            // Rotate to scan area
            LookAround(searchScanSpeed * Time.deltaTime);
            searchScanTimer += Time.deltaTime;
            
            if (searchScanTimer >= 2f) // Scan for 2 seconds at each point
            {
                isScanning = false;
                searchPointsChecked++;
                
                if (searchPointsChecked >= searchPointsToCheck)
                {
                    // Checked all points
                    ChangeState(AIState.PATROL);
                }
                else
                {
                    GenerateSearchPoint();
                }
            }
        }
        else
        {
            // Move to search point
            if (HasReachedDestination())
            {
                isScanning = true;
                searchScanTimer = 0f;
                navAgent.isStopped = true;
            }
        }
    }

    private void UpdateRespondAlert()
    {
        float elapsed = Time.time - alertResponseStartTime;
        
        // Timeout - go back to patrol
        if (elapsed >= alertResponseDuration)
        {
            ChangeState(AIState.PATROL);
            return;
        }
        
        if (!hasReachedAlertPosition)
        {
            // Move to alert position
            navAgent.isStopped = false;
            navAgent.SetDestination(alertTargetPosition);
            
            if (HasReachedDestination())
            {
                hasReachedAlertPosition = true;
                navAgent.isStopped = true;
                searchScanTimer = 0f;
                
                if (enableDebug)
                {
                    Debug.Log($"[EnemyAI_Adv] {gameObject.name}: Arrived at alert position, searching...");
                }
            }
        }
        else
        {
            // Look around at alert position
            LookAround(searchScanSpeed * Time.deltaTime);
            searchScanTimer += Time.deltaTime;
            
            if (searchScanTimer >= 3f) // Look around for 3 seconds
            {
                // Nothing found, go to search or patrol
                if (alertMeter > 0.5f)
                {
                    searchCenter = alertTargetPosition;
                    ChangeState(AIState.SEARCH);
                }
                else
                {
                    ChangeState(AIState.PATROL);
                }
            }
        }
    }

    #endregion

    #region ═══════════════════ ALERT LEVEL ═══════════════════

    private void UpdateAlertLevel()
    {
        // Increase alert from sensors
        float visionContrib = (visionSensor?.DetectionMeter ?? 0f) * 0.5f;
        float hearingContrib = (hearingSensor?.SuspicionMeter ?? 0f) * 0.3f;
        
        float targetAlert = visionContrib + hearingContrib;
        
        if (currentState == AIState.CHASE)
        {
            targetAlert = 1f;
        }
        else if (currentState == AIState.SEARCH)
        {
            targetAlert = Mathf.Max(targetAlert, 0.6f);
        }
        
        // Lerp toward target
        if (targetAlert > alertMeter)
        {
            alertMeter = Mathf.MoveTowards(alertMeter, targetAlert, Time.deltaTime * 2f);
        }
        else
        {
            alertMeter = Mathf.MoveTowards(alertMeter, targetAlert, alertDecayRate * Time.deltaTime);
        }
        
        UpdateAlertLevelFromMeter();
    }

    private void UpdateAlertLevelFromMeter()
    {
        AlertLevel newLevel;
        
        if (alertMeter >= 0.9f)
            newLevel = AlertLevel.Combat;
        else if (alertMeter >= 0.7f)
            newLevel = AlertLevel.Alert;
        else if (alertMeter >= 0.4f)
            newLevel = AlertLevel.Suspicious;
        else if (alertMeter >= 0.1f)
            newLevel = AlertLevel.Cautious;
        else
            newLevel = AlertLevel.Relaxed;
        
        if (newLevel != alertLevel)
        {
            alertLevel = newLevel;
            OnAlertLevelChanged?.Invoke(alertLevel);
        }
    }

    private float GetAlertSpeedMultiplier()
    {
        return alertLevel == AlertLevel.Relaxed ? relaxedPatrolSpeedMultiplier : 1f;
    }

    #endregion

    #region ═══════════════════ HELPER METHODS ═══════════════════

    private bool HasReachedDestination()
    {
        if (navAgent.pathPending) return false;
        if (navAgent.remainingDistance > waypointReachThreshold) return false;
        return true;
    }

    private void AdvanceToNextWaypoint()
    {
        currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
        
        // Skip null waypoints
        int safety = 0;
        while (waypoints[currentWaypointIndex] == null && safety < waypoints.Length)
        {
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
            safety++;
        }
        
        navAgent.isStopped = false;
    }

    private void LookAround(float angle)
    {
        transform.Rotate(0f, angle * Time.deltaTime, 0f);
    }

    private void GenerateSearchPoint()
    {
        // Generate random point around search center
        Vector2 randomDir = UnityEngine.Random.insideUnitCircle * 5f;
        Vector3 searchPoint = searchCenter + new Vector3(randomDir.x, 0f, randomDir.y);
        
        // Skip recently searched areas
        if (aiMemory != null && aiMemory.WasRecentlySearched(searchPoint))
        {
            randomDir = UnityEngine.Random.insideUnitCircle * 5f;
            searchPoint = searchCenter + new Vector3(randomDir.x, 0f, randomDir.y);
        }
        
        NavMeshHit hit;
        if (NavMesh.SamplePosition(searchPoint, out hit, 3f, NavMesh.AllAreas))
        {
            navAgent.isStopped = false;
            navAgent.SetDestination(hit.position);
        }
    }

    private void CheckCatchCondition()
    {
        if (player == null || currentState != AIState.CHASE) return;
        
        float distance = Vector3.Distance(transform.position, player.position);
        if (distance <= catchDistance)
        {
            OnPlayerCaught?.Invoke();
        }
    }

    #endregion

    #region ═══════════════════ DEBUG ═══════════════════

    private void OnDrawGizmos()
    {
        if (!enableDebug) return;
        
        // Draw waypoints
        if (waypoints != null)
        {
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                
                bool isCurrent = Application.isPlaying && i == currentWaypointIndex;
                Gizmos.color = isCurrent ? Color.cyan : Color.green;
                Gizmos.DrawWireSphere(waypoints[i].position, isCurrent ? 0.7f : 0.5f);
            }
        }
        
        // Draw state-specific
        if (Application.isPlaying)
        {
            Gizmos.color = GetStateColor();
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 2.5f, 0.3f);
            
            if (currentState == AIState.SEARCH)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(searchCenter, 5f);
            }
            else if (currentState == AIState.INVESTIGATE)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f);
                Gizmos.DrawLine(transform.position, investigateTarget);
                Gizmos.DrawWireSphere(investigateTarget, 1f);
            }
        }
    }

    private Color GetStateColor()
    {
        switch (currentState)
        {
            case AIState.PATROL: return Color.green;
            case AIState.INVESTIGATE: return new Color(1f, 0.5f, 0f); // Orange
            case AIState.SUSPICIOUS: return Color.yellow;
            case AIState.CHASE: return Color.red;
            case AIState.SEARCH: return Color.yellow;
            case AIState.RESPOND_ALERT: return Color.magenta; // Purple for responding to alert
            default: return Color.gray;
        }
    }

    private void OnGUI()
    {
        if (!enableDebug || !Application.isPlaying) return;
        
        Camera cam = Camera.main;
        if (cam == null) return;
        
        Vector3 screenPos = cam.WorldToScreenPoint(transform.position + Vector3.up * 3f);
        if (screenPos.z < 0) return;
        
        float x = screenPos.x - 70;
        float y = Screen.height - screenPos.y;
        
        // Background
        GUI.color = new Color(0, 0, 0, 0.8f);
        GUI.DrawTexture(new Rect(x - 5, y - 5, 150, 65), Texture2D.whiteTexture);
        
        // State
        GUI.color = GetStateColor();
        GUI.Label(new Rect(x, y, 140, 20), $"State: {currentState}");
        
        // Alert level
        GUI.color = alertLevel >= AlertLevel.Alert ? Color.red : 
                    alertLevel >= AlertLevel.Suspicious ? Color.yellow : Color.white;
        GUI.Label(new Rect(x, y + 18, 140, 20), $"Alert: {alertLevel} ({alertMeter:F2})");
        
        // Detection
        GUI.color = Color.white;
        GUI.Label(new Rect(x, y + 36, 140, 20), $"Vision: {(visionSensor?.DetectionMeter ?? 0f):P0}");
    }

    #endregion
}
