using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Code Breaker: rooms, checkpoints, questions and the dynamic door system for ONE level.
//
// Every room has two answer doors. Behind each door is a small dark vestibule, so you can't
// see where a door leads. Each time a room gets a question, a coin flip decides which of the
// two doors is correct (independent of the previous attempt), and the signs above the doors
// and the lectern are updated together.
//
//  Correct door -> success sound, fade, you arrive at the next room's checkpoint
//                  (the next room gets its own random question and door placement).
//  Wrong door   -> fade into the burning dead-end room: fire + blood + horror hit + HP loss
//                  at the same moment, then back to THIS room's checkpoint with a new question
//                  and a new random door placement. The timer keeps running.
[System.Serializable]
public class EscapeRoom
{
    public string roomName = "Room";
    public Transform checkpoint;
    public LecternQuestion lectern;

    [Header("The two answer doors (either one can be the correct door)")]
    public DoorInteraction door1;
    public AnswerSign sign1;
    public DoorInteraction door2;
    public AnswerSign sign2;

    [Header("Triggers just past the two doors (decide where the player really goes)")]
    public DoorThreshold threshold1;
    public DoorThreshold threshold2;

    [Header("Trap room (looks like the next room, but nothing in it works)")]
    public FireHazard fireRoom;
    [Tooltip("Where the player stands in the burning room after choosing the wrong door.")]
    public Transform fireRoomSpot;

    [System.NonSerialized] public bool completed;
    [System.NonSerialized] public int correctDoor = -1;   // 0 = door1, 1 = door2

    public DoorInteraction Door(int i) => i == 0 ? door1 : door2;
    public AnswerSign Sign(int i) => i == 0 ? sign1 : sign2;
    public DoorThreshold Threshold(int i) => i == 0 ? threshold1 : threshold2;
    public bool HasThresholds => threshold1 != null && threshold2 != null;
    public DoorThreshold RealWay => threshold1 != null && threshold1.leadsToNextRoom ? threshold1 : threshold2;
    public DoorThreshold TrapWay => RealWay == threshold1 ? threshold2 : threshold1;
}

public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance { get; private set; }

    [Header("Questions")]
    public QuestionBank questionBank;

    [Header("Rooms (in play order)")]
    public List<EscapeRoom> rooms = new List<EscapeRoom>();

    [Header("Level")]
    public string levelDisplayName = "Level 1";

    [Header("Timing")]
    public float fadeTime = 0.45f;
    [Tooltip("Seconds the player stays in the burning room before going back to the checkpoint.")]
    public float fireDuration = 2.4f;


    public int CurrentRoomIndex { get; private set; }
    public EscapeRoom CurrentRoom => rooms.Count > 0 ? rooms[CurrentRoomIndex] : null;
    public QuestionPool Pool => pool;

    QuestionPool pool;
    bool busy;

    public bool IsBusy => busy;   // true while a door result / fade is playing

    void Awake()
    {
        Instance = this;
        foreach (EscapeRoom room in rooms)
        {
            if (room.door1 != null) room.door1.DoorChosen += OnDoorChosen;
            if (room.door2 != null) room.door2.DoorChosen += OnDoorChosen;
        }
    }

    void OnDestroy()
    {
        foreach (EscapeRoom room in rooms)
        {
            if (room.door1 != null) room.door1.DoorChosen -= OnDoorChosen;
            if (room.door2 != null) room.door2.DoorChosen -= OnDoorChosen;
        }
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (questionBank == null || questionBank.Count == 0)
        {
            Debug.LogError("RoomManager: no Question Bank assigned.", this);
            return;
        }

        pool = new QuestionPool(questionBank.Count);

        // Only the room the player is in has an active question; later rooms get theirs on arrival.
        foreach (EscapeRoom r in rooms)
        {
            if (r.sign1 != null) r.sign1.Hide();
            if (r.sign2 != null) r.sign2.Hide();
        }

        CurrentRoomIndex = 0;
        ActivateRoom(0);
        UpdateLocationLabel();
    }

    public QuizQuestion QuestionFor(int roomIndex) =>
        pool == null ? null : questionBank.Get(pool.CurrentFor(roomIndex));

    // New question for the room + a fresh coin flip for which door is correct.
    void ActivateRoom(int index)
    {
        pool.AssignNew(index);
        EscapeRoom room = rooms[index];
        room.correctDoor = Random.Range(0, 2);

        for (int i = 0; i < 2; i++)
            if (room.Door(i) != null)
                room.Door(i).isCorrectDoor = i == room.correctDoor;

        ApplyQuestion(index);
    }

    // Lectern + both signs show the room's CURRENT question, with the correct choice on the correct door.
    void ApplyQuestion(int index)
    {
        EscapeRoom room = rooms[index];
        QuizQuestion q = QuestionFor(index);
        if (q == null) return;

        int c = room.correctDoor;
        if (room.Sign(c) != null) room.Sign(c).Show(q.correctAnswer, q.GetAnswer(q.correctAnswer));
        if (room.Sign(1 - c) != null) room.Sign(1 - c).Show(q.WrongAnswer, q.GetAnswer(q.WrongAnswer));
        if (room.lectern != null) room.lectern.SetQuestion(room.roomName, q);
    }

    void OnDoorChosen(DoorInteraction door)
    {
        if (busy) return;
        GameFlowManager gm = GameFlowManager.Instance;
        if (gm != null && gm.InteractionsBlocked) return;

        int index = rooms.FindIndex(r => r.door1 == door || r.door2 == door);
        if (index < 0 || index != CurrentRoomIndex) return;

        EscapeRoom room = rooms[index];
        if (room.completed) return;
        if (room.HasThresholds) return;   // the door thresholds decide (see OnThresholdCrossed)

        // The answer picked = the letter on the chosen door, checked against the CURRENT question.
        int chosen = door == room.door1 ? 0 : 1;
        AnswerSign sign = room.Sign(chosen);
        QuizQuestion q = QuestionFor(index);
        bool correct = sign != null && q != null && sign.HasAnswer && sign.CurrentLetter == q.correctAnswer;

        StartCoroutine(correct ? CorrectChoice(index, door) : WrongChoice(index, door));
    }

    // ---------------------------------------------------------------- door thresholds
    // The CURRENT correct answer decides the destination, every time:
    //   correct door -> the real next room   (if this door physically opens into the trap room,
    //                                           the player is moved to the same spot behind the
    //                                           other door - both rooms look identical from there)
    //   wrong door   -> the trap room         (the same in reverse)
    [Header("Wrong door: burning in the trap room")]
    [Tooltip("How long the player burns (the wrong-answer damage is spread over this time).")]
    public float burnTime = 3f;
    [Range(0.2f, 1f)] public float burnSpeedFactor = 0.6f;

    public void OnThresholdCrossed(DoorThreshold crossed)
    {
        if (busy || crossed == null) return;
        GameFlowManager gm = GameFlowManager.Instance;
        if (gm != null && gm.InteractionsBlocked) return;

        int index = rooms.FindIndex(r => r.threshold1 == crossed || r.threshold2 == crossed);
        if (index < 0 || index != CurrentRoomIndex) return;
        EscapeRoom room = rooms[index];
        if (room.completed) return;

        int chosen = room.threshold1 == crossed ? 0 : 1;
        AnswerSign sign = room.Sign(chosen);
        QuizQuestion q = QuestionFor(index);
        bool correct = sign != null && q != null && sign.HasAnswer && sign.CurrentLetter == q.correctAnswer;

        DoorThreshold destination = correct ? room.RealWay : room.TrapWay;
        if (destination != crossed) SwapToDoor(crossed, destination);

        // Same sequence for both: the door closes behind the player, and only once it is
        // fully shut does the result happen (YOU'RE ALIVE! or the fire + damage).
        busy = true;   // blocks a second trigger / duplicate damage for this entry
        StartCoroutine(CloseThenResult(index, destination.door, correct));
    }

    IEnumerator CloseThenResult(int index, DoorInteraction door, bool correct)
    {
        door.CloseAndLock();   // existing close animation + close sound
        for (float t = 0f; !door.IsFullyClosed && t < 4f; t += Time.deltaTime)
            yield return null;

        GameFlowManager gm = GameFlowManager.Instance;
        if (gm != null && (gm.IsGameOver || gm.GameCompleted)) { busy = false; yield break; }   // e.g. time ran out meanwhile

        if (correct)
        {
            busy = false;
            EnterNextRoom(index);
        }
        else
        {
            yield return BurnInTrapRoom(index);
        }
    }

    // Moves the player from just past one door to the same spot just past the other door.
    void SwapToDoor(DoorThreshold from, DoorThreshold to)
    {
        to.door.SetOpenInstant(true);   // so the view behind the player matches
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            from.TransferPose(player.transform, to);
            if (cc != null) cc.enabled = true;
        }
        from.door.SetOpenInstant(false);
    }

    // The door shuts behind the player once they have stepped away from it.
    IEnumerator CloseBehind(DoorInteraction door)
    {
        GameObject player = GameObject.FindWithTag("Player");
        float t = 0f;
        while (player != null && t < 2.5f &&
               Vector3.Distance(Flat(player.transform.position), Flat(door.transform.position)) < 4.5f)
        {
            t += Time.deltaTime;
            yield return null;
        }
        door.CloseAndLock();
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    void EnterNextRoom(int index)
    {
        GameFlowManager gm = GameFlowManager.Instance;
        EscapeRoom room = rooms[index];
        room.completed = true;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySuccess();

        if (index + 1 >= rooms.Count)
        {
            if (gm != null) gm.LevelCompleted(levelDisplayName, rooms.Count);
            return;
        }

        if (gm != null) gm.ShowMessage("YOU'RE ALIVE!", "#43C979", "You chose the correct door.", null, 2.2f);
        CurrentRoomIndex = index + 1;
        ActivateRoom(CurrentRoomIndex);   // new random question + answer placement for the next room
        UpdateLocationLabel();
    }

    IEnumerator BurnInTrapRoom(int index)
    {
        busy = true;   // one burning event at a time: no repeated damage
        GameFlowManager gm = GameFlowManager.Instance;
        EscapeRoom room = rooms[index];
        PlayerHealth health = gm != null ? gm.playerHealth : null;
        GameObject player = GameObject.FindWithTag("Player");
        AudioManager am = AudioManager.Instance;

        // The trap: the room bursts into flames, the player catches fire, horror sting
        if (room.fireRoom != null) room.fireRoom.Ignite();
        Transform burnFx = player != null ? player.transform.Find("Burn Effect") : null;
        if (burnFx != null) SetBurnFx(burnFx, true);
        if (am != null) { am.PlayWrong(); if (player != null) am.PlayFireLoop(player.transform.position); }

        PlayerMovement mover = player != null ? player.GetComponent<PlayerMovement>() : null;
        float normalSpeed = mover != null ? mover.speed : 0f;
        if (mover != null) mover.speed = normalSpeed * burnSpeedFactor;   // still walking, just staggering
        Camera camC = player != null ? player.GetComponentInChildren<Camera>() : null;
        Transform cam = camC != null ? camC.transform : null;
        Vector3 camPos = cam != null ? cam.localPosition : Vector3.zero;

        int total = health != null ? health.BeginWrongAnswer() : 0;
        if (gm != null)
            gm.ShowMessage("WRONG DOOR!", "#F14141", "It's a trap! You're burning! -" + total + " HP", null, burnTime + 0.4f);

        // Damage drips in while the player burns (exactly 'total' HP in the end)
        const string death = "You chose the wrong door and burned to death.";
        int ticks = 10, dealt = 0;
        for (int i = 1; i <= ticks; i++)
        {
            int target = Mathf.RoundToInt(total * i / (float)ticks);
            int chunk = target - dealt;
            dealt = target;
            if (health != null && chunk > 0) health.TakeDamage(chunk, death, i == 1);   // blood splatter on the first burn only

            for (float t = 0f; t < burnTime / ticks; t += Time.deltaTime)
            {
                if (am != null && player != null) am.MoveFireLoop(player.transform.position);
                if (cam != null) cam.localPosition = camPos + Random.insideUnitSphere * 0.04f;   // shaking while burning
                yield return null;
            }
            if (gm == null || gm.IsGameOver) break;
        }

        if (cam != null) cam.localPosition = camPos;
        if (mover != null) mover.speed = normalSpeed;
        if (gm == null || gm.IsGameOver) { busy = false; yield break; }

        // Smoothly back to the checkpoint
        gm.SetControlsEnabled(false);
        yield return gm.FadeTo(1f, fadeTime);
        if (am != null) am.StopFireLoop();
        if (burnFx != null) SetBurnFx(burnFx, false);
        if (room.fireRoom != null) room.fireRoom.Extinguish();
        if (room.door1 != null) room.door1.ResetDoor();
        if (room.door2 != null) room.door2.ResetDoor();
        RespawnAt(room.checkpoint);
        ActivateRoom(index);   // new question + new random answer placement

        yield return new WaitForSeconds(0.15f);
        yield return gm.FadeTo(0f, fadeTime);
        gm.SetControlsEnabled(true);
        gm.ShowMessage("TRY AGAIN", "#E8C547",
            room.roomName + " has a new question. Read the lectern again.", null, 2.5f);
        busy = false;
    }

    static void SetBurnFx(Transform fx, bool on)
    {
        foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            if (on) ps.Play(true); else ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        foreach (Light l in fx.GetComponentsInChildren<Light>(true))
            l.enabled = on;
    }

    IEnumerator CorrectChoice(int index, DoorInteraction door)
    {
        busy = true;
        GameFlowManager gm = GameFlowManager.Instance;
        EscapeRoom room = rooms[index];
        room.completed = true;   // stays completed for the rest of the game

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySuccess();

        if (index + 1 >= rooms.Count)
        {
            if (gm != null) gm.LevelCompleted(levelDisplayName, rooms.Count);
            busy = false;
            yield break;
        }

        door.ShowResultMessage();   // existing "YOU'RE ALIVE!" message
        if (gm != null) { gm.SetControlsEnabled(false); yield return gm.FadeTo(1f, fadeTime); }

        CurrentRoomIndex = index + 1;          // checkpoint moves to the next room
        ActivateRoom(CurrentRoomIndex);        // new random question + door placement there
        RespawnAt(rooms[CurrentRoomIndex].checkpoint);
        UpdateLocationLabel();

        yield return new WaitForSeconds(0.15f);
        if (gm != null) { yield return gm.FadeTo(0f, fadeTime); gm.SetControlsEnabled(true); }
        busy = false;
    }

    IEnumerator WrongChoice(int index, DoorInteraction door)
    {
        busy = true;
        GameFlowManager gm = GameFlowManager.Instance;
        EscapeRoom room = rooms[index];

        // Into the dead end: the room is already burning when the screen opens.
        if (room.fireRoom != null) room.fireRoom.Ignite();
        if (gm != null) { gm.SetControlsEnabled(false); yield return gm.FadeTo(1f, fadeTime * 0.8f); }
        if (room.fireRoomSpot != null) RespawnAt(room.fireRoomSpot);
        if (gm != null) yield return gm.FadeTo(0f, 0.25f);

        // The hit: fire, blood, horror sound and HP loss together.
        if (AudioManager.Instance != null) AudioManager.Instance.PlayWrong();
        int damage = 0;
        if (gm != null && gm.playerHealth != null)
            damage = gm.playerHealth.TakeWrongAnswerDamage("You chose the wrong door and your health ran out.");

        if (gm == null || gm.IsGameOver) { busy = false; yield break; }

        gm.ShowMessage("WRONG DOOR!", "#F14141",
            "-" + damage + " HP. Returning to the start of " + room.roomName + "...", null, fireDuration);
        yield return new WaitForSeconds(fireDuration);
        if (gm.IsGameOver) { busy = false; yield break; }

        yield return gm.FadeTo(1f, fadeTime);
        if (room.fireRoom != null) room.fireRoom.Extinguish();
        door.ResetDoor();
        RespawnAt(room.checkpoint);
        ActivateRoom(index);   // new question for THIS room + new random door placement

        yield return gm.FadeTo(0f, fadeTime);
        gm.SetControlsEnabled(true);
        gm.ShowMessage("TRY AGAIN", "#E8C547",
            room.roomName + " has a new question. Read the lectern again.", null, 2.5f);
        busy = false;
    }

    void RespawnAt(Transform target)
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null || target == null) return;

        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.transform.SetPositionAndRotation(target.position, target.rotation);
        if (cc != null) cc.enabled = true;
    }

    void UpdateLocationLabel()
    {
        if (GameFlowManager.Instance == null || rooms.Count == 0) return;
        GameFlowManager.Instance.SetLocation(
            levelDisplayName.ToUpper() + "  ·  ROOM " + (CurrentRoomIndex + 1) + " / " + rooms.Count);
    }

    public string DebugState()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Current room: " + (CurrentRoomIndex + 1) + "   unused questions left: " + (pool != null ? pool.UnusedCount : 0));
        for (int i = 0; i < rooms.Count; i++)
        {
            EscapeRoom r = rooms[i];
            QuizQuestion q = QuestionFor(i);
            string correctDoorName = r.correctDoor < 0 ? "-" : (r.Door(r.correctDoor) != null && r.Door(r.correctDoor).transform.parent != null
                ? r.Door(r.correctDoor).transform.parent.name : "door " + (r.correctDoor + 1));
            sb.AppendLine(r.roomName + (r.completed ? " [completed]" : "") +
                          "  Q" + (pool != null && pool.CurrentFor(i) >= 0 ? (pool.CurrentFor(i) + 1).ToString() : "-") +
                          ": " + (q != null ? q.question.Replace("\n", " ") : "(not reached yet)") +
                          "  correct=" + (q != null ? q.correctAnswer.ToString() : "-") +
                          "  correct door=" + correctDoorName +
                          "  owns(0-based)=[" + (pool != null ? string.Join(", ", pool.OwnedBy(i)) : "") + "]");
        }
        return sb.ToString();
    }
}
