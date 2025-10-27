    public class LlamaModelConfig
    {
        public required string Name { get; init; }
        public required string ModelPath { get; init; }
        public uint? ContextSize { get; init; } = 4096;
        public int GpuLayerCount { get; init; } = 0;
        public int MainGpu { get; init; } = 0;
        public int Threads { get; init; } = -1;
        public uint BatchSize { get; init; } = 512;
        public uint UBatchSize { get; init; } = 512;
        public required string SystemPrompt { get; init; }
    }

    public class LlamaModelFactory
    {
        private readonly Dictionary<string, LlamaModelConfig> _configs;

        public LlamaModelFactory(IEnumerable<LlamaModelConfig> configs)
        {
            _configs = configs.ToDictionary(c => c.Name);
        }

        public LlamaModelConfig GetConfig(string name) =>
            _configs.TryGetValue(name, out var cfg) ? cfg :
                throw new KeyNotFoundException($"Unknown model: {name}");

        public LlamaSharpChatClient CreateClient(string name)
        {
            var config = GetConfig(name);
            return new LlamaSharpChatClient(config);
        }
    }

    public static class PromptProvider
    {
        // Contoh prompt per model; sesuaikan sesuai kebutuhan Anda.
        public static string GetSystemPrompt(string modelName) => modelName switch
        {
            "Qwen3-Coder"        => "You are Qwen3-Coder, a long-context code assistant. Use terse reasoning and optimize for repository-scale tasks.",
            "Deepseek-Coder-v2"  => "You are Deepseek-Coder-v2, an MoE coding assistant. Provide detailed explanations and handle up to 128K tokens.",
            "ZAI-GLM-4.5"        => "You are ZAI-GLM 4.5, a multilingual GLM agent. Answer in Indonesian unless instructed otherwise.",
            "Qwen2.5-Coder-7B"   => "You are Qwen2.5-Coder-7B, provide concise coding answers and follow instructions carefully.",
            "Codegemma2-7B"      => "You are CodeGemma2-7B, focus on C# examples.",
            "Codegemma2-12B"     => "You are CodeGemma2-12B, offer detailed and advanced coding assistance.",
            "ZAI-CodeGeex4"      => "You are ZAI CodeGeex4, a lightweight code assistant, return succinct code snippets.",
            _ => "You are a helpful coding assistant."
        };
    }
