using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// AI MEMORY SYSTEM - Remembers Player Encounters
/// ═══════════════════════════════════════════════════════════════════════════════
/// 
/// Stores and manages AI's memory of player sightings and sounds.
/// Creates more intelligent and believable AI behavior.
/// 
/// FEATURES:
/// - Remember last seen positions (fades over time)
/// - Remember patrol areas where player was spotted
/// - Heat map of player activity
/// - Persistent memory across searches
/// 
/// ACADEMIC RELEVANCE:
/// - Believable AI through memory
/// - Emergent patrol modification
/// - Player profiling and prediction
/// 
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class AIMemory : MonoBehaviour
{
    #region ═══════════════════ MEMORY ENTRY ═══════════════════

    [System.Serializable]
    public class MemoryEntry
    {
        public Vector3 position;
        public float timestamp;
        public MemoryType type;
        public float importance;  // 0-1, decays over time
        
        public MemoryEntry(Vector3 pos, MemoryType memType, float imp = 1f)
        {
            position = pos;
            timestamp = Time.time;
            type = memType;
            importance = imp;
        }
        
        public float Age => Time.time - timestamp;
    }

    public enum MemoryType
    {
        LastSeen,       // Directly saw player
        LastHeard,      // Heard player
        SearchedArea,   // Already searched this spot
        SuspiciousArea  // Something suspicious happened here
    }

    #endregion

    #region ═══════════════════ SERIALIZED FIELDS ═══════════════════

    [Header("═══ MEMORY SETTINGS ═══")]
    [Tooltip("How long memories last (seconds)")]
    [SerializeField] private float memoryDuration = 60f;
    
    [Tooltip("Maximum memories to store")]
    [SerializeField] private int maxMemories = 20;
    
    [Tooltip("How fast memory importance decays")]
    [SerializeField] private float importanceDecayRate = 0.1f;
    
    [Tooltip("Minimum distance between memories (avoid duplicates)")]
    [SerializeField] private float memoryMergeDistance = 2f;

    [Header("═══ BEHAVIOR MODIFICATION ═══")]
    [Tooltip("Extra time to search areas where player was seen before")]
    [SerializeField] private float hotspotSearchBonus = 3f;
    
    [Tooltip("Importance threshold to consider area a 'hotspot'")]
    [Range(0f, 1f)]
    [SerializeField] private float hotspotThreshold = 0.3f;

    [Header("═══ DEBUG ═══")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private bool logMemoryEvents = false;

    #endregion

    #region ═══════════════════ PRIVATE FIELDS ═══════════════════

    private List<MemoryEntry> memories = new List<MemoryEntry>();
    private MemoryEntry lastSeenMemory;
    private MemoryEntry lastHeardMemory;

    #endregion

    #region ═══════════════════ PUBLIC PROPERTIES ═══════════════════

    public List<MemoryEntry> AllMemories => memories;
    public int MemoryCount => memories.Count;
    
    public Vector3 LastSeenPosition => lastSeenMemory?.position ?? Vector3.zero;
    public float TimeSinceLastSeen => lastSeenMemory != null ? lastSeenMemory.Age : float.MaxValue;
    public bool HasSeenPlayer => lastSeenMemory != null && lastSeenMemory.Age < memoryDuration;
    
    public Vector3 LastHeardPosition => lastHeardMemory?.position ?? Vector3.zero;
    public float TimeSinceLastHeard => lastHeardMemory != null ? lastHeardMemory.Age : float.MaxValue;
    public bool HasHeardPlayer => lastHeardMemory != null && lastHeardMemory.Age < memoryDuration;

    #endregion

    #region ═══════════════════ EVENTS ═══════════════════

    public event Action<MemoryEntry> OnMemoryAdded;
    public event Action<MemoryEntry> OnMemoryExpired;

    #endregion

    #region ═══════════════════ UNITY LIFECYCLE ═══════════════════

    private void Update()
    {
        UpdateMemories();
    }

    #endregion

    #region ═══════════════════ MEMORY MANAGEMENT ═══════════════════

    private void UpdateMemories()
    {
        // Decay importance and remove expired memories
        for (int i = memories.Count - 1; i >= 0; i--)
        {
            var memory = memories[i];
            
            // Decay importance
            memory.importance -= importanceDecayRate * Time.deltaTime;
            
            // Remove if expired or importance too low
            if (memory.Age > memoryDuration || memory.importance <= 0)
            {
                OnMemoryExpired?.Invoke(memory);
                memories.RemoveAt(i);
                
                if (logMemoryEvents)
                {
                    Debug.Log($"[AIMemory] Memory expired: {memory.type} at {memory.position}");
                }
            }
        }
        
        // Update last seen/heard references
        if (lastSeenMemory != null && lastSeenMemory.Age > memoryDuration)
        {
            lastSeenMemory = null;
        }
        if (lastHeardMemory != null && lastHeardMemory.Age > memoryDuration)
        {
            lastHeardMemory = null;
        }
    }

    /// <summary>
    /// Add a new memory or reinforce existing nearby memory.
    /// </summary>
    public void AddMemory(Vector3 position, MemoryType type, float importance = 1f)
    {
        // Check for nearby existing memory of same type
        for (int i = 0; i < memories.Count; i++)
        {
            if (memories[i].type == type && 
                Vector3.Distance(memories[i].position, position) < memoryMergeDistance)
            {
                // Reinforce existing memory
                memories[i].importance = Mathf.Min(1f, memories[i].importance + importance * 0.5f);
                memories[i].timestamp = Time.time;
                memories[i].position = position; // Update to more recent position
                
                UpdateSpecialReferences(memories[i]);
                return;
            }
        }
        
        // Create new memory
        var newMemory = new MemoryEntry(position, type, importance);
        memories.Add(newMemory);
        
        UpdateSpecialReferences(newMemory);
        
        // Enforce max memories
        while (memories.Count > maxMemories)
        {
            // Remove oldest, least important memory
            int removeIndex = 0;
            float lowestScore = float.MaxValue;
            
            for (int i = 0; i < memories.Count; i++)
            {
                float score = memories[i].importance / (1f + memories[i].Age * 0.1f);
                if (score < lowestScore)
                {
                    lowestScore = score;
                    removeIndex = i;
                }
            }
            
            memories.RemoveAt(removeIndex);
        }
        
        OnMemoryAdded?.Invoke(newMemory);
        
        if (logMemoryEvents)
        {
            Debug.Log($"[AIMemory] New memory: {type} at {position}, importance: {importance:F2}");
        }
    }

    private void UpdateSpecialReferences(MemoryEntry memory)
    {
        if (memory.type == MemoryType.LastSeen)
        {
            lastSeenMemory = memory;
        }
        else if (memory.type == MemoryType.LastHeard)
        {
            lastHeardMemory = memory;
        }
    }

    /// <summary>
    /// Remember where player was last seen.
    /// </summary>
    public void RememberPlayerSeen(Vector3 position)
    {
        AddMemory(position, MemoryType.LastSeen, 1f);
    }

    /// <summary>
    /// Remember where player was heard.
    /// </summary>
    public void RememberPlayerHeard(Vector3 position)
    {
        AddMemory(position, MemoryType.LastHeard, 0.7f);
    }

    /// <summary>
    /// Mark an area as searched (lower priority for future searches).
    /// </summary>
    public void MarkAsSearched(Vector3 position)
    {
        AddMemory(position, MemoryType.SearchedArea, 0.3f);
    }

    /// <summary>
    /// Clear all memories.
    /// </summary>
    public void ClearMemories()
    {
        memories.Clear();
        lastSeenMemory = null;
        lastHeardMemory = null;
    }

    #endregion

    #region ═══════════════════ QUERY METHODS ═══════════════════

    /// <summary>
    /// Get all memories of a specific type.
    /// </summary>
    public List<MemoryEntry> GetMemoriesOfType(MemoryType type)
    {
        return memories.FindAll(m => m.type == type);
    }

    /// <summary>
    /// Get memories near a position.
    /// </summary>
    public List<MemoryEntry> GetMemoriesNear(Vector3 position, float radius)
    {
        return memories.FindAll(m => Vector3.Distance(m.position, position) <= radius);
    }

    /// <summary>
    /// Check if an area is a hotspot (player seen here before).
    /// </summary>
    public bool IsHotspot(Vector3 position, float radius = 5f)
    {
        foreach (var memory in memories)
        {
            if (memory.type == MemoryType.LastSeen && 
                memory.importance >= hotspotThreshold &&
                Vector3.Distance(memory.position, position) <= radius)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Get the most important unsearched position.
    /// </summary>
    public Vector3 GetBestSearchPosition()
    {
        MemoryEntry best = null;
        float bestScore = 0f;
        
        foreach (var memory in memories)
        {
            // Skip searched areas
            if (memory.type == MemoryType.SearchedArea) continue;
            
            float score = memory.importance;
            
            // Prioritize recent sightings
            if (memory.type == MemoryType.LastSeen)
            {
                score *= 2f;
            }
            
            if (score > bestScore)
            {
                bestScore = score;
                best = memory;
            }
        }
        
        return best?.position ?? Vector3.zero;
    }

    /// <summary>
    /// Check if position was recently searched.
    /// </summary>
    public bool WasRecentlySearched(Vector3 position, float radius = 3f)
    {
        foreach (var memory in memories)
        {
            if (memory.type == MemoryType.SearchedArea &&
                Vector3.Distance(memory.position, position) <= radius)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Get suggested search duration based on area importance.
    /// </summary>
    public float GetSearchDurationBonus(Vector3 position)
    {
        if (IsHotspot(position))
        {
            return hotspotSearchBonus;
        }
        return 0f;
    }

    #endregion

    #region ═══════════════════ DEBUG ═══════════════════

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || !Application.isPlaying) return;
        
        foreach (var memory in memories)
        {
            // Color based on type
            Color color;
            float size;
            
            switch (memory.type)
            {
                case MemoryType.LastSeen:
                    color = Color.red;
                    size = 0.5f;
                    break;
                case MemoryType.LastHeard:
                    color = new Color(1f, 0.5f, 0f); // Orange
                    size = 0.4f;
                    break;
                case MemoryType.SearchedArea:
                    color = Color.gray;
                    size = 0.3f;
                    break;
                default:
                    color = Color.yellow;
                    size = 0.35f;
                    break;
            }
            
            // Fade based on importance
            color.a = memory.importance;
            
            Gizmos.color = color;
            Gizmos.DrawSphere(memory.position, size * memory.importance);
            Gizmos.DrawWireSphere(memory.position, size);
            
            // Draw line from AI to memory
            Gizmos.color = new Color(color.r, color.g, color.b, 0.2f);
            Gizmos.DrawLine(transform.position, memory.position);
        }
    }

    #endregion
}
