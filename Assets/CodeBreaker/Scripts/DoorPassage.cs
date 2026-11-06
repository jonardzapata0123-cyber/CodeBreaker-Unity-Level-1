using UnityEngine;

// Code Breaker: UNUSED since the duplicate-room version (kept only so older setups compile).
// Code Breaker: the walk-in passage behind an answer door.
//
// Both passages of a room are built exactly alike: a short corridor that turns at the far end
// and opens to the side. No end wall, no barrier, and from the doorway you can't see where it leads.
//
//   Decide zone (a few steps in) -> RoomManager checks the CURRENT correct door:
//       correct: the player keeps walking and steps out into the next room
//                (if this passage can't physically reach the next room, the player is moved
//                 to the identical twin passage that can - same spot, same view, no fade)
//       wrong:   the player catches fire and keeps moving while burning; the way ahead
//                erupts in flames (and becomes impassable), then back to the checkpoint.
//   Exit zone (stepping out the side) -> the door behind slams shut.
public class DoorPassage : MonoBehaviour
{
    public DoorInteraction door;

    [Tooltip("True when the side exit of this passage opens into the next room.")]
    public bool leadsToNextRoom;

    [Tooltip("Flames filling the far end and the side exit (wrong door).")]
    public FireHazard fire;
    [Tooltip("Solid collider in the side exit, only switched on while the flames burn.")]
    public Collider fireBlock;

    [Header("Corridor light")]
    public Light corridorLight;
    public float flickerAmount = 0.35f;

    float baseIntensity;

    void Awake()
    {
        if (corridorLight != null) baseIntensity = corridorLight.intensity;
        if (fireBlock != null) fireBlock.enabled = false;
    }

    void Update()
    {
        // Old lamp flicker. Driven by time only, so twin passages always look identical.
        if (corridorLight != null && corridorLight.enabled)
        {
            float n = Mathf.PerlinNoise(Time.time * 7f, 0.37f);
            float drop = Mathf.PerlinNoise(Time.time * 1.3f, 5.1f) > 0.82f ? 0.35f : 1f;
            corridorLight.intensity = baseIntensity * (1f - flickerAmount * n) * drop;
        }
    }

    // No longer used: rooms now use DoorThreshold (the corridors were replaced by duplicate rooms).
    public void ZoneEntered(PassageZone.Kind kind) { }

    public void Burn()
    {
        if (fire != null) fire.Ignite();
        if (fireBlock != null) fireBlock.enabled = true;
    }

    public void ResetPassage()
    {
        if (fire != null) fire.Extinguish();
        if (fireBlock != null) fireBlock.enabled = false;
    }

    // Moves a pose from this passage to the same spot in another (identical) passage.
    public void TransferPose(Transform t, DoorPassage target)
    {
        Vector3 localPos = transform.InverseTransformPoint(t.position);
        Quaternion localRot = Quaternion.Inverse(transform.rotation) * t.rotation;
        t.SetPositionAndRotation(target.transform.TransformPoint(localPos), target.transform.rotation * localRot);
    }
}
