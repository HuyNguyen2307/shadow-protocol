using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// GAME MANAGER - Supports MULTIPLE Enemies (FSM & BT)
/// ═══════════════════════════════════════════════════════════════════════════════
/// 
/// Works with ANY NUMBER of enemies using either EnemyAI (FSM) or EnemyAI_BT.
/// 
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class GameManager : MonoBehaviour
{
    // ==================== SINGLETON ====================
    public static GameManager Instance { get; private set; }

    // ==================== GAME STATE ====================
    public enum GameState
    {
        Playing,
        Won,
        Lost,
        Paused
    }

    [Header("Current State")]
    [SerializeField] private GameState currentState = GameState.Playing;
    public GameState CurrentState => currentState;

    // ==================== AI TYPE ====================
    public enum AIType { None, FSM, BehaviorTree, Advanced, Mixed }
    
    [Header("AI Detection")]
    [SerializeField] private AIType detectedAIType = AIType.None;
    [SerializeField] private int enemyCount = 0;
    public AIType DetectedAIType => detectedAIType;
    public int EnemyCount => enemyCount;

    // ==================== REFERENCES ====================
    [Header("References")]
    public Transform player;
    
    // Multiple enemies
    private List<EnemyAI> enemyAI_FSMs = new List<EnemyAI>();
    private List<EnemyAI_BT> enemyAI_BTs = new List<EnemyAI_BT>();
    private List<EnemyAI_Advanced> enemyAI_Advanced = new List<EnemyAI_Advanced>();
    private List<Transform> allEnemies = new List<Transform>();

    // ==================== CATCH SETTINGS ====================
    [Header("Catch Settings")]
    public bool useDistanceCheck = true;
    public float catchDistance = 1.5f;

    // ==================== UI SETTINGS ====================
    [Header("UI Settings")]
    public int mainTextSize = 72;
    public int instructionTextSize = 24;
    public Color winColor = Color.green;
    public Color loseColor = Color.red;

    // ==================== AUDIO ====================
    [Header("Audio (Optional)")]
    public AudioClip winSound;
    public AudioClip loseSound;
    private AudioSource audioSource;

    // ==================== INTERNAL ====================
    private bool hasTriggeredEnd = false;
    private string endMessage = "";
    private string instructionMessage = "Press R to Restart";
    private Color currentColor = Color.white;

    // ==================== EVENTS ====================
    public System.Action OnGameWon;
    public System.Action OnGameLost;
    public System.Action OnGameRestarted;

    // ==================== INITIALIZATION ====================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null && (winSound != null || loseSound != null))
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    private void Start()
    {
        FindReferences();
        SubscribeToEvents();
        StartGame();
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
        if (Instance == this) Instance = null;
    }

    private void FindReferences()
    {
        // Find Player
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
                Debug.Log("[GameManager] Found Player", this);
            }
            else
            {
                Debug.LogError("[GameManager] Player not found!", this);
            }
        }

        // Find ALL FSM enemies
        enemyAI_FSMs.Clear();
        EnemyAI[] fsmEnemies = FindObjectsOfType<EnemyAI>();
        foreach (var enemy in fsmEnemies)
        {
            if (enemy.enabled)
            {
                enemyAI_FSMs.Add(enemy);
                allEnemies.Add(enemy.transform);
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
                allEnemies.Add(enemy.transform);
            }
        }
        
        // Find ALL Advanced AI enemies
        enemyAI_Advanced.Clear();
        EnemyAI_Advanced[] advEnemies = FindObjectsOfType<EnemyAI_Advanced>();
        foreach (var enemy in advEnemies)
        {
            if (enemy.enabled)
            {
                enemyAI_Advanced.Add(enemy);
                allEnemies.Add(enemy.transform);
            }
        }
        
        // Determine AI type
        enemyCount = enemyAI_FSMs.Count + enemyAI_BTs.Count + enemyAI_Advanced.Count;
        
        int typesPresent = 0;
        if (enemyAI_FSMs.Count > 0) typesPresent++;
        if (enemyAI_BTs.Count > 0) typesPresent++;
        if (enemyAI_Advanced.Count > 0) typesPresent++;
        
        if (typesPresent > 1)
        {
            detectedAIType = AIType.Mixed;
        }
        else if (enemyAI_Advanced.Count > 0)
        {
            detectedAIType = AIType.Advanced;
        }
        else if (enemyAI_BTs.Count > 0)
        {
            detectedAIType = AIType.BehaviorTree;
        }
        else if (enemyAI_FSMs.Count > 0)
        {
            detectedAIType = AIType.FSM;
        }
        else
        {
            detectedAIType = AIType.None;
        }
        
        Debug.Log($"[GameManager] Found {enemyCount} enemies. Type: {detectedAIType} (FSM: {enemyAI_FSMs.Count}, BT: {enemyAI_BTs.Count}, Adv: {enemyAI_Advanced.Count})", this);
    }

    private void SubscribeToEvents()
    {
        // Subscribe to ALL FSM enemies
        foreach (var enemy in enemyAI_FSMs)
        {
            enemy.OnPlayerCaught += HandlePlayerCaught;
        }
        
        // Subscribe to ALL BT enemies
        foreach (var enemy in enemyAI_BTs)
        {
            enemy.OnPlayerCaught += HandlePlayerCaught;
        }
        
        // Subscribe to ALL Advanced AI enemies
        foreach (var enemy in enemyAI_Advanced)
        {
            enemy.OnPlayerCaught += HandlePlayerCaught;
        }
    }

    private void UnsubscribeFromEvents()
    {
        foreach (var enemy in enemyAI_FSMs)
        {
            if (enemy != null)
                enemy.OnPlayerCaught -= HandlePlayerCaught;
        }
        
        foreach (var enemy in enemyAI_BTs)
        {
            if (enemy != null)
                enemy.OnPlayerCaught -= HandlePlayerCaught;
        }
        
        foreach (var enemy in enemyAI_Advanced)
        {
            if (enemy != null)
                enemy.OnPlayerCaught -= HandlePlayerCaught;
        }
    }

    // ==================== GAME LOOP ====================

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            RestartGame();
            return;
        }

        if (currentState != GameState.Playing) return;

        // Distance-based catch check for ALL enemies
        if (useDistanceCheck && player != null)
        {
            CheckDistanceCatchAllEnemies();
        }
    }

    private void CheckDistanceCatchAllEnemies()
    {
        // Check ALL FSM enemies
        foreach (var enemy in enemyAI_FSMs)
        {
            if (enemy == null || !enemy.enabled) continue;
            
            // Only catch during CHASE state
            if (enemy.State != EnemyAI.AIState.CHASE) continue;
            
            float distance = Vector3.Distance(player.position, enemy.transform.position);
            if (distance <= catchDistance)
            {
                TriggerLose("CAUGHT!");
                return;
            }
        }
        
        // Check ALL BT enemies
        foreach (var enemy in enemyAI_BTs)
        {
            if (enemy == null || !enemy.enabled) continue;
            
            // Only catch during CHASE behavior
            if (enemy.CurrentBehavior != EnemyAI_BT.AIBehavior.CHASE) continue;
            
            float distance = Vector3.Distance(player.position, enemy.transform.position);
            if (distance <= catchDistance)
            {
                TriggerLose("CAUGHT!");
                return;
            }
        }
        
        // Check ALL Advanced AI enemies
        foreach (var enemy in enemyAI_Advanced)
        {
            if (enemy == null || !enemy.enabled) continue;
            
            // Only catch during CHASE state
            if (enemy.State != EnemyAI_Advanced.AIState.CHASE) continue;
            
            float distance = Vector3.Distance(player.position, enemy.transform.position);
            if (distance <= catchDistance)
            {
                TriggerLose("CAUGHT!");
                return;
            }
        }
    }

    // ==================== GAME STATE METHODS ====================

    public void StartGame()
    {
        currentState = GameState.Playing;
        hasTriggeredEnd = false;
        endMessage = "";
        Time.timeScale = 1f;
        Debug.Log($"[GameManager] Game Started - {enemyCount} enemies ({detectedAIType})", this);
    }

    public void TriggerWin(string message = "YOU WIN!")
    {
        if (hasTriggeredEnd) return;
        
        hasTriggeredEnd = true;
        currentState = GameState.Won;
        endMessage = message;
        currentColor = winColor;
        
        if (audioSource != null && winSound != null)
            audioSource.PlayOneShot(winSound);
        
        Time.timeScale = 0.5f;
        OnGameWon?.Invoke();
        
        Debug.Log($"[GameManager] {message}", this);
    }

    public void TriggerLose(string message = "GAME OVER")
    {
        if (hasTriggeredEnd) return;
        
        hasTriggeredEnd = true;
        currentState = GameState.Lost;
        endMessage = message;
        currentColor = loseColor;
        
        if (audioSource != null && loseSound != null)
            audioSource.PlayOneShot(loseSound);
        
        Time.timeScale = 0f;
        OnGameLost?.Invoke();
        
        Debug.Log($"[GameManager] {message}", this);
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        OnGameRestarted?.Invoke();
        Debug.Log("[GameManager] Restarting...", this);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void HandlePlayerCaught()
    {
        TriggerLose("CAUGHT!");
    }

    // ==================== HELPER METHODS ====================

    /// <summary>
    /// Get the nearest enemy to player
    /// </summary>
    public Transform GetNearestEnemy()
    {
        if (player == null || allEnemies.Count == 0) return null;
        
        Transform nearest = null;
        float nearestDist = float.MaxValue;
        
        foreach (var enemy in allEnemies)
        {
            if (enemy == null) continue;
            
            float dist = Vector3.Distance(player.position, enemy.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = enemy;
            }
        }
        
        return nearest;
    }

    /// <summary>
    /// Check if any enemy is chasing
    /// </summary>
    public bool IsAnyEnemyChasing()
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
        
        foreach (var enemy in enemyAI_Advanced)
        {
            if (enemy != null && enemy.State == EnemyAI_Advanced.AIState.CHASE)
                return true;
        }
        
        return false;
    }

    /// <summary>
    /// Get list of all enemies
    /// </summary>
    public List<Transform> GetAllEnemies()
    {
        return allEnemies;
    }

    // ==================== UI ====================

    private void OnGUI()
    {
        if (currentState == GameState.Playing)
        {
            DrawAITypeIndicator();
            return;
        }

        // End game overlay
        float screenWidth = Screen.width;
        float screenHeight = Screen.height;
        
        GUI.color = new Color(0, 0, 0, 0.7f);
        GUI.DrawTexture(new Rect(0, 0, screenWidth, screenHeight), Texture2D.whiteTexture);
        
        GUI.color = currentColor;
        GUIStyle mainStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = mainTextSize,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        
        Rect mainRect = new Rect(0, screenHeight * 0.3f, screenWidth, mainTextSize + 20);
        GUI.Label(mainRect, endMessage, mainStyle);
        
        GUI.color = Color.white;
        GUIStyle instructionStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = instructionTextSize,
            alignment = TextAnchor.MiddleCenter
        };
        
        Rect instructionRect = new Rect(0, screenHeight * 0.3f + mainTextSize + 40, screenWidth, instructionTextSize + 10);
        GUI.Label(instructionRect, instructionMessage, instructionStyle);
    }

    private void DrawAITypeIndicator()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };
        
        string aiText = $"Enemies: {enemyCount} ({detectedAIType})";
        
        GUI.color = detectedAIType == AIType.FSM ? Color.cyan : 
                    detectedAIType == AIType.BehaviorTree ? Color.magenta : 
                    detectedAIType == AIType.Mixed ? Color.yellow : Color.gray;
        
        GUI.Label(new Rect(Screen.width - 200, 10, 190, 25), aiText, style);
    }

    // ==================== DEBUG ====================

    private void OnDrawGizmos()
    {
        if (!useDistanceCheck) return;
        
        // Draw catch radius around ALL enemies
        Gizmos.color = new Color(1, 0, 0, 0.2f);
        
        foreach (var enemy in allEnemies)
        {
            if (enemy != null)
            {
                Gizmos.DrawWireSphere(enemy.position, catchDistance);
            }
        }
    }
}
