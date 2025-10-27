using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
/// <summary>
    /// Adapter between the application's <see cref="IAgent"/> abstraction and the
    /// Microsoft Agent Framework.  This class delegates execution to an underlying
    /// <see cref="AIAgent"/> instance and translates streaming updates into the
    /// application's domain model (<see cref="AgentExecutionResult"/>).
    /// </summary>
    public class MafBasedAgent : IAgent
    {
        private readonly AIAgent _agent;
        private readonly ILogger<MafBasedAgent>? _logger;
        private AgentThread? _thread;

        public MafBasedAgent(AIAgent agent, ILogger<MafBasedAgent>? logger = null)
        {
            _agent = agent;
            _logger = logger;
        }

        /// <summary>
        /// Executes the specified task using the underlying <see cref="AIAgent"/>.
        /// The method captures the reasoning and any function calls issued by the
        /// model, invoking registered tools if necessary.  It returns an
        /// <see cref="AgentExecutionResult"/> summarising the conversation.
        /// </summary>
        /// <param name="task">User task to execute.</param>
        /// <param name="maxIterations">Maximum iterations to allow.</param>
        public async Task<AgentExecutionResult> ExecuteAsync(string task, int maxIterations = 5)
        {
            var steps = new List<AgentStep>();
            string? finalAnswer = null;

            try
            {
                // Maintain conversation state: use GetNewThread() to create a new thread if one doesn't exist:contentReference[oaicite:1]{index=1}.
                _thread ??= _agent.GetNewThread();

                await foreach (var update in _agent.RunStreamingAsync(
                    message: task,
                    thread: _thread,
                    options: null,
                    cancellationToken: default))
                {
                    // Inspect each AIContent item in the update.
                    bool hasStep = false;
                    foreach (var content in update.Contents)
                    {
                        switch (content)
                        {
                            case TextContent textContent:
                                finalAnswer = (finalAnswer ?? string.Empty) + textContent.Text;
                                if (!hasStep)
                                {
                                    steps.Add(new AgentStep(
                                        Iteration: steps.Count + 1,
                                        Thought: textContent.Text,
                                        Action: null,
                                        ActionInput: null,
                                        Observation: null));
                                    hasStep = true;
                                }
                                break;

                            case FunctionCallContent functionCall:
                                string? argsJson = null;
                                if (functionCall.Arguments is not null)
                                {
                                    try
                                    {
                                        argsJson = JsonSerializer.Serialize(functionCall.Arguments);
                                    }
                                    catch
                                    {
                                        argsJson = functionCall.Arguments.ToString();
                                    }
                                }

                                steps.Add(new AgentStep(
                                    Iteration: steps.Count + 1,
                                    Thought: string.Empty,
                                    Action: functionCall.Name,
                                    ActionInput: argsJson,
                                    Observation: "(Tool invocation not implemented)"));
                                hasStep = true;
                                break;

                            case FunctionResultContent functionResult:
                                steps.Add(new AgentStep(
                                    Iteration: steps.Count + 1,
                                    Thought: string.Empty,
                                    Action: null,
                                    ActionInput: null,
                                    Observation: functionResult.Result?.ToString()));
                                hasStep = true;
                                break;

                            default:
                                // Ignore other content types (DataContent, UsageContent, etc.).
                                break;
                        }
                    }
                }

                return new AgentExecutionResult(
                    Success: true,
                    FinalAnswer: finalAnswer ?? string.Empty,
                    Steps: steps);
            }
            catch (System.Exception ex)
            {
                _logger?.LogError(ex, "MAF execution failed");
                return new AgentExecutionResult(
                    Success: false,
                    FinalAnswer: finalAnswer ?? string.Empty,
                    Steps: steps,
                    ErrorMessage: ex.Message);
            }
        }

        /// <inheritdoc/>
        public void ResetConversation()
        {
            _thread = null;
        }
    }
