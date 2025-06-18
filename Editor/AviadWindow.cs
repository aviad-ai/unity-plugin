using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;
using System.IO;

namespace Aviad
{
    public class AviadWindow : EditorWindow
    {
        private const string ApiKeyPref = "Aviad_ApiKey";

        private string apiKey = "";

        private AviadLocalProject.LocalProjectData projectData = null;
        private int selectedModelIndex = 0;
        private string readinessIndicator = "";
        private string windowStatus = "";
        private string modelIdField = "";
        private LoggingLevel selectedLoggingLevel = LoggingLevel.Error;
        private AviadAPIClient client;

        [MenuItem("Window/Aviad AI")]
        public static void ShowWindow()
        {
            GetWindow<AviadWindow>("Aviad AI");
        }

        private void OnEnable()
        {
            RefreshCredentials();
            client = new AviadAPIClient();
            selectedLoggingLevel = RuntimeContext.GetLoggingLevel();
            projectData = AviadLocalProject.LoadCachedProjectData();

            if (!string.IsNullOrEmpty(apiKey))
            {
                client.SetCredentials(apiKey);
                if (projectData == null)
                {
                    // Fetched results will be parsed and will update projectData.
                    FetchProjectData();
                    return;
                }
            }
            if (projectData == null)
            {
                projectData = AviadLocalProject.DefaultProjectData();
            }
            // Runtime context may remember the user's previously selected model.
            SelectModelFromContext();
            EnsureModelLoaded();
            UpdateReadinessIndicator(GetReadinessIndicator());
        }

        public void SetCredentials(string key)
        {
            apiKey = key;
            PlayerPrefs.SetString(ApiKeyPref, key);
        }

        private void RefreshCredentials()
        {
            if (PlayerPrefs.HasKey(ApiKeyPref))
            {
                // Keep current apiKey value by default.
                apiKey = PlayerPrefs.GetString(ApiKeyPref, apiKey);
            }
        }

        private void SelectModelFromContext()
        {
            var context = RuntimeContext.GetContext();
            if (context != null && context.modelMetadata != null && projectData.modelOptions.Count > 0)
            {
                string contextModelId = context.modelMetadata.modelId;
                for (int i = 0; i < projectData.modelOptions.Count; i++)
                {
                    if (projectData.modelOptions[i].modelId == contextModelId)
                    {
                        selectedModelIndex = i;
                        break;
                    }
                }
            }
        }


        private void EnsureModelLoaded()
        {
            string modelId = projectData.modelOptions[selectedModelIndex].modelId;
            if (!CheckModelExists(modelId)) {
                FetchModel(modelId);
            } else {
                SaveContext();
            }
        }

        private string GetReadinessIndicator()
        {
            if (projectData.modelOptions.Count == 0)
            {
                return "❌ Please add a model to your project";
            }
            if (0 <= selectedModelIndex && selectedModelIndex < projectData.modelOptions.Count)
            {
                if (CheckModelExists(projectData.modelOptions[selectedModelIndex].modelId))
                {
                    return  "✅ Model ready";
                }
                else
                {
                    return "❌ Please download model";
                }
            } else
            {
                return "";
            }
        }

        private static bool CheckModelExists(string modelId)
        {
            var modelPath = RuntimeContext.GetModelPath(modelId);
            ModelMetadata.DialogueModelMetadata metadata = ModelMetadata.LoadMetadata(modelId);
            return File.Exists(modelPath) && metadata != null;
        }

        private void OnGUI()
        {
            GUILayout.Label("Model Download Configuration", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            apiKey = EditorGUILayout.TextField("API Key", apiKey);

            if (EditorGUI.EndChangeCheck())
            {
                if (!string.IsNullOrEmpty(apiKey.Trim()))
                {
                    client.SetCredentials(apiKey.Trim());
                    FetchProjectData();
                }
                SetCredentials(apiKey);
                if (string.IsNullOrEmpty(apiKey))
                {
                    projectData = AviadLocalProject.DefaultProjectData();
                }
            }

            EditorGUILayout.Space();

            var newLoggingLevel = (LoggingLevel)EditorGUILayout.EnumPopup("Logging Level", selectedLoggingLevel);

            if (newLoggingLevel != selectedLoggingLevel)
            {
                selectedLoggingLevel = newLoggingLevel;
                UpdateWindowStatus($"Logging level set to {selectedLoggingLevel}");
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Fetch Project Data"))
            {
                client.SetCredentials(apiKey.Trim());
                FetchProjectData();
            }

            if (GUILayout.Button("Reinstall Runtime Files"))
            {
                PluginInstaller.Install();
            }

            modelIdField = EditorGUILayout.TextField("Add Model ID", modelIdField);
            if (GUILayout.Button("Add model by ID"))
            {
                AddModel(modelIdField.Trim());
            }

            EditorGUILayout.Space();
            EditorGUI.BeginDisabledGroup(projectData.hasLinkedProject);
            EditorGUILayout.LabelField("Project Name", string.IsNullOrEmpty(projectData.projectName) ? "" : projectData.projectName);
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(projectData.modelOptions.Count == 0);
            int newSelectedIndex = EditorGUILayout.Popup(
                "Model",
                selectedModelIndex,
                projectData.modelOptions.ConvertAll(m => m.name).ToArray()
            );
            if (GUILayout.Button("Download Model"))
            {
                client.SetCredentials(apiKey.Trim());
                EnsureModelLoaded();
            }
            EditorGUI.EndDisabledGroup();

            if (newSelectedIndex != selectedModelIndex)
            {
                selectedModelIndex = newSelectedIndex;
                UpdateReadinessIndicator(GetReadinessIndicator());
                SaveContext();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Readiness", readinessIndicator, EditorStyles.boldLabel);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status", windowStatus, EditorStyles.wordWrappedLabel);
        }

        // SaveContext needs to be called at any point that a model could become ready
        // TODO: Handle state better to remove number of responsible callsites needed.
        private void SaveContext()
        {
            if (projectData.modelOptions.Count == 0) return;
            string modelId = projectData.modelOptions[selectedModelIndex].modelId;
            if (!CheckModelExists(modelId)) return;
            ModelMetadata.DialogueModelMetadata modelMetadata = ModelMetadata.LoadMetadata(modelId);
            var contextData = new RuntimeContextData
            {
                modelMetadata = new RuntimeModelMetadata
                {
                    modelId = modelId,
                    name = modelMetadata.name,
                    systemPrompt = modelMetadata.systemPrompt,
                    initialMessage = modelMetadata.initialMessage,
                    characterName = modelMetadata.characterName,
                    playerName = modelMetadata.playerName
                },
                loggingLevel = selectedLoggingLevel
            };
            RuntimeContext.SaveContext(contextData);
        }

        private void UpdateWindowStatus(string status, bool repaint = true)
        {
            windowStatus = status;
            if (repaint)
            {
                Repaint();
            }
        }

        private void UpdateReadinessIndicator(string status, bool repaint = true)
        {
            readinessIndicator = status;
            if (repaint)
            {
                Repaint();
            }
        }

        private void AddModel(string modelId)
        {
            bool exists = projectData.modelOptions.Exists(m => m.modelId == modelId);
            if (!exists)
            {
                FetchModel(modelId, true);
            }
            else
            {
                UpdateWindowStatus("Model already added...");
            }
        }

        private void FetchProjectData()
        {
            UpdateWindowStatus("Fetching project data...");
            client.GetProjectData(
                onSuccess: data =>
                {
                    try
                    {
                        AviadLocalProject.LocalProjectData newProjectData = AviadLocalProject.ParseProjectData(data);
                        projectData = AviadLocalProject.ResolveCachedData(newProjectData);
                        UpdateWindowStatus("Project loaded.", false);
                        UpdateReadinessIndicator(GetReadinessIndicator());
                        SaveContext();
                    }
                    catch (System.Exception e)
                    {
                        UpdateWindowStatus($"Error parsing project data: {e.Message}");
                    }
                },
                onFailure: (code, msg) =>
                {
                    Debug.LogError($"[Aviad] Failed to fetch project data: {msg}");
                    UpdateWindowStatus($"Failed to fetch project data ({code})");
                });
        }


        private void FetchModel(string modelId, bool appendToProjectData = false)
        {
            if (CheckModelExists(modelId))
            {
                UpdateWindowStatus("Model already available...");
                if (appendToProjectData)
                {
                    projectData.AppendModel(modelId, ModelMetadata.LoadMetadata(modelId).name, false);
                    AviadLocalProject.CacheProjectData(projectData);
                    SaveContext();
                }
                UpdateReadinessIndicator(GetReadinessIndicator());
                return;
            }
            UpdateWindowStatus($"Fetching model '{modelId}'...");
            client.GetModelData(
                modelId,
                onSuccess: data =>
                {
                    ModelMetadata.DialogueModelMetadata metadata = ModelMetadata.ParseDialogueModelData(data);
                    if (metadata != null)
                    {
                        ModelMetadata.SaveMetadata(metadata, modelId);
                        if (appendToProjectData)
                        {
                            projectData.AppendModel(modelId, metadata.name, false);
                            AviadLocalProject.CacheProjectData(projectData);
                        }
                        UpdateReadinessIndicator(GetReadinessIndicator());
                        SaveContext();
                        DownloadModel(modelId);
                    }
                    else
                    {
                        UpdateWindowStatus("Failed to parse model data.");
                    }
                },
                onFailure: (code, msg) =>
                {
                    Debug.LogError($"[Aviad] Failed to fetch model data: {msg}");
                    UpdateWindowStatus($"Failed to fetch model ({code})");
                });
        }

        private void DownloadModel(string modelId)
        {
            if (CheckModelExists(modelId))
            {
                UpdateWindowStatus("Model already available...");
                SaveContext();
                return;
            }
            UpdateWindowStatus($"Fetching download URL for model '{modelId}'...");
            client.GetModelDownloadUrl(
                modelId,
                onSuccess: data =>
                {
                    string url = data.TryGetValue("url", out var val) ? val as string : null;
                    if (!string.IsNullOrEmpty(url))
                    {
                        UpdateWindowStatus("Downloading model...");
                        Repaint();
                        EditorCoroutineRunner.Start(DownloadAndSaveModel(url, modelId));
                    }
                    else
                    {
                        UpdateWindowStatus("Download URL missing in response.");
                    }
                },
                onFailure: (code, msg) =>
                {
                    Debug.LogError($"[Aviad] Failed to fetch model URL: {msg}");
                    UpdateWindowStatus($"Failed to fetch model ({code})");
                });
        }

        private System.Collections.IEnumerator DownloadAndSaveModel(string url, string modelId)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SendWebRequest();

                while (!request.isDone)
                {
                    UpdateWindowStatus($"Downloading model... ({(request.downloadProgress * 100f):0.0}%)");
                    yield return null;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    yield break;
                }

                try
                {
                    string filePath = RuntimeContext.GetModelPath(modelId);
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    File.WriteAllBytes(filePath, request.downloadHandler.data);
                    UpdateWindowStatus($"Model saved to {filePath}", false);
                    UpdateReadinessIndicator(GetReadinessIndicator());
                    SaveContext();
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[Aviad] Failed to save model: {e.Message}");
                    UpdateWindowStatus($"Failed to save model: {e.Message}");
                }
                Repaint();
            }
        }
    }
}
