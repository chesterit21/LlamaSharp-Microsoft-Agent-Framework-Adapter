# README: LlamaSharp Adapter for Microsoft Agents Framework (MAF)
````
# LlamaSharp MAF Bridge

This project integrates local GGUF models from **LlamaSharp** with the **Microsoft Agent Framework (MAF)**. It wraps local AI models—such as Qwen, DeepSeek and CodeGemma—as `IChatClient` implementations so they can be used within MAF for agentic tasks like function calling, streaming responses and tool orchestration.

## Key Features

- **Multi‑model configuration** via `LlamaModelConfig`: customise context size, GPU layer count, system prompt and more.
- **`LlamaSharpChatClient` adaptor**: implements `IChatClient` so LlamaSharp models plug into MAF seamlessly.
- **`ChatRequestProcessor` service**: splits user requests (by `#`), selects the appropriate model, orchestrates multi‑turn dialogues and publishes progress via SignalR.
- **Sample ASP.NET Core endpoints** for sending prompts and receiving responses via SSE or through MAF agents.

## Installation

1. Ensure you have .NET 8 installed and a CUDA‑enabled GPU if you plan to offload layers.
2. Clone or download this repository.
3. Add the following NuGet packages to your API project:

   ```bash
   dotnet add package LLamaSharp
   dotnet add package Microsoft.Agents.AI
   dotnet add package Microsoft.Agents.AI.OpenAI  # optional: for cloud providers
````

4. Place your `.gguf` model files in a `Models/` folder or adjust the `ModelPath` in configuration accordingly.

## Model Configuration

Models are described with the `LlamaModelConfig` class. You can define multiple configurations to support different models and register them with the DI container:

```csharp
var modelConfigs = new[]
{
    new LlamaModelConfig
    {
        Name         = "Qwen2.5-Coder-7B",
        ModelPath    = "Models/qwen2.5-coder-7b.gguf",
        ContextSize  = 8192,
        GpuLayerCount= 20,
        MainGpu      = 0,
        Threads      = 20,
        BatchSize    = 512,
        UBatchSize   = 256,
        SystemPrompt = "You are a helpful coding assistant."
    },
    // Add other models here...
};

builder.Services.AddSingleton(new LlamaModelFactory(modelConfigs));
```

When choosing `ContextSize` and `GpuLayerCount`, be mindful of your GPU’s VRAM capacity. For an RTX 4060 8 GB, offloading 10–20 layers to the GPU and using memory‑mapped model loading is a good starting point.

## Implementation Example

### Chat Request Endpoint (ASP.NET Core)

A sample endpoint that accepts a query parameter `prompt`, splits it on `#`, and delegates processing to `ChatRequestProcessor`:

```csharp
// ChatApiEndpoints.cs
group.MapGet("", async ([FromQuery] string prompt,
                        [FromServices] ChatRequestProcessor processor) =>
{
    var userId = Guid.NewGuid(); // replace with actual user id
    var result = await processor.ProcessAsync(prompt, userId);
    return Results.Ok(new { result });
});
```

### ChatRequestProcessor Service

```csharp
public class ChatRequestProcessor
{
    private readonly LlamaModelFactory _factory;
    private readonly IAgentHubService _hubService;

    private readonly Dictionary<string, string> _requestToModel = new()
    {
        { "code",     "Qwen2.5-Coder-7B" },
        { "analysis", "Deepseek-Coder-v2" },
        { "agent",    "Qwen3-Coder" },
        { "default",  "ZAI-GLM-4.5" }
    };

    public async Task<string> ProcessAsync(string rawPrompt, Guid userId,
        CancellationToken cancellationToken = default)
    {
        var parts = (rawPrompt ?? string.Empty).Split('#');
        var chat  = parts.Length > 0 ? parts[0] : string.Empty;
        var info  = parts.Length > 1 ? parts[1] : string.Empty;
        var type  = parts.Length > 2 ? parts[2].ToLowerInvariant() : "default";

        _requestToModel.TryGetValue(type, out var modelName);
        modelName ??= "default";

        var config    = _factory.GetConfig(modelName);
        var systemMsg = $"{config.SystemPrompt}\n\n[Info: {info}]";

        using var client = new LlamaSharpChatClient(config);
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, systemMsg),
            new ChatMessage(ChatRole.User, chat)
        };
        var response = await client.GetResponseAsync(messages, null, cancellationToken);
        await _hubService.SendProgressUpdateAsync(userId, new AgentProgressUpdate
        {
            TaskId = Guid.NewGuid(),
            Stage  = "Completed",
            Message = response.Text,
            ProgressPercentage = 100
        });
        return response.Text;
    }
}
```

### MAF Integration for Agent Endpoints

To run MAF agents with LlamaSharp, register `IChatClient`, build an `AIAgent`, and inject a `MafBasedAgent`:

```csharp
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var cfg = sp.GetRequiredService<LlamaModelFactory>().GetConfig("Qwen2.5-Coder-7B");
    return new LlamaSharpChatClient(cfg);
});
builder.Services.AddScoped<AIAgent>(sp =>
{
    var chatClient  = sp.GetRequiredService<IChatClient>();
    var toolManager = sp.GetRequiredService<IToolManager>();
    var functions   = toolManager.GetAIFunctions();
    var instructions = cfg.SystemPrompt;
    return new ChatClientAgent(chatClient, instructions, name: "SfAgent", tools: functions);
});
builder.Services.AddScoped<IAgent, MafBasedAgent>();
```

Your `/api/agent` endpoints can then inject `IAgent` and call `ExecuteAsync` without worrying about the underlying model.

## Contributing

Contributions are welcome! Open an issue or send a pull request to add new model support, enhance context management or fix bugs.

## License

This project is licensed under the MIT License. You are free to use, modify and distribute it provided you include the copyright notice.

```
```
