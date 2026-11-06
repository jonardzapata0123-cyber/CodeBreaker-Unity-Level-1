using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

// Code Breaker: player health + health bar.
// Lives on the persistent "Game Session" object, so health carries over between rooms,
// checkpoints and levels. It is only reset to full when a completely new game starts.
public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;

    [Header("Wrong Answer Damage")]
    [Tooltip("Damage for the 1st, 2nd, 3rd... wrong door. After the last entry the last value repeats.")]
    public int[] wrongAnswerDamage = { 35, 35, 30 };

    [Header("Health Bar UI")]
    [Tooltip("The coloured bar inside the health bar. Its width shrinks as health goes down.")]
    public Image healthFill;
    public TMP_Text healthText;
    public Color fullHealthColor = new Color(0.26f, 0.79f, 0.47f);   // #43C979
    public Color lowHealthColor = new Color(0.95f, 0.25f, 0.25f);    // #F14141

    [Header("Damage Effect")]
    [Tooltip("Full-screen red image that flashes when the player gets hurt.")]
    public Image damageOverlay;
    public float overlayMaxAlpha = 0.45f;
    public float overlayFadeSpeed = 0.6f;
    [Tooltip("Blood splatter image that appears suddenly on every hit and fades out.")]
    public Image bloodOverlay;
    public float bloodDuration = 1.8f;

    [Header("Events")]
    public UnityEvent onDeath;

    public float CurrentHealth { get; private set; }
    public bool IsDead { get; private set; }
    public int WrongAttempts { get; private set; }

    float overlayAlpha;
    float bloodTimer;

    void Awake()
    {
        CurrentHealth = maxHealth;
        UpdateHealthUI();
        SetOverlayAlpha(0f);
        SetBlood(0f, 1f);
    }

    void Update()
    {
        if (overlayAlpha > 0f)
        {
            overlayAlpha = Mathf.MoveTowards(overlayAlpha, 0f, overlayFadeSpeed * Time.deltaTime);
            SetOverlayAlpha(overlayAlpha);
        }

        if (bloodTimer > 0f)
        {
            bloodTimer -= Time.deltaTime;
            float t = 1f - Mathf.Clamp01(bloodTimer / bloodDuration);   // 0 -> 1 over the effect
            float a = t < 0.12f ? 1f : 1f - (t - 0.12f) / 0.88f;        // sudden, short hold, then fade
            SetBlood(a, 1f + 0.06f * (1f - t));
        }
    }

    void SetBlood(float alpha, float scale)
    {
        if (bloodOverlay == null) return;
        Color c = bloodOverlay.sprite != null ? Color.white : new Color(0.55f, 0f, 0f);   // no sprite -> dark red, never white
        c.a = Mathf.Clamp01(alpha);
        bloodOverlay.color = c;
        bloodOverlay.rectTransform.localScale = Vector3.one * scale;
        bloodOverlay.enabled = alpha > 0.001f;
    }

    public void TakeDamage(float amount, string cause = "You lost all your health.", bool bloodSplatter = true)
    {
        if (IsDead || amount <= 0f)
            return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        UpdateHealthUI();

        overlayAlpha = Mathf.Max(overlayAlpha, overlayMaxAlpha);
        SetOverlayAlpha(overlayAlpha);
        if (bloodSplatter)
        {
            bloodTimer = bloodDuration;   // blood splatter: appears at once, then fades
            SetBlood(1f, 1.06f);
        }

        if (CurrentHealth <= 0f)
            Die(cause);
    }

    // Applies the damage for the next wrong answer (35, 35, 30) and returns the amount.
    public int TakeWrongAnswerDamage(string deathMessage)
    {
        if (IsDead || wrongAnswerDamage == null || wrongAnswerDamage.Length == 0)
            return 0;

        int damage = wrongAnswerDamage[Mathf.Min(WrongAttempts, wrongAnswerDamage.Length - 1)];
        WrongAttempts++;
        TakeDamage(damage, deathMessage);
        return damage;
    }

    // Starts a wrong answer without applying it yet: counts the attempt and returns how much
    // damage it is worth (35, 35, 30). The caller applies it gradually with TakeDamage.
    public int BeginWrongAnswer()
    {
        if (IsDead || wrongAnswerDamage == null || wrongAnswerDamage.Length == 0)
            return 0;

        int damage = wrongAnswerDamage[Mathf.Min(WrongAttempts, wrongAnswerDamage.Length - 1)];
        WrongAttempts++;
        return damage;
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f)
            return;

        CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
        UpdateHealthUI();
    }

    void Die(string cause)
    {
        IsDead = true;
        onDeath?.Invoke();

        if (GameFlowManager.Instance != null)
            GameFlowManager.Instance.PlayerDied(cause);
    }

    void UpdateHealthUI()
    {
        float percent = maxHealth > 0f ? CurrentHealth / maxHealth : 0f;

        if (healthFill != null)
        {
            // Works with a plain Image (width shrinks) and with a Filled Image.
            RectTransform rt = healthFill.rectTransform;
            rt.anchorMax = new Vector2(percent, rt.anchorMax.y);
            healthFill.fillAmount = percent;
            healthFill.color = Color.Lerp(lowHealthColor, fullHealthColor, percent);
        }

        if (healthText != null)
            healthText.text = Mathf.CeilToInt(CurrentHealth) + " / " + Mathf.CeilToInt(maxHealth);
    }

    void SetOverlayAlpha(float a)
    {
        if (damageOverlay == null)
            return;

        Color c = damageOverlay.color;
        c.a = a;
        damageOverlay.color = c;
    }
}
