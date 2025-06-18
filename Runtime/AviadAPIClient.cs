using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Aviad
{
    public class AviadAPIClient
    {
        private const string ApiUrl = "https://api.aviad.ai";

        private string apiKey = "";

        public AviadAPIClient() { }

        public AviadAPIClient(string apiKey)
        {
            this.apiKey = apiKey;
        }

        public void SetCredentials(string key)
        {
            apiKey = key;
        }

        private bool EnsureEnabled()
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError("API Key must be set before making requests.");
                return false;
            }
            return true;
        }

        public void GetProjectData(Action<Dictionary<string, object>> onSuccess, Action<int, string> onFailure)
        {
            if (!EnsureEnabled()) return;
            SendGetRequest("/defaultProject", new Dictionary<string, string> { }, onSuccess, onFailure);
        }

        public void GetModelDownloadUrl(string modelId, Action<Dictionary<string, object>> onSuccess, Action<int, string> onFailure)
        {
            SendGetRequest($"/model/{modelId}/download", new Dictionary<string, string> { }, onSuccess, onFailure);
        }

        public void GetModelData(string modelId, Action<Dictionary<string, object>> onSuccess, Action<int, string> onFailure)
        {
            SendGetRequest($"/model/{modelId}", new Dictionary<string, string> { }, onSuccess, onFailure);
        }

        private void SendGetRequest(string endpoint, Dictionary<string, string> queryParams, Action<Dictionary<string, object>> onSuccess, Action<int, string> onFailure)
        {
            string url = ApiUrl + endpoint;
            Debug.Log(url);
            if (queryParams?.Count > 0)
            {
                List<string> queryList = new();
                foreach (var kvp in queryParams)
                    queryList.Add($"{UnityWebRequest.EscapeURL(kvp.Key)}={UnityWebRequest.EscapeURL(kvp.Value)}");
                url += "?" + string.Join("&", queryList);
            }

            UnityWebRequest request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("User-Agent", "UnityPlaytest/1.0");

            var operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    onFailure?.Invoke((int)request.responseCode, request.error);
                    return;
                }

                try
                {
                    var parsed = MiniJSON.Json.Deserialize(request.downloadHandler.text) as Dictionary<string, object>;
                    if (parsed != null)
                        onSuccess?.Invoke(parsed);
                    else
                        onFailure?.Invoke((int)request.responseCode, "Response JSON was not an object.");
                }
                catch (Exception e)
                {
                    onFailure?.Invoke((int)request.responseCode, $"Failed to parse JSON: {e.Message}");
                }
            };
        }
    }
}
