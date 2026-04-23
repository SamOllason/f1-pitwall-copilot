namespace Application.Features.AskPitWall;

// Derives a confidence score from observable signals — no LLM call needed.
//
// Signals used (in priority order):
//   1. Retrieval coverage  — were relevant chunks found at all?
//   2. Retrieval quality   — how high was the top chunk's similarity score?
//   3. Tool grounding      — did the model call backend tools to gather evidence?
//   4. Answer hedging      — does the answer text contain phrases that signal uncertainty?
//
// Thresholds are intentionally coarse (High / Medium / Low / VeryLow).
// A numeric score would imply false precision for something this heuristic.
public static class ConfidenceEvaluator
{
    private static readonly string[] HedgingPhrases =
    [
        "i don't have specific",
        "no specific retrieved",
        "based on general knowledge",
        "based on general racing",
        "you may want to verify",
        "i cannot confirm",
        "i'm not sure",
        "i do not have information",
        "unable to find",
        "no information available"
    ];

    // chunkScores: the @search.score values for every RAG chunk retrieved across this request.
    // toolCallCount: number of backend tools the LLM invoked.
    // answerText: the final answer string, used for passive hedging detection.
    public static AnswerConfidence Evaluate(
        IReadOnlyList<double> chunkScores,
        int toolCallCount,
        string answerText)
    {
        var hasChunks = chunkScores.Count > 0;
        var topScore = hasChunks ? chunkScores.Max() : 0.0;
        var hasTools = toolCallCount > 0;
        var isHedging = ContainsHedging(answerText);

        // VeryLow — no grounding at all, or the model is explicitly admitting uncertainty.
        if ((!hasChunks && !hasTools) || isHedging)
        {
            var hedgeReason = isHedging
                ? "answer contains uncertainty phrases indicating the model lacked evidence"
                : "no RAG chunks retrieved and no tools called — answer is from general knowledge only";
            return new AnswerConfidence(ConfidenceLevel.VeryLow, hedgeReason);
        }

        // High — strong retrieval signal and at least one grounding tool call.
        if (hasChunks && topScore >= 0.75 && hasTools)
            return new AnswerConfidence(
                ConfidenceLevel.High,
                $"top chunk score {topScore:0.###} with {toolCallCount} tool call(s) providing grounded evidence");

        // High — very strong retrieval even without a tool call (RAG alone is sufficient).
        if (hasChunks && topScore >= 0.85)
            return new AnswerConfidence(
                ConfidenceLevel.High,
                $"top chunk score {topScore:0.###} — strong retrieval match covers the question");

        // Medium — decent retrieval or some tool grounding, but not both at high quality.
        if (hasChunks && topScore >= 0.5)
            return new AnswerConfidence(
                ConfidenceLevel.Medium,
                $"top chunk score {topScore:0.###} — retrieval match is adequate but not strong");

        if (hasTools && !hasChunks)
            return new AnswerConfidence(
                ConfidenceLevel.Medium,
                $"{toolCallCount} tool call(s) used but no RAG context was retrieved");

        // Low — chunks exist but similarity is weak.
        if (hasChunks)
            return new AnswerConfidence(
                ConfidenceLevel.Low,
                $"top chunk score {topScore:0.###} — retrieval match is too weak to strongly ground the answer");

        return new AnswerConfidence(ConfidenceLevel.Low, "limited grounding evidence available");
    }

    private static bool ContainsHedging(string text) =>
        HedgingPhrases.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));
}
