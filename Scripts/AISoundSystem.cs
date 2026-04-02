using UnityEngine;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// AI SOUND SYSTEM - Enemy voice lines and alert sounds
/// ═══════════════════════════════════════════════════════════════════════════════
/// 
/// FEATURES:
/// - State-based voice lines (patrol, alert, chase, etc.)
/// - Alert callouts when spotting player
/// - Investigation mumbles
/// - Search comments
/// 
/// SETUP:
/// 1. Attach to each enemy
/// 2. Assign AudioClips for each category
/// 3. Script auto-plays based on AI state
/// 
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AISoundSystem : MonoBehaviour
{
    #region ═══════════════════ SETTINGS ═══════════════════

    [Header("═══ DEBUG ═══")]
    [Tooltip("Show debug logs when sounds would play")]
    [SerializeField] private bool debugMode = true;

    [Header("═══ DETECTION SOUNDS ═══")]
    [Tooltip("Quick reaction when first noticing something")]
    [SerializeField] private AudioClip[] noticeSounds;        // "Huh?" "What was that?"
    
    [Tooltip("When fully spotting the player")]
    [SerializeField] private AudioClip[] spottedSounds;       // "There you are!" "Intruder!"
    
    [Tooltip("Alerting other guards")]
    [SerializeField] private AudioClip[] alertSounds;         // "Over here!" "We have contact!"

    [Header("═══ INVESTIGATION SOUNDS ═══")]
    [Tooltip("While investigating a sound/disturbance")]
    [SerializeField] private AudioClip[] investigateSounds;   // "I'll check it out" "Thought I heard something"
    
    [Tooltip("When finding nothing")]
    [SerializeField] private AudioClip[] nothingFoundSounds;  // "Must be nothing" "All clear"

    [Header("═══ SEARCH SOUNDS ═══")]
    [Tooltip("While searching for lost player")]
    [SerializeField] private AudioClip[] searchSounds;        // "Where did you go?" "Come out!"
    
    [Tooltip("When giving up search")]
    [SerializeField] private AudioClip[] giveUpSounds;        // "Lost them" "They got away"

    [Header("═══ CHASE SOUNDS ═══")]
    [Tooltip("During active pursuit")]
    [SerializeField] private AudioClip[] chaseSounds;         // "Stop!" "Get back here!"

    [Header("═══ PATROL SOUNDS ═══")]
    [Tooltip("Idle chatter while patrolling")]
    [SerializeField] private AudioClip[] patrolSounds;        // Humming, yawning, mumbling

    [Header("═══ RESPOND ALERT SOUNDS ═══")]
    [Tooltip("When responding to another guard's alert")]
    [SerializeField] private AudioClip[] respondSounds;       // "On my way!" "I'm coming!"

    [Header("═══ VOLUME SETTINGS ═══")]
    [SerializeField] private float normalVolume = 0.7f;
    [SerializeField] private float alertVolume = 1.0f;
    [SerializeField] private float whisperVolume = 0.4f;

    [Header("═══ COOLDOWNS ═══")]
    [SerializeField] private float patrolSoundInterval = 15f;
    [SerializeField] private float searchSoundInterval = 5f;
    [SerializeField] private float chaseSoundInterval = 3f;
    [SerializeField] private float minTimeBetweenSounds = 1f;

    [Header("═══ PITCH VARIATION ═══")]
    [SerializeField] private float minPitch = 0.95f;
    [SerializeField] private float maxPitch = 1.05f;

    #endregion

    #region ═══════════════════ PRIVATE FIELDS ═══════════════════

    private AudioSource audioSource;
    private EnemyAI_Advanced aiController;
    private EnemyAI_Advanced.AIState lastState;
    
    private float lastSoundTime;
    private float stateEnterTime;
    private float nextPatrolSoundTime;
    private float nextSearchSoundTime;
    private float nextChaseSoundTime;
    
    private bool hasPlayedStateEnterSound;

    #endregion

    #region ═══════════════════ UNITY LIFECYCLE ═══════════════════

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f; // 3D sound
        audioSource.maxDistance = 20f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
    }

    private void Start()
    {
        aiController = GetComponent<EnemyAI_Advanced>();
        
        if (aiController != null)
        {
            lastState = aiController.State;
            aiController.OnStateChanged += HandleStateChange;
        }
        
        // Randomize initial patrol sound time
        nextPatrolSoundTime = Time.time + Random.Range(5f, patrolSoundInterval);
    }

    private void OnDestroy()
    {
        if (aiController != null)
        {
            aiController.OnStateChanged -= HandleStateChange;
        }
    }

    private void Update()
    {
        if (aiController == null) return;
        
        HandlePeriodicSounds();
    }

    #endregion

    #region ═══════════════════ STATE CHANGE HANDLING ═══════════════════

    private void HandleStateChange(EnemyAI_Advanced.AIState newState)
    {
        // Play appropriate sound for state transition
        switch (newState)
        {
            case EnemyAI_Advanced.AIState.INVESTIGATE:
                if (lastState == EnemyAI_Advanced.AIState.PATROL)
                {
                    PlayRandomSound(noticeSounds, normalVolume);
                    DelayedPlay(investigateSounds, normalVolume, 1.5f);
                }
                break;
                
            case EnemyAI_Advanced.AIState.SUSPICIOUS:
                PlayRandomSound(noticeSounds, normalVolume);
                break;
                
            case EnemyAI_Advanced.AIState.CHASE:
                PlayRandomSound(spottedSounds, alertVolume);
                // Alert other guards
                DelayedPlay(alertSounds, alertVolume, 1f);
                nextChaseSoundTime = Time.time + chaseSoundInterval;
                break;
                
            case EnemyAI_Advanced.AIState.SEARCH:
                if (lastState == EnemyAI_Advanced.AIState.CHASE)
                {
                    // Lost the player
                    PlayRandomSound(searchSounds, normalVolume);
                }
                nextSearchSoundTime = Time.time + searchSoundInterval;
                break;
                
            case EnemyAI_Advanced.AIState.RESPOND_ALERT:
                PlayRandomSound(respondSounds, alertVolume);
                break;
                
            case EnemyAI_Advanced.AIState.PATROL:
                if (lastState == EnemyAI_Advanced.AIState.SEARCH || 
                    lastState == EnemyAI_Advanced.AIState.INVESTIGATE)
                {
                    PlayRandomSound(nothingFoundSounds, normalVolume);
                }
                else if (lastState == EnemyAI_Advanced.AIState.CHASE)
                {
                    PlayRandomSound(giveUpSounds, normalVolume);
                }
                break;
        }

        lastState = newState;
        stateEnterTime = Time.time;
        hasPlayedStateEnterSound = true;
    }

    #endregion

    #region ═══════════════════ PERIODIC SOUNDS ═══════════════════

    private void HandlePeriodicSounds()
    {
        var state = aiController.State;
        
        switch (state)
        {
            case EnemyAI_Advanced.AIState.PATROL:
                if (Time.time >= nextPatrolSoundTime)
                {
                    PlayRandomSound(patrolSounds, whisperVolume);
                    nextPatrolSoundTime = Time.time + patrolSoundInterval + Random.Range(-3f, 3f);
                }
                break;
                
            case EnemyAI_Advanced.AIState.SEARCH:
                if (Time.time >= nextSearchSoundTime)
                {
                    PlayRandomSound(searchSounds, normalVolume);
                    nextSearchSoundTime = Time.time + searchSoundInterval + Random.Range(-1f, 1f);
                }
                break;
                
            case EnemyAI_Advanced.AIState.CHASE:
                if (Time.time >= nextChaseSoundTime)
                {
                    PlayRandomSound(chaseSounds, alertVolume);
                    nextChaseSoundTime = Time.time + chaseSoundInterval + Random.Range(-0.5f, 0.5f);
                }
                break;
        }
    }

    #endregion

    #region ═══════════════════ SOUND PLAYBACK ═══════════════════

    private void PlayRandomSound(AudioClip[] clips, float volume, string category = "")
    {
        if (Time.time - lastSoundTime < minTimeBetweenSounds) return;

        // DEBUG: Log even without audio files
        if (debugMode)
        {
            string catName = !string.IsNullOrEmpty(category) ? category : GetCategoryName(clips);
            Debug.Log($"[AISoundSystem] 🔊 {gameObject.name}: Playing {catName} (Volume: {volume:F2})");
        }

        if (clips == null || clips.Length == 0) 
        {
            if (debugMode)
            {
                Debug.LogWarning($"[AISoundSystem] ⚠️ {gameObject.name}: No audio clips assigned! Add clips in Inspector.");
            }
            return;
        }

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        
        audioSource.pitch = Random.Range(minPitch, maxPitch);
        audioSource.PlayOneShot(clip, volume);
        
        lastSoundTime = Time.time;
    }

    private string GetCategoryName(AudioClip[] clips)
    {
        if (clips == noticeSounds) return "NOTICE (Huh?)";
        if (clips == spottedSounds) return "SPOTTED (Intruder!)";
        if (clips == alertSounds) return "ALERT (Over here!)";
        if (clips == investigateSounds) return "INVESTIGATE";
        if (clips == nothingFoundSounds) return "NOTHING FOUND";
        if (clips == searchSounds) return "SEARCH (Where are you?)";
        if (clips == giveUpSounds) return "GIVE UP";
        if (clips == chaseSounds) return "CHASE (Stop!)";
        if (clips == patrolSounds) return "PATROL (idle)";
        if (clips == respondSounds) return "RESPOND ALERT";
        return "UNKNOWN";
    }

    private void DelayedPlay(AudioClip[] clips, float volume, float delay)
    {
        if (clips == null || clips.Length == 0) return;
        
        StartCoroutine(PlayDelayed(clips, volume, delay));
    }

    private System.Collections.IEnumerator PlayDelayed(AudioClip[] clips, float volume, float delay)
    {
        yield return new WaitForSeconds(delay);
        PlayRandomSound(clips, volume);
    }

    #endregion

    #region ═══════════════════ PUBLIC METHODS ═══════════════════

    /// <summary>
    /// Force play a specific sound type
    /// </summary>
    public void PlaySound(SoundType type)
    {
        switch (type)
        {
            case SoundType.Notice:
                PlayRandomSound(noticeSounds, normalVolume);
                break;
            case SoundType.Spotted:
                PlayRandomSound(spottedSounds, alertVolume);
                break;
            case SoundType.Alert:
                PlayRandomSound(alertSounds, alertVolume);
                break;
            case SoundType.Search:
                PlayRandomSound(searchSounds, normalVolume);
                break;
            case SoundType.GiveUp:
                PlayRandomSound(giveUpSounds, normalVolume);
                break;
        }
    }

    public enum SoundType
    {
        Notice,
        Spotted,
        Alert,
        Search,
        GiveUp
    }

    #endregion
}
