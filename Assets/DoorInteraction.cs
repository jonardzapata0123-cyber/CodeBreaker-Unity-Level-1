
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
using System.Collections.Generic;

public class DoorInteraction : MonoBehaviour
{
    [Header("Door Settings")]
    public Transform doorPivot;
    public GameObject interactionText;

    [Header("Opening Settings")]
    public float openAngle = 90f;
    public float openSpeed = 3f;

    [Header("Result Settings")]
    public bool isCorrectDoor;
    public GameObject resultCanvas;
    public TMP_Text resultTitle;
    public TMP_Text resultDescription;
    public GameObject tryAgainButton;

    [Header("Result Timer")]
    public float resultDuration = 3f;

    // ---- Code Breaker additions (optional; leave empty for the old behaviour) ----
    [Header("Fire (wrong door only)")]
    [Tooltip("Fire in the room behind this door. If set, closing this wrong door starts the fire instead of an instant death.")]
    public FireHazard fireHazard;

    [Header("Progression")]
    [Tooltip("Lock the door after the player has closed it behind them, so they can't go back.")]
    public bool lockAfterChoice = true;
    [Tooltip("Tick on the last correct door of the level: closing it completes the level.")]
    public bool isFinalDoor;

    private bool isLocked;

    // Code Breaker room system: RoomManager listens to this and decides correct / wrong.
    // When nobody listens, the door uses its original result logic below.
    public event Action<DoorInteraction> DoorChosen;

    // The moving door leaf has its own solid BoxCollider. It is passable only WHILE the door swings,
    // so it can never wedge the player, and solid again as soon as the door stands still (open or
    // closed) and the player is clear of it - so the player (and the camera) can't walk into the door.
    // The side walls and the top beam (door frame) are never touched.
    private readonly List<Collider> leafColliders = new List<Collider>();
    private bool leafSolid = true;
    private CharacterController playerController;
    private TMP_Text interactionLabel;
    private Collider interactionZone;
    private string openPrompt = "Press E to open the door.";
    private const string closePrompt = "Press E to close the door.";

    private bool playerNearby;
    private bool isOpen;
    private bool hasCrossedDoor;
    private bool resultShown;
    private bool waitingForClose;

    private Quaternion closedRotation;
    private Quaternion openRotation;

    void Start()
    {
        if (doorPivot == null)
        {
            Debug.LogError("DoorPivot is not assigned!", this);
            enabled = false;
            return;
        }

        closedRotation = doorPivot.localRotation;

        foreach (Collider c in doorPivot.GetComponentsInChildren<Collider>(true))
            if (!c.isTrigger)
                leafColliders.Add(c);

        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
            playerController = player.GetComponent<CharacterController>();

        interactionZone = GetComponent<Collider>();

        if (interactionText != null)
        {
            interactionLabel = interactionText.GetComponentInChildren<TMP_Text>(true);
            if (interactionLabel != null && !string.IsNullOrEmpty(interactionLabel.text))
                openPrompt = interactionLabel.text;
        }

        openRotation = closedRotation *
            Quaternion.Euler(0f, openAngle, 0f);

        if (interactionText != null)
            interactionText.SetActive(false);

        if (resultCanvas != null)
            resultCanvas.SetActive(false);

        if (tryAgainButton != null)
            tryAgainButton.SetActive(false);
    }

    void Update()
    {
        // R key para mag-restart kapag maling pinto
        if (resultShown && !isCorrectDoor)
        {
            if (Input.GetKeyDown(KeyCode.R))
                RestartLevel();

            return;
        }

        bool gameOver = GameFlowManager.Instance != null &&
                        GameFlowManager.Instance.InteractionsBlocked;

        // Safety check: after a respawn/teleport Unity may not send OnTriggerExit
        if (playerNearby && interactionZone != null && playerController != null &&
            interactionZone.bounds.SqrDistance(playerController.transform.position) > 2.25f)
        {
            playerNearby = false;
            if (interactionText != null)
                interactionText.SetActive(false);
        }

        // E key para buksan o isara ang pinto (hindi habang bukas ang lectern)
        if (playerNearby && !waitingForClose && !isLocked && !gameOver &&
            !LecternQuestion.AnyOpen &&
            Input.GetKeyDown(KeyCode.E))
        {
            bool wasOpen = isOpen;
            isOpen = !isOpen;

            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayDoor(isOpen, transform.position);   // creak / close

            // Hintayin munang magsara pagkatapos tumawid
            if (hasCrossedDoor && wasOpen && !isOpen &&
                !resultShown)
            {
                waitingForClose = true;
                StartCoroutine(WaitUntilDoorCloses());
            }
        }

        Quaternion targetRotation =
            isOpen ? openRotation : closedRotation;

        doorPivot.localRotation = Quaternion.Slerp(
            doorPivot.localRotation,
            targetRotation,
            Time.deltaTime * openSpeed
        );

        // Door leaf collider: passable only while swinging, solid when standing still (open or closed).
        bool atRest = Quaternion.Angle(doorPivot.localRotation, targetRotation) < 1f;
        if (!atRest && leafSolid)
            SetLeafSolid(false);
        else if (atRest && !leafSolid && !PlayerTouchesLeaf())
            SetLeafSolid(true);

        // Prompt follows the door state
        if (interactionLabel != null && interactionText != null && interactionText.activeSelf && playerNearby)
            interactionLabel.text = isOpen ? closePrompt : openPrompt;
    }

    void SetLeafSolid(bool solid)
    {
        foreach (Collider c in leafColliders)
            if (c != null)
                c.isTrigger = !solid;

        leafSolid = solid;
    }

    bool PlayerTouchesLeaf()
    {
        if (playerController == null) return false;

        Bounds player = playerController.bounds;
        player.Expand(0.1f);

        foreach (Collider c in leafColliders)
            if (c != null && c.bounds.Intersects(player))
                return true;

        return false;
    }

    // Puts the door back to its starting state (used when the player retries a room).
    public void ResetDoor()
    {
        StopAllCoroutines();

        isOpen = false;
        isLocked = false;
        hasCrossedDoor = false;
        resultShown = false;
        waitingForClose = false;
        playerNearby = false;

        if (doorPivot != null)
            doorPivot.localRotation = closedRotation;

        if (interactionText != null)
            interactionText.SetActive(false);

        if (interactionLabel != null)
            interactionLabel.text = openPrompt;

        SetLeafSolid(true);
    }

    // Puts the door straight into the open or closed position (no swing, no sound).
    // Used when the player is moved between two identical passages.
    public void SetOpenInstant(bool open)
    {
        isOpen = open;
        if (doorPivot != null)
            doorPivot.localRotation = open ? openRotation : closedRotation;
    }

    // True once the door has swung all the way shut (not just told to close).
    public bool IsFullyClosed =>
        !isOpen && doorPivot != null && Quaternion.Angle(doorPivot.localRotation, closedRotation) < 1.5f;

    // Shuts the door behind the player (existing close sound) and keeps it shut.
    public void CloseAndLock()
    {
        StopAllCoroutines();
        waitingForClose = false;

        if (isOpen && AudioManager.Instance != null)
            AudioManager.Instance.PlayDoor(false, transform.position);

        isOpen = false;
        isLocked = true;

        if (interactionText != null)
            interactionText.SetActive(false);
    }

    // Shows the original result message for this door (e.g. YOU'RE ALIVE! for a correct door).
    public void ShowResultMessage()
    {
        resultShown = false;
        ShowResult();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player"))
            return;

        playerNearby = true;

        if (interactionText != null && !isLocked &&
            (!resultShown || isCorrectDoor))
        {
            interactionText.SetActive(true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player"))
            return;

        playerNearby = false;

        if (interactionText != null)
            interactionText.SetActive(false);
    }

    public void PlayerCrossedDoor()
    {
        if (resultShown || hasCrossedDoor)
            return;

        hasCrossedDoor = true;

        if (interactionText != null)
            interactionText.SetActive(true);
    }

    IEnumerator WaitUntilDoorCloses()
    {
        while (Quaternion.Angle(
            doorPivot.localRotation,
            closedRotation) > 0.5f)
        {
            yield return null;
        }

        waitingForClose = false;

        if (lockAfterChoice)
        {
            isLocked = true;
            if (interactionText != null)
                interactionText.SetActive(false);
        }

        // May RoomManager: siya ang magpapasya kung tama o mali (batay sa aktibong tanong)
        if (DoorChosen != null)
        {
            DoorChosen.Invoke(this);
            yield break;
        }

        // Maling pinto + may apoy: sisindi ang apoy, ang health ang magpapasya
        if (!isCorrectDoor && fireHazard != null)
        {
            fireHazard.Ignite();
            yield break;
        }

        // Huling tamang pinto: tapos na ang level
        if (isCorrectDoor && isFinalDoor && GameFlowManager.Instance != null)
        {
            resultShown = true;
            GameFlowManager.Instance.LevelComplete();
            yield break;
        }

        ShowResult();
    }

    void ShowResult()
    {
        if (resultShown)
            return;

        resultShown = true;

        if (interactionText != null)
            interactionText.SetActive(false);

        if (resultCanvas != null)
            resultCanvas.SetActive(true);

        ColorUtility.TryParseHtmlString(
            "#D7D7D7", out Color descriptionColor);

        if (isCorrectDoor)
        {
            if (resultTitle != null)
            {
                resultTitle.text = "YOU'RE ALIVE!";

                ColorUtility.TryParseHtmlString(
                    "#43C979", out Color aliveColor);

                resultTitle.color = aliveColor;
            }

            if (resultDescription != null)
            {
                resultDescription.text =
                    "You chose the correct door.";

                resultDescription.color = descriptionColor;
            }

            if (tryAgainButton != null)
                tryAgainButton.SetActive(false);

            StartCoroutine(HideResultAfterDelay());
        }
        else
        {
            if (resultTitle != null)
            {
                resultTitle.text = "YOU'RE DEAD!";

                ColorUtility.TryParseHtmlString(
                    "#F14141", out Color deadColor);

                resultTitle.color = deadColor;
            }

            if (resultDescription != null)
            {
                resultDescription.text =
                    "You chose the wrong door.";

                resultDescription.color = descriptionColor;
            }

            if (tryAgainButton != null)
            {
                tryAgainButton.SetActive(true);

                TMP_Text buttonText =
                    tryAgainButton.GetComponentInChildren<TMP_Text>();

                if (buttonText != null)
                {
                    buttonText.text = "PRESS R TO TRY AGAIN";

                    ColorUtility.TryParseHtmlString(
                        "#CFCFCF", out Color buttonColor);

                    buttonText.color = buttonColor;
                }
            }
        }
    }

    IEnumerator HideResultAfterDelay()
    {
        yield return new WaitForSeconds(resultDuration);

        // Huwag itago kung tapos na ang laro (patay / time's up / escaped)
        if (GameFlowManager.Instance != null &&
            GameFlowManager.Instance.InteractionsBlocked)
            yield break;

        if (resultCanvas != null)
            resultCanvas.SetActive(false);
    }

    public void RestartLevel()
    {
        SceneManager.LoadScene(
            SceneManager.GetActiveScene().buildIndex
        );
    }
}
