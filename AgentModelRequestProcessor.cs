// File: SFCore.AgentWorkflow.Core/AI/Services/AgentModelRequestProcessor.cs
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SFCore.AgentWorkflow.Core.AI.Clients;
using SFCore.AgentWorkflow.Application.Common.DTOs.AgentHub;
using SFCore.AgentWorkflow.Application.Common.Interfaces;
using SFCore.AgentWorkflow.Core.AI.Factories;

//--Example hoe to use LlamaSharpChatClient

    public class AgentModelRequestProcessor
    {
        private readonly LlamaModelFactory _factory;
        private readonly IAgentHubService _hubService; // SignalR hub untuk progres
        private readonly ILogger<AgentModelRequestProcessor> _logger;

        // Peta sederhana dari jenis permintaan ke nama model. Bisa dipindahkan ke konfigurasi.
        private readonly Dictionary<string, string> _requestToModel = new()
        {
            { "code",   "Qwen2.5-Coder-7B" },
            { "agent",  "Qwen3-Coder" },
            { "analysis", "Deepseek-Coder-v2" },
            { "default", "ZAI-GLM-4.5" }
        };

        public AgentModelRequestProcessor(
            LlamaModelFactory factory,
            IAgentHubService hubService,
            ILogger<AgentModelRequestProcessor> logger)
        {
            _factory = factory;
            _hubService = hubService;
            _logger = logger;
        }

        public async Task<string> ProcessAsync(string rawPrompt, Guid userId,
            CancellationToken cancellationToken = default)
        {
            // 1. Ekstrak bagian‑bagian prompt (chat, info tambahan, jenis permintaan)
            var parts = (rawPrompt ?? string.Empty).Split('#');
            var chat = parts.Length > 0 ? parts[0] : string.Empty;
            var info = parts.Length > 1 ? parts[1] : string.Empty;
            var requestType = parts.Length > 2 ? parts[2].ToLowerInvariant() : "default";

            // 2. Tentukan model yang akan digunakan
            if (!_requestToModel.TryGetValue(requestType, out var modelName))
            {
                modelName = _requestToModel["default"];
            }

            var config = _factory.GetConfig(modelName);
            var systemMsg = $"{config.SystemPrompt}\n\n[Informasi tambahan: {info}]";

            // 3. Inisialisasi client untuk model ini
            using var chatClient = new LlamaSharpChatClient(config);

            // 4. Loop dialog: kirim pesan user dan terima balasan. Lakukan iterasi jika diperlukan.
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, systemMsg),
                new ChatMessage(ChatRole.User, chat)
            };

            string finalAnswer = string.Empty;
            int iteration = 1;
            while (!cancellationToken.IsCancellationRequested)
            {
                var response = await chatClient.GetResponseAsync(messages, null, cancellationToken);
                finalAnswer = response.Text.Trim();

                // Kirim progres via SignalR (gunakan hub service Anda)
                var update = new AgentProgressUpdate
                {
                    TaskId = Guid.NewGuid(),
                    Stage = $"Iteration {iteration}",
                    Message = finalAnswer,
                    ProgressPercentage = Math.Min(95, iteration * 20) // contoh
                };
                await _hubService.SendProgressUpdateAsync(userId, update);

                // Cek kondisi berhenti: misalnya ada tag [DONE] dalam jawaban
                if (finalAnswer.Contains("[DONE]") || iteration >= 5)
                {
                    break;
                }

                // Rekonstruksi prompt dengan tugas berikutnya (contoh: tambahkan jawaban sebagai konteks)
                messages.Add(new ChatMessage(ChatRole.Assistant, finalAnswer));
                messages.Add(new ChatMessage(ChatRole.User, "Selesaikan langkah berikutnya."));
                iteration++;
            }

            // Satu progres final 100%
            await _hubService.SendProgressUpdateAsync(userId, new AgentProgressUpdate
            {
                TaskId = Guid.NewGuid(),
                Stage = "Completed",
                Message = finalAnswer,
                ProgressPercentage = 100
            });

            return finalAnswer;
        }
    }

