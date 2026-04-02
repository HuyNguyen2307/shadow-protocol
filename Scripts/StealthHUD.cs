using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// STEALTH HUD - Player Detection Feedback
/// ═══════════════════════════════════════════════════════════════════════════════
/// 
/// Displays critical stealth information to the player:
/// - Detection meter (how close to being spotted)
/// - Threat direction indicators
/// - Global alert level
/// - Enemy awareness states
/// 
/// DESIGN PRINCIPLES:
/// - Non-intrusive when safe
/// - Clear warning when in danger
/// - Directional awareness of threats
/// 
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class StealthHUD : MonoBehaviour
{
    #region ═══════════════════ SINGLETON ═══════════════════

    public static StealthHUD Instance { get; private set; }

    #endregion

    #region ═══════════════════ SETTINGS ═══════════════════

    [Header("═══ DETECTION METER ═══")]
    [SerializeField] private bool showDetectionMeter = true;
    [SerializeField] private Vector2 meterPosition = new Vector2(20, 20);
    [SerializeField] private Vector2 meterSize = new Vector2(200, 25);
    [SerializeField] private float meterFadeSpeed = 3f;

    [Header("═══ THREAT INDICATOR ═══")]
    [SerializeField] private bool showThreatIndicator = true;
    [SerializeField] private float indicatorRadius = 100f;
    [SerializeField] private float indicatorSize = 30f;

    [Header("═══ COLORS ═══")]
    [SerializeField] private Color safeColor = new Color(0.2f, 0.8f, 0.2f, 0.8f);
    [SerializeField] private Color cautionColor = new Color(1f, 0.8f, 0f, 0.9f);
    [SerializeField] private Color dangerColor = new Color(1f, 0.3f, 0.1f, 1f);
    [SerializeField] private Color combatColor = new Color(1f, 0f, 0f, 1f);

    [Header("═══ VIGNETTE EFFECT ═══")]
    [SerializeField] private bool showVignette = true;
    [SerializeField] private float vignetteMaxAlpha = 0.4f;

    [Header("═══ REFERENCES ═══")]
    [SerializeField] private Transform player;

    #endregion

    #region ═══════════════════ PRIVATE FIELDS ═══════════════════

    // Detection tracking
    private float maxDetection = 0f;
    private float displayedDetection = 0f;
    private EnemyAI_Advanced mostDangerousEnemy;
    private List<ThreatInfo> activeThreats = new List<ThreatInfo>();

    // UI state
    private float meterAlpha = 0f;
    private float lastThreatTime;

    // Textures
    private Texture2D whiteTex;
    private Texture2D vignetteTex;
    private Texture2D arrowTex;

    // Cache
    private Camera mainCamera;
    private EnemyAI_Advanced[] allEnemies;
    private float lastEnemyScanTime;

    #endregion

    #region ═══════════════════ STRUCTS ═══════════════════

    private struct ThreatInfo
    {
        public Vector3 Position;
        public float Detection;
        public EnemyAI_Advanced.AIState State;
        public float Angle;
    }

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

        CreateTextures();
    }

    private void Start()
    {
        mainCamera = Camera.main;
        
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                player = playerObj.transform;
        }

        // Initial enemy scan
        ScanForEnemies();
    }

    private void Update()
    {
        // Periodically rescan for enemies
        if (Time.time - lastEnemyScanTime > 2f)
        {
            ScanForEnemies();
            lastEnemyScanTime = Time.time;
        }

        UpdateDetectionTracking();
        UpdateMeterVisibility();
    }

    private void OnDestroy()
    {
        if (whiteTex != null) Destroy(whiteTex);
        if (vignetteTex != null) Destroy(vignetteTex);
        if (arrowTex != null) Destroy(arrowTex);
    }

    #endregion

    #region ═══════════════════ SETUP ═══════════════════

    private void CreateTextures()
    {
        // White texture for UI elements
        whiteTex = new Texture2D(1, 1);
        whiteTex.SetPixel(0, 0, Color.white);
        whiteTex.Apply();

        // Vignette texture (radial gradient)
        int vigSize = 256;
        vignetteTex = new Texture2D(vigSize, vigSize);
        Vector2 center = new Vector2(vigSize / 2f, vigSize / 2f);
        
        for (int y = 0; y < vigSize; y++)
        {
            for (int x = 0; x < vigSize; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / (vigSize / 2f);
                float alpha = Mathf.Clamp01(dist * dist);
                vignetteTex.SetPixel(x, y, new Color(0, 0, 0, alpha));
            }
        }
        vignetteTex.Apply();

        // Arrow texture for threat indicator
        int arrowSize = 32;
        arrowTex = new Texture2D(arrowSize, arrowSize);
        
        for (int y = 0; y < arrowSize; y++)
        {
            for (int x = 0; x < arrowSize; x++)
            {
                // Simple triangle arrow pointing up
                float nx = (x - arrowSize / 2f) / (arrowSize / 2f);
                float ny = (y - arrowSize / 2f) / (arrowSize / 2f);
                
                bool inArrow = ny > -0.5f && Mathf.Abs(nx) < (0.8f - ny) * 0.6f;
                arrowTex.SetPixel(x, y, inArrow ? Color.white : Color.clear);
            }
        }
        arrowTex.Apply();
    }

    private void ScanForEnemies()
    {
        allEnemies = FindObjectsOfType<EnemyAI_Advanced>();
    }

    #endregion

    #region ═══════════════════ DETECTION TRACKING ═══════════════════

    private void UpdateDetectionTracking()
    {
        if (allEnemies == null || player == null) return;

        maxDetection = 0f;
        mostDangerousEnemy = null;
        activeThreats.Clear();

        foreach (var enemy in allEnemies)
        {
            if (enemy == null || !enemy.enabled) continue;

            // Get detection level from VisionSensor
            VisionSensor vision = enemy.GetComponent<VisionSensor>();
            HearingSensor hearing = enemy.GetComponent<HearingSensor>();
            
            float detection = 0f;
            
            if (vision != null)
            {
                detection = vision.DetectionMeter;
            }

            // Add hearing suspicion
            if (hearing != null)
            {
                detection = Mathf.Max(detection, hearing.SuspicionMeter * 0.5f);
            }

            // Boost if enemy is in dangerous state
            var state = enemy.State;
            if (state == EnemyAI_Advanced.AIState.CHASE)
            {
                detection = 1f;
            }
            else if (state == EnemyAI_Advanced.AIState.SEARCH || 
                     state == EnemyAI_Advanced.AIState.RESPOND_ALERT)
            {
                detection = Mathf.Max(detection, 0.7f);
            }

            // Track most dangerous
            if (detection > maxDetection)
            {
                maxDetection = detection;
                mostDangerousEnemy = enemy;
            }

            // Add to threats if significant
            if (detection > 0.1f)
            {
                Vector3 dirToEnemy = enemy.transform.position - player.position;
                float angle = Mathf.Atan2(dirToEnemy.x, dirToEnemy.z) * Mathf.Rad2Deg;
                
                // Adjust for camera rotation
                if (mainCamera != null)
                {
                    angle -= mainCamera.transform.eulerAngles.y;
                }

                activeThreats.Add(new ThreatInfo
                {
                    Position = enemy.transform.position,
                    Detection = detection,
                    State = state,
                    Angle = angle
                });

                lastThreatTime = Time.time;
            }
        }

        // Smooth displayed detection
        displayedDetection = Mathf.Lerp(displayedDetection, maxDetection, Time.deltaTime * 8f);
    }

    private void UpdateMeterVisibility()
    {
        // Show meter when there's detection or recently had threats
        bool shouldShow = maxDetection > 0.05f || Time.time - lastThreatTime < 2f;
        
        float targetAlpha = shouldShow ? 1f : 0f;
        meterAlpha = Mathf.Lerp(meterAlpha, targetAlpha, Time.deltaTime * meterFadeSpeed);
    }

    #endregion

    #region ═══════════════════ GUI RENDERING ═══════════════════

    private void OnGUI()
    {
        if (player == null) return;

        // Danger vignette
        if (showVignette && displayedDetection > 0.1f)
        {
            DrawVignette();
        }

        // Detection meter
        if (showDetectionMeter && meterAlpha > 0.01f)
        {
            DrawDetectionMeter();
        }

        // Threat indicators
        if (showThreatIndicator && activeThreats.Count > 0)
        {
            DrawThreatIndicators();
        }

        // State warning text
        if (maxDetection >= 1f)
        {
            DrawCombatWarning();
        }
    }

    private void DrawVignette()
    {
        float alpha = displayedDetection * vignetteMaxAlpha;
        Color vigColor = Color.Lerp(dangerColor, combatColor, displayedDetection);
        vigColor.a = alpha;
        
        GUI.color = vigColor;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vignetteTex);
        GUI.color = Color.white;
    }

    private void DrawDetectionMeter()
    {
        float x = meterPosition.x;
        float y = Screen.height - meterPosition.y - meterSize.y;

        // Apply alpha
        Color bgColor = new Color(0, 0, 0, 0.6f * meterAlpha);
        Color fillColor = GetDetectionColor();
        fillColor.a *= meterAlpha;

        // Background
        GUI.color = bgColor;
        GUI.DrawTexture(new Rect(x - 2, y - 2, meterSize.x + 4, meterSize.y + 4), whiteTex);

        // Border
        GUI.color = new Color(0.3f, 0.3f, 0.3f, meterAlpha);
        GUI.DrawTexture(new Rect(x, y, meterSize.x, meterSize.y), whiteTex);

        // Fill
        GUI.color = fillColor;
        float fillWidth = meterSize.x * displayedDetection;
        GUI.DrawTexture(new Rect(x, y, fillWidth, meterSize.y), whiteTex);

        // Detection percentage text
        GUI.color = new Color(1, 1, 1, meterAlpha);
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.alignment = TextAnchor.MiddleCenter;
        style.fontStyle = FontStyle.Bold;
        style.fontSize = 14;
        
        string text = displayedDetection >= 1f ? "DETECTED!" : $"{displayedDetection:P0}";
        GUI.Label(new Rect(x, y, meterSize.x, meterSize.y), text, style);

        // Label
        style.alignment = TextAnchor.MiddleLeft;
        style.fontSize = 12;
        style.fontStyle = FontStyle.Normal;
        GUI.Label(new Rect(x, y - 20, meterSize.x, 20), "DETECTION", style);

        // State indicator
        if (mostDangerousEnemy != null)
        {
            style.alignment = TextAnchor.MiddleRight;
            style.fontSize = 11;
            GUI.color = GetStateColor(mostDangerousEnemy.State);
            GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, meterAlpha);
            GUI.Label(new Rect(x, y - 20, meterSize.x, 20), mostDangerousEnemy.State.ToString(), style);
        }

        GUI.color = Color.white;
    }

    private void DrawThreatIndicators()
    {
        Vector2 center = new Vector2(Screen.width / 2f, Screen.height / 2f);

        foreach (var threat in activeThreats)
        {
            // Calculate position on screen edge
            float angle = threat.Angle * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
            Vector2 indicatorPos = center + direction * indicatorRadius;

            // Size based on threat level
            float size = indicatorSize * (0.5f + threat.Detection * 0.5f);

            // Color based on state
            Color color = GetStateColor(threat.State);
            color.a = 0.3f + threat.Detection * 0.7f;

            // Draw rotated arrow
            Matrix4x4 matrixBackup = GUI.matrix;
            GUIUtility.RotateAroundPivot(-threat.Angle, indicatorPos);
            
            GUI.color = color;
            GUI.DrawTexture(new Rect(indicatorPos.x - size/2, indicatorPos.y - size/2, size, size), arrowTex);
            
            GUI.matrix = matrixBackup;
        }

        GUI.color = Color.white;
    }

    private void DrawCombatWarning()
    {
        // Flashing "DETECTED" warning
        float flash = Mathf.Sin(Time.time * 8f) * 0.5f + 0.5f;
        
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.alignment = TextAnchor.MiddleCenter;
        style.fontSize = 36;
        style.fontStyle = FontStyle.Bold;

        // Shadow
        GUI.color = new Color(0, 0, 0, 0.8f);
        GUI.Label(new Rect(2, 52, Screen.width, 50), "⚠ DETECTED! ⚠", style);

        // Main text
        GUI.color = new Color(1f, 0.2f, 0.1f, 0.7f + flash * 0.3f);
        GUI.Label(new Rect(0, 50, Screen.width, 50), "⚠ DETECTED! ⚠", style);

        GUI.color = Color.white;
    }

    #endregion

    #region ═══════════════════ HELPERS ═══════════════════

    private Color GetDetectionColor()
    {
        if (displayedDetection >= 1f)
            return combatColor;
        if (displayedDetection >= 0.6f)
            return Color.Lerp(dangerColor, combatColor, (displayedDetection - 0.6f) / 0.4f);
        if (displayedDetection >= 0.3f)
            return Color.Lerp(cautionColor, dangerColor, (displayedDetection - 0.3f) / 0.3f);
        
        return Color.Lerp(safeColor, cautionColor, displayedDetection / 0.3f);
    }

    private Color GetStateColor(EnemyAI_Advanced.AIState state)
    {
        switch (state)
        {
            case EnemyAI_Advanced.AIState.PATROL:
                return safeColor;
            case EnemyAI_Advanced.AIState.INVESTIGATE:
            case EnemyAI_Advanced.AIState.SUSPICIOUS:
                return cautionColor;
            case EnemyAI_Advanced.AIState.SEARCH:
            case EnemyAI_Advanced.AIState.RESPOND_ALERT:
                return dangerColor;
            case EnemyAI_Advanced.AIState.CHASE:
                return combatColor;
            default:
                return Color.gray;
        }
    }

    #endregion

    #region ═══════════════════ PUBLIC METHODS ═══════════════════

    /// <summary>
    /// Get current max detection level (0-1)
    /// </summary>
    public float GetDetectionLevel()
    {
        return maxDetection;
    }

    /// <summary>
    /// Check if player is fully detected
    /// </summary>
    public bool IsDetected()
    {
        return maxDetection >= 1f;
    }

    /// <summary>
    /// Get number of active threats
    /// </summary>
    public int GetThreatCount()
    {
        return activeThreats.Count;
    }

    #endregion
}
