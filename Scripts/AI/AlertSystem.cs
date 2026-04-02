using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// ALERT SYSTEM - Multi-AI Communication Manager
/// ═══════════════════════════════════════════════════════════════════════════════
/// 
/// Manages communication between AI enemies:
/// - Enemies can raise alerts when spotting player
/// - Nearby enemies respond to alerts
/// - Supports different alert levels
/// 
/// ALERT TYPES:
/// - SUSPICIOUS: "I think I heard something" - nearby enemies become cautious
/// - SPOTTED: "I see the intruder!" - nearby enemies investigate
/// - COMBAT: "Engaging target!" - nearby enemies join chase
/// - LOST: "Lost visual" - inform others player escaped
/// 
/// ACADEMIC RELEVANCE:
/// - Demonstrates scalability of FSM vs BT
/// - Adding coordination requires different approaches in each architecture
/// - Tests maintainability when adding features
/// 
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class AlertSystem : MonoBehaviour
{
    #region ═══════════════════ SINGLETON ═══════════════════

    public static AlertSystem Instance { get; private set; }

    #endregion

    #region ═══════════════════ ENUMS ═══════════════════

    public enum AlertType
    {
        SUSPICIOUS,     // Heard something, low priority
        SPOTTED,        // Saw player briefly
        COMBAT,         // In active pursuit
        LOST,           // Lost the player
        BACKUP_REQUEST  // Need help!
    }

    public enum AlertPropagation
    {
        RANGE_BASED,    // Only nearby enemies hear
        LINE_OF_SIGHT,  // Only enemies who can see the alerter
        GLOBAL          // All enemies hear (radio/alarm)
    }

    #endregion

    #region ═══════════════════ SETTINGS ═══════════════════

    [Header("═══ ALERT SETTINGS ═══")]
    [Tooltip("Default range for voice alerts")]
    [SerializeField] private float defaultAlertRange = 20f;
    
    [Tooltip("Range for shout/combat alerts")]
    [SerializeField] private float shoutRange = 30f;
    
    [Tooltip("Delay before alert propagates (simulates reaction time)")]
    [SerializeField] private float alertPropagationDelay = 0.5f;

    [Header("═══ GLOBAL ALERT ═══")]
    [Tooltip("Time before global alert level decays")]
    [SerializeField] private float globalAlertDecayTime = 30f;
    
    [Tooltip("Current global alert level (0-1)")]
    [SerializeField] private float globalAlertLevel = 0f;

    [Header("═══ COOLDOWNS ═══")]
    [Tooltip("Minimum time between alerts from same enemy")]
    [SerializeField] private float alertCooldown = 3f;

    [Header("═══ DEBUG ═══")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private bool logAlerts = true;

    #endregion

    #region ═══════════════════ PRIVATE FIELDS ═══════════════════

    // Registered enemies
    private List<IAlertListener> listeners = new List<IAlertListener>();
    
    // Alert cooldowns per enemy
    private Dictionary<IAlertListener, float> lastAlertTime = new Dictionary<IAlertListener, float>();
    
    // Active alerts for visualization
    private List<ActiveAlert> activeAlerts = new List<ActiveAlert>();

    private float lastGlobalAlertTime;

    #endregion

    #region ═══════════════════ STRUCTS ═══════════════════

    public struct AlertData
    {
        public AlertType Type;
        public Vector3 Position;            // Where alert originated
        public Vector3 TargetPosition;      // Where player was seen
        public IAlertListener Source;       // Who raised the alert
        public float Timestamp;
        
        public AlertData(AlertType type, Vector3 position, Vector3 targetPos, IAlertListener source)
        {
            Type = type;
            Position = position;
            TargetPosition = targetPos;
            Source = source;
            Timestamp = Time.time;
        }
    }

    private struct ActiveAlert
    {
        public Vector3 Position;
        public float Range;
        public float Timestamp;
        public AlertType Type;
    }

    #endregion

    #region ═══════════════════ EVENTS ═══════════════════

    /// <summary>Fired when any alert is raised</summary>
    public event Action<AlertData> OnAlertRaised;
    
    /// <summary>Fired when global alert level changes</summary>
    public event Action<float> OnGlobalAlertChanged;

    #endregion

    #region ═══════════════════ PUBLIC PROPERTIES ═══════════════════

    public float GlobalAlertLevel => globalAlertLevel;
    public int RegisteredListenerCount => listeners.Count;

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

    private void Update()
    {
        UpdateGlobalAlertDecay();
        CleanupOldAlerts();
    }

    #endregion

    #region ═══════════════════ REGISTRATION ═══════════════════

    public void RegisterListener(IAlertListener listener)
    {
        if (!listeners.Contains(listener))
        {
            listeners.Add(listener);
            lastAlertTime[listener] = -alertCooldown;
            
            if (logAlerts)
            {
                Debug.Log($"[AlertSystem] Registered: {listener}. Total listeners: {listeners.Count}");
            }
        }
    }

    public void UnregisterListener(IAlertListener listener)
    {
        listeners.Remove(listener);
        lastAlertTime.Remove(listener);
    }

    #endregion

    #region ═══════════════════ RAISE ALERTS ═══════════════════

    /// <summary>
    /// Raise an alert that nearby enemies can hear.
    /// </summary>
    public void RaiseAlert(AlertType type, Vector3 position, Vector3 targetPosition, IAlertListener source, AlertPropagation propagation = AlertPropagation.RANGE_BASED)
    {
        // Check cooldown
        if (source != null && lastAlertTime.ContainsKey(source))
        {
            if (Time.time - lastAlertTime[source] < alertCooldown)
            {
                return; // Still on cooldown
            }
            lastAlertTime[source] = Time.time;
        }

        // Create alert data
        AlertData alert = new AlertData(type, position, targetPosition, source);
        
        // Determine range based on type
        float range = GetAlertRange(type, propagation);
        
        // Store for visualization
        activeAlerts.Add(new ActiveAlert
        {
            Position = position,
            Range = range,
            Timestamp = Time.time,
            Type = type
        });

        // Update global alert
        UpdateGlobalAlert(type);

        // Fire event
        OnAlertRaised?.Invoke(alert);

        if (logAlerts)
        {
            Debug.Log($"[AlertSystem] ALERT: {type} at {position} by {source}, range: {range}m");
        }

        // Propagate to listeners
        PropagateAlert(alert, range, propagation);
    }

    /// <summary>
    /// Quick method to raise combat alert at enemy's position
    /// </summary>
    public void RaiseCombatAlert(Vector3 enemyPosition, Vector3 playerPosition, IAlertListener source)
    {
        RaiseAlert(AlertType.COMBAT, enemyPosition, playerPosition, source, AlertPropagation.RANGE_BASED);
    }

    /// <summary>
    /// Trigger global alarm - all enemies alerted
    /// </summary>
    public void TriggerGlobalAlarm(Vector3 playerLastKnownPosition)
    {
        globalAlertLevel = 1f;
        lastGlobalAlertTime = Time.time;
        OnGlobalAlertChanged?.Invoke(globalAlertLevel);

        AlertData alert = new AlertData(AlertType.COMBAT, Vector3.zero, playerLastKnownPosition, null);
        
        foreach (var listener in listeners)
        {
            if (listener != null)
            {
                listener.OnAlertReceived(alert);
            }
        }

        if (logAlerts)
        {
            Debug.Log($"[AlertSystem] GLOBAL ALARM TRIGGERED! All {listeners.Count} enemies alerted!");
        }
    }

    #endregion

    #region ═══════════════════ ALERT PROPAGATION ═══════════════════

    private void PropagateAlert(AlertData alert, float range, AlertPropagation propagation)
    {
        foreach (var listener in listeners)
        {
            if (listener == null || listener == alert.Source)
                continue;

            bool shouldReceive = false;
            Vector3 listenerPos = listener.GetPosition();

            switch (propagation)
            {
                case AlertPropagation.RANGE_BASED:
                    float distance = Vector3.Distance(alert.Position, listenerPos);
                    shouldReceive = distance <= range;
                    break;

                case AlertPropagation.LINE_OF_SIGHT:
                    // Check if listener can see the alerter
                    Vector3 direction = alert.Position - listenerPos;
                    if (!Physics.Raycast(listenerPos, direction.normalized, direction.magnitude, LayerMask.GetMask("Wall", "Obstacle")))
                    {
                        shouldReceive = Vector3.Distance(alert.Position, listenerPos) <= range;
                    }
                    break;

                case AlertPropagation.GLOBAL:
                    shouldReceive = true;
                    break;
            }

            if (shouldReceive)
            {
                // Slight delay for realism
                StartCoroutine(DelayedAlertDelivery(listener, alert));
            }
        }
    }

    private System.Collections.IEnumerator DelayedAlertDelivery(IAlertListener listener, AlertData alert)
    {
        yield return new WaitForSeconds(alertPropagationDelay);
        
        if (listener != null)
        {
            listener.OnAlertReceived(alert);
            
            if (logAlerts)
            {
                Debug.Log($"[AlertSystem] {listener} received {alert.Type} alert");
            }
        }
    }

    private float GetAlertRange(AlertType type, AlertPropagation propagation)
    {
        if (propagation == AlertPropagation.GLOBAL)
            return float.MaxValue;

        switch (type)
        {
            case AlertType.SUSPICIOUS:
                return defaultAlertRange * 0.5f;
            case AlertType.SPOTTED:
                return defaultAlertRange;
            case AlertType.COMBAT:
            case AlertType.BACKUP_REQUEST:
                return shoutRange;
            case AlertType.LOST:
                return defaultAlertRange * 0.75f;
            default:
                return defaultAlertRange;
        }
    }

    #endregion

    #region ═══════════════════ GLOBAL ALERT ═══════════════════

    private void UpdateGlobalAlert(AlertType type)
    {
        float increase = 0f;
        
        switch (type)
        {
            case AlertType.SUSPICIOUS:
                increase = 0.1f;
                break;
            case AlertType.SPOTTED:
                increase = 0.25f;
                break;
            case AlertType.COMBAT:
                increase = 0.5f;
                break;
            case AlertType.BACKUP_REQUEST:
                increase = 0.4f;
                break;
        }

        globalAlertLevel = Mathf.Clamp01(globalAlertLevel + increase);
        lastGlobalAlertTime = Time.time;
        OnGlobalAlertChanged?.Invoke(globalAlertLevel);
    }

    private void UpdateGlobalAlertDecay()
    {
        if (globalAlertLevel > 0 && Time.time - lastGlobalAlertTime > globalAlertDecayTime)
        {
            globalAlertLevel -= Time.deltaTime * 0.05f;
            globalAlertLevel = Mathf.Max(0f, globalAlertLevel);
            OnGlobalAlertChanged?.Invoke(globalAlertLevel);
        }
    }

    private void CleanupOldAlerts()
    {
        activeAlerts.RemoveAll(a => Time.time - a.Timestamp > 3f);
    }

    #endregion

    #region ═══════════════════ QUERIES ═══════════════════

    /// <summary>
    /// Get all enemies within range of a position
    /// </summary>
    public List<IAlertListener> GetEnemiesInRange(Vector3 position, float range)
    {
        List<IAlertListener> inRange = new List<IAlertListener>();
        
        foreach (var listener in listeners)
        {
            if (listener != null)
            {
                float dist = Vector3.Distance(position, listener.GetPosition());
                if (dist <= range)
                {
                    inRange.Add(listener);
                }
            }
        }
        
        return inRange;
    }

    /// <summary>
    /// Check if any enemy is in combat state
    /// </summary>
    public bool IsAnyEnemyInCombat()
    {
        foreach (var listener in listeners)
        {
            if (listener != null && listener.IsInCombat())
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Get the closest enemy to a position
    /// </summary>
    public IAlertListener GetClosestEnemy(Vector3 position, float maxRange = float.MaxValue)
    {
        IAlertListener closest = null;
        float closestDist = maxRange;

        foreach (var listener in listeners)
        {
            if (listener != null)
            {
                float dist = Vector3.Distance(position, listener.GetPosition());
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = listener;
                }
            }
        }

        return closest;
    }

    #endregion

    #region ═══════════════════ DEBUG ═══════════════════

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || !Application.isPlaying) return;

        // Draw active alerts
        foreach (var alert in activeAlerts)
        {
            float alpha = 1f - (Time.time - alert.Timestamp) / 3f;
            
            Color color = GetAlertColor(alert.Type);
            color.a = alpha * 0.3f;
            Gizmos.color = color;
            Gizmos.DrawSphere(alert.Position, alert.Range);
            
            color.a = alpha;
            Gizmos.color = color;
            Gizmos.DrawWireSphere(alert.Position, alert.Range);
        }
    }

    private Color GetAlertColor(AlertType type)
    {
        switch (type)
        {
            case AlertType.SUSPICIOUS: return Color.yellow;
            case AlertType.SPOTTED: return new Color(1f, 0.5f, 0f); // Orange
            case AlertType.COMBAT: return Color.red;
            case AlertType.BACKUP_REQUEST: return Color.magenta;
            case AlertType.LOST: return Color.gray;
            default: return Color.white;
        }
    }

    private void OnGUI()
    {
        if (!showDebugGizmos || !Application.isPlaying) return;

        // Draw global alert level
        float barWidth = 150f;
        float barHeight = 20f;
        float x = Screen.width - barWidth - 20;
        float y = 10;

        GUI.color = new Color(0, 0, 0, 0.7f);
        GUI.DrawTexture(new Rect(x - 5, y - 5, barWidth + 10, barHeight + 25), Texture2D.whiteTexture);

        GUI.color = Color.white;
        GUI.Label(new Rect(x, y, barWidth, 15), "GLOBAL ALERT");

        // Bar background
        GUI.color = Color.gray;
        GUI.DrawTexture(new Rect(x, y + 18, barWidth, barHeight), Texture2D.whiteTexture);

        // Bar fill
        GUI.color = Color.Lerp(Color.green, Color.red, globalAlertLevel);
        GUI.DrawTexture(new Rect(x, y + 18, barWidth * globalAlertLevel, barHeight), Texture2D.whiteTexture);

        GUI.color = Color.white;
        GUI.Label(new Rect(x + 5, y + 20, barWidth, barHeight), $"{globalAlertLevel:P0}");
    }

    #endregion
}

/// <summary>
/// Interface for objects that can receive alerts
/// </summary>
public interface IAlertListener
{
    void OnAlertReceived(AlertSystem.AlertData alert);
    Vector3 GetPosition();
    bool IsInCombat();
}
