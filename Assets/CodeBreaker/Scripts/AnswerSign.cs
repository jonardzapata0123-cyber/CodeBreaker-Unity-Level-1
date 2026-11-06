using UnityEngine;
using TMPro;

// Code Breaker: the sign above a door that shows one answer choice (A or B).
// Both signs in a room look exactly the same, so the design never gives the answer away.
public class AnswerSign : MonoBehaviour
{
    public TMP_Text letterText;
    public TMP_Text answerText;

    public AnswerLetter CurrentLetter { get; private set; }
    public bool HasAnswer { get; private set; }

    public void Show(AnswerLetter letter, string answer)
    {
        CurrentLetter = letter;
        HasAnswer = true;

        if (letterText != null)
            letterText.text = letter.ToString();

        if (answerText != null)
            answerText.text = answer;

        gameObject.SetActive(true);
    }

    // Rooms the player hasn't reached yet have no question, so their signs stay hidden.
    public void Hide()
    {
        HasAnswer = false;
        gameObject.SetActive(false);
    }
}
