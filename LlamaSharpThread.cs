// File: LlamaSharpThread.cs
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
    /// <summary>
    /// Implementasi AgentThread konkret sederhana untuk LlamaSharpAIAgent.
    /// Ini hanya menyimpan pesan dalam daftar di memori.
    /// </summary>
    public class LlamaSharpThread : AgentThread
    {
        private readonly List<ChatMessage> _messages = new();

        // Konstruktor untuk thread baru
        public LlamaSharpThread() { }

        // Konstruktor untuk deserialisasi (jika diperlukan)
        public LlamaSharpThread(IEnumerable<ChatMessage> messages)
        {
            _messages.AddRange(messages);
        }

        // PERBAIKAN 1: 'GetMessagesAsync' dihapus karena tidak ada di base class.

        // PERBAIKAN 2: Access modifier diubah menjadi 'protected' agar sesuai
        // dengan aturan override 'protected internal' dari assembly eksternal.
        protected override Task MessagesReceivedAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
        {
            _messages.AddRange(messages);
            return Task.CompletedTask;
        }

        // PERBAIKAN 3: Tambahkan method 'public' (bukan override) agar 
        // LlamaSharpAIAgent bisa mengambil pesan-pesan ini.
        public IEnumerable<ChatMessage> GetMessages()
        {
            return _messages.AsReadOnly();
        }

        public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
        {
            // Implementasi serialisasi sederhana jika Anda perlu menyimpan thread
            var json = JsonSerializer.Serialize(_messages, jsonSerializerOptions);
            return JsonDocument.Parse(json).RootElement;
        }
    }
