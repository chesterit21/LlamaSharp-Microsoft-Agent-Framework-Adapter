# LlamaSharp-Microsoft-Agent-Framework-Adapter
LlamaSharp MAF Bridge / Jembatan LlamaSharp dengan Agent Framework
<!-- This README provides a concise, bilingual description of the project. The Indonesian sections come first, followed by the English equivalents. -->
# README: LlamaSharp Adapter for Microsoft Agents Framework (MAF)

## 1. Ikhtisar 🚀

Dokumen ini menjelaskan arsitektur dan implementasi *adapter* kustom yang menjembatani *library* **LlamaSharp** (untuk inferensi LLM lokal) dengan **Microsoft Agents Framework (MAF)**.

Tujuan utama *adapter* ini adalah agar `AIAgent` (abstraksi agen utama MAF) dapat menggunakan model GGUF lokal (seperti GLM, Llama, dll.) alih-alih memanggil API cloud seperti OpenAI.

## 2. Masalah & Solusi

* **Masalah:** `AIAgent` (MAF) dirancang untuk bekerja dengan `IChatClient`, sebuah antarmuka *stateless* yang mengharapkan *seluruh* riwayat obrolan dikirim di setiap panggilan. Di sisi lain, `ChatSession` LlamaSharp dirancang untuk *stateful* dan memelihara historinya sendiri. Menggabungkan keduanya secara naif menyebabkan *error* (seperti `Executor must be StatefulExecutorBase`) atau kebocoran histori antar-sesi.
* **Solusi:** *Adapter* ini mengimplementasikan `IChatClient` secara *stateless* dengan **membuat `LLamaContext` dan `InteractiveExecutor` (yang stateful) baru di dalam *setiap* panggilan**, lalu segera membuangnya (`using` block). Ini memenuhi kontrak *stateless* `IChatClient` sambil tetap memberi `ChatSession` *executor stateful* yang dibutuhkannya.

## 3. Komponen Utama 🛠️

Arsitektur *adapter* ini terdiri dari 4 kelas utama:

1.  **`LlamaSharpChatClient.cs` (Inti Adapter)**
    * Mengimplementasikan `IChatClient`.
    * **STATELESS**: Tidak menyimpan `ChatSession` atau `LLamaContext`.
    * Di setiap panggilan (`GetResponseAsync` / `GetStreamingResponseAsync`), ia membuat `LLamaContext` dan `InteractiveExecutor` baru, membangun histori, menjalankan inferensi, dan membuang *context* tersebut.
    * Menerima `ChatOptions` (dari MAF) dan memetakannya ke `InferenceParams` (LlamaSharp), termasuk `MaxOutputTokens`, `Temperature`, dll.

2.  **`LlamaSharpAIAgent.cs` (Jembatan MAF)**
    * Mengimplementasikan *abstract class* `AIAgent`.
    * Ini adalah kelas yang disuntikkan (`inject`) ke `MafBasedAgent` Anda.
    * Tugasnya adalah menerima `AgentRunOptions` (dari *caller*), mengubahnya menjadi `ChatOptions`, dan meneruskannya ke `IChatClient` (`LlamaSharpChatClient`).
    * Menggunakan `LlamaSharpThread` untuk mengelola histori obrolan.

3.  **`LlamaSharpThread.cs`**
    * Implementasi *abstract class* `AgentThread` yang sangat sederhana.
    * Pada dasarnya hanya sebuah `List<ChatMessage>` di dalam memori untuk menyimpan histori dari satu sesi `RunAsync`.

4.  **`LlamaSharpAgentRunOptions.cs`**
    * Subclass kustom dari `AgentRunOptions` (yang bawaannya kosong).
    * Kita menambahkannya properti kustom seperti `MaxOutputTokens`, `Temperature`, dll. Ini adalah cara kita meneruskan parameter inferensi dari lapisan aplikasi tertinggi (`MafBasedAgent`) turun ke lapisan `LlamaSharpChatClient`.

## 4. Setup Dependency Injection (`Program.cs`) ⚙️

Ini adalah bagian terpentING untuk menghindari *error* `Unable to resolve service`. Pastikan registrasi DI Anda di `Program.cs` terlihat seperti ini:

```csharp
// --- Di Program.cs ---
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI; 
using SFCore.AgentWorkflow.Core.AI.Agents;
using SFCore.AgentWorkflow.Core.AI.Clients; 
using SFCore.AgentWorkflow.Core.AI.Configs;
using SFCore.AgentWorkflow.Core.AI.Factories;

// ...
var builder = WebApplication.CreateBuilder(args);

// 1. Daftarkan LlamaModelFactory Anda (seperti yang sudah Anda miliki)
// (Contoh mengambil dari config)
var modelConfigs = new[]
{
    new LlamaModelConfig
    {
        Name = "Default", 
        ModelPath    = builder.Configuration["AI:ModelPath"],
        ContextSize  = 8192,
        GpuLayerCount= 50, // Sesuaikan
        SystemPrompt = "You are a helpful AI assistant." // Atur system prompt Anda
    }
};
builder.Services.AddSingleton(new LlamaModelFactory(modelConfigs));


// =========================================================
// ===     PENDAFTARAN MAF UNTUK LLAMASHARP (INTI)     ===
// =========================================================

// 2. Daftarkan LlamaSharpChatClient sebagai IChatClient
//    Gunakan 'Scoped' karena IDisposable (memegang LLamaWeights)
//    dan kita ingin satu instance per request.
builder.Services.AddScoped<IChatClient>(sp => 
{
    var factory = sp.GetRequiredService<LlamaModelFactory>();
    // 'CreateClient' akan membuat LlamaSharpChatClient baru
    return factory.CreateClient("Default"); 
});

// 3. Daftarkan implementasi AIAgent KONKRET Anda
//    Ini adalah perbaikan untuk error DI 'Unable to resolve service'.
//    Gunakan 'Scoped' karena bergantung pada 'IChatClient' (yang Scoped).
builder.Services.AddScoped<AIAgent, LlamaSharpAIAgent>();

// =========================================================
    
// 4. Daftarkan agen-agen aplikasi Anda
//    Pendaftaran 'MafBasedAgent' Anda sekarang akan berhasil
//    karena DI tahu cara membuat 'AIAgent'.
builder.Services.AddScoped<IAgent, MafBasedAgent>(); 
// builder.Services.AddScoped<IAgent, ReActAgent>(); // Agen lama Anda (jika masih ada)

// ...
// Daftarkan sisa servis Anda (AgentOrchestrationService, dll)
// ...
```

## 5. Cara Penggunaan (Contoh) 💡

Setelah DI diatur, Anda dapat menyuntikkan (`inject`) `AIAgent` atau `MafBasedAgent` Anda. Untuk mengatur `MaxOutputTokens`, buat instance dari `LlamaSharpAgentRunOptions` kustom kita.

**Contoh di `MafBasedAgent.cs` atau `AgentOrchestrationService.cs`:**

```csharp
using Microsoft.Agents.AI;
using SFCore.AgentWorkflow.Core.AI.Agents; // <-- Impor options kustom

public class MyAgentService
{
    private readonly AIAgent _agent; // Suntik AIAgent
    private AgentThread? _thread; // Simpan thread untuk percakapan

    public MyAgentService(AIAgent agent)
    {
        _agent = agent;
    }

    public async Task<string> SendMessageAsync(string userMessage)
    {
        // 1. Pastikan thread ada
        _thread ??= _agent.GetNewThread();

        // 2. Buat options kustom kita
        var runOptions = new LlamaSharpAgentRunOptions
        {
            MaxOutputTokens = 4096, // <-- Atur MaxOutputTokens di sini
            Temperature = 0.7f,
            TopP = 0.9f
        };

        // 3. Panggil agent dengan options tersebut
        var response = await _agent.RunAsync(
            messages: new[] { new ChatMessage(ChatRole.User, userMessage) },
            thread: _thread,
            options: runOptions, // <-- Teruskan options kustom
            cancellationToken: default);

        // 'response.Text' berisi jawaban akhir dari AI
        return response.Text;
    }
}
```

## 6. Catatan Penting & Performa ⚠️

* **Stateless = Performa**: Pendekatan ini 100% *stateless* dan aman dari kebocoran histori. Namun, ia memiliki biaya performa karena `LLamaContext` dan `InteractiveExecutor` dibuat **di setiap pesan**. Ini berarti model harus memproses ulang *seluruh* riwayat obrolan (termasuk *system prompt*) setiap kali Anda mengirim pesan.
* **mlock Warning**: Anda mungkin melihat *warning* `failed to mlock ... Cannot allocate memory` di log konsol Anda. Ini adalah *warning* performa Linux, bukan *error* fatal. Ini berarti model tidak dikunci di RAM. Untuk performa produksi, jalankan `ulimit -l unlimited` sebagai root sebelum memulai aplikasi.
Lisensi / License

Proyek ini dilisensikan di bawah MIT License
. Anda bebas menggunakan, memodifikasi, dan mendistribusikannya, selama mencantumkan pemberitahuan hak cipta.
