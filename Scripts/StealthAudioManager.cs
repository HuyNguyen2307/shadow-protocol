using UnityEngine;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// STEALTH AUDIO MANAGER - Central audio control
/// ═══════════════════════════════════════════════════════════════════════════════
/// 
/// FEATURES:
/// - Background music with tension-based changes
/// - Ambient sounds
/// - UI feedback sounds
/// - Detection stingers
/// - Volume mixing
/// 
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class StealthAudioManager : MonoBehaviour
{
    #region ═══════════════════ SINGLETON ═══════════════════

    public static StealthAudioManager Instance { get; private set; }

    #endregion

    #region ═══════════════════ SETTINGS ═══════════════════

    [Header("═══ DEBUG ═══")]
    [Tooltip("Show debug logs for music and audio state changes")]
    [SerializeField] private bool debugMode = true;

    [Header("═══ MUSIC ═══")]
    [SerializeField] private AudioClip ambientMusic;          // Calm exploration
    [SerializeField] private AudioClip tensionMusic;          // Being detected
    [SerializeField] private AudioClip chaseMusic;            // Full pursuit
    [SerializeField] private float musicTransitionSpeed = 1f;
    [SerializeField] private float musicVolume = 0.3f;

    [Header("═══ AMBIENT ═══")]
    [SerializeField] private AudioClip[] ambientSounds;       // Random ambient sounds
    [SerializeField] private float ambientInterval = 10f;
    [SerializeField] private float ambientVolume = 0.2f;

    [Header("═══ STINGERS ═══")]
    [SerializeField] private AudioClip detectionStinger;      // When detection starts
    [SerializeField] private AudioClip spottedStinger;        // When fully detected
    [SerializeField] private AudioClip escapeStinger;         // When losing enemy
    [SerializeField] private float stingerVolume = 0.6f;

    [Header("═══ UI SOUNDS ═══")]
    [SerializeField] private AudioClip flashlightToggle;
    [SerializeField] private AudioClip heartbeat;             // Plays when detection high
    [SerializeField] private float heartbeatThreshold = 0.6f;
    [SerializeField] private float uiVolume = 0.5f;

    [Header("═══ DETECTION FEEDBACK ═══")]
    [SerializeField] private AudioClip detectionBuildLoop;    // Tense sound while being seen
    [SerializeField] private float detectionAudioThreshold = 0.3f;

    #endregion

    #region ═══════════════════ PRIVATE FIELDS ═══════════════════

    // Audio Sources
    private AudioSource musicSource;
    private AudioSource ambientSource;
    private AudioSource stingerSource;
    private AudioSource uiSource;
    private AudioSource detectionSource;
    private AudioSource heartbeatSource;

    // State tracking
    private MusicState currentMusicState = MusicState.Ambient;
    private float targetMusicVolume;
    private float lastAmbientTime;
    private float currentDetection;
    private bool wasDetected;
    private bool isHeartbeatPlaying;

    private enum MusicState
    {
        Ambient,
        Tension,
        Chase
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
        DontDestroyOnLoad(gameObject);

        CreateAudioSources();
    }

    private void Start()
    {
        StartAmbientMusic();
        lastAmbientTime = Time.time + Random.Range(3f, ambientInterval);
    }

    private void Update()
    {
        UpdateMusicState();
        UpdateHeartbeat();
        UpdateAmbientSounds();
        UpdateDetectionAudio();
    }

    #endregion

    #region ═══════════════════ SETUP ═══════════════════

    private void CreateAudioSources()
    {
        // Music source
        musicSource = CreateAudioSource("Music", true, 0f);
        
        // Ambient source
        ambientSource = CreateAudioSource("Ambient", false, 0f);
        
        // Stinger source
        stingerSource = CreateAudioSource("Stingers", false, 0f);
        
        // UI source
        uiSource = CreateAudioSource("UI", false, 0f);
        
        // Detection source
        detectionSource = CreateAudioSource("Detection", true, 0f);
        
        // Heartbeat source
        heartbeatSource = CreateAudioSource("Heartbeat", true, 0f);
    }

    private AudioSource CreateAudioSource(string name, bool loop, float spatialBlend)
    {
        GameObject obj = new GameObject($"AudioSource_{name}");
        obj.transform.SetParent(transform);
        
        AudioSource source = obj.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = spatialBlend;
        
        return source;
    }

    #endregion

    #region ═══════════════════ MUSIC ═══════════════════

    private void StartAmbientMusic()
    {
        if (ambientMusic != null)
        {
            musicSource.clip = ambientMusic;
            musicSource.volume = musicVolume;
            musicSource.Play();
            currentMusicState = MusicState.Ambient;
        }
    }

    private void UpdateMusicState()
    {
        // Get detection level from HUD
        float detection = 0f;
        if (StealthHUD.Instance != null)
        {
            detection = StealthHUD.Instance.GetDetectionLevel();
        }
        currentDetection = detection;

        // Determine target music state
        MusicState targetState = MusicState.Ambient;
        
        if (detection >= 1f)
        {
            targetState = MusicState.Chase;
        }
        else if (detection >= 0.3f)
        {
            targetState = MusicState.Tension;
        }

        // Transition music if state changed
        if (targetState != currentMusicState)
        {
            TransitionMusic(targetState);
        }
    }

    private void TransitionMusic(MusicState newState)
    {
        // DEBUG: Log music state change
        if (debugMode)
        {
            Debug.Log($"[AudioManager] 🎵 Music: {currentMusicState} → {newState} (Detection: {currentDetection:P0})");
        }

        AudioClip newClip = null;
        
        switch (newState)
        {
            case MusicState.Ambient:
                newClip = ambientMusic;
                break;
            case MusicState.Tension:
                newClip = tensionMusic;
                break;
            case MusicState.Chase:
                newClip = chaseMusic;
                break;
        }

        if (newClip != null && newClip != musicSource.clip)
        {
            StartCoroutine(CrossfadeMusic(newClip));
        }
        else if (newClip == null && debugMode)
        {
            Debug.LogWarning($"[AudioManager] ⚠️ No music clip assigned for {newState}! Add clip in Inspector.");
        }

        currentMusicState = newState;
    }

    private System.Collections.IEnumerator CrossfadeMusic(AudioClip newClip)
    {
        // Fade out
        float startVolume = musicSource.volume;
        while (musicSource.volume > 0)
        {
            musicSource.volume -= musicTransitionSpeed * Time.deltaTime;
            yield return null;
        }

        // Switch clip
        musicSource.clip = newClip;
        musicSource.Play();

        // Fade in
        while (musicSource.volume < musicVolume)
        {
            musicSource.volume += musicTransitionSpeed * Time.deltaTime;
            yield return null;
        }
        
        musicSource.volume = musicVolume;
    }

    #endregion

    #region ═══════════════════ HEARTBEAT ═══════════════════

    private void UpdateHeartbeat()
    {
        if (heartbeat == null) return;

        bool shouldPlay = currentDetection >= heartbeatThreshold && currentDetection < 1f;

        if (shouldPlay && !isHeartbeatPlaying)
        {
            heartbeatSource.clip = heartbeat;
            heartbeatSource.volume = 0f;
            heartbeatSource.Play();
            isHeartbeatPlaying = true;
        }
        else if (!shouldPlay && isHeartbeatPlaying)
        {
            heartbeatSource.Stop();
            isHeartbeatPlaying = false;
        }

        // Volume based on detection
        if (isHeartbeatPlaying)
        {
            float targetVolume = Mathf.InverseLerp(heartbeatThreshold, 1f, currentDetection) * uiVolume;
            heartbeatSource.volume = Mathf.Lerp(heartbeatSource.volume, targetVolume, Time.deltaTime * 3f);
        }
    }

    #endregion

    #region ═══════════════════ DETECTION AUDIO ═══════════════════

    private void UpdateDetectionAudio()
    {
        if (detectionBuildLoop == null) return;

        bool shouldPlay = currentDetection >= detectionAudioThreshold && currentDetection < 1f;

        if (shouldPlay && !detectionSource.isPlaying)
        {
            detectionSource.clip = detectionBuildLoop;
            detectionSource.volume = 0f;
            detectionSource.Play();
        }
        else if (!shouldPlay && detectionSource.isPlaying)
        {
            detectionSource.Stop();
        }

        // Volume based on detection
        if (detectionSource.isPlaying)
        {
            float targetVolume = Mathf.InverseLerp(detectionAudioThreshold, 1f, currentDetection) * 0.4f;
            detectionSource.volume = Mathf.Lerp(detectionSource.volume, targetVolume, Time.deltaTime * 5f);
        }

        // Play spotted stinger
        if (currentDetection >= 1f && !wasDetected)
        {
            PlayStinger(spottedStinger);
            wasDetected = true;
        }
        else if (currentDetection < 0.5f && wasDetected)
        {
            PlayStinger(escapeStinger);
            wasDetected = false;
        }
    }

    #endregion

    #region ═══════════════════ AMBIENT SOUNDS ═══════════════════

    private void UpdateAmbientSounds()
    {
        if (ambientSounds == null || ambientSounds.Length == 0) return;
        
        // Don't play ambient during chase
        if (currentMusicState == MusicState.Chase) return;

        if (Time.time >= lastAmbientTime)
        {
            PlayRandomAmbient();
            lastAmbientTime = Time.time + ambientInterval + Random.Range(-3f, 5f);
        }
    }

    private void PlayRandomAmbient()
    {
        AudioClip clip = ambientSounds[Random.Range(0, ambientSounds.Length)];
        ambientSource.PlayOneShot(clip, ambientVolume);
    }

    #endregion

    #region ═══════════════════ PUBLIC METHODS ═══════════════════

    /// <summary>
    /// Play a stinger sound
    /// </summary>
    public void PlayStinger(AudioClip clip)
    {
        if (clip == null) return;
        stingerSource.PlayOneShot(clip, stingerVolume);
    }

    /// <summary>
    /// Play UI sound effect
    /// </summary>
    public void PlayUISound(AudioClip clip)
    {
        if (clip == null) return;
        uiSource.PlayOneShot(clip, uiVolume);
    }

    /// <summary>
    /// Play flashlight toggle sound
    /// </summary>
    public void PlayFlashlightSound()
    {
        PlayUISound(flashlightToggle);
    }

    /// <summary>
    /// Set master music volume
    /// </summary>
    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        if (musicSource != null)
        {
            musicSource.volume = musicVolume;
        }
    }

    /// <summary>
    /// Trigger detection stinger
    /// </summary>
    public void TriggerDetectionStinger()
    {
        PlayStinger(detectionStinger);
    }

    #endregion
}
