// File: LlamaSharpChatClient.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Abstractions;
using LLama.Common;
using Microsoft.Extensions.AI;
using SFCore.AgentWorkflow.Core.AI.Configs;

    /// <summary>
    /// Implementasi IChatClient yang membungkus LlamaSharp.
    /// 
    /// PENTING: Implementasi ini STATELESS.
    /// Ia membuat Context dan InteractiveExecutor (Stateful) baru 
    /// untuk SETIAP panggilan, lalu membuangnya.
    /// Ini memenuhi kontrak IChatClient (stateless) dan ChatSession (stateful).
    /// </summary>
    public class LlamaSharpChatClient : IChatClient, IDisposable
    {
        private readonly LLamaWeights _weights;
        private readonly ModelParams _parameters;
        private readonly string _systemPrompt;
        private bool _disposed = false;

        public LlamaSharpChatClient(LlamaModelConfig config)
        {
            _systemPrompt = config.SystemPrompt; // Simpan system prompt
            _parameters = new ModelParams(config.ModelPath)
            {
                ContextSize = config.ContextSize,
                GpuLayerCount = config.GpuLayerCount,
                MainGpu = config.MainGpu,
                UseMemorymap = true,
                UseMemoryLock = true,
                Embeddings = false,
                BatchSize = config.BatchSize,
                UBatchSize = config.UBatchSize,
                Threads = config.Threads >= 1 ? config.Threads : Environment.ProcessorCount,
                FlashAttention = true
            };

            _weights = LLamaWeights.LoadFromFile(_parameters);
        }

        /// <summary>
        /// Mengirim riwayat chat dan mengembalikan respons penuh dari model.
        /// </summary>
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var messageList = messages as IList<ChatMessage> ?? messages.ToList();
            if (!messageList.Any())
            {
                return new ChatResponse();
            }

            // *** PERBAIKAN ***
            // 1. Buat Context dan Executor BARU yang STATEFUL untuk panggilan ini.
            //    Blok 'using' akan membuangnya setelah selesai.
            using var context = _weights.CreateContext(_parameters);
            var executor = new InteractiveExecutor(context); // <-- Ini adalah StatefulExecutorBase

            // 2. Bangun session menggunakan executor yang baru
            var (session, lastMessage) = BuildLlamaSession(executor, messageList);

            // 3. Petakan ChatOptions ke InferenceParams
            var inferenceParams = new InferenceParams()
            {
                MaxTokens = 8176,// options?.MaxOutputTokens ?? -1,
                                 // Temperature = options?.Temperature ?? 0.8f,
                                 // TopP = options?.TopP ?? 0.95f,
                AntiPrompts = options?.StopSequences?.ToList() ?? new List<string>()
            };

            var sb = new StringBuilder();

            // 4. Panggil ChatAsync
            await foreach (var token in session.ChatAsync(
                lastMessage,
                inferenceParams: inferenceParams,
                cancellationToken: cancellationToken))
            {
                sb.Append(token);
            }

            var responseText = sb.ToString();
            var assistantMessage = new ChatMessage(
                role: ChatRole.Assistant,
                contents: new[] { new TextContent(responseText) });

            return new ChatResponse(new List<ChatMessage> { assistantMessage });
        }

        /// <summary>
        /// Mengirim riwayat chat dan mengembalikan stream pembaruan respons.
        /// </summary>
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var messageList = messages as IList<ChatMessage> ?? messages.ToList();
            if (!messageList.Any())
            {
                yield break;
            }

            // *** PERBAIKAN ***
            // 1. Buat Context dan Executor BARU yang STATEFUL untuk panggilan ini.
            //    Blok 'using' akan membuangnya setelah stream selesai.
            using var context = _weights.CreateContext(_parameters);
            var executor = new InteractiveExecutor(context); // <-- Ini adalah StatefulExecutorBase

            // 2. Bangun session menggunakan executor yang baru
            var (session, lastMessage) = BuildLlamaSession(executor, messageList);

            // 3. Petakan ChatOptions ke InferenceParams
            var inferenceParams = new InferenceParams()
            {
                MaxTokens = 8176,// options?.MaxOutputTokens ?? -1,
                // Temperature = options?.Temperature ?? 0.8f,
                // TopP = options?.TopP ?? 0.95f,
                AntiPrompts = options?.StopSequences?.ToList() ?? new List<string>()
            };

            // 4. Panggil ChatAsync
            await foreach (var token in session.ChatAsync(
                lastMessage,
                inferenceParams: inferenceParams,
                cancellationToken: cancellationToken))
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = new List<AIContent> { new TextContent(token) }
                };
            }
        }

        /// <summary>
        /// Helper untuk membangun ChatSession LlamaSharp dari IEnumerable<ChatMessage> MAF.
        /// </summary>
        private (ChatSession, ChatHistory.Message) BuildLlamaSession(
            // *** PERBAIKAN ***: Terima InteractiveExecutor
            InteractiveExecutor executor,
            IList<ChatMessage> messages)
        {
            var history = new ChatHistory();

            history.AddMessage(AuthorRole.System, _systemPrompt);

            for (int i = 0; i < messages.Count - 1; i++)
            {
                var msg = messages[i];
                var role = GetAuthorRole(msg.Role);
                if (role == AuthorRole.System) continue;

                history.AddMessage(role, GetTextFromMessage(msg));
            }

            // Ini sekarang akan berhasil karena executor-nya stateful
            var session = new ChatSession(executor, history);

            var lastMsg = messages.Last();
            var lastLlamaMessage = new ChatHistory.Message(
                GetAuthorRole(lastMsg.Role),
                GetTextFromMessage(lastMsg)
            );

            return (session, lastLlamaMessage);
        }

        /// <summary>
        /// Helper untuk mengekstrak teks gabungan dari ChatMessage.
        /// </summary>
        private string GetTextFromMessage(ChatMessage message)
        {
            return string.Concat(message.Contents.OfType<TextContent>().Select(c => c.Text));
        }

        /// <summary>
        /// Helper untuk menerjemahkan ChatRole (MAF) ke AuthorRole (LlamaSharp).
        /// </summary>
        private AuthorRole GetAuthorRole(ChatRole role) => role switch
        {
            _ when role == ChatRole.User => AuthorRole.User,
            _ when role == ChatRole.Assistant => AuthorRole.Assistant,
            _ when role == ChatRole.System => AuthorRole.System,
            _ => AuthorRole.Unknown
        };

        public object? GetService(System.Type serviceType, object? serviceName = null)
        {
            return null;
        }

        // --- Logika Dispose ---
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Tidak ada managed objects (seperti _session) untuk di-dispose
                }
                _weights.Dispose(); // Hanya weights yang perlu di-dispose
                _disposed = true;
            }
        }

        ~LlamaSharpChatClient()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
