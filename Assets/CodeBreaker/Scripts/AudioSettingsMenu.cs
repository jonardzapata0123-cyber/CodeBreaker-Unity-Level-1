using UnityEngine;
using TMPro;

// Code Breaker: in-game audio settings (keyboard only, no mouse or EventSystem needed).
//   Tab        open / close (the game pauses while it is open)
//   Up / Down  choose Music or Sound Effects
//   Left/Right lower / raise the volume in 10% steps
public class AudioSettingsMenu : MonoBehaviour
{
    public GameObject panel;
    public TMP_Text musicRow;
    public TMP_Text sfxRow;
    public KeyCode toggleKey = KeyCode.Tab;

    public bool IsOpen { get; private set; }

    int selected;   // 0 = music, 1 = sfx

    void Start()
    {
        if (panel != null) panel.SetActive(false);
    }

    void Update()
    {
        GameFlowManager gm = GameFlowManager.Instance;
        bool canOpen = gm == null || (!gm.IsGameOver && !gm.GameCompleted && !gm.IsTransitioning && !gm.IsBusy);

        if (Input.GetKeyDown(toggleKey))
        {
            if (IsOpen) Close();
            else if (canOpen) Open();
        }

        if (!IsOpen) return;

        if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) selected = 0;
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) selected = 1;

        int step = 0;
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) step = -1;
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) step = 1;

        AudioManager am = AudioManager.Instance;
        if (step != 0 && am != null)
        {
            if (selected == 0) am.SetMusicVolume(Mathf.Round((am.musicVolume + step * 0.1f) * 10f) / 10f);
            else am.SetSfxVolume(Mathf.Round((am.sfxVolume + step * 0.1f) * 10f) / 10f);
            if (selected == 1) am.PlayLecternOpen();   // short preview of the effects level
        }

        Refresh();
    }

    void Open()
    {
        IsOpen = true;
        if (panel != null) panel.SetActive(true);
        Time.timeScale = 0f;   // pauses the game and the timer while adjusting
        if (GameFlowManager.Instance != null) GameFlowManager.Instance.SetControlsEnabled(false);
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (panel != null) panel.SetActive(false);
        Time.timeScale = 1f;
        if (GameFlowManager.Instance != null && !GameFlowManager.Instance.InteractionsBlocked)
            GameFlowManager.Instance.SetControlsEnabled(true);
    }

    void OnDisable()
    {
        if (IsOpen) Time.timeScale = 1f;
    }

    void Refresh()
    {
        AudioManager am = AudioManager.Instance;
        float m = am != null ? am.musicVolume : 0f;
        float s = am != null ? am.sfxVolume : 0f;
        if (musicRow != null) musicRow.text = Row("MUSIC", m, selected == 0);
        if (sfxRow != null) sfxRow.text = Row("SOUND EFFECTS", s, selected == 1);
    }

    static string Row(string label, float v, bool selected)
    {
        int filled = Mathf.RoundToInt(v * 10f);
        string bar = "<color=#E8C547>" + new string('|', filled) + "</color><color=#555555>" + new string('|', 10 - filled) + "</color>";
        string pointer = selected ? "<color=#E8C547>></color> " : "  ";
        return pointer + label.PadRight(14) + "  <  " + bar + "  >  " + Mathf.RoundToInt(v * 100f) + "%";
    }
}
