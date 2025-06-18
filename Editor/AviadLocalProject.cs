using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace Aviad
{

    public class AviadLocalProject
    {
        private const string CachedProjectDataKey = "Aviad_CachedProjectData";

        [System.Serializable]
        public class ModelOption
        {
            public string modelId;
            public string name;
            public bool fromProject;
        }

        [System.Serializable]
        public class LocalProjectData
        {
            public bool hasLinkedProject;
            public string projectId;
            public string projectName;
            public string projectDescription;
            public List<AviadLocalProject.ModelOption> modelOptions;

            public void AppendModel(string modelId, string name, bool fromProject)
            {
                // Avoid adding duplicates with the same modelId
                bool exists = modelOptions.Exists(m => m.modelId == modelId);
                if (!exists)
                {
                    modelOptions.Add(new AviadLocalProject.ModelOption
                    {
                        modelId = modelId,
                        name = name,
                        fromProject = fromProject
                    });
                }
            }
        }

        public static LocalProjectData DefaultProjectData()
        {
            return new LocalProjectData
            {
                hasLinkedProject = false,
                projectId = "",
                projectName = "",
                projectDescription = "",
                modelOptions = new List<ModelOption>()
            };
        }

        public static LocalProjectData LoadCachedProjectData()
        {
            string json = EditorPrefs.GetString(CachedProjectDataKey, null);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonUtility.FromJson<LocalProjectData>(json);
            }
            catch
            {
                Debug.LogError($"[Aviad] Failed to parse project data from json");
                return null;
            }
        }

        public static void CacheProjectData(LocalProjectData data)
        {
            string json;
            try
            {
                json = JsonUtility.ToJson(data);
            }
            catch
            {
                Debug.LogError($"[Aviad] Failed to save project data as json");
                throw;
            }
            if (json != null)
            {
                EditorPrefs.SetString(CachedProjectDataKey, json);
            }
        }

        public static LocalProjectData ParseProjectData(Dictionary<string, object> data)
        {
            bool hasLinkedProject = false;
            string projectId = "";
            string projectName = "";
            string projectDescription = "";
            if (data.TryGetValue("data", out var pdObj) &&
                pdObj is Dictionary<string, object> projectData &&
                projectData.TryGetValue("data", out var pData) &&
                pData is Dictionary<string, object> projectFields)
            {
                projectId = projectData.TryGetValue("projectId", out var id) ? id as string : "";
                projectName = projectFields.TryGetValue("name", out var n) ? n as string : "";
                projectDescription = projectFields.TryGetValue("description", out var desc) ? desc as string : "";
                hasLinkedProject = !string.IsNullOrEmpty(projectId);
            }

            List<ModelOption> modelOptions = new();
            if (data.TryGetValue("models", out var modelIdsObj) &&
                data.TryGetValue("modelNames", out var modelNamesObj) &&
                modelIdsObj is List<object> modelIds &&
                modelNamesObj is List<object> modelNames &&
                modelIds.Count == modelNames.Count)
            {
                for (int i = 0; i < modelIds.Count; i++)
                {
                    modelOptions.Add(new ModelOption
                    {
                        modelId = modelIds[i] as string,
                        name = modelNames[i] as string,
                        fromProject = true
                    });
                }
            }

            return new LocalProjectData
            {
                hasLinkedProject = hasLinkedProject,
                projectId = projectId,
                projectName = projectName,
                projectDescription = projectDescription,
                modelOptions = modelOptions
            };
        }

        public static LocalProjectData ResolveCachedData(LocalProjectData newData)
        {
            var cachedData = LoadCachedProjectData();

            // If there's no cached data, cache and return the new data directly
            if (cachedData == null)
            {
                CacheProjectData(newData);
                return newData;
            }

            // Keep existing options where fromProject == false
            List<ModelOption> resolvedOptions = new();
            foreach (var option in cachedData.modelOptions)
            {
                if (!option.fromProject)
                {
                    resolvedOptions.Add(option);
                }
            }

            // Add all fromProject=true options from the new data
            foreach (var option in newData.modelOptions)
            {
                resolvedOptions.Add(option);
            }

            var resolvedData = new LocalProjectData
            {
                hasLinkedProject = newData.hasLinkedProject,
                projectId = newData.projectId,
                projectName = newData.projectName,
                projectDescription = newData.projectDescription,
                modelOptions = resolvedOptions
            };
            CacheProjectData(resolvedData);
            return resolvedData;
        }
    }
}
