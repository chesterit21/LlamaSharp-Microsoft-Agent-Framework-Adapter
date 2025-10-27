public interface IAgent
{
    Task<AgentExecutionResult> ExecuteAsync(string task, int maxIterations = 5);
    void ResetConversation();
}

public record AgentExecutionResult(
    bool Success,
    string FinalAnswer,
    List<AgentStep> Steps,
    string? ErrorMessage = null
);

public record AgentStep(
    int Iteration,
    string Thought,
    string? Action = null,
    string? ActionInput = null,
    string? Observation = null
);
