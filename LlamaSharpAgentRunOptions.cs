// File: LlamaSharpAgentRunOptions.cs
// (Anda bisa letakkan ini di namespace Agents Anda, misal: SFCore.AgentWorkflow.Core.AI.Agents)

using Microsoft.Agents.AI;
using System.Collections.Generic;

/// <summary>
    /// Opsi run kustom untuk LlamaSharpAIAgent.
    /// Mewarisi dari AgentRunOptions untuk menambahkan parameter
    /// yang spesifik untuk LlamaSharp/model lokal.
    /// </summary>
    public class LlamaSharpAgentRunOptions : AgentRunOptions
    {
        /// <summary>
        /// Sesuai dengan ChatOptions.MaxOutputTokens
        /// </summary>
        public int? MaxOutputTokens { get; set; }

        /// <summary>
        /// Sesuai dengan ChatOptions.Temperature
        /// </summary>
        public float? Temperature { get; set; }

        /// <summary>
        /// Sesuai dengan ChatOptions.TopP
        /// </summary>
        public float? TopP { get; set; }

        /// <summary>
        /// Sesuai dengan ChatOptions.StopSequences
        /// </summary>
        public IList<string>? StopSequences { get; set; }
        
        // Anda bisa tambahkan properti lain dari ChatOptions di sini (TopK, etc.)

        public LlamaSharpAgentRunOptions() : base() { }
    }
