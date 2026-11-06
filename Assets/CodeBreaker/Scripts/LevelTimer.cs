using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

// Code Breaker: countdown timer with its own time limit per level.
//
//  - Each level starts with the limit set for it below (Level 1 = 3:00, Level 2 = 6:00).
//  - Wrong answers and checkpoint respawns do NOT touch the timer; it keeps running.
//  - The timer is only reset when the next level starts (GameFlowManager calls BeginLevel),
//    or when a completely new game starts.
//  - When it reaches 0, GameFlowManager shows Game Over (time's up).
[System.Serializable]
public class LevelTimeLimit
{
    [Tooltip("Scene name exactly as in Build Settings, e.g. Level 1")]
    public string sceneName;
    [Tooltip("Time limit in seconds (180 = 3 minutes). 0 = not decided yet, use the default below.")]
    public float seconds;
}

public class LevelTimer : MonoBehaviour
{
    [Header("Time Limit Per Level (seconds)")]
    public List<LevelTimeLimit> levelTimeLimits = new List<LevelTimeLimit>
    {
        new LevelTimeLimit { sceneName = "Level 1", seconds = 180f },
        new LevelTimeLimit { sceneName = "Level 2", seconds = 360f },
        new LevelTimeLimit { sceneName = "Level 3", seconds = 0f },     // to be decided
    };

    [Tooltip("Used for any level not listed above, or listed with 0 seconds (e.g. Level 3 until decided).")]
    public float timeLimit = 360f;
    public bool startAutomatically = true;

    [Header("UI")]
    public TMP_Text timerText;
    public Color normalColor = Color.white;
    public Color warningColor = new Color(0.95f, 0.25f, 0.25f);
    [Tooltip("Below this many seconds the timer turns red and pulses.")]
    public float warningTime = 30f;

    public float CurrentTimeLimit { get; private set; }
    public string CurrentLevel { get; private set; }
    public float RemainingTime { get; private set; }
    public float ElapsedTime => CurrentTimeLimit - RemainingTime;
    public bool IsRunning { get; private set; }

    bool begun;

    void Start()
    {
        if (!begun)
            BeginLevel(SceneManager.GetActiveScene().name);
    }

    // Starts the countdown for a level with that level's own time limit.
    public void BeginLevel(string sceneName)
    {
        begun = true;
        CurrentLevel = sceneName;
        CurrentTimeLimit = LimitFor(sceneName);
        RemainingTime = CurrentTimeLimit;
        IsRunning = startAutomatically;
        UpdateTimerUI();
    }

    public float LimitFor(string sceneName)
    {
        foreach (LevelTimeLimit l in levelTimeLimits)
            if (l != null && l.sceneName == sceneName && l.seconds > 0f)
                return l.seconds;
        return timeLimit;
    }

    void Update()
    {
        if (!IsRunning)
            return;

        RemainingTime -= Time.deltaTime;

        if (RemainingTime <= 0f)
        {
            RemainingTime = 0f;
            IsRunning = false;
            UpdateTimerUI();

            if (GameFlowManager.Instance != null)
                GameFlowManager.Instance.TimeUp();
            return;
        }

        UpdateTimerUI();
    }

    public void StartTimer() => IsRunning = true;
    public void StopTimer() => IsRunning = false;
    public void SetRemainingTime(float seconds) => RemainingTime = Mathf.Clamp(seconds, 0f, CurrentTimeLimit);

    public static string Format(float seconds)
    {
        int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
    }

    void UpdateTimerUI()
    {
        if (timerText == null)
            return;

        timerText.text = Format(RemainingTime);

        bool warning = RemainingTime <= warningTime;
        timerText.color = warning ? warningColor : normalColor;

        float pulse = warning && IsRunning ? 1f + Mathf.Abs(Mathf.Sin(Time.time * 4f)) * 0.12f : 1f;
        timerText.rectTransform.localScale = Vector3.one * pulse;
    }
}
