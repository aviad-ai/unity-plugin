using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace Aviad
{
    public class AviadAsync
    {
        private static AviadAsync _instance;
        private static readonly object _instanceLock = new();
        public static bool IsCreated => _instance != null;

        private bool _isInitialized = false;
        private bool _isInitializing = false;
        private bool _hasInitialContext = false;
        private readonly object _lock = new();
        private Task _initializationTask = null;

        public static AviadAsync Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                        {
                            _instance = new AviadAsync();
                        }
                    }
                }
                return _instance;
            }
        }

        private AviadAsync() { }

        public async Task Initialize(LlamaModelParams modelParams, bool loggingEnabled)
        {
            if (_isInitialized) return;

            float startTime = 0;
            if (RuntimeContext.GetLoggingLevel() >= LoggingLevel.Debug)
                startTime = Time.realtimeSinceStartup;

            _isInitializing = true;
            try
            {
                _initializationTask = Task.Run(() =>
                {
                    lock (_lock)
                    {
                        if (_isInitialized) return;
                        AviadGeneration.SetLoggingEnabled(loggingEnabled);
                        AviadGeneration.Initialize(ref modelParams);
                        _isInitialized = true;
                    }
                });

                await _initializationTask;

                if (RuntimeContext.GetLoggingLevel() >= LoggingLevel.Debug)
                {
                    float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
                    Debug.Log($"[Aviad] [Debug] Model initialization completed in {elapsed:F1} ms.");
                }
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public async Task SetInitialContext(
            string[] roles,
            string[] contents,
            string chatTemplate)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("AviadAsync not initialized.");

            float startTime = 0;
            if (RuntimeContext.GetLoggingLevel() >= LoggingLevel.Debug)
                startTime = Time.realtimeSinceStartup;

            await Task.Run(() =>
            {
                var sequence = MarshalMessageSequence(roles, contents);
                try
                {
                    if (!AviadGeneration.DecodeToKVCache(ref sequence, chatTemplate))
                        throw new Exception("DecodeToKVCache failed.");
                    _hasInitialContext = true;
                }
                finally
                {
                    FreeMessageSequence(ref sequence, roles.Length);
                }
            });
            if (RuntimeContext.GetLoggingLevel() >= LoggingLevel.Debug)
            {
                float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
                Debug.Log($"[Aviad] [Debug] Initial context decoded in {elapsed:F1} ms.");
            }        }

        public async Task ResetContext()
        {
            if (!_isInitialized)
                throw new InvalidOperationException("AviadAsync not initialized.");
            if (!_hasInitialContext)
                throw new InvalidOperationException("AviadAsync did not have initial context set.");

            await Task.Run(() =>
            {
                if (!AviadGeneration.LoadKVCache())
                    throw new Exception("LoadKVCache failed.");
            });
        }

        public async Task<string> GenerateInitial(
            string[] roles,
            string[] contents,
            LlamaGenerationConfig config)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("Aviad must be initialized before generating response.");

            float startTime = 0;
            if (RuntimeContext.GetLoggingLevel() >= LoggingLevel.Debug)
                startTime = Time.realtimeSinceStartup;

            string result = await Task.Run(() =>
            {
                var sequence = MarshalMessageSequence(roles, contents);
                try
                {
                    return AviadGeneration.GenerateResponse(ref sequence, ref config);
                }
                finally
                {
                    FreeMessageSequence(ref sequence, roles.Length);
                }
            });

            if (RuntimeContext.GetLoggingLevel() >= LoggingLevel.Debug)
            {
                float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
                Debug.Log($"[Aviad] [Debug] GenerateInitial completed in {elapsed:F1} ms.");
            }

            return result;
        }

        public async Task GenerateInitialStreaming(
            string[] roles,
            string[] contents,
            LlamaGenerationConfig config,
            AviadGeneration.TokenStreamCallback onToken,
            AviadGeneration.StreamDoneCallback onDone,
            int chunkSize)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("Aviad must be initialized before streaming response.");

            float startTime = 0;
            if (RuntimeContext.GetLoggingLevel() >= LoggingLevel.Debug)
                startTime = Time.realtimeSinceStartup;

            await Task.Run(() =>
            {
                var sequence = MarshalMessageSequence(roles, contents);
                try
                {
                    if (!AviadGeneration.GenerateResponseStreaming(ref sequence, ref config, onToken, onDone, chunkSize))
                        throw new Exception("GenerateResponseStreaming failed.");
                }
                finally
                {
                    FreeMessageSequence(ref sequence, roles.Length);
                }
            });

            if (RuntimeContext.GetLoggingLevel() >= LoggingLevel.Debug)
            {
                float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
                Debug.Log($"[Aviad] [Debug] GenerateInitialStreaming completed in {elapsed:F1} ms.");
            }
        }

        public async Task Shutdown()
        {
            if (!_isInitialized && !_isInitializing) return;

            // If initialization is in progress, wait for it to complete
            if (_isInitializing && _initializationTask != null)
            {
                try
                {
                    await _initializationTask;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Aviad] Error waiting for initialization during shutdown: {e.Message}");
                }
            }

            await Task.Run(() =>
            {
                try
                {
                    AviadGeneration.Shutdown();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Aviad] Error during shutdown: {e.Message}");
                }
                finally
                {
                    Debug.Log("[Aviad] Model shutdown successful");
                    _isInitialized = false;
                    _isInitializing = false;
                    _initializationTask = null;
                }
            });
        }

        private static LlamaMessageSequence MarshalMessageSequence(string[] roles, string[] contents)
        {
            if (roles.Length != contents.Length)
                throw new ArgumentException("Roles and contents must have the same length.");

            int count = roles.Length;
            var rolePtrs = new IntPtr[count];
            var contentPtrs = new IntPtr[count];

            for (int i = 0; i < count; i++)
            {
                rolePtrs[i] = Marshal.StringToHGlobalAnsi(roles[i]);
                contentPtrs[i] = Marshal.StringToHGlobalAnsi(contents[i]);
            }

            IntPtr rolesPtr = Marshal.AllocHGlobal(IntPtr.Size * count);
            IntPtr contentsPtr = Marshal.AllocHGlobal(IntPtr.Size * count);
            Marshal.Copy(rolePtrs, 0, rolesPtr, count);
            Marshal.Copy(contentPtrs, 0, contentsPtr, count);

            return new LlamaMessageSequence
            {
                roles = rolesPtr,
                contents = contentsPtr,
                message_count = count
            };
        }

        private static void FreeMessageSequence(ref LlamaMessageSequence sequence, int count)
        {
            for (int i = 0; i < count; i++)
            {
                IntPtr rolePtr = Marshal.ReadIntPtr(sequence.roles, i * IntPtr.Size);
                IntPtr contentPtr = Marshal.ReadIntPtr(sequence.contents, i * IntPtr.Size);

                Marshal.FreeHGlobal(rolePtr);
                Marshal.FreeHGlobal(contentPtr);
            }

            Marshal.FreeHGlobal(sequence.roles);
            Marshal.FreeHGlobal(sequence.contents);
        }
    }
}
