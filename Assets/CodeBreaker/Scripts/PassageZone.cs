using UnityEngine;

// Code Breaker: trigger area inside a door passage (see DoorPassage).
//   Decide = a few steps in: right or wrong is decided here
//   Exit   = the side exit: the player has walked out into the next room
[RequireComponent(typeof(BoxCollider))]
public class PassageZone : MonoBehaviour
{
    public enum Kind { Decide, Exit }

    public Kind kind;
    public DoorPassage passage;

    void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;   // never blocks the player
    }

    void OnTriggerEnter(Collider other)
    {
        if (passage != null && other.CompareTag("Player"))
            passage.ZoneEntered(kind);
    }
}
