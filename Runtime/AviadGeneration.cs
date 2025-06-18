using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using UnityEngine;

namespace Aviad
{
    public static class AviadGeneration
    {
        private static IntPtr _library;
        private static bool _initialized;
        private static readonly List<IntPtr> _dependentLibraries = new List<IntPtr>();


        private static readonly string[] PreloadDependencies =
        {
            "ggml-base.dll",
            "ggml-cpu.dll",
            "ggml.dll",
            "llama.dll"
        };
        
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void LogCallbackWithLevelDelegate(int level, [MarshalAs(UnmanagedType.LPStr)] string message);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void TokenStreamCallback([MarshalAs(UnmanagedType.LPStr)] string token);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void StreamDoneCallback([MarshalAs(UnmanagedType.I1)] bool done);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool SetLogCallbackDelegate(LogCallbackWithLevelDelegate callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool InitializeGenerationModelDelegate(ref LlamaModelParams modelParams);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool DecodeToKVCacheDelegate(ref LlamaMessageSequence messages, string chatTemplate);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool LoadKVCacheDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool GenerateResponseDelegate(
            ref LlamaMessageSequence messageSequence,
            ref LlamaGenerationConfig config,
            out IntPtr outResponse);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool GenerateResponseStreamingDelegate(
            ref LlamaMessageSequence messageSequence,
            ref LlamaGenerationConfig config,
            TokenStreamCallback onToken,
            StreamDoneCallback onDone,
            int chunkSize);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool FreeResponseDelegate(IntPtr response);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool ShutdownGenerationModelDelegate();

        private static SetLogCallbackDelegate _setLogCallback;
        private static InitializeGenerationModelDelegate _initialize;
        private static DecodeToKVCacheDelegate _decodeToKVCache;
        private static LoadKVCacheDelegate _loadKVCache;
        private static GenerateResponseDelegate _generateResponse;
        private static GenerateResponseStreamingDelegate _generateResponseStreaming;
        private static FreeResponseDelegate _free;
        private static ShutdownGenerationModelDelegate _shutdown;
        
        private static readonly LogCallbackWithLevelDelegate DefaultLogCallbackWithLevel = (level, message) =>
        {
            Debug.Log($"[Aviad:Native][Level {level}] {message}");
        };

        public static void SetLoggingEnabled(bool enabled)
        {
            EnsureLoaded();
            if (enabled)
            {
                _setLogCallback(DefaultLogCallbackWithLevel);
            }
        }

        public static bool Initialize(ref LlamaModelParams modelParams)
        {
            EnsureLoaded();
            return _initialize(ref modelParams);
        }

        public static bool DecodeToKVCache(ref LlamaMessageSequence messages, string chatTemplate)
        {
            EnsureLoaded();
            return _decodeToKVCache(ref messages, chatTemplate);
        }

        public static bool LoadKVCache()
        {
            EnsureLoaded();
            return _loadKVCache();
        }

        public static string GenerateResponse(ref LlamaMessageSequence messages, ref LlamaGenerationConfig config)
        {
            EnsureLoaded();

            if (!_generateResponse(ref messages, ref config, out IntPtr resultPtr))
            {
                Debug.LogError("[Aviad] Native generate_response failed.");
                return "[ERROR]";
            }

            string result = Marshal.PtrToStringAnsi(resultPtr);
            _free(resultPtr);
            return result;
        }

        public static bool GenerateResponseStreaming(
            ref LlamaMessageSequence messages,
            ref LlamaGenerationConfig config,
            TokenStreamCallback onToken,
            StreamDoneCallback onDone,
            int chunkSize)
        {
            EnsureLoaded();

            return _generateResponseStreaming(ref messages, ref config, onToken, onDone, chunkSize);
        }

        public static void Shutdown()
        {
            if (_initialized)
            {

                _shutdown();
                _initialized = false;
                _setLogCallback = null;
                _initialize = null;
                _decodeToKVCache           = null;
                _loadKVCache               = null;
                _generateResponse          = null;
                _generateResponseStreaming = null;
                _free                      = null;
                _shutdown                  = null;
            }
            foreach (var handle in _dependentLibraries)
            {
                if (handle != IntPtr.Zero)
                {
                    LibraryLoader.FreeLibrary(handle);
                }
            }
            _dependentLibraries.Clear();
            if (_library != IntPtr.Zero)
            {
                LibraryLoader.FreeLibrary(_library);
            }
        }

        private static void EnsureLoaded()
        {
            if (_initialized) return;

            string arch = Environment.Is64BitProcess ? "x86_64" : "x86";
            string basePath = Path.Combine(Application.streamingAssetsPath, "Aviad", "bin", arch);

            foreach (var dll in PreloadDependencies)
            {
                string depPath = Path.Combine(basePath, dll);
                var handle = LibraryLoader.LoadLibrary(depPath);
                if (handle == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    Debug.LogError($"[Aviad] Failed to load '{dll}' (Error {err})");
                    throw new DllNotFoundException($"Missing dependency: {dll}");
                }
                _dependentLibraries.Add(handle);
            }

            string aviadPath = Path.Combine(basePath, "aviad-main.dll");
            _library = LibraryLoader.LoadLibrary(aviadPath);
            if (_library == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                throw new DllNotFoundException($"Unable to load aviad-main.dll (Error {err})");
            }

            _setLogCallback            = LibraryLoader.GetSymbolDelegate<SetLogCallbackDelegate>(_library, "set_log_callback");
            _initialize                = LibraryLoader.GetSymbolDelegate<InitializeGenerationModelDelegate>(_library, "initialize_generation_model");
            _decodeToKVCache           = LibraryLoader.GetSymbolDelegate<DecodeToKVCacheDelegate>(_library, "decode_to_kv_cache");
            _loadKVCache               = LibraryLoader.GetSymbolDelegate<LoadKVCacheDelegate>(_library, "load_kv_cache");
            _generateResponse          = LibraryLoader.GetSymbolDelegate<GenerateResponseDelegate>(_library, "generate_response");
            _generateResponseStreaming = LibraryLoader.GetSymbolDelegate<GenerateResponseStreamingDelegate>(_library, "generate_response_streaming");
            _free                      = LibraryLoader.GetSymbolDelegate<FreeResponseDelegate>(_library, "free_response");
            _shutdown                  = LibraryLoader.GetSymbolDelegate<ShutdownGenerationModelDelegate>(_library, "shutdown_generation_model");
            _initialized = true;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct LlamaModelParams
    {
        public string model_path;
        public int max_context_length;
        public int gpu_layers;
        public int threads;
        public int max_batch_length;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LlamaMessageSequence
    {
        public IntPtr roles;    // char**
        public IntPtr contents; // char**
        public int message_count;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct LlamaGenerationConfig
    {
        public string chatTemplate;
        public string grammarString;
        public float temperature;
        public float top_p;
        public int max_tokens;
    }
}
