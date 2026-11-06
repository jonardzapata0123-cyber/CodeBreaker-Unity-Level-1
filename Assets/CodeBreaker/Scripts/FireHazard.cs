using UnityEngine;

// Code Breaker: fire that fills a "wrong door" room.
// DoorInteraction calls Ignite() when the player closes a wrong door behind them.
// While the fire burns, a player standing inside this object's BoxCollider loses health.
[RequireComponent(typeof(BoxCollider))]
public class FireHazard : MonoBehaviour
{
    [Header("Fire Visuals")]
    public ParticleSystem[] fireEffects;
    public Light[] fireLights;
    public float lightIntensity = 6f;

    [Header("Damage")]
    [Tooltip("Off = visual effect only (the room system applies the wrong-answer damage).")]
    public bool damagePlayer = false;
    public float damagePerSecond = 20f;
    [Tooltip("Seconds after the fire starts before it begins to hurt the player.")]
    public float damageDelay = 0.75f;
    public string deathMessage = "You chose the wrong door and burned in the fire.";

    public bool IsBurning { get; private set; }

    BoxCollider area;
    PlayerHealth player;
    float ignitedAt;

    void Awake()
    {
        area = GetComponent<BoxCollider>();
        area.isTrigger = true;   // the area must never block the player

        foreach (ParticleSystem ps in fireEffects)
            if (ps != null)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        foreach (Light l in fireLights)
            if (l != null)
                l.enabled = false;
    }

    void Start()
    {
        player = FindAnyObjectByType<PlayerHealth>();
    }

    public void Ignite()
    {
        if (IsBurning)
            return;

        IsBurning = true;
        ignitedAt = Time.time;

        foreach (ParticleSystem ps in fireEffects)
            if (ps != null)
                ps.Play(true);

        foreach (Light l in fireLights)
            if (l != null)
                l.enabled = true;
    }

    public void Extinguish()
    {
        IsBurning = false;

        foreach (ParticleSystem ps in fireEffects)
            if (ps != null)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        foreach (Light l in fireLights)
            if (l != null)
                l.enabled = false;
    }

    public bool IsPlayerInside()
    {
        return player != null && area != null && area.bounds.Contains(player.transform.position);
    }

    void Update()
    {
        if (!IsBurning)
            return;

        // Flickering firelight
        for (int i = 0; i < fireLights.Length; i++)
        {
            if (fireLights[i] == null) continue;
            float n = Mathf.PerlinNoise(Time.time * 8f, i * 3.7f);
            fireLights[i].intensity = lightIntensity * (0.7f + n * 0.6f);
        }

        if (!damagePlayer || Time.time < ignitedAt + damageDelay)
            return;

        if (player != null && !player.IsDead && IsPlayerInside())
            player.TakeDamage(damagePerSecond * Time.deltaTime, deathMessage);
    }

    void OnDrawGizmosSelected()
    {
        BoxCollider b = GetComponent<BoxCollider>();
        if (b == null) return;
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(b.center, b.size);
    }
}
