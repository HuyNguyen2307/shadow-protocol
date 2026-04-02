using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// METRICS LOGGER - Supports MULTIPLE Enemies (FSM & BT)
/// ═══════════════════════════════════════════════════════════════════════════════
/// 
/// Logs metrics for ALL enemies in the scene.
/// Tracks nearest enemy state for primary metrics.
/// 
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class MetricsLogger : MonoBehaviour
{
    // ==================== AI TYPE ====================
    public enum AIType { Unknown, FSM, BehaviorTree, Mixed }
    
    [Header("Detected AI")]
    [SerializeField] private AIType detectedAIType = AIType.Unknown;
    [SerializeField] private int enemyCount = 0;

    // ==================== REFERENCES ====================
    [Header("References (Auto-assigned)")]
    public GameManager gameManager;
    
    // Multiple enemies
    private List<EnemyAI> enemyAI_FSMs = new List<EnemyAI>();
    private List<EnemyAI_BT> enemyAI_BTs = new List<EnemyAI_BT>();
    private List<VisionSensor> visionSensors = new List<VisionSensor>();

    // ==================== SETTINGS ====================
    [Header("Logging Settings")]
    public float logInterval = 0.1f;
    public float flushInterval = 2.0f;
    public bool includeHeader = true;

    [Header("Debug")]
    public bool showDebugMessages = true;
    public bool showFilePathUI = true;

    // ==================== INTERNAL ====================
    private string filePath;
    private string fileName;
    private List<string> logBuffer;
    private bool isInitialized = false;
    
    private float gameStartTime;
    private float lastLogTime;
    private float lastFlushTime;
    
    private int totalRowsLogged = 0;
    private int totalEventsLogged = 0;

    // State tracking for events
    private Dictionary<int, string> previousStates = new Dictionary<int, string>();

    // ==================== CSV COLUMNS ====================
    private const string CSV_HEADER = "t,ai_type,enemy_count,any_chasing,nearest_state,max_detection,event,bt_total_evals,bt_total_switches";

    // ==================== EVENT TYPES ====================
    private const string EVENT_NONE = "";
    private const string EVENT_STATE_CHANGE = "STATE_CHANGE";
    private const string EVENT_WIN = "WIN";
    private const string EVENT_LOSE = "LOSE";
    private const string EVENT_SESSION_START = "SESSION_START";
    private const string EVENT_SESSION_END = "SESSION_END";

    // ==================== INITIALIZATION ====================

    private void Awake()
    {
        logBuffer = new List<string>();
    }

    private void Start()
    {
        FindReferences();
        InitializeLogFile();
        SubscribeToEvents();
        
        gameStartTime = Time.time;
        lastLogTime = 0f;
        lastFlushTime = 0f;
        
        LogEvent(EVENT_SESSION_START, $"AI:{detectedAIType},Count:{enemyCount}");
        
        isInitialized = true;
        
        if (showDebugMessages)
        {
            Debug.Log($"[MetricsLogger] Initialized. AI: {detectedAIType}, Enemies: {enemyCount}", this);
            Debug.Log($"[MetricsLogger] File: {filePath}", this);
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    private void OnApplicationQuit()
    {
        LogEvent(EVENT_SESSION_END, $"AI:{detectedAIType}");
        FlushBuffer();
        
        if (showDebugMessages)
        {
            Debug.Log($"[MetricsLogger] Session ended. Rows: {totalRowsLogged}, Events: {totalEventsLogged}", this);
        }
    }

    private void FindReferences()
    {
        // Find GameManager
        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
            if (gameManager == null)
                gameManager = FindObjectOfType<GameManager>();
        }

        // Find ALL FSM enemies
        enemyAI_FSMs.Clear();
        EnemyAI[] fsmEnemies = FindObjectsOfType<EnemyAI>();
        int id = 0;
        foreach (var enemy in fsmEnemies)
        {
            if (enemy.enabled)
            {
                enemyAI_FSMs.Add(enemy);
                
                // Get vision sensor
                var sensor = enemy.GetComponent<VisionSensor>();
                if (sensor != null) visionSensors.Add(sensor);
                
                // Track state
                previousStates[id] = enemy.CurrentState;
                id++;
            }
        }
        
        // Find ALL BT enemies
        enemyAI_BTs.Clear();
        EnemyAI_BT[] btEnemies = FindObjectsOfType<EnemyAI_BT>();
        foreach (var enemy in btEnemies)
        {
            if (enemy.enabled)
            {
                enemyAI_BTs.Add(enemy);
                
                // Get vision sensor
                var sensor = enemy.GetComponent<VisionSensor>();
                if (sensor != null) visionSensors.Add(sensor);
                
                // Track state
                previousStates[id] = enemy.CurrentBehaviorName;
                id++;
            }
        }
        
        // Determine AI type
        enemyCount = enemyAI_FSMs.Count + enemyAI_BTs.Count;
        
        if (enemyAI_FSMs.Count > 0 && enemyAI_BTs.Count == 0)
        {
            detectedAIType = AIType.FSM;
        }
        else if (enemyAI_BTs.Count > 0 && enemyAI_FSMs.Count == 0)
        {
            detectedAIType = AIType.BehaviorTree;
        }
        else if (enemyAI_FSMs.Count > 0 && enemyAI_BTs.Count > 0)
        {
            detectedAIType = AIType.Mixed;
        }
        else
        {
            detectedAIType = AIType.Unknown;
        }
    }

    private void InitializeLogFile()
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string aiTypeStr = detectedAIType.ToString();
        fileName = $"metrics_{aiTypeStr}_{enemyCount}enemies_{timestamp}.csv";
        
        filePath = Path.Combine(Application.persistentDataPath, fileName);
        
        string directory = Path.GetDirectoryName(filePath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        if (includeHeader)
        {
            logBuffer.Add(CSV_HEADER);
        }
    }

    private void SubscribeToEvents()
    {
        // Subscribe to game events
        if (gameManager != null)
        {
            gameManager.OnGameWon += HandleGameWon;
            gameManager.OnGameLost += HandleGameLost;
        }
        
        // Subscribe to ALL FSM state changes
        foreach (var enemy in enemyAI_FSMs)
        {
            enemy.OnStateChanged += (state) => HandleStateChanged(enemy.GetInstanceID(), state.ToString());
        }
        
        // Subscribe to ALL BT behavior changes
        foreach (var enemy in enemyAI_BTs)
        {
            enemy.OnBehaviorChanged += (behavior) => HandleStateChanged(enemy.GetInstanceID(), behavior.ToString());
        }
    }

    private void UnsubscribeFromEvents()
    {
        if (gameManager != null)
        {
            gameManager.OnGameWon -= HandleGameWon;
            gameManager.OnGameLost -= HandleGameLost;
        }
    }

    // ==================== UPDATE ====================

    private void Update()
    {
        if (!isInitialized) return;
        
        float currentTime = Time.time - gameStartTime;
        
        // Log at interval
        if (currentTime - lastLogTime >= logInterval)
        {
            LogCurrentState();
            lastLogTime = currentTime;
        }
        
        // Flush at interval
        if (currentTime - lastFlushTime >= flushInterval)
        {
            FlushBuffer();
            lastFlushTime = currentTime;
        }
    }

    // ==================== EVENT HANDLERS ====================

    private void HandleStateChanged(int enemyId, string newState)
    {
        string prevState = previousStates.ContainsKey(enemyId) ? previousStates[enemyId] : "UNKNOWN";
        previousStates[enemyId] = newState;
        
        LogEventRow($"{EVENT_STATE_CHANGE}:{prevState}->{newState}");
        totalEventsLogged++;
    }

    private void HandleGameWon()
    {
        LogEvent(EVENT_WIN, "");
        FlushBuffer();
    }

    private void HandleGameLost()
    {
        LogEvent(EVENT_LOSE, "");
        FlushBuffer();
    }

    // ==================== LOGGING ====================

    private void LogCurrentState()
    {
        LogRow(GetCurrentTime(), GetNearestEnemyState(), GetMaxDetection(), 
               IsAnyChasing() ? 1 : 0, EVENT_NONE, GetTotalBTEvals(), GetTotalBTSwitches());
    }

    private void LogEvent(string eventType, string details)
    {
        string eventField = string.IsNullOrEmpty(details) ? eventType : $"{eventType}:{details}";
        LogRow(GetCurrentTime(), GetNearestEnemyState(), GetMaxDetection(),
               IsAnyChasing() ? 1 : 0, eventField, GetTotalBTEvals(), GetTotalBTSwitches());
        totalEventsLogged++;
    }

    private void LogEventRow(string eventField)
    {
        LogRow(GetCurrentTime(), GetNearestEnemyState(), GetMaxDetection(),
               IsAnyChasing() ? 1 : 0, eventField, GetTotalBTEvals(), GetTotalBTSwitches());
    }

    private void LogRow(float time, string nearestState, float maxDetection, 
                        int anyChasing, string eventType, int btEvals, int btSwitches)
    {
        // Format: t,ai_type,enemy_count,any_chasing,nearest_state,max_detection,event,bt_total_evals,bt_total_switches
        string row = $"{time:F3},{detectedAIType},{enemyCount},{anyChasing},{nearestState},{maxDetection:F3},{EscapeCSV(eventType)},{btEvals},{btSwitches}";
        logBuffer.Add(row);
        totalRowsLogged++;
    }

    // ==================== DATA GETTERS ====================

    private float GetCurrentTime()
    {
        return Time.time - gameStartTime;
    }

    private string GetNearestEnemyState()
    {
        if (gameManager == null || gameManager.player == null) return "UNKNOWN";
        
        Transform player = gameManager.player;
        float nearestDist = float.MaxValue;
        string nearestState = "UNKNOWN";
        
        // Check FSM enemies
        foreach (var enemy in enemyAI_FSMs)
        {
            if (enemy == null) continue;
            float dist = Vector3.Distance(player.position, enemy.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestState = enemy.CurrentState;
            }
        }
        
        // Check BT enemies
        foreach (var enemy in enemyAI_BTs)
        {
            if (enemy == null) continue;
            float dist = Vector3.Distance(player.position, enemy.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestState = enemy.CurrentBehaviorName;
            }
        }
        
        return nearestState;
    }

    private float GetMaxDetection()
    {
        float maxDetection = 0f;
        
        foreach (var sensor in visionSensors)
        {
            if (sensor != null && sensor.DetectionMeter > maxDetection)
            {
                maxDetection = sensor.DetectionMeter;
            }
        }
        
        return maxDetection;
    }

    private bool IsAnyChasing()
    {
        foreach (var enemy in enemyAI_FSMs)
        {
            if (enemy != null && enemy.State == EnemyAI.AIState.CHASE)
                return true;
        }
        
        foreach (var enemy in enemyAI_BTs)
        {
            if (enemy != null && enemy.CurrentBehavior == EnemyAI_BT.AIBehavior.CHASE)
                return true;
        }
        
        return false;
    }

    private int GetTotalBTEvals()
    {
        int total = 0;
        foreach (var enemy in enemyAI_BTs)
        {
            if (enemy != null)
                total += enemy.TotalNodeEvaluations;
        }
        return total;
    }

    private int GetTotalBTSwitches()
    {
        int total = 0;
        foreach (var enemy in enemyAI_BTs)
        {
            if (enemy != null)
                total += enemy.BranchSwitchCount;
        }
        return total;
    }

    // ==================== FILE OPERATIONS ====================

    private void FlushBuffer()
    {
        if (logBuffer.Count == 0) return;
        
        try
        {
            using (StreamWriter writer = new StreamWriter(filePath, true, Encoding.UTF8))
            {
                foreach (string line in logBuffer)
                {
                    writer.WriteLine(line);
                }
            }
            
            int flushedCount = logBuffer.Count;
            logBuffer.Clear();
            
            if (showDebugMessages)
            {
                Debug.Log($"[MetricsLogger] Flushed {flushedCount} rows", this);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[MetricsLogger] Write failed: {e.Message}", this);
        }
    }

    private string EscapeCSV(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    // ==================== PUBLIC METHODS ====================

    public string GetFilePath() => filePath;
    public (int rows, int events) GetStats() => (totalRowsLogged, totalEventsLogged);
    public void ForceFlush() => FlushBuffer();

    [ContextMenu("Open Log Folder")]
    public void OpenLogFolder()
    {
        string folder = Application.persistentDataPath;
        
        #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        System.Diagnostics.Process.Start("explorer.exe", folder.Replace("/", "\\"));
        #elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        System.Diagnostics.Process.Start("open", folder);
        #else
        Debug.Log($"Log folder: {folder}");
        #endif
    }

    // ==================== DEBUG UI ====================

    private void OnGUI()
    {
        if (!showFilePathUI || !isInitialized) return;

        float width = 520f;
        float height = 75f;
        float x = 10f;
        float y = Screen.height - height - 10f;

        Rect box = new Rect(x, y, width, height);

        // Background
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);

        // Text style
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = { textColor = detectedAIType == AIType.FSM ? Color.cyan : 
                                   detectedAIType == AIType.BehaviorTree ? Color.magenta : Color.yellow }
        };

        GUI.Label(new Rect(x + 8, y + 5, width - 16, 20), $"[{detectedAIType}] {enemyCount} enemies - {fileName}", style);
        
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(x + 8, y + 25, width - 16, 20), $"Rows: {totalRowsLogged} | Events: {totalEventsLogged} | Chasing: {IsAnyChasing()}", style);
        
        style.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
        GUI.Label(new Rect(x + 8, y + 45, width - 16, 20), $"Path: {Application.persistentDataPath}", style);
    }
}
