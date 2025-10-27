// File: LlamaSharpAIAgent.cs
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

    public class LlamaSharpAIAgent : AIAgent
    {
        private readonly IChatClient _chatClient;
        private readonly ILogger<LlamaSharpAIAgent> _logger;

        public override string Name => "LlamaSharpAIAgent";
        public override string Description => "Agen MAF yang didukung oleh model LlamaSharp lokal.";

        public LlamaSharpAIAgent(
            IChatClient chatClient, 
            ILogger<LlamaSharpAIAgent> logger)
        {
            _chatClient = chatClient;
            _logger = logger;
        }

        // ... (GetNewThread dan DeserializeThread tetap sama) ...
        
        public override AgentThread GetNewThread()
        {
            return new LlamaSharpThread();
        }

        public override AgentThread DeserializeThread(JsonElement serializedThread, JsonSerializerOptions? jsonSerializerOptions = null)
        {
            try
            {
                var messages = serializedThread.Deserialize<List<ChatMessage>>(jsonSerializerOptions);
                return new LlamaSharpThread(messages ?? Enumerable.Empty<ChatMessage>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendeserialisasi LlamaSharpThread.");
                throw new JsonException("Data thread tidak valid.", ex);
            }
        }

        public override async IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentThread? thread = null,
            AgentRunOptions? options = null, // <-- Parameter 'options'
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var agentThread = thread ?? GetNewThread(); 
            
            if (agentThread is not LlamaSharpThread llamaThread)
            {
                throw new InvalidOperationException($"Agen ini hanya mendukung {nameof(LlamaSharpThread)}.");
            }

            await NotifyThreadOfNewMessagesAsync(llamaThread, messages, cancellationToken); 

            var allMessages = llamaThread.GetMessages();
            
            // *** PERUBAHAN UTAMA DI SINI ***
            ChatOptions? chatOptions = null;
            
            // Periksa apakah 'options' adalah subclass kustom kita
            if (options is LlamaSharpAgentRunOptions llamaOptions)
            {
                // Jika ya, petakan properti kustom kita ke ChatOptions
                chatOptions = new ChatOptions
                {
                    MaxOutputTokens = llamaOptions.MaxOutputTokens,
                    Temperature = llamaOptions.Temperature,
                    TopP = llamaOptions.TopP,
                    StopSequences = llamaOptions.StopSequences
                };
            }
            // Jika 'options' null atau hanya tipe dasar, 
            // 'chatOptions' akan tetap null, dan LlamaSharpChatClient
            // akan menggunakan nilai default-nya (ini aman).
            // *********************************

            var aggregatedContents = new List<AIContent>();
            ChatRole? responseRole = null;
            AgentRunResponseUpdate? lastUpdate = null;

            await foreach (var update in _chatClient.GetStreamingResponseAsync(
                allMessages, 
                chatOptions, // <-- Teruskan chatOptions
                cancellationToken))
            {
                var responseUpdate = new AgentRunResponseUpdate(update);
                
                if (responseUpdate.Role.HasValue) responseRole = responseUpdate.Role.Value;
                aggregatedContents.AddRange(responseUpdate.Contents);
                lastUpdate = responseUpdate;

                yield return responseUpdate;
            }

            if (responseRole.HasValue && aggregatedContents.Any())
            {
                var finalMessage = new ChatMessage(responseRole.Value, aggregatedContents);
                if(lastUpdate != null)
                {
                    finalMessage.MessageId = lastUpdate.MessageId;
                    finalMessage.AuthorName = lastUpdate.AuthorName;
                }
                await NotifyThreadOfNewMessagesAsync(
                    llamaThread, 
                    new[] { finalMessage },
                    cancellationToken);
            }
        }

        public override async Task<AgentRunResponse> RunAsync(
            IEnumerable<ChatMessage> messages,
            AgentThread? thread = null,
            AgentRunOptions? options = null, // <-- Parameter 'options'
            CancellationToken cancellationToken = default)
        {
            var activeThread = thread ?? GetNewThread();
            
            if (activeThread is not LlamaSharpThread llamaThread)
            {
                throw new InvalidOperationException($"Agen ini hanya mendukung {nameof(LlamaSharpThread)}.");
            }
            
            var allUpdates = new List<AgentRunResponseUpdate>();

            // Cukup teruskan 'options' ke RunStreamingAsync.
            // Logika pemetaan HANYA perlu ada di RunStreamingAsync.
            await foreach (var update in RunStreamingAsync(messages, llamaThread, options, cancellationToken))
            {
                allUpdates.Add(update);
            }
            
            // (Sisa logika agregasi tetap sama seperti sebelumnya)
            var allContents = new List<AIContent>();
            ChatRole? finalRole = null;
            string? agentId = null;
            string? responseId = null;
            DateTimeOffset? createdAt = null;
            UsageDetails? usage = null;

            foreach(var update in allUpdates)
            {
                if (update.Role.HasValue) finalRole = update.Role.Value;
                allContents.AddRange(update.Contents);
                if (update.AgentId != null) agentId = update.AgentId;
                if (update.ResponseId != null) responseId = update.ResponseId;
                if (update.CreatedAt.HasValue) createdAt = update.CreatedAt;
                
                var usageContent = update.Contents.OfType<UsageContent>().FirstOrDefault();
                if (usageContent != null)
                {
                    usage = usageContent.Details; 
                }
            }

            var filteredContents = allContents
                .Where(c => c != null && !(c is TextContent tc && string.IsNullOrEmpty(tc.Text)))
                .ToList();

            var finalMessage = new ChatMessage(finalRole ?? ChatRole.Assistant, filteredContents);

            var finalResponse = new AgentRunResponse(new List<ChatMessage> { finalMessage })
            {
                AgentId = agentId,
                ResponseId = responseId,
                CreatedAt = createdAt,
                Usage = usage,
                RawRepresentation = allUpdates.LastOrDefault()?.RawRepresentation
            };

            return finalResponse;
        }
    }
