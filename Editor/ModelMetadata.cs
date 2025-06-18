using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace Aviad
{
    public class ModelMetadata
    {
        [System.Serializable]
        public class DialogueModelMetadata
        {
            public string name;
            public string systemPrompt;
            public string initialMessage;
            public string characterName;
            public string playerName;
        }

        private static string GetPrefsKey(string modelId)
        {
            return $"Aviad_Metadata_{modelId}";
        }

        public static void SaveMetadata(DialogueModelMetadata data, string modelId)
        {
            string json = null;
            try
            {
                json = JsonUtility.ToJson(data);
            }
            catch
            {
                Debug.LogError($"[Aviad] Failed to save model metadata to file.");
                throw;
            }
            if (json != null)
            {
                EditorPrefs.SetString(GetPrefsKey(modelId), json);
            }
        }

        public static DialogueModelMetadata LoadMetadata(string modelId)
        {
            string json = EditorPrefs.GetString(GetPrefsKey(modelId), null);
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }
            try
            {
                return JsonUtility.FromJson<DialogueModelMetadata>(json);
            }
            catch
            {
                Debug.LogError($"[Aviad] Failed to parse project data from json");
                return null;
            }
        }

        public static DialogueModelMetadata ParseDialogueModelData(Dictionary<string, object> data)
        {
            if (data.TryGetValue("data", out var mdObj) &&
                mdObj is Dictionary<string, object> modelData)
            {
                return new DialogueModelMetadata
                {
                    name = modelData.TryGetValue("name", out var n) ? n as string : "",
                    systemPrompt = modelData.TryGetValue("systemPrompt", out var sP) ? sP as string : "",
                    initialMessage = modelData.TryGetValue("initialMessage", out var iM) ? iM as string : "",
                    characterName = modelData.TryGetValue("characterName", out var cN) ? cN as string : "",
                    playerName = modelData.TryGetValue("playerName", out var pN) ? pN as string : ""
                };
            }
            return null;
        }
    }
}