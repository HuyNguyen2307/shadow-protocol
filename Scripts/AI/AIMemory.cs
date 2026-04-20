using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stores and manages the AI's memory of player sightings and sounds.
/// Entries decay in importance over time, allowing the FSM to naturally
/// de-prioritize stale intel rather than abruptly forgetting it.
/// </summary>
public class AIMemory : MonoBehaviour
{
    #region Inspector Settings

    [Header("Memory Settings")]
    [Tooltip("How long memories last (seconds)")]
    [SerializeField] private float memoryDuration = 60f;

    [Tooltip("Maximum number of memories to retain at once")]
    [SerializeField] private int _capacity = 20;

    [Tooltip("Rate at which memory importance falls per second")]
    [SerializeField] private float _importanceDecayRate = 0.1f;

    [Tooltip("Spatial radius within which two memories of the same type are merged")]
    [SerializeField] private float _mergeRadius = 2f;

    [Header("Behavior Modification")]
    [Tooltip("Extra search time awarded when the AI revisits a known hotspot")]
    [SerializeField] private float hotspotSearchBonus = 3f;

    [Tooltip("Minimum importance for a position to be treated as a hotspot")]
    [Range(0f, 1f)]
    [SerializeField] private float hotspotThreshold = 0.3f;

    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private bool logMemoryEvents = false;

    #endregion

    #region Private Fields

    private List<MemoryEntry> _memories = new List<MemoryEntry>();
    private MemoryEntry _lastSeenMemory;
    private MemoryEntry _lastHeardMemory;

    #endregion

    #region Nested Types

    /// <summary>
    /// A single remembered event: where something happened, how important it still is,
    /// and when it was recorded.
    /// </summary>
    [System.Serializable]
    public class MemoryEntry
    {
        public Vector3 Position;
        public float Timestamp;
        public MemoryType Type;

        // Importance ranges 0–1 and decays each frame so stale memories
        // fade out gradually rather than disappearing at a hard cutoff.
        public float Importance;

        public MemoryEntry(Vector3 position, MemoryType type, float importance = 1f)
        {
            Position  = position;
            Timestamp = Time.time;
            Type      = type;
            Importance = importance;
        }

        public float Age => Time.time - Timestamp;
    }

    public enum MemoryType
    {
        LastSeen,       // Directly saw player
        LastHeard,      // Heard player
        SearchedArea,   // Already searched this spot
        SuspiciousArea  // Something suspicious happened here
    }

    #endregion

    #region Public Properties

    public List<MemoryEntry> AllMemories      => _memories;
    public int               MemoryCount      => _memories.Count;

    public Vector3 LastSeenPosition   => _lastSeenMemory?.Position ?? Vector3.zero;
    public float   TimeSinceLastSeen  => _lastSeenMemory  != null ? _lastSeenMemory.Age  : float.MaxValue;
    public bool    HasSeenPlayer      => _lastSeenMemory  != null && _lastSeenMemory.Age  < memoryDuration;

    public Vector3 LastHeardPosition  => _lastHeardMemory?.Position ?? Vector3.zero;
    public float   TimeSinceLastHeard => _lastHeardMemory != null ? _lastHeardMemory.Age : float.MaxValue;
    public bool    HasHeardPlayer     => _lastHeardMemory != null && _lastHeardMemory.Age < memoryDuration;

    #endregion

    #region Events

    public event Action<MemoryEntry> OnMemoryAdded;
    public event Action<MemoryEntry> OnMemoryExpired;

    #endregion

    #region Unity Lifecycle

    private void Update()
    {
        DecayAndPruneMemories();
    }

    #endregion

    #region Memory Management

    // Importance decays gradually each frame so the AI deprioritizes stale intel
    // smoothly, producing better behavior transitions than a hard-timeout removal.
    private void DecayAndPruneMemories()
    {
        for (int i = _memories.Count - 1; i >= 0; i--)
        {
            MemoryEntry memory = _memories[i];
            memory.Importance -= _importanceDecayRate * Time.deltaTime;

            if (memory.Age > memoryDuration || memory.Importance <= 0f)
            {
                OnMemoryExpired?.Invoke(memory);
                _memories.RemoveAt(i);

                if (logMemoryEvents)
                    Debug.Log($"[AIMemory] Memory expired: {memory.Type} at {memory.Position}");
            }
        }

        if (_lastSeenMemory  != null && _lastSeenMemory.Age  > memoryDuration) _lastSeenMemory  = null;
        if (_lastHeardMemory != null && _lastHeardMemory.Age > memoryDuration) _lastHeardMemory = null;
    }

    /// <summary>
    /// Records a new memory at <paramref name="position"/> or reinforces a
    /// nearby existing memory of the same type.
    /// </summary>
    /// <param name="position">World-space location of the event.</param>
    /// <param name="type">Category of the memory.</param>
    /// <param name="importance">Initial importance in the range 0–1.</param>
    public void AddMemory(Vector3 position, MemoryType type, float importance = 1f)
    {
        MemoryEntry nearby = FindNearbyMemory(position, type);

        if (nearby != null)
            MergeIntoExisting(nearby, position, importance);
        else
            CreateNewMemory(position, type, importance);
    }

    /// <summary>Remember where the player was last seen.</summary>
    public void RememberPlayerSeen(Vector3 position)   => AddMemory(position, MemoryType.LastSeen,  1.0f);

    /// <summary>Remember where the player was heard.</summary>
    public void RememberPlayerHeard(Vector3 position)  => AddMemory(position, MemoryType.LastHeard, 0.7f);

    /// <summary>Mark a position as already searched, lowering its future priority.</summary>
    public void MarkAsSearched(Vector3 position)       => AddMemory(position, MemoryType.SearchedArea, 0.3f);

    /// <summary>Discard all stored memories.</summary>
    public void ClearMemories()
    {
        _memories.Clear();
        _lastSeenMemory  = null;
        _lastHeardMemory = null;
    }

    #endregion

    #region Queries

    /// <summary>
    /// Returns all memories of the specified type.
    /// </summary>
    public List<MemoryEntry> GetMemoriesOfType(MemoryType type)
        => _memories.FindAll(m => m.Type == type);

    /// <summary>
    /// Returns the single most important memory, or <c>null</c> when there are none.
    /// O(n) scan is intentional: enemy memory is capped at <c>_capacity</c> entries,
    /// so the list is always small and a sorted structure would add needless overhead.
    /// The FSM only ever needs the one most-actionable entry to choose a search
    /// destination, making this the primary query method.
    /// </summary>
    public MemoryEntry GetMostImportantMemory()
    {
        MemoryEntry best      = null;
        float       bestScore = float.MinValue;

        foreach (MemoryEntry memory in _memories)
        {
            if (memory.Importance > bestScore)
            {
                bestScore = memory.Importance;
                best      = memory;
            }
        }

        return best;
    }

    /// <summary>
    /// Returns <c>true</c> when a high-importance <see cref="MemoryType.LastSeen"/>
    /// entry exists within <paramref name="radius"/> of <paramref name="position"/>.
    /// </summary>
    public bool IsHotspot(Vector3 position, float radius = 5f)
    {
        foreach (MemoryEntry memory in _memories)
        {
            if (memory.Type      == MemoryType.LastSeen
                && memory.Importance >= hotspotThreshold
                && Vector3.Distance(memory.Position, position) <= radius)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when a <see cref="MemoryType.SearchedArea"/> entry
    /// exists within <paramref name="radius"/> of <paramref name="position"/>.
    /// </summary>
    public bool WasRecentlySearched(Vector3 position, float radius = 3f)
    {
        foreach (MemoryEntry memory in _memories)
        {
            if (memory.Type == MemoryType.SearchedArea
                && Vector3.Distance(memory.Position, position) <= radius)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns extra search time when the AI revisits a known hotspot,
    /// encouraging thorough coverage of areas where the player was previously seen.
    /// </summary>
    public float GetSearchDurationBonus(Vector3 position)
        => IsHotspot(position) ? hotspotSearchBonus : 0f;

    #endregion

    #region Helpers

    // Returns the first existing memory of the same type within _mergeRadius,
    // or null if none qualifies.
    private MemoryEntry FindNearbyMemory(Vector3 position, MemoryType type)
    {
        foreach (MemoryEntry memory in _memories)
        {
            if (memory.Type == type
                && Vector3.Distance(memory.Position, position) < _mergeRadius)
            {
                return memory;
            }
        }
        return null;
    }

    // Merging nearby memories prevents the AI from treating the same physical
    // location as multiple independent high-priority targets, which would cause
    // the FSM to oscillate between near-identical positions.
    private void MergeIntoExisting(MemoryEntry existing, Vector3 position, float importance)
    {
        existing.Importance = Mathf.Min(1f, existing.Importance + importance * 0.5f);
        existing.Timestamp  = Time.time;
        existing.Position   = position;

        UpdateSpecialReferences(existing);
    }

    private void CreateNewMemory(Vector3 position, MemoryType type, float importance)
    {
        MemoryEntry newMemory = new MemoryEntry(position, type, importance);
        _memories.Add(newMemory);

        UpdateSpecialReferences(newMemory);
        EvictIfOverCapacity();

        OnMemoryAdded?.Invoke(newMemory);

        if (logMemoryEvents)
            Debug.Log($"[AIMemory] New memory: {type} at {position}, importance: {importance:F2}");
    }

    // Eviction targets the lowest-scoring entry rather than the oldest because
    // recency is already encoded in each entry's decayed Importance value;
    // an old memory that still carries high importance is more useful than a
    // recent one that was low-importance to begin with.
    private void EvictIfOverCapacity()
    {
        while (_memories.Count > _capacity)
        {
            int   evictIndex = 0;
            float lowestScore = float.MaxValue;

            for (int i = 0; i < _memories.Count; i++)
            {
                float score = _memories[i].Importance / (1f + _memories[i].Age * 0.1f);
                if (score < lowestScore)
                {
                    lowestScore  = score;
                    evictIndex = i;
                }
            }

            _memories.RemoveAt(evictIndex);
        }
    }

    private void UpdateSpecialReferences(MemoryEntry memory)
    {
        if      (memory.Type == MemoryType.LastSeen)  _lastSeenMemory  = memory;
        else if (memory.Type == MemoryType.LastHeard) _lastHeardMemory = memory;
    }

    #endregion

    #region Debug

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || !Application.isPlaying) return;

        foreach (MemoryEntry memory in _memories)
        {
            Color color;
            float size;

            switch (memory.Type)
            {
                case MemoryType.LastSeen:
                    color = Color.red;
                    size  = 0.5f;
                    break;
                case MemoryType.LastHeard:
                    color = new Color(1f, 0.5f, 0f);
                    size  = 0.4f;
                    break;
                case MemoryType.SearchedArea:
                    color = Color.gray;
                    size  = 0.3f;
                    break;
                default:
                    color = Color.yellow;
                    size  = 0.35f;
                    break;
            }

            color.a = memory.Importance;

            Gizmos.color = color;
            Gizmos.DrawSphere(memory.Position, size * memory.Importance);
            Gizmos.DrawWireSphere(memory.Position, size);

            Gizmos.color = new Color(color.r, color.g, color.b, 0.2f);
            Gizmos.DrawLine(transform.position, memory.Position);
        }
    }

    #endregion
}
