using System.Collections.Generic;
using UnityEngine;

// Code Breaker: the ONE place where questions, choices and correct answers live.
// The lectern text, the answer signs above the doors and the door checking
// all read from this same data.

public enum AnswerLetter { A, B }

[System.Serializable]
public class QuizQuestion
{
    [TextArea(2, 5)] public string question;
    [TextArea(1, 3)] public string answerA;
    [TextArea(1, 3)] public string answerB;
    public AnswerLetter correctAnswer;

    public string GetAnswer(AnswerLetter letter) => letter == AnswerLetter.A ? answerA : answerB;
    public AnswerLetter WrongAnswer => correctAnswer == AnswerLetter.A ? AnswerLetter.B : AnswerLetter.A;
}

[CreateAssetMenu(menuName = "Code Breaker/Question Bank", fileName = "QuestionBank")]
public class QuestionBank : ScriptableObject
{
    public List<QuizQuestion> questions = new List<QuizQuestion>();

    public int Count => questions.Count;
    public QuizQuestion Get(int index) => index >= 0 && index < questions.Count ? questions[index] : null;
}
