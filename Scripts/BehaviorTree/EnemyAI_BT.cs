using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using BehaviorTree;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// ENEMY AI - BEHAVIOR TREE IMPLEMENTATION
/// ═══════════════════════════════════════════════════════════════════════════════
/// 
/// Project: Shadow Protocol - Comparative Study of FSM vs Behavior Trees
/// 
/// PURPOSE:
/// Implements IDENTICAL enemy behaviors as EnemyAI_Advanced.cs (FSM) using Behavior Tree.
/// This allows fair comparison of the two architectures.
/// 
/// BEHAVIORS (same as FSM - 5 behaviors):
/// - PATROL: Follow waypoints in sequence
/// - INVESTIGATE: Check out suspicious sounds (from HearingSensor)
/// - CHASE: Pursue player when fully detected (DetectionMeter >= 1.0)
/// - SEARCH: Look for player at last known position after losing sight
/// - RESPOND_ALERT: Respond to alerts from other guards (AlertSystem)
/// 
/// TREE STRUCTURE:
/// ┌─────────────────────────────────────────────────────────────────────────────┐
/// │  Root (Selector) - Try behaviors in priority order                          │
/// │  ├── [Sequence] CHASE BRANCH (highest priority)                             │
/// │  │   ├── Condition: IsPlayerFullyDetected (meter >= 1.0)                   │
/// │  │   ├── Action: SetChaseSpeed                                              │
/// │  │   └── Action: ChasePlayer (returns RUNNING while chasing)               │
/// │  │                                                                          │
/// │  ├── [Sequence] RESPOND_ALERT BRANCH (high priority)                        │
/// │  │   ├── Condition: HasPendingAlert (received alert from ally)             │
/// │  │   ├── Action: SetAlertSpeed                                              │
/// │  │   ├── Action: MoveToAlertPosition (RUNNING until arrived)               │
/// │  │   └── Action: ScanAlertArea (RUNNING while scanning)                    │
/// │  │                                                                          │
/// │  ├── [Sequence] SEARCH BRANCH                                               │
/// │  │   ├── Condition: ShouldSearch (has last known pos, lost sight)          │
/// │  │   ├── Action: SetSearchSpeed                                             │
/// │  │   ├── Action: MoveToLastKnown (RUNNING until arrived)                   │
/// │  │   └── Action: ScanArea (RUNNING while scanning)                         │
/// │  │                                                                          │
/// │  ├── [Sequence] INVESTIGATE BRANCH (responds to sounds)                     │
/// │  │   ├── Condition: ShouldInvestigate (heard suspicious sound)             │
/// │  │   ├── Action: SetInvestigateSpeed                                        │
/// │  │   ├── Action: MoveToSoundSource (RUNNING until arrived)                 │
/// │  │   └── Action: LookAroundAtSound (RUNNING while looking)                 │
/// │  │                                                                          │
/// │  └── [Sequence] PATROL BRANCH (lowest priority / fallback)                  │
/// │      ├── Action: SetPatrolSpeed                                             │
/// │      ├── Action: MoveToWaypoint (RUNNING until arrived)                    │
/// │      └── Action: WaitAtWaypoint (RUNNING while waiting)                    │
/// └─────────────────────────────────────────────────────────────────────────────┘
/// 
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(VisionSensor))]
public class EnemyAI_BT : MonoBehaviour, IAlertListener
{
    #region ═══════════════════ ENUMS ═══════════════════

    public enum AIBehavior
    {
        IDLE,
        PATROL,
        INVESTIGATE,
        CHASE,
        SEARCH,
        RESPOND_ALERT
    }

    #endregion

    #region ═══════════════════ SERIALIZED FIELDS ═══════════════════

    [Header("═══ REFERENCES ═══")]
    [SerializeField] private VisionSensor visionSensor;
    [SerializeField] private HearingSensor hearingSensor;
    [SerializeField] private Transform player;
    [SerializeField] private Transform[] waypoints;

    [Header("═══ PATROL SETTINGS ═══")]
    [SerializeField] private float patrolSpeed = 2.5f;
    [SerializeField] private float waypointWaitTime = 1.0f;
    [SerializeField] private float waypointReachThreshold = 0.5f;

    [Header("═══ INVESTIGATE SETTINGS ═══")]
    [SerializeField] private float investigateSpeed = 3.0f;
    [SerializeField] private float investigateLookDuration = 3.0f;
    [SerializeField] private float investigateLookSpeed = 90f;

    [Header("═══ CHASE SETTINGS ═══")]
    [SerializeField] private float chaseSpeed = 5.0f;
    [SerializeField] private float loseSightThreshold = 2.0f;
    [SerializeField] private float catchDistance = 1.5f;

    [Header("═══ SEARCH SETTINGS ═══")]
    [SerializeField] private float searchSpeed = 3.5f;
    [SerializeField] private float searchDuration = 5.0f;
    [SerializeField] private float searchScanSpeed = 120f;

    [Header("═══ ALERT RESPONSE SETTINGS ═══")]
    [SerializeField] private float alertResponseSpeed = 4.5f;
    [SerializeField] private float alertScanDuration = 4.0f;
    [SerializeField] private float alertScanSpeed = 100f;

    [Header("═══ DEBUG ═══")]
    [SerializeField] private bool enableDebug = true;

    #endregion

    #region ═══════════════════ PRIVATE FIELDS ═══════════════════

    private NavMeshAgent navAgent;
    private BehaviorTreeRunner btRunner;
    private AIBehavior currentBehavior = AIBehavior.IDLE;
    private string currentNodeName = "";
    
    // Patrol
    private int currentWaypointIndex = 0;
    private float waypointWaitTimer = 0f;
    private bool isWaitingAtWaypoint = false;
    
    // Investigate
    private Vector3 investigatePosition;
    private bool hasInvestigateTarget = false;
    private bool hasReachedInvestigateTarget = false;
    private float investigateLookTimer = 0f;
    
    // Chase
    private float lastPlayerSeenTime;
    private bool wasChasing = false;
    
    // Search
    private Vector3 searchTargetPosition;
    private float searchStartTime;
    private bool hasReachedSearchTarget = false;
    private float totalSearchRotation = 0f;
    
    // Alert Response
    private bool hasPendingAlert = false;
    private Vector3 alertPosition;
    private Vector3 alertTargetPosition;
    private bool hasReachedAlertPosition = false;
    private float alertScanTimer = 0f;
    
    // Metrics
    private int nodeEvaluationsThisTick = 0;
    private int totalNodeEvaluations = 0;
    private int branchSwitchCount = 0;
    private Dictionary<AIBehavior, float> behaviorTimers = new Dictionary<AIBehavior, float>();

    #endregion

    #region ═══════════════════ PROPERTIES & EVENTS ═══════════════════

    public AIBehavior CurrentBehavior => currentBehavior;
    public string CurrentBehaviorName => currentBehavior.ToString();
    public float DetectionLevel => visionSensor?.DetectionMeter ?? 0f;
    public int BranchSwitchCount => branchSwitchCount;
    public int TotalNodeEvaluations => totalNodeEvaluations;
    
    public System.Action<AIBehavior> OnBehaviorChanged;
    public System.Action OnPlayerCaught;

    #endregion

    #region ═══════════════════ UNITY LIFECYCLE ═══════════════════

    private void Awake()
    {
        navAgent = GetComponent<NavMeshAgent>();
        if (visionSensor == null) visionSensor = GetComponent<VisionSensor>();
        if (hearingSensor == null) hearingSensor = GetComponent<HearingSensor>();
        
        foreach (AIBehavior b in System.Enum.GetValues(typeof(AIBehavior)))
            behaviorTimers[b] = 0f;
    }

    private void Start()
    {
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
        
        if (hearingSensor != null)
            hearingSensor.OnSoundHeard += HandleSoundHeard;
        
        // Register with AlertSystem for multi-AI communication
        if (AlertSystem.Instance != null)
        {
            AlertSystem.Instance.RegisterListener(this);
            if (enableDebug) Debug.Log($"[EnemyAI_BT] Registered with AlertSystem", this);
        }
        
        BuildBehaviorTree();
        currentBehavior = AIBehavior.PATROL;
    }

    private void OnDestroy()
    {
        if (hearingSensor != null)
            hearingSensor.OnSoundHeard -= HandleSoundHeard;
        
        if (AlertSystem.Instance != null)
            AlertSystem.Instance.UnregisterListener(this);
    }

    private void Update()
    {
        nodeEvaluationsThisTick = 0;
        btRunner.Tick();
        totalNodeEvaluations += nodeEvaluationsThisTick;
        behaviorTimers[currentBehavior] += Time.deltaTime;
        CheckCatchCondition();
    }

    #endregion

    #region ═══════════════════ IALERTLISTENER ═══════════════════

    public void OnAlertReceived(AlertSystem.AlertData alert)
    {
        if (alert.Source == this as IAlertListener) return;
        if (currentBehavior == AIBehavior.CHASE) return;
        
        hasPendingAlert = true;
        alertPosition = alert.Position;
        alertTargetPosition = alert.TargetPosition;
        hasReachedAlertPosition = false;
        alertScanTimer = 0f;
        
        if (enableDebug)
            Debug.Log($"[EnemyAI_BT] Alert received! Responding to {alertTargetPosition}", this);
    }

    public Vector3 GetPosition() => transform.position;
    public bool IsInCombat() => currentBehavior == AIBehavior.CHASE;

    #endregion

    #region ═══════════════════ BUILD TREE ═══════════════════

    private void BuildBehaviorTree()
    {
        btRunner = new BehaviorTreeRunner();
        var root = new Selector("Root");
        
        // CHASE (highest priority)
        var chase = new Sequence("Chase")
            .AddChild(new ConditionNode("Detected?", IsPlayerDetected))
            .AddChild(new ActionNode("ChaseSpeed", SetChaseSpeed))
            .AddChild(new ActionNode("Chase", ChasePlayer));
        
        // RESPOND_ALERT (high priority)
        var alert = new Sequence("Alert")
            .AddChild(new ConditionNode("HasAlert?", () => hasPendingAlert))
            .AddChild(new ActionNode("AlertSpeed", SetAlertSpeed))
            .AddChild(new ActionNode("MoveToAlert", MoveToAlertPosition))
            .AddChild(new ActionNode("ScanAlert", ScanAlertArea));
        
        // SEARCH
        var search = new Sequence("Search")
            .AddChild(new ConditionNode("ShouldSearch?", ShouldSearch))
            .AddChild(new ActionNode("SearchSpeed", SetSearchSpeed))
            .AddChild(new ActionNode("MoveToLast", MoveToLastKnown))
            .AddChild(new ActionNode("Scan", ScanArea));
        
        // INVESTIGATE
        var investigate = new Sequence("Investigate")
            .AddChild(new ConditionNode("HeardSound?", () => hasInvestigateTarget))
            .AddChild(new ActionNode("InvestSpeed", SetInvestigateSpeed))
            .AddChild(new ActionNode("MoveToSound", MoveToSoundSource))
            .AddChild(new ActionNode("LookAround", LookAroundAtSound));
        
        // PATROL (fallback)
        var patrol = new Sequence("Patrol")
            .AddChild(new ActionNode("PatrolSpeed", SetPatrolSpeed))
            .AddChild(new ActionNode("MoveWaypoint", MoveToWaypoint))
            .AddChild(new ActionNode("Wait", WaitAtWaypoint));
        
        root.AddChild(chase);
        root.AddChild(alert);
        root.AddChild(search);
        root.AddChild(investigate);
        root.AddChild(patrol);
        
        btRunner.SetRoot(root);
        
        if (enableDebug)
            Debug.Log("[EnemyAI_BT] Tree built: Chase > Alert > Search > Investigate > Patrol", this);
    }

    #endregion

    #region ═══════════════════ EVENT HANDLERS ═══════════════════

    private void HandleSoundHeard(Vector3 pos, float vol)
    {
        if (currentBehavior == AIBehavior.CHASE || currentBehavior == AIBehavior.RESPOND_ALERT) return;
        investigatePosition = pos;
        hasInvestigateTarget = true;
        hasReachedInvestigateTarget = false;
        investigateLookTimer = 0f;
    }

    #endregion

    #region ═══════════════════ CONDITIONS ═══════════════════

    private bool IsPlayerDetected()
    {
        nodeEvaluationsThisTick++;
        if (visionSensor == null) return false;
        bool detected = visionSensor.DetectionMeter >= 1.0f;
        if (detected) RaiseAlert();
        return detected;
    }

    private bool ShouldSearch()
    {
        nodeEvaluationsThisTick++;
        if (!wasChasing) return false;
        if (visionSensor != null && visionSensor.CanSeePlayer)
        {
            lastPlayerSeenTime = Time.time;
            return false;
        }
        float t = Time.time - lastPlayerSeenTime;
        return t >= loseSightThreshold && t < searchDuration + loseSightThreshold;
    }

    #endregion

    #region ═══════════════════ CHASE ACTIONS ═══════════════════

    private NodeState SetChaseSpeed()
    {
        nodeEvaluationsThisTick++;
        SetBehavior(AIBehavior.CHASE);
        navAgent.speed = chaseSpeed;
        wasChasing = true;
        lastPlayerSeenTime = Time.time;
        hasInvestigateTarget = false;
        hasPendingAlert = false;
        return NodeState.SUCCESS;
    }

    private NodeState ChasePlayer()
    {
        nodeEvaluationsThisTick++;
        currentNodeName = "ChasePlayer";
        if (player == null) return NodeState.FAILURE;
        
        if (visionSensor != null && visionSensor.CanSeePlayer)
        {
            lastPlayerSeenTime = Time.time;
            searchTargetPosition = player.position;
        }
        
        navAgent.isStopped = false;
        navAgent.SetDestination(player.position);
        
        if (Time.time - lastPlayerSeenTime >= loseSightThreshold)
            return NodeState.FAILURE;
        
        return NodeState.RUNNING;
    }

    private void RaiseAlert()
    {
        if (AlertSystem.Instance == null || player == null) return;
        AlertSystem.Instance.RaiseAlert(AlertSystem.AlertType.SPOTTED, transform.position, player.position, this);
        if (enableDebug) Debug.Log($"[EnemyAI_BT] ALERT RAISED!", this);
    }

    #endregion

    #region ═══════════════════ ALERT ACTIONS ═══════════════════

    private NodeState SetAlertSpeed()
    {
        nodeEvaluationsThisTick++;
        SetBehavior(AIBehavior.RESPOND_ALERT);
        navAgent.speed = alertResponseSpeed;
        hasReachedAlertPosition = false;
        alertScanTimer = 0f;
        return NodeState.SUCCESS;
    }

    private NodeState MoveToAlertPosition()
    {
        nodeEvaluationsThisTick++;
        currentNodeName = "MoveToAlert";
        if (hasReachedAlertPosition) return NodeState.SUCCESS;
        
        navAgent.isStopped = false;
        navAgent.SetDestination(alertTargetPosition);
        
        if (HasReached())
        {
            hasReachedAlertPosition = true;
            navAgent.isStopped = true;
            return NodeState.SUCCESS;
        }
        
        if (visionSensor != null && visionSensor.DetectionMeter >= 1.0f)
        {
            hasPendingAlert = false;
            return NodeState.FAILURE;
        }
        
        return NodeState.RUNNING;
    }

    private NodeState ScanAlertArea()
    {
        nodeEvaluationsThisTick++;
        currentNodeName = "ScanAlert";
        alertScanTimer += Time.deltaTime;
        transform.Rotate(0, alertScanSpeed * Time.deltaTime, 0);
        
        if (visionSensor != null && visionSensor.DetectionMeter >= 1.0f)
        {
            hasPendingAlert = false;
            return NodeState.FAILURE;
        }
        
        if (alertScanTimer >= alertScanDuration)
        {
            hasPendingAlert = false;
            if (enableDebug) Debug.Log("[EnemyAI_BT] Alert response complete", this);
            return NodeState.FAILURE;
        }
        
        return NodeState.RUNNING;
    }

    #endregion

    #region ═══════════════════ SEARCH ACTIONS ═══════════════════

    private NodeState SetSearchSpeed()
    {
        nodeEvaluationsThisTick++;
        SetBehavior(AIBehavior.SEARCH);
        navAgent.speed = searchSpeed;
        searchStartTime = Time.time;
        hasReachedSearchTarget = false;
        totalSearchRotation = 0f;
        return NodeState.SUCCESS;
    }

    private NodeState MoveToLastKnown()
    {
        nodeEvaluationsThisTick++;
        currentNodeName = "MoveToLast";
        if (hasReachedSearchTarget) return NodeState.SUCCESS;
        
        navAgent.isStopped = false;
        navAgent.SetDestination(searchTargetPosition);
        
        if (HasReached())
        {
            hasReachedSearchTarget = true;
            navAgent.isStopped = true;
            return NodeState.SUCCESS;
        }
        
        if (Time.time - searchStartTime >= searchDuration)
        {
            wasChasing = false;
            return NodeState.FAILURE;
        }
        
        return NodeState.RUNNING;
    }

    private NodeState ScanArea()
    {
        nodeEvaluationsThisTick++;
        currentNodeName = "ScanArea";
        transform.Rotate(0, searchScanSpeed * Time.deltaTime, 0);
        totalSearchRotation += searchScanSpeed * Time.deltaTime;
        
        if (visionSensor != null && visionSensor.DetectionMeter >= 1.0f)
            return NodeState.FAILURE;
        
        if (Time.time - searchStartTime >= searchDuration)
        {
            wasChasing = false;
            return NodeState.FAILURE;
        }
        
        return NodeState.RUNNING;
    }

    #endregion

    #region ═══════════════════ INVESTIGATE ACTIONS ═══════════════════

    private NodeState SetInvestigateSpeed()
    {
        nodeEvaluationsThisTick++;
        SetBehavior(AIBehavior.INVESTIGATE);
        navAgent.speed = investigateSpeed;
        hasReachedInvestigateTarget = false;
        investigateLookTimer = 0f;
        return NodeState.SUCCESS;
    }

    private NodeState MoveToSoundSource()
    {
        nodeEvaluationsThisTick++;
        currentNodeName = "MoveToSound";
        if (hasReachedInvestigateTarget) return NodeState.SUCCESS;
        
        navAgent.isStopped = false;
        navAgent.SetDestination(investigatePosition);
        
        if (HasReached())
        {
            hasReachedInvestigateTarget = true;
            navAgent.isStopped = true;
            return NodeState.SUCCESS;
        }
        
        if (visionSensor != null && visionSensor.DetectionMeter >= 1.0f)
            return NodeState.FAILURE;
        
        return NodeState.RUNNING;
    }

    private NodeState LookAroundAtSound()
    {
        nodeEvaluationsThisTick++;
        currentNodeName = "LookAround";
        investigateLookTimer += Time.deltaTime;
        transform.Rotate(0, investigateLookSpeed * Time.deltaTime, 0);
        
        if (visionSensor != null && visionSensor.DetectionMeter >= 1.0f)
        {
            hasInvestigateTarget = false;
            return NodeState.FAILURE;
        }
        
        if (investigateLookTimer >= investigateLookDuration)
        {
            hasInvestigateTarget = false;
            if (hearingSensor != null) hearingSensor.ResetHearing();
            return NodeState.FAILURE;
        }
        
        return NodeState.RUNNING;
    }

    #endregion

    #region ═══════════════════ PATROL ACTIONS ═══════════════════

    private NodeState SetPatrolSpeed()
    {
        nodeEvaluationsThisTick++;
        SetBehavior(AIBehavior.PATROL);
        navAgent.speed = patrolSpeed;
        return NodeState.SUCCESS;
    }

    private NodeState MoveToWaypoint()
    {
        nodeEvaluationsThisTick++;
        currentNodeName = "MoveWaypoint";
        if (waypoints == null || waypoints.Length == 0) return NodeState.FAILURE;
        if (isWaitingAtWaypoint) return NodeState.SUCCESS;
        
        navAgent.isStopped = false;
        navAgent.SetDestination(waypoints[currentWaypointIndex].position);
        
        return HasReached() ? NodeState.SUCCESS : NodeState.RUNNING;
    }

    private NodeState WaitAtWaypoint()
    {
        nodeEvaluationsThisTick++;
        currentNodeName = "WaitWaypoint";
        
        if (!isWaitingAtWaypoint)
        {
            isWaitingAtWaypoint = true;
            waypointWaitTimer = 0f;
            navAgent.isStopped = true;
        }
        
        waypointWaitTimer += Time.deltaTime;
        
        if (waypointWaitTimer >= waypointWaitTime)
        {
            isWaitingAtWaypoint = false;
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
            return NodeState.SUCCESS;
        }
        
        return NodeState.RUNNING;
    }

    #endregion

    #region ═══════════════════ HELPERS ═══════════════════

    private bool HasReached()
    {
        return !navAgent.pathPending && navAgent.remainingDistance <= waypointReachThreshold;
    }

    private void SetBehavior(AIBehavior b)
    {
        if (b != currentBehavior)
        {
            currentBehavior = b;
            branchSwitchCount++;
            OnBehaviorChanged?.Invoke(b);
            if (enableDebug) Debug.Log($"[EnemyAI_BT] → {b}", this);
        }
    }

    private void CheckCatchCondition()
    {
        if (player == null || currentBehavior != AIBehavior.CHASE) return;
        if (Vector3.Distance(transform.position, player.position) <= catchDistance)
            OnPlayerCaught?.Invoke();
    }

    public void ResetAI()
    {
        btRunner.Reset();
        currentWaypointIndex = 0;
        wasChasing = false;
        hasInvestigateTarget = false;
        hasPendingAlert = false;
        currentBehavior = AIBehavior.PATROL;
        if (visionSensor != null) visionSensor.ResetDetection();
        if (hearingSensor != null) hearingSensor.ResetHearing();
    }

    #endregion

    #region ═══════════════════ DEBUG ═══════════════════

    private void OnDrawGizmos()
    {
        if (!enableDebug) return;
        
        // Waypoints
        if (waypoints != null)
        {
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                Gizmos.color = (Application.isPlaying && i == currentWaypointIndex) ? Color.cyan : Color.green;
                Gizmos.DrawWireSphere(waypoints[i].position, 0.5f);
            }
        }
        
        if (!Application.isPlaying) return;
        
        // Behavior-specific
        switch (currentBehavior)
        {
            case AIBehavior.CHASE:
                Gizmos.color = Color.red;
                if (player != null) Gizmos.DrawLine(transform.position, player.position);
                break;
            case AIBehavior.SEARCH:
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(searchTargetPosition, 1f);
                break;
            case AIBehavior.INVESTIGATE:
                Gizmos.color = new Color(1f, 0.5f, 0f);
                Gizmos.DrawWireSphere(investigatePosition, 1f);
                break;
            case AIBehavior.RESPOND_ALERT:
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(alertTargetPosition, 1.5f);
                Gizmos.DrawLine(transform.position, alertTargetPosition);
                break;
        }
    }

    private void OnGUI()
    {
        if (!enableDebug || !Application.isPlaying || Camera.main == null) return;
        
        Vector3 sp = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 3f);
        if (sp.z < 0) return;
        
        GUI.color = new Color(0, 0, 0, 0.7f);
        GUI.DrawTexture(new Rect(sp.x - 60, Screen.height - sp.y - 5, 120, 50), Texture2D.whiteTexture);
        
        GUI.color = Color.cyan;
        GUI.Label(new Rect(sp.x - 55, Screen.height - sp.y, 110, 20), "[BT] " + currentBehavior);
        GUI.color = Color.white;
        GUI.Label(new Rect(sp.x - 55, Screen.height - sp.y + 18, 110, 20), currentNodeName);
    }

    #endregion
}
