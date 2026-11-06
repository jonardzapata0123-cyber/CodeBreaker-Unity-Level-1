using UnityEngine;

// Code Breaker: invisible trigger just past an answer door, on the far side.
//
// Its transform is the door's "frame": positioned at the doorway (floor level) and facing into
// the space behind the door. Both destinations of a room (the real next room and the trap room)
// are laid out identically relative to their door frames, so RoomManager can move the player from
// one door's frame to the other's without anything visibly changing.
[RequireComponent(typeof(BoxCollider))]
public class DoorThreshold : MonoBehaviour
{
    public DoorInteraction door;

    [Tooltip("True when this door physically opens into the real next room (false = into the trap room).")]
    public bool leadsToNextRoom;

    void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;   // never blocks the player
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && RoomManager.Instance != null)
            RoomManager.Instance.OnThresholdCrossed(this);
    }

    // Moves a pose from this door's frame to the same spot relative to another door's frame.
    public void TransferPose(Transform t, DoorThreshold target)
    {
        Vector3 localPos = transform.InverseTransformPoint(t.position);
        Quaternion localRot = Quaternion.Inverse(transform.rotation) * t.rotation;
        t.SetPositionAndRotation(target.transform.TransformPoint(localPos), target.transform.rotation * localRot);
    }
}
