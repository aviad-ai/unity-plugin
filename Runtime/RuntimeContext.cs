using UnityEngine;
using System.IO;

namespace Aviad
{
    public enum LoggingLevel
    {
        None,
        Error,
        Debug,
    }

    [System.Serializable]
    public class RuntimeModelMetadata
    {
        public string modelId;
        public string name;
        public string characterName;
        public string systemPrompt;
        public string initialMessage;
        public string playerName;
    }

    [System.Serializable]
    public class RuntimeContextData
    {
        public RuntimeModelMetadata modelMetadata = null;
        public LoggingLevel loggingLevel = LoggingLevel.Error;
    }

    public static class RuntimeContext
    {
        private const string MetadataFileName = "aviad_runtime_context.json";
        private static RuntimeContextData cachedContext;

        public static string GetContextPath()
        {
            return Path.Combine(Application.streamingAssetsPath, "Aviad", MetadataFileName);
        }

        public static string GetModelPath(string modelId)
        {
            return Path.Combine(Application.streamingAssetsPath, "Aviad", "Models", $"{modelId}.gguf");
        }

        public static string GetActiveModelPath()
        {
            return GetModelPath(GetContext().modelMetadata.modelId);
        }

        public static RuntimeContextData GetContext()
        {
            if (cachedContext != null)
            {
                return cachedContext;
            }
            string path = GetContextPath();
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                cachedContext = JsonUtility.FromJson<RuntimeContextData>(json);
            }
            else
            {
                cachedContext = new RuntimeContextData();
            }
            return cachedContext;
        }

        public static RuntimeModelMetadata GetModelMetadata()
        {
            return GetContext().modelMetadata;
        }

        public static LoggingLevel GetLoggingLevel()
        {
            return GetContext().loggingLevel;
        }

#if UNITY_EDITOR
        public static void SaveContext(RuntimeContextData context)
        {
            cachedContext = context;

            string path = GetContextPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(context, true));
            Debug.Log($"[Aviad] Saved runtime context to {path}");
        }
#endif
    }
}
