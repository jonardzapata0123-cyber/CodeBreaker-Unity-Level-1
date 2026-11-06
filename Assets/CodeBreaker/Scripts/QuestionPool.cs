using System.Collections.Generic;

// Code Breaker: decides which question each room gets.
//
// Rules (from the game design):
//  - Questions are shuffled, so the order is different every game.
//  - A question is owned by ONE room for the whole game session and never moves to another room.
//  - After a wrong answer the room gets a new unused question (it then owns that one too).
//  - When no unused questions are left, the room picks again from the questions it already owns.
//
// This is plain C# (no MonoBehaviour) so the rules can be tested automatically
// from the editor menu: Code Breaker > Run Question Pool Self-Test.
public class QuestionPool
{
    readonly System.Random rng;
    readonly List<int> unused = new List<int>();
    readonly Dictionary<int, List<int>> owned = new Dictionary<int, List<int>>();
    readonly Dictionary<int, int> current = new Dictionary<int, int>();

    public QuestionPool(int questionCount, int? seed = null)
    {
        rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();

        for (int i = 0; i < questionCount; i++)
            unused.Add(i);

        // Fisher-Yates shuffle
        for (int i = unused.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (unused[i], unused[j]) = (unused[j], unused[i]);
        }
    }

    public int UnusedCount => unused.Count;

    public int CurrentFor(int room) => current.TryGetValue(room, out int q) ? q : -1;

    public IReadOnlyList<int> OwnedBy(int room) =>
        owned.TryGetValue(room, out List<int> list) ? list : (IReadOnlyList<int>)new int[0];

    // Returns the room that owns a question, or -1 if nobody owns it yet.
    public int OwnerOf(int question)
    {
        foreach (KeyValuePair<int, List<int>> kv in owned)
            if (kv.Value.Contains(question))
                return kv.Key;
        return -1;
    }

    // Gives the room a (new) active question and returns its index, or -1 if impossible.
    public int AssignNew(int room)
    {
        if (!owned.TryGetValue(room, out List<int> mine))
        {
            mine = new List<int>();
            owned[room] = mine;
        }

        int q;
        if (unused.Count > 0)
        {
            // Take a question nobody has used yet; this room now owns it.
            q = unused[unused.Count - 1];
            unused.RemoveAt(unused.Count - 1);
            mine.Add(q);
        }
        else if (mine.Count > 0)
        {
            // Pool is empty: reuse one of THIS room's own questions (different from the current one if possible).
            int cur = CurrentFor(room);
            if (mine.Count == 1)
            {
                q = mine[0];
            }
            else
            {
                do { q = mine[rng.Next(mine.Count)]; } while (q == cur);
            }
        }
        else
        {
            return -1;   // no questions available at all
        }

        current[room] = q;
        return q;
    }
}
