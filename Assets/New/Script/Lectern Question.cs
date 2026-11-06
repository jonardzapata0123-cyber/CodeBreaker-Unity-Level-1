
using UnityEngine;
using TMPro;

public class LecternQuestion : MonoBehaviour
{
    public GameObject questionCanvas;

    [Header("Code Breaker: question display")]
    [Tooltip("Text on the note/book page where this room's active question is written.")]
    public TMP_Text questionText;

    // Only one question interface can be open at a time.
    public static LecternQuestion OpenLectern { get; private set; }
    public static bool AnyOpen => OpenLectern != null;

    private bool playerNearby = false;
    private string roomName = "";
    private Collider zone;
    private Transform player;
    private QuizQuestion question;

    void Start()
    {
        questionCanvas.SetActive(false);

        zone = GetComponent<Collider>();
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
    }

    // Safety check: after a respawn/teleport Unity may not send OnTriggerExit,
    // so also confirm the player is really inside the zone.
    bool PlayerReallyNearby()
    {
        if (zone == null || player == null) return true;
        return zone.bounds.SqrDistance(player.position) < 2.25f;   // within 1.5 units of the zone
    }

    void Update()
    {
        bool blocked = GameFlowManager.Instance != null && GameFlowManager.Instance.InteractionsBlocked;

        if (blocked && OpenLectern == this)
            Close();

        if (playerNearby && !PlayerReallyNearby())
        {
            playerNearby = false;
            Close();
        }

        // E para buksan / isara ang tanong
        if (playerNearby && !blocked && Input.GetKeyDown(KeyCode.E))
        {
            if (OpenLectern == this)
                Close();
            else if (OpenLectern == null)
                Open();
        }
    }

    void Open()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayLecternOpen();
        RefreshText();
        questionCanvas.SetActive(true);
        OpenLectern = this;
    }

    public void Close()
    {
        if (OpenLectern == this)
        {
            questionCanvas.SetActive(false);
            OpenLectern = null;
        }
    }

    // Called by RoomManager whenever this room gets a (new) active question.
    public void SetQuestion(string room, QuizQuestion q)
    {
        roomName = room;
        question = q;

        if (OpenLectern == this)
            RefreshText();   // never show an old question
    }

    void RefreshText()
    {
        if (questionText == null)
            return;

        if (question == null)
        {
            questionText.text = "<i>The page is blank...</i>";
            return;
        }

        questionText.text =
            "<size=75%><b>" + roomName.ToUpper() + "</b></size>\n\n" +
            "<b>" + question.question + "</b>\n\n" +
            "<b>A.</b> " + question.answerA + "\n\n" +
            "<b>B.</b> " + question.answerB + "\n\n" +
            "<size=65%><i>Go through the door that shows your answer.\nPress E to close.</i></size>";
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerNearby = true;
            if (AudioManager.Instance != null) AudioManager.Instance.PlayLecternApproach();
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerNearby = false;
            Close();
        }
    }

    void OnDestroy()
    {
        if (OpenLectern == this)
            OpenLectern = null;
    }
}
