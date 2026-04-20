using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using BehaviorTree;

/// <summary>
/// Behavior Tree implementation of the enemy AI with Patrol, Investigate, Chase, Search,
/// and Alert Response behaviors. Mirrors EnemyAI_Advanced for a direct FSM-vs-BT comparison
/// in the portfolio.
///
/// The tree is a priority Selector at the root so that higher-priority branches automatically
/// interrupt lower-priority ones every tick — Chase will preempt Patrol the moment the
/// detection meter fills, without any manual state-transition code.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(VisionSensor))]
public class EnemyAI_BT : MonoBehaviour, IAlertListener
{
    #region Enums

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

    #region Blackboard Keys

    // Const strings rather than an enum avoid the boxing/unboxing cast that a Dictionary<string,object>
    // would require for enum keys, and match the idiomatic convention for BT blackboard implementations.
    private const string BbHasTarget        = "hasTarget";
    private const string BbPlayerPos        = "playerPos";
    private const string BbSearchPos        = "searchPos";
    private const string BbWasChasing       = "wasChasing";
    private const string BbLastSeenTime     = "lastSeenTime";
    private const string BbSearchStartTime  = "searchStartTime";

    #endregion

    #region Inspector Settings

    [Header("References")]
    [SerializeField] private VisionSensor  _visionInspector;
    [SerializeField] private HearingSensor _hearingInspector;
    [SerializeField] private Transform     _player;
    [SerializeField] private Transform[]   _patrolRoute;

    [Header("Patrol Settings")]
    [SerializeField] private float _patrolSpeed            = 2.5f;
    [SerializeField] private float _waypointWaitTime       = 1.0f;
    [SerializeField] private float _waypointReachThreshold = 0.5f;

    [Header("Investigate Settings")]
    [SerializeField] private float _investigateSpeed       = 3.0f;
    [SerializeField] private float _investigateLookDuration = 3.0f;
    [SerializeField] private float _investigateLookSpeed   = 90f;

    [Header("Chase Settings")]
    [SerializeField] private float _chaseSpeed         = 5.0f;
    [SerializeField] private float _loseSightThreshold = 2.0f;
    [SerializeField] private float _catchDistance      = 1.5f;

    [Header("Search Settings")]
    [SerializeField] private float _searchSpeed    = 3.5f;
    [SerializeField] private float _searchDuration = 5.0f;
    [SerializeField] private float _searchScanSpeed = 120f;

    [Header("Alert Response Settings")]
    [SerializeField] private float _alertResponseSpeed = 4.5f;
    [SerializeField] private float _alertScanDuration  = 4.0f;
    [SerializeField] private float _alertScanSpeed     = 100f;

    [Header("Debug")]
    [SerializeField] private bool _enableDebug = true;

    #endregion

    #region Private Fields

    // Component references — cached in Awake to avoid repeated GetComponent calls per tick.
    private NavMeshAgent  _navAgent;
    private VisionSensor  _vision;
    private HearingSensor _hearing;

    private BehaviorTreeRunner _tree;
    private AIBehavior         _currentBehavior = AIBehavior.IDLE;
    private string             _currentNodeName = "";

    // Patrol
    private int   _patrolIndex          = 0;
    private float _waypointWaitTimer    = 0f;
    private bool  _isWaitingAtWaypoint  = false;

    // Investigate
    private Vector3 _searchCenter               = Vector3.zero;
    private bool    _hasInvestigateTarget        = false;
    private bool    _hasReachedInvestigateTarget = false;
    private float   _investigateLookTimer        = 0f;

    // Chase
    private float _lastPlayerSeenTime = 0f;
    private bool  _wasChasing         = false;

    // Search
    private Vector3 _searchTargetPosition  = Vector3.zero;
    private float   _searchStartTime       = 0f;
    private bool    _hasReachedSearchTarget = false;
    private float   _totalSearchRotation   = 0f;

    // Alert Response
    private bool    _hasPendingAlert         = false;
    private Vector3 _alertPosition           = Vector3.zero;
    private Vector3 _alertTargetPosition     = Vector3.zero;
    private bool    _hasReachedAlertPosition = false;
    private float   _alertScanTimer          = 0f;

    // SetDestination deduplication — skip the NavMesh call when the destination hasn't changed
    // to avoid flooding the NavMesh with redundant path requests every frame.
    private Vector3 _lastSetDestination = Vector3.positiveInfinity;

    // Metrics
    private int                          _nodeEvaluationsThisTick = 0;
    private int                          _totalNodeEvaluations    = 0;
    private int                          _branchSwitchCount       = 0;
    private Dictionary<AIBehavior, float> _behaviorTimers          = new Dictionary<AIBehavior, float>();

    #endregion

    #region Properties and Events

    /// <summary>Gets the currently active high-level behavior.</summary>
    public AIBehavior CurrentBehavior => _currentBehavior;

    /// <summary>Gets the name of the currently active behavior.</summary>
    public string CurrentBehaviorName => _currentBehavior.ToString();

    /// <summary>Gets the vision sensor's detection meter value (0–1).</summary>
    public float DetectionLevel => _vision?.DetectionMeter ?? 0f;

    /// <summary>Gets the total number of times the active branch has changed.</summary>
    public int BranchSwitchCount => _branchSwitchCount;

    /// <summary>Gets the cumulative BT node evaluations since start.</summary>
    public int TotalNodeEvaluations => _totalNodeEvaluations;

    /// <summary>Raised whenever the active behavior branch changes.</summary>
    public System.Action<AIBehavior> OnBehaviorChanged;

    /// <summary>Raised when the enemy reaches catch distance of the player.</summary>
    public System.Action OnPlayerCaught;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // Cache components once here rather than using GetComponent inside per-tick node lambdas.
        _navAgent = GetComponent<NavMeshAgent>();
        _vision   = _visionInspector  != null ? _visionInspector  : GetComponent<VisionSensor>();
        _hearing  = _hearingInspector != null ? _hearingInspector : GetComponent<HearingSensor>();

        foreach (AIBehavior b in System.Enum.GetValues(typeof(AIBehavior)))
            _behaviorTimers[b] = 0f;
    }

    private void Start()
    {
        if (_player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) _player = p.transform;
        }

        if (_hearing != null)
            _hearing.OnSoundHeard += HandleSoundHeard;

        if (AlertSystem.Instance != null)
        {
            AlertSystem.Instance.RegisterListener(this);
            if (_enableDebug) Debug.Log("[EnemyAI_BT] Registered with AlertSystem", this);
        }

        BuildTree();
        SetBehavior(AIBehavior.PATROL);
    }

    private void OnDestroy()
    {
        if (_hearing != null)
            _hearing.OnSoundHeard -= HandleSoundHeard;

        if (AlertSystem.Instance != null)
            AlertSystem.Instance.UnregisterListener(this);
    }

    private void Update()
    {
        _nodeEvaluationsThisTick = 0;
        _tree.Tick();
        _totalNodeEvaluations += _nodeEvaluationsThisTick;
        _behaviorTimers[_currentBehavior] += Time.deltaTime;
        CheckCatchCondition();
    }

    #endregion

    #region IAlertListener

    /// <summary>
    /// Receives alert broadcasts from other AI agents via AlertSystem.
    /// Alert response is lower priority than direct detection because raw sensor data
    /// is more reliable than a second-hand broadcast that may have been raised
    /// several frames ago by a now-dead agent.
    /// </summary>
    public void OnAlertReceived(AlertSystem.AlertData alert)
    {
        if (alert.Source == this as IAlertListener) return;
        if (_currentBehavior == AIBehavior.CHASE) return;

        _hasPendingAlert         = true;
        _alertPosition           = alert.Position;
        _alertTargetPosition     = alert.TargetPosition;
        _hasReachedAlertPosition = false;
        _alertScanTimer          = 0f;

        if (_enableDebug)
            Debug.Log($"[EnemyAI_BT] Alert received — moving to {_alertTargetPosition}", this);
    }

    /// <inheritdoc/>
    public Vector3 GetPosition() => transform.position;

    /// <inheritdoc/>
    public bool IsInCombat() => _currentBehavior == AIBehavior.CHASE;

    #endregion

    #region Tree Construction

    /// <summary>
    /// Assembles the priority Selector tree and assigns it to the runner.
    /// Each subtree is isolated in its own method so the priority order is visible
    /// at a glance from the AddChild call sequence below.
    /// </summary>
    private void BuildTree()
    {
        _tree = new BehaviorTreeRunner();
        var root = new Selector("Root");

        root.AddChild(BuildChaseSubtree());
        root.AddChild(BuildAlertSubtree());
        root.AddChild(BuildSearchSubtree());
        root.AddChild(BuildInvestigateSubtree());
        root.AddChild(BuildPatrolSubtree());

        _tree.SetRoot(root);

        if (_enableDebug)
            Debug.Log("[EnemyAI_BT] Tree built: Chase > Alert > Search > Investigate > Patrol", this);
    }

    private BTNode BuildChaseSubtree()
    {
        // Chase checks detection meter >= 1 rather than CanSeePlayer so that once the enemy
        // is fully committed to a chase it continues even if the player briefly steps out of
        // the vision cone — breaking line-of-sight mid-sprint should not instantly drop the chase.
        return new Sequence("Chase")
            .AddChild(new ConditionNode("Detected?",  IsPlayerDetected))
            .AddChild(new ActionNode("ChaseSpeed",    SetChaseSpeed))
            .AddChild(new ActionNode("Chase",         ChasePlayer));
    }

    private BTNode BuildAlertSubtree()
    {
        return new Sequence("Alert")
            .AddChild(new ConditionNode("HasAlert?",  () => _hasPendingAlert))
            .AddChild(new ActionNode("AlertSpeed",    SetAlertSpeed))
            .AddChild(new ActionNode("MoveToAlert",   MoveToAlertPosition))
            .AddChild(new ActionNode("ScanAlert",     ScanAlertArea));
    }

    private BTNode BuildSearchSubtree()
    {
        // Search uses a stored last-known position rather than just re-navigating to wherever
        // the player was last spotted, to prevent the enemy from circling the same spot when
        // the player moves away before the search begins.
        return new Sequence("Search")
            .AddChild(new ConditionNode("ShouldSearch?", ShouldSearch))
            .AddChild(new ActionNode("SearchSpeed",      SetSearchSpeed))
            .AddChild(new ActionNode("MoveToLast",       MoveToLastKnown))
            .AddChild(new ActionNode("Scan",             ScanArea));
    }

    private BTNode BuildInvestigateSubtree()
    {
        return new Sequence("Investigate")
            .AddChild(new ConditionNode("HeardSound?", () => _hasInvestigateTarget))
            .AddChild(new ActionNode("InvestSpeed",    SetInvestigateSpeed))
            .AddChild(new ActionNode("MoveToSound",    MoveToSoundSource))
            .AddChild(new ActionNode("LookAround",     LookAroundAtSound));
    }

    private BTNode BuildPatrolSubtree()
    {
        // Patrol is the unconditional fallback — no ConditionNode guard so the Selector
        // always has a leaf to return RUNNING when every higher branch fails.
        return new Sequence("Patrol")
            .AddChild(new ActionNode("PatrolSpeed",  SetPatrolSpeed))
            .AddChild(new ActionNode("MoveWaypoint", MoveToWaypoint))
            .AddChild(new ActionNode("Wait",         WaitAtWaypoint));
    }

    #endregion

    #region Sensor Integration

    private void HandleSoundHeard(Vector3 pos, float vol)
    {
        // Ignore new sound events when already committed to a higher-priority behavior;
        // the BT's priority Selector will keep those branches running without interference.
        if (_currentBehavior == AIBehavior.CHASE || _currentBehavior == AIBehavior.RESPOND_ALERT) return;

        _searchCenter               = pos;
        _hasInvestigateTarget       = true;
        _hasReachedInvestigateTarget = false;
        _investigateLookTimer       = 0f;
    }

    #endregion

    #region Chase Behavior

    private bool IsPlayerDetected()
    {
        _nodeEvaluationsThisTick++;
        if (_vision == null) return false;

        bool detected = _vision.DetectionMeter >= 1.0f;
        if (detected) RaiseAlert();
        return detected;
    }

    private NodeState SetChaseSpeed()
    {
        _nodeEvaluationsThisTick++;
        SetBehavior(AIBehavior.CHASE);
        _navAgent.speed     = _chaseSpeed;
        _wasChasing         = true;
        _lastPlayerSeenTime = Time.time;

        // Flush lower-priority pending triggers so the Search/Investigate branches
        // don't fire immediately when Chase ends.
        _hasInvestigateTarget = false;
        _hasPendingAlert      = false;

        return NodeState.SUCCESS;
    }

    private NodeState ChasePlayer()
    {
        _nodeEvaluationsThisTick++;
        _currentNodeName = "ChasePlayer";

        if (_player == null) return NodeState.FAILURE;

        if (_vision != null && _vision.CanSeePlayer)
        {
            _lastPlayerSeenTime  = Time.time;
            _searchTargetPosition = _player.position;
        }

        _navAgent.isStopped = false;
        SetNavDestination(_player.position);

        // Transition to Search once the player has been out of sight long enough;
        // returning FAILURE lets the Selector fall through to the Search branch.
        if (Time.time - _lastPlayerSeenTime >= _loseSightThreshold)
            return NodeState.FAILURE;

        return NodeState.RUNNING;
    }

    private void RaiseAlert()
    {
        if (AlertSystem.Instance == null || _player == null) return;

        AlertSystem.Instance.RaiseAlert(
            AlertSystem.AlertType.SPOTTED,
            transform.position,
            _player.position,
            this);

        if (_enableDebug) Debug.Log("[EnemyAI_BT] ALERT RAISED!", this);
    }

    #endregion

    #region Search Behavior

    private bool ShouldSearch()
    {
        _nodeEvaluationsThisTick++;
        if (!_wasChasing) return false;

        if (_vision != null && _vision.CanSeePlayer)
        {
            _lastPlayerSeenTime = Time.time;
            return false;
        }

        float elapsed = Time.time - _lastPlayerSeenTime;
        return elapsed >= _loseSightThreshold && elapsed < _searchDuration + _loseSightThreshold;
    }

    private NodeState SetSearchSpeed()
    {
        _nodeEvaluationsThisTick++;
        SetBehavior(AIBehavior.SEARCH);
        _navAgent.speed         = _searchSpeed;
        _searchStartTime        = Time.time;
        _hasReachedSearchTarget = false;
        _totalSearchRotation    = 0f;
        return NodeState.SUCCESS;
    }

    private NodeState MoveToLastKnown()
    {
        _nodeEvaluationsThisTick++;
        _currentNodeName = "MoveToLast";

        if (_hasReachedSearchTarget) return NodeState.SUCCESS;

        _navAgent.isStopped = false;
        SetNavDestination(_searchTargetPosition);

        if (HasReached())
        {
            _hasReachedSearchTarget = true;
            _navAgent.isStopped     = true;
            return NodeState.SUCCESS;
        }

        if (Time.time - _searchStartTime >= _searchDuration)
        {
            _wasChasing = false;
            return NodeState.FAILURE;
        }

        return NodeState.RUNNING;
    }

    private NodeState ScanArea()
    {
        _nodeEvaluationsThisTick++;
        _currentNodeName      = "ScanArea";
        _totalSearchRotation += _searchScanSpeed * Time.deltaTime;
        transform.Rotate(0, _searchScanSpeed * Time.deltaTime, 0);

        if (_vision != null && _vision.DetectionMeter >= 1.0f)
            return NodeState.FAILURE;

        if (Time.time - _searchStartTime >= _searchDuration)
        {
            _wasChasing = false;
            return NodeState.FAILURE;
        }

        return NodeState.RUNNING;
    }

    #endregion

    #region Investigation

    private NodeState SetInvestigateSpeed()
    {
        _nodeEvaluationsThisTick++;
        SetBehavior(AIBehavior.INVESTIGATE);
        _navAgent.speed              = _investigateSpeed;
        _hasReachedInvestigateTarget = false;
        _investigateLookTimer        = 0f;
        return NodeState.SUCCESS;
    }

    private NodeState MoveToSoundSource()
    {
        _nodeEvaluationsThisTick++;
        _currentNodeName = "MoveToSound";

        if (_hasReachedInvestigateTarget) return NodeState.SUCCESS;

        _navAgent.isStopped = false;
        SetNavDestination(_searchCenter);

        if (HasReached())
        {
            _hasReachedInvestigateTarget = true;
            _navAgent.isStopped          = true;
            return NodeState.SUCCESS;
        }

        if (_vision != null && _vision.DetectionMeter >= 1.0f)
            return NodeState.FAILURE;

        return NodeState.RUNNING;
    }

    private NodeState LookAroundAtSound()
    {
        _nodeEvaluationsThisTick++;
        _currentNodeName      = "LookAround";
        _investigateLookTimer += Time.deltaTime;
        transform.Rotate(0, _investigateLookSpeed * Time.deltaTime, 0);

        if (_vision != null && _vision.DetectionMeter >= 1.0f)
        {
            _hasInvestigateTarget = false;
            return NodeState.FAILURE;
        }

        if (_investigateLookTimer >= _investigateLookDuration)
        {
            _hasInvestigateTarget = false;
            if (_hearing != null) _hearing.ResetHearing();
            return NodeState.FAILURE;
        }

        return NodeState.RUNNING;
    }

    #endregion

    #region Patrol

    private NodeState SetPatrolSpeed()
    {
        _nodeEvaluationsThisTick++;
        SetBehavior(AIBehavior.PATROL);
        _navAgent.speed = _patrolSpeed;
        return NodeState.SUCCESS;
    }

    private NodeState MoveToWaypoint()
    {
        _nodeEvaluationsThisTick++;
        _currentNodeName = "MoveWaypoint";

        if (_patrolRoute == null || _patrolRoute.Length == 0) return NodeState.FAILURE;
        if (_isWaitingAtWaypoint) return NodeState.SUCCESS;

        _navAgent.isStopped = false;
        SetNavDestination(_patrolRoute[_patrolIndex].position);

        return HasReached() ? NodeState.SUCCESS : NodeState.RUNNING;
    }

    private NodeState WaitAtWaypoint()
    {
        _nodeEvaluationsThisTick++;
        _currentNodeName = "WaitWaypoint";

        if (!_isWaitingAtWaypoint)
        {
            _isWaitingAtWaypoint = true;
            _waypointWaitTimer   = 0f;
            _navAgent.isStopped  = true;
        }

        _waypointWaitTimer += Time.deltaTime;

        if (_waypointWaitTimer >= _waypointWaitTime)
        {
            _isWaitingAtWaypoint = false;
            _patrolIndex         = (_patrolIndex + 1) % _patrolRoute.Length;
            return NodeState.SUCCESS;
        }

        return NodeState.RUNNING;
    }

    #endregion

    #region Alert Response

    private NodeState SetAlertSpeed()
    {
        _nodeEvaluationsThisTick++;
        SetBehavior(AIBehavior.RESPOND_ALERT);
        _navAgent.speed          = _alertResponseSpeed;
        _hasReachedAlertPosition = false;
        _alertScanTimer          = 0f;
        return NodeState.SUCCESS;
    }

    private NodeState MoveToAlertPosition()
    {
        _nodeEvaluationsThisTick++;
        _currentNodeName = "MoveToAlert";

        if (_hasReachedAlertPosition) return NodeState.SUCCESS;

        _navAgent.isStopped = false;
        SetNavDestination(_alertTargetPosition);

        if (HasReached())
        {
            _hasReachedAlertPosition = true;
            _navAgent.isStopped      = true;
            return NodeState.SUCCESS;
        }

        // Abort alert response the moment the sensor confirms direct player detection.
        if (_vision != null && _vision.DetectionMeter >= 1.0f)
        {
            _hasPendingAlert = false;
            return NodeState.FAILURE;
        }

        return NodeState.RUNNING;
    }

    private NodeState ScanAlertArea()
    {
        _nodeEvaluationsThisTick++;
        _currentNodeName = "ScanAlert";
        _alertScanTimer += Time.deltaTime;
        transform.Rotate(0, _alertScanSpeed * Time.deltaTime, 0);

        if (_vision != null && _vision.DetectionMeter >= 1.0f)
        {
            _hasPendingAlert = false;
            return NodeState.FAILURE;
        }

        if (_alertScanTimer >= _alertScanDuration)
        {
            _hasPendingAlert = false;
            if (_enableDebug) Debug.Log("[EnemyAI_BT] Alert response complete", this);
            return NodeState.FAILURE;
        }

        return NodeState.RUNNING;
    }

    #endregion

    #region Helpers

    private bool HasReached()
    {
        return !_navAgent.pathPending && _navAgent.remainingDistance <= _waypointReachThreshold;
    }

    /// <summary>
    /// Wraps NavMeshAgent.SetDestination with deduplication so that branches running every
    /// tick do not flood the NavMesh with redundant path requests when the target hasn't moved.
    /// </summary>
    private void SetNavDestination(Vector3 destination)
    {
        if (destination == _lastSetDestination) return;
        _navAgent.SetDestination(destination);
        _lastSetDestination = destination;
    }

    private void SetBehavior(AIBehavior b)
    {
        if (b == _currentBehavior) return;

        _currentBehavior = b;
        _branchSwitchCount++;
        OnBehaviorChanged?.Invoke(b);

        if (_enableDebug) Debug.Log($"[EnemyAI_BT] -> {b}", this);
    }

    private void CheckCatchCondition()
    {
        if (_player == null || _currentBehavior != AIBehavior.CHASE) return;
        if (Vector3.Distance(transform.position, _player.position) <= _catchDistance)
            OnPlayerCaught?.Invoke();
    }

    /// <summary>Resets all behavior state and restarts from Patrol.</summary>
    public void ResetAI()
    {
        _tree.Reset();
        _patrolIndex          = 0;
        _wasChasing           = false;
        _hasInvestigateTarget = false;
        _hasPendingAlert      = false;
        _lastSetDestination   = Vector3.positiveInfinity;
        SetBehavior(AIBehavior.PATROL);

        if (_vision   != null) _vision.ResetDetection();
        if (_hearing  != null) _hearing.ResetHearing();
    }

    #endregion

    #region Debug

    private void OnDrawGizmos()
    {
        if (!_enableDebug) return;

        if (_patrolRoute != null)
        {
            for (int i = 0; i < _patrolRoute.Length; i++)
            {
                if (_patrolRoute[i] == null) continue;
                Gizmos.color = (Application.isPlaying && i == _patrolIndex) ? Color.cyan : Color.green;
                Gizmos.DrawWireSphere(_patrolRoute[i].position, 0.5f);
            }
        }

        if (!Application.isPlaying) return;

        switch (_currentBehavior)
        {
            case AIBehavior.CHASE:
                Gizmos.color = Color.red;
                if (_player != null) Gizmos.DrawLine(transform.position, _player.position);
                break;
            case AIBehavior.SEARCH:
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(_searchTargetPosition, 1f);
                break;
            case AIBehavior.INVESTIGATE:
                Gizmos.color = new Color(1f, 0.5f, 0f);
                Gizmos.DrawWireSphere(_searchCenter, 1f);
                break;
            case AIBehavior.RESPOND_ALERT:
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(_alertTargetPosition, 1.5f);
                Gizmos.DrawLine(transform.position, _alertTargetPosition);
                break;
        }
    }

    private void OnGUI()
    {
        if (!_enableDebug || !Application.isPlaying || Camera.main == null) return;

        Vector3 sp = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 3f);
        if (sp.z < 0) return;

        GUI.color = new Color(0, 0, 0, 0.7f);
        GUI.DrawTexture(new Rect(sp.x - 60, Screen.height - sp.y - 5, 120, 50), Texture2D.whiteTexture);

        GUI.color = Color.cyan;
        GUI.Label(new Rect(sp.x - 55, Screen.height - sp.y,      110, 20), "[BT] " + _currentBehavior);
        GUI.color = Color.white;
        GUI.Label(new Rect(sp.x - 55, Screen.height - sp.y + 18, 110, 20), _currentNodeName);
    }

    #endregion
}
