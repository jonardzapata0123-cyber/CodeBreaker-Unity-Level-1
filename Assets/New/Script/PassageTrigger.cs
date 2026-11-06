
using UnityEngine;

public class PassageTrigger : MonoBehaviour
{
    public DoorInteraction doorInteraction;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            doorInteraction.PlayerCrossedDoor();
        }
    }
}
