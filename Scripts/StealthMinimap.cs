using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// STEALTH MINIMAP - Top-down tactical view
/// ═══════════════════════════════════════════════════════════════════════════════
/// 
/// Shows:
/// - Player position and direction
/// - Enemy positions and states
/// - Vision cones (optional)
/// - Alert status
/// 
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class StealthMinimap : MonoBehaviour
{
    #region ═══════════════════ SETTINGS ═══════════════════

    [Header("═══ POSITION & SIZE ═══")]
    [SerializeField] private Vector2 position = new Vector2(20, 20);
    [SerializeField] private float size = 180f;
    [SerializeField] private float zoom = 15f; // Units to show

    [Header("═══ APPEARANCE ═══")]
    [SerializeField] private Color backgroundColor = new Color(0, 0, 0, 0.7f);
    [SerializeField] private Color borderColor = new Color(0.3f, 0.3f, 0.3f, 0.8f);
    [SerializeField] private Color playerColor = Color.cyan;
    [SerializeField] private float playerSize = 10f;

    [Header("═══ ENEMY DISPLAY ═══")]
    [SerializeField] private bool showEnemies = true;
    [SerializeField] private bool showVisionCones = true;
    [SerializeField] private float enemySize = 8f;
    [SerializeField] private float visionConeAlpha = 0.2f;

    [Header("═══ REFERENCES ═══")]
    [SerializeField] private Transform player;

    #endregion

    #region ═══════════════════ PRIVATE FIELDS ═══════════════════

    private Texture2D whiteTex;
    private Texture2D circleTex;
    private EnemyAI_Advanced[] enemies;
    private float lastScanTime;

    #endregion

    #region ═══════════════════ UNITY LIFECYCLE ═══════════════════

    private void Start()
    {
        CreateTextures();
        
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                player = playerObj.transform;
        }

        ScanEnemies();
    }

    private void Update()
    {
        if (Time.time - lastScanTime > 1f)
        {
            ScanEnemies();
            lastScanTime = Time.time;
        }
    }

    private void OnDestroy()
    {
        if (whiteTex != null) Destroy(whiteTex);
        if (circleTex != null) Destroy(circleTex);
    }

    #endregion

    #region ═══════════════════ SETUP ═══════════════════

    private void CreateTextures()
    {
        whiteTex = new Texture2D(1, 1);
        whiteTex.SetPixel(0, 0, Color.white);
        whiteTex.Apply();

        // Circle texture
        int circleSize = 32;
        circleTex = new Texture2D(circleSize, circleSize);
        Vector2 center = new Vector2(circleSize / 2f, circleSize / 2f);
        
        for (int y = 0; y < circleSize; y++)
        {
            for (int x = 0; x < circleSize; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float alpha = dist < circleSize / 2f - 1 ? 1f : 0f;
                circleTex.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        }
        circleTex.Apply();
    }

    private void ScanEnemies()
    {
        enemies = FindObjectsOfType<EnemyAI_Advanced>();
    }

    #endregion

    #region ═══════════════════ GUI ═══════════════════

    private void OnGUI()
    {
        if (player == null) return;

        float x = Screen.width - position.x - size;
        float y = position.y;
        Vector2 center = new Vector2(x + size / 2f, y + size / 2f);

        // Background
        GUI.color = backgroundColor;
        DrawCircle(center, size / 2f);

        // Border
        GUI.color = borderColor;
        DrawCircleOutline(center, size / 2f, 2f);

        // Clip to minimap area
        GUI.BeginGroup(new Rect(x, y, size, size));

        // Draw enemies
        if (showEnemies && enemies != null)
        {
            foreach (var enemy in enemies)
            {
                if (enemy == null) continue;
                DrawEnemy(enemy, new Vector2(size / 2f, size / 2f));
            }
        }

        // Draw player
        DrawPlayer(new Vector2(size / 2f, size / 2f));

        GUI.EndGroup();

        // North indicator
        GUI.color = Color.white;
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.alignment = TextAnchor.MiddleCenter;
        style.fontStyle = FontStyle.Bold;
        style.fontSize = 12;
        GUI.Label(new Rect(x + size / 2 - 10, y - 18, 20, 20), "N", style);

        GUI.color = Color.white;
    }

    private void DrawPlayer(Vector2 minimapCenter)
    {
        // Player is always at center
        GUI.color = playerColor;
        
        // Draw player dot
        GUI.DrawTexture(new Rect(
            minimapCenter.x - playerSize / 2f,
            minimapCenter.y - playerSize / 2f,
            playerSize,
            playerSize
        ), circleTex);

        // Draw direction indicator
        float angle = player.eulerAngles.y * Mathf.Deg2Rad;
        Vector2 forward = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * playerSize;
        
        DrawLine(minimapCenter, minimapCenter + forward, playerColor, 2f);
    }

    private void DrawEnemy(EnemyAI_Advanced enemy, Vector2 minimapCenter)
    {
        // Calculate relative position
        Vector3 relativePos = enemy.transform.position - player.position;
        float mapX = relativePos.x / zoom * (size / 2f);
        float mapY = -relativePos.z / zoom * (size / 2f); // Flip Z for top-down

        Vector2 enemyPos = minimapCenter + new Vector2(mapX, mapY);

        // Check if within minimap bounds
        if (Vector2.Distance(enemyPos, minimapCenter) > size / 2f - enemySize)
        {
            // Clamp to edge
            Vector2 dir = (enemyPos - minimapCenter).normalized;
            enemyPos = minimapCenter + dir * (size / 2f - enemySize);
        }

        // Get enemy state color
        Color color = GetEnemyColor(enemy.State);

        // Draw vision cone first (behind enemy dot)
        if (showVisionCones)
        {
            VisionSensor vision = enemy.GetComponent<VisionSensor>();
            if (vision != null)
            {
                DrawVisionCone(enemy, enemyPos, minimapCenter, color);
            }
        }

        // Draw enemy dot
        GUI.color = color;
        GUI.DrawTexture(new Rect(
            enemyPos.x - enemySize / 2f,
            enemyPos.y - enemySize / 2f,
            enemySize,
            enemySize
        ), circleTex);

        // Draw direction
        float angle = enemy.transform.eulerAngles.y * Mathf.Deg2Rad;
        Vector2 forward = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * enemySize * 0.8f;
        DrawLine(enemyPos, enemyPos + forward, color, 1.5f);
    }

    private void DrawVisionCone(EnemyAI_Advanced enemy, Vector2 enemyPos, Vector2 minimapCenter, Color color)
    {
        VisionSensor vision = enemy.GetComponent<VisionSensor>();
        if (vision == null) return;

        // Use correct property names from VisionSensor
        float visionRange = vision.range / zoom * (size / 2f);
        visionRange = Mathf.Min(visionRange, size / 2f);
        
        float fov = vision.fovAngle;
        float angle = enemy.transform.eulerAngles.y;

        // Simple cone approximation with lines
        color.a = visionConeAlpha;
        GUI.color = color;

        float leftAngle = (angle - fov / 2f) * Mathf.Deg2Rad;
        float rightAngle = (angle + fov / 2f) * Mathf.Deg2Rad;

        Vector2 leftDir = new Vector2(Mathf.Sin(leftAngle), -Mathf.Cos(leftAngle)) * visionRange;
        Vector2 rightDir = new Vector2(Mathf.Sin(rightAngle), -Mathf.Cos(rightAngle)) * visionRange;

        // Clamp to minimap bounds
        Vector2 leftEnd = ClampToMinimapCircle(enemyPos + leftDir, minimapCenter, size / 2f);
        Vector2 rightEnd = ClampToMinimapCircle(enemyPos + rightDir, minimapCenter, size / 2f);

        DrawLine(enemyPos, leftEnd, color, 1f);
        DrawLine(enemyPos, rightEnd, color, 1f);
    }

    #endregion

    #region ═══════════════════ HELPERS ═══════════════════

    private Color GetEnemyColor(EnemyAI_Advanced.AIState state)
    {
        switch (state)
        {
            case EnemyAI_Advanced.AIState.PATROL:
                return Color.green;
            case EnemyAI_Advanced.AIState.INVESTIGATE:
                return new Color(1f, 0.6f, 0f);
            case EnemyAI_Advanced.AIState.SUSPICIOUS:
                return Color.yellow;
            case EnemyAI_Advanced.AIState.CHASE:
                return Color.red;
            case EnemyAI_Advanced.AIState.SEARCH:
            case EnemyAI_Advanced.AIState.RESPOND_ALERT:
                return new Color(1f, 0.3f, 0.3f);
            default:
                return Color.gray;
        }
    }

    private void DrawCircle(Vector2 center, float radius)
    {
        GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), circleTex);
    }

    private void DrawCircleOutline(Vector2 center, float radius, float thickness)
    {
        int segments = 32;
        for (int i = 0; i < segments; i++)
        {
            float angle1 = (float)i / segments * Mathf.PI * 2;
            float angle2 = (float)(i + 1) / segments * Mathf.PI * 2;
            
            Vector2 p1 = center + new Vector2(Mathf.Cos(angle1), Mathf.Sin(angle1)) * radius;
            Vector2 p2 = center + new Vector2(Mathf.Cos(angle2), Mathf.Sin(angle2)) * radius;
            
            DrawLine(p1, p2, GUI.color, thickness);
        }
    }

    private void DrawLine(Vector2 p1, Vector2 p2, Color color, float thickness)
    {
        GUI.color = color;
        
        Vector2 delta = p2 - p1;
        float length = delta.magnitude;
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        Matrix4x4 backup = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, p1);
        GUI.DrawTexture(new Rect(p1.x, p1.y - thickness / 2f, length, thickness), whiteTex);
        GUI.matrix = backup;
    }

    private Vector2 ClampToMinimapCircle(Vector2 point, Vector2 center, float radius)
    {
        Vector2 dir = point - center;
        if (dir.magnitude > radius)
        {
            return center + dir.normalized * radius;
        }
        return point;
    }

    #endregion
}
