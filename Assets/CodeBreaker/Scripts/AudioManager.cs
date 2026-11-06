using UnityEngine;
using UnityEngine.Audio;

// Code Breaker: all game audio in one place.
// Lives on the persistent "Game Session" object, so the background music keeps playing
// across rooms and levels without restarting, and everything stops when a new game starts.
//
//  - Background music: looping horror ambience.
//  - Heartbeat: starts at low HP, gets faster and louder as HP drops, stops when HP is restored.
//  - Countdown: a tick every second in the last 30 s, louder/urgent in the last 10 s;
//    it follows the timer, so retries never restart it.
//  - One-shots: door open/close, lectern, wrong answer (horror hit), correct answer (success).
//  - Volume: separate Music and SFX levels (saved), routed through an AudioMixer when one is assigned.
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Music")]
    public AudioClip backgroundMusic;

    [Header("Player State")]
    public AudioClip heartbeat;
    [Tooltip("Heartbeat starts below this fraction of max HP (0.5 = 50 HP of 100).")]
    [Range(0f, 1f)] public float heartbeatStartsBelow = 0.5f;

    [Header("Countdown")]
    public AudioClip tick;
    public AudioClip tickUrgent;
    public float tickFromSeconds = 30f;
    public float urgentFromSeconds = 10f;

    [Header("Doors")]
    public AudioClip doorOpen;
    public AudioClip doorCreak;
    public AudioClip doorClose;

    [Header("Lectern")]
    public AudioClip lecternApproach;
    public AudioClip lecternOpen;

    [Header("Answers")]
    public AudioClip wrongAnswer;
    public AudioClip correctAnswer;

    [Header("Wrong-door corridor")]
    public AudioClip fireLoop;
    [Tooltip("Played when the end wall of a correct corridor slides open.")]
    public AudioClip passageOpen;

    [Header("Footsteps")]
    public AudioClip[] footstepsWalk;
    public AudioClip[] footstepsRun;
    [Tooltip("Distance walked between two steps (the player moves fast, 12 units/s).")]
    public float walkStride = 5f;
    public float runStride = 6.5f;
    [Tooltip("Above this speed the run sounds are used (the current controller has one speed, so only walk sounds play).")]
    public float runSpeed = 16f;
    [Range(0f, 1f)] public float footstepVolume = 0.55f;

    [Header("Volume (0-1, saved between sessions)")]
    [Range(0f, 1f)] public float musicVolume = 0.6f;
    [Range(0f, 1f)] public float sfxVolume = 0.8f;
    [Tooltip("Hard ceiling so nothing gets too loud, even at 100%.")]
    [Range(0f, 1f)] public float musicCeiling = 0.45f;
    [Range(0f, 1f)] public float sfxCeiling = 0.9f;

    [Header("Mixer (optional)")]
    public AudioMixer mixer;
    public AudioMixerGroup musicGroup;
    public AudioMixerGroup sfxGroup;
    public string musicVolumeParameter = "MusicVolume";
    public string sfxVolumeParameter = "SFXVolume";

    const string MusicKey = "CodeBreaker.MusicVolume";
    const string SfxKey = "CodeBreaker.SFXVolume";

    AudioSource musicSource, heartbeatSource, uiSource, worldSource, fireSource, stepSource;
    CharacterController playerCC;
    Behaviour playerMover;
    Vector3 lastPlayerPos;
    float strideProgress;
    int lastStep = -1, stepCount;
    float lastStepTime = -1f;
    PlayerHealth health;
    LevelTimer timer;
    int lastTickSecond = -1;
    float duckUntil;
    float lastLecternApproach = -99f;
    bool silenced;
    bool mixerOk;
    readonly System.Collections.Generic.List<string> history = new System.Collections.Generic.List<string>();

    void Note(string what)
    {
        history.Add(Time.time.ToString("0.0") + "s  " + what);
        if (history.Count > 30) history.RemoveAt(0);
    }

    // For testing: what is playing right now and the last sounds that were triggered.
    public string DebugState()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Mixer: " + (mixer != null ? (mixerOk ? "in use (MusicVolume / SFXVolume)" : "assigned but parameters missing") : "none (direct volumes)") +
                      "   music " + Mathf.RoundToInt(musicVolume * 100) + "%   sfx " + Mathf.RoundToInt(sfxVolume * 100) + "%   silenced=" + silenced);
        sb.AppendLine("Music: " + (musicSource.isPlaying ? "PLAYING" : "stopped") + " '" + (musicSource.clip ? musicSource.clip.name : "-") +
                      "' t=" + musicSource.time.ToString("0.0") + "s vol=" + musicSource.volume.ToString("0.00"));
        sb.AppendLine("Heartbeat: " + (heartbeatSource.isPlaying ? "PLAYING" : "stopped") + " pitch=" + heartbeatSource.pitch.ToString("0.00") +
                      " vol=" + heartbeatSource.volume.ToString("0.00") + (health != null ? "  (HP " + health.CurrentHealth + ")" : ""));
        sb.AppendLine("Fire loop: " + (fireSource != null && fireSource.isPlaying ? "PLAYING" : "stopped") +
                      "   Footsteps played: " + stepCount + (lastStepTime >= 0f ? " (last " + (Time.time - lastStepTime).ToString("0.0") + "s ago)" : ""));
        sb.AppendLine("AudioSources on Game Session: " + GetComponentsInChildren<AudioSource>(true).Length);
        sb.AppendLine("Recent sounds:");
        foreach (string h in history) sb.AppendLine("  " + h);
        return sb.ToString();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        musicVolume = PlayerPrefs.GetFloat(MusicKey, musicVolume);
        sfxVolume = PlayerPrefs.GetFloat(SfxKey, sfxVolume);

        // Six sources, created once (no duplicates per room, door or step).
        musicSource = MakeSource("Music", musicGroup, true, 0f);
        heartbeatSource = MakeSource("Heartbeat", sfxGroup, true, 0f);
        uiSource = MakeSource("SFX 2D", sfxGroup, false, 0f);
        worldSource = MakeSource("SFX 3D", sfxGroup, false, 1f);
        worldSource.minDistance = 4f;
        worldSource.maxDistance = 45f;
        worldSource.rolloffMode = AudioRolloffMode.Linear;
        fireSource = MakeSource("Fire", sfxGroup, true, 1f);
        fireSource.minDistance = 4f;
        fireSource.maxDistance = 40f;
        fireSource.rolloffMode = AudioRolloffMode.Linear;
        stepSource = MakeSource("Footsteps", sfxGroup, false, 0f);

        health = GetComponent<PlayerHealth>();
        timer = GetComponent<LevelTimer>();

        ApplyVolumes();

        if (backgroundMusic != null)
        {
            musicSource.clip = backgroundMusic;
            musicSource.Play();
        }
        if (heartbeat != null) heartbeatSource.clip = heartbeat;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    AudioSource MakeSource(string name, AudioMixerGroup group, bool loop, float spatial)
    {
        GameObject go = new GameObject("Audio " + name);
        go.transform.SetParent(transform, false);
        AudioSource s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.spatialBlend = spatial;
        s.outputAudioMixerGroup = group;
        return s;
    }

    // ---------------------------------------------------------------- volume
    public void SetMusicVolume(float v)
    {
        musicVolume = Mathf.Clamp01(v);
        PlayerPrefs.SetFloat(MusicKey, musicVolume);
        ApplyVolumes();
    }

    public void SetSfxVolume(float v)
    {
        sfxVolume = Mathf.Clamp01(v);
        PlayerPrefs.SetFloat(SfxKey, sfxVolume);
        ApplyVolumes();
    }

    void ApplyVolumes()
    {
        // Through the mixer when it has the exposed parameters; otherwise on the sources directly.
        mixerOk = mixer != null &&
                  mixer.SetFloat(musicVolumeParameter, ToDecibels(musicVolume)) &&
                  mixer.SetFloat(sfxVolumeParameter, ToDecibels(sfxVolume));
        if (musicSource != null) musicSource.volume = musicCeiling * (mixerOk ? 1f : musicVolume);
    }

    float SfxScale => sfxCeiling * (mixerOk ? 1f : sfxVolume);

    static float ToDecibels(float v) => v <= 0.0001f ? -80f : Mathf.Log10(v) * 20f;

    // ---------------------------------------------------------------- per-frame: heartbeat + countdown
    void Update()
    {
        if (silenced) return;
        UpdateHeartbeat();
        UpdateCountdown();
        UpdateFootsteps();
    }

    // ---------------------------------------------------------------- footsteps
    // Driven by how far the player actually moved on the ground, so they stop the moment the
    // player stops, never play while standing, and never play during teleports or the stagger.
    void UpdateFootsteps()
    {
        if (stepSource == null || footstepsWalk == null || footstepsWalk.Length == 0) return;

        if (playerCC == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p == null) return;
            playerCC = p.GetComponent<CharacterController>();
            playerMover = p.GetComponent("PlayerMovement") as Behaviour;
            if (playerCC == null) return;
            lastPlayerPos = playerCC.transform.position;
            return;
        }

        Vector3 pos = playerCC.transform.position;
        Vector3 delta = pos - lastPlayerPos; delta.y = 0f;
        lastPlayerPos = pos;
        float dist = delta.magnitude;

        bool canStep = Time.deltaTime > 0f && dist < 3f &&                 // a teleport is not a step
                       (playerMover == null || playerMover.enabled) &&     // no steps while controls are off
                       playerCC.isGrounded;
        float speed = Time.deltaTime > 0f ? dist / Time.deltaTime : 0f;

        if (!canStep || speed < 1.5f)
        {
            strideProgress = walkStride * 0.75f;   // the first step comes quickly when moving again
            return;
        }

        bool running = speed > runSpeed && footstepsRun != null && footstepsRun.Length > 0;
        float stride = running ? runStride : walkStride;
        strideProgress += dist;
        if (strideProgress < stride) return;
        strideProgress = 0f;

        AudioClip[] set = running ? footstepsRun : footstepsWalk;
        int i = Random.Range(0, set.Length);
        if (set.Length > 1 && i == lastStep) i = (i + 1) % set.Length;   // never the same sample twice in a row
        lastStep = i;
        stepSource.pitch = Random.Range(0.94f, 1.06f);
        stepSource.PlayOneShot(set[i], footstepVolume * (running ? 1.15f : 1f) * SfxScale);
        stepCount++;
        lastStepTime = Time.time;
    }

    // ---------------------------------------------------------------- corridor sounds
    public void PlayFireLoop(Vector3 position)
    {
        if (silenced || fireLoop == null) return;
        fireSource.transform.position = position;
        fireSource.clip = fireLoop;
        fireSource.volume = 0.8f * SfxScale;
        if (!fireSource.isPlaying) fireSource.Play();
        Note("fire loop START");
    }

    public void MoveFireLoop(Vector3 position)
    {
        if (fireSource != null) fireSource.transform.position = position;   // the fire is on the player
    }

    public void StopFireLoop()
    {
        if (fireSource != null && fireSource.isPlaying) { fireSource.Stop(); Note("fire loop STOP"); }
    }

    public void PlayPassageOpen(Vector3 position)
    {
        if (silenced) return;
        AudioClip c = passageOpen != null ? passageOpen : doorOpen;
        if (c == null) return;
        worldSource.transform.position = position;
        worldSource.PlayOneShot(c, 0.7f * SfxScale);
        Note("passage end opens");
    }

    void UpdateHeartbeat()
    {
        if (heartbeatSource == null || heartbeat == null || health == null) return;

        float hp = health.maxHealth > 0f ? health.CurrentHealth / health.maxHealth : 1f;
        bool low = !health.IsDead && hp < heartbeatStartsBelow;

        if (!low)
        {
            if (heartbeatSource.isPlaying) { heartbeatSource.Stop(); Note("heartbeat STOP"); }
            return;
        }

        // 0 at the threshold -> 1 near death: faster and louder
        float danger = 1f - Mathf.Clamp01(hp / heartbeatStartsBelow);
        heartbeatSource.pitch = Mathf.Lerp(1.0f, 1.6f, danger);
        float duck = Time.unscaledTime < duckUntil ? 0.45f : 1f;   // step back while a big sound plays
        heartbeatSource.volume = Mathf.Lerp(0.35f, 0.85f, danger) * SfxScale * duck;
        if (!heartbeatSource.isPlaying) { heartbeatSource.Play(); Note("heartbeat START"); }
    }

    void UpdateCountdown()
    {
        if (timer == null || !timer.IsRunning) { lastTickSecond = -1; return; }

        float remaining = timer.RemainingTime;
        if (remaining > tickFromSeconds || remaining <= 0f) { lastTickSecond = -1; return; }

        int second = Mathf.CeilToInt(remaining);
        if (second == lastTickSecond) return;
        lastTickSecond = second;

        bool urgent = remaining <= urgentFromSeconds;
        AudioClip clip = urgent && tickUrgent != null ? tickUrgent : tick;
        if (clip != null) { uiSource.PlayOneShot(clip, (urgent ? 0.9f : 0.45f) * SfxScale); Note((urgent ? "TICK (urgent) " : "tick ") + second + "s"); }
    }

    // ---------------------------------------------------------------- one-shots
    public void PlayDoor(bool opening, Vector3 position)
    {
        if (silenced) return;
        worldSource.transform.position = position;
        if (opening)
        {
            if (doorOpen != null) worldSource.PlayOneShot(doorOpen, 0.8f * SfxScale);
            if (doorCreak != null) worldSource.PlayOneShot(doorCreak, 0.55f * SfxScale);
            Note("door open + creak");
        }
        else if (doorClose != null)
        {
            worldSource.PlayOneShot(doorClose, 0.85f * SfxScale);
            Note("door close");
        }
    }

    public void PlayLecternApproach()
    {
        if (silenced || lecternApproach == null) return;
        if (Time.unscaledTime - lastLecternApproach < 10f) return;   // no repeats while walking around it
        lastLecternApproach = Time.unscaledTime;
        uiSource.PlayOneShot(lecternApproach, 0.35f * SfxScale);
        Note("lectern approach");
    }

    public void PlayLecternOpen()
    {
        if (silenced || lecternOpen == null) return;
        uiSource.PlayOneShot(lecternOpen, 0.6f * SfxScale);
        Note("lectern open");
    }

    public void PlayWrong()
    {
        if (silenced || wrongAnswer == null) return;
        duckUntil = Time.unscaledTime + 1.6f;
        uiSource.PlayOneShot(wrongAnswer, 1f * SfxScale);
        Note("WRONG ANSWER hit");
    }

    public void PlaySuccess()
    {
        if (silenced || correctAnswer == null) return;
        duckUntil = Time.unscaledTime + 1.2f;
        uiSource.PlayOneShot(correctAnswer, 0.75f * SfxScale);
        Note("CORRECT ANSWER chime");
    }

    // Game over / game complete: stop the loops and the countdown, fade the music down.
    public void StopGameplayAudio()
    {
        silenced = true;
        if (heartbeatSource != null) heartbeatSource.Stop();
        if (fireSource != null) fireSource.Stop();
        if (musicSource != null) musicSource.volume *= 0.35f;
        Note("gameplay audio stopped (game over / complete)");
    }
}
