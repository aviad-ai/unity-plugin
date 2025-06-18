using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using System.Text;
using System;

namespace Aviad
{
    public class AviadDialogue
    {
        private bool isInitialized = false;

        public bool IsInitialized => isInitialized;

        private readonly List<string> roles = new();
        private readonly List<string> contents = new();

        public IReadOnlyList<string> Roles => roles;
        public IReadOnlyList<string> Contents => contents;

        private string systemPrompt;
        private string initialMessage;
        private string characterName;
        private string playerName;

        public string CharacterName => characterName;
        public string PlayerName => playerName;

        private float temperature;
        private float topP;
        private int maxTokens;

        public async Task InitializeAsync(float temp = 0.7f, float topp = 0.9f, int maxToks = 100)
        {
            if (isInitialized) return;

            temperature = temp;
            topP = topp;
            maxTokens = maxToks;

            var metadata = RuntimeContext.GetModelMetadata();
            var modelPath = RuntimeContext.GetModelPath(metadata.modelId);

            systemPrompt = metadata.systemPrompt;
            initialMessage = metadata.initialMessage;
            characterName = metadata.characterName;
            playerName = metadata.playerName;


            bool enableLogging = RuntimeContext.GetLoggingLevel() >= LoggingLevel.Debug;
            await AviadAsync.Instance.Initialize(new LlamaModelParams
            {
                model_path = modelPath,
                max_context_length = 2048,
                gpu_layers = 0,
                threads = 4,
                max_batch_length = 2048
            }, enableLogging);

            // Prepare initial context
            ResetLocalCacheState();
            await AviadAsync.Instance.SetInitialContext(roles.ToArray(), contents.ToArray(), "llama3");
            isInitialized = true;
        }

        public async Task Reset()
        {
            if (!isInitialized)
            {
                Debug.LogWarning("[Aviad] Cannot reset before initialization.");
                return;
            }
            ResetLocalCacheState();
            await AviadAsync.Instance.ResetContext();
        }

        void ResetLocalCacheState()
        {
            roles.Clear();
            contents.Clear();
            AddMessage("system", systemPrompt);
            AddMessage("user", initialMessage);
        }

        public async Task StartConversation(Action<string> onUpdate)
        {
            if (!isInitialized)
            {
                Debug.LogError("[Aviad] Not initialized. Call InitializeAsync first.");
                return;
            }

            Task GenStream = GenerateStreaming(onUpdate);
            await GenStream;
        }
        public async Task Say(string dialogue, Action<string> onUpdate)
        {
            if (!isInitialized || roles.Count == 0)
            {
                Debug.LogError("[Aviad] Conversation not started. Call StartConversation first.");
                return;
            }

            string userMessage = $"{playerName}: {dialogue}";
            AddMessage("user", userMessage);

            Task GenStream = GenerateStreaming(onUpdate);
            await GenStream;
        }
        private async Task GenerateStreaming(Action<string>  onUpdate){

            StringBuilder responseBuilder = new();
            bool firstTokenReceived = false;
            DateTime startTime = DateTime.UtcNow;

            void OnToken(string token)
            {
                if (!firstTokenReceived)
                {
                    firstTokenReceived = true;
                    if (RuntimeContext.GetLoggingLevel() >= LoggingLevel.Debug)
                    {
                        TimeSpan firstTokenTime = DateTime.UtcNow - startTime;
                        Debug.Log($"[Aviad] First token received after {firstTokenTime.TotalMilliseconds} ms");
                    }
                }

                responseBuilder.Append(token);
                onUpdate(ParseAssistantResponse(responseBuilder.ToString()));
            }

            void OnDone(bool done)
            {
                if (done)
                {
                    string fullResponse = responseBuilder.ToString();
                    AddMessage("assistant", fullResponse);
                }
            }

            await AviadAsync.Instance.GenerateInitialStreaming(
                roles.ToArray(),
                contents.ToArray(),
                GetDefaultConfig(),
                OnToken,
                OnDone,
                chunkSize: 8
            );
        }

        public async Task Shutdown()
        {
            if (AviadAsync.IsCreated)
            {
                await AviadAsync.Instance.Shutdown();
            }
            isInitialized = false;
        }

        private void AddMessage(string role, string content)
        {
            roles.Add(role);
            contents.Add(content);
        }

        private LlamaGenerationConfig GetDefaultConfig()
        {
            string sanitizedCharacterName = characterName.Replace(":", "");
            string grammarString =
                $"root        ::= \"{sanitizedCharacterName}: \" word (space word)* [\\n]?\n" +
                "word        ::= anychar*\n" +
                "anychar     ::= [a-zA-Z0-9'-,.!?]\n" +
                "space       ::= [ ]";
            return new LlamaGenerationConfig
            {
                chatTemplate = "llama3",
                grammarString = grammarString,
                temperature = temperature,
                top_p = topP,
                max_tokens = maxTokens
            };
        }

        private string ParseAssistantResponse(string response)
        {
            int colonIndex = response.IndexOf(':');
            return (colonIndex >= 0 && colonIndex < response.Length - 1)
                ? response[(colonIndex + 1)..].TrimStart()
                : string.Empty;
        }
    }
}
