#if UNITY_EDITOR
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;

#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Repetitionless.Editor.Updating
{
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class UpdateChecker
    {
        [Serializable]
        private struct GithubTagResponse
        {
            // Only name is needed
            public string Name;
        }

        [Serializable]
        private struct GithubTagResponseArray
        {
            public GithubTagResponse[] Items;
        }

        private static HttpClient _client = new HttpClient();
        private static string _versionCache = "";

        public static string GetLatestVersion(bool forceRequest = false)
        {
            if (_versionCache != "" && !forceRequest)
                return _versionCache;

            HttpRequestMessage request = new HttpRequestMessage() {
                RequestUri = new Uri(Constants.GITHUB_TAGS_URL),
                Method = HttpMethod.Get
            };

            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Mozilla", "5.0"));

            Task<HttpResponseMessage> getTask;
            try {
                getTask = _client.SendAsync(request);
                getTask.Wait();
            } catch (Exception) {
                return "";
            }

            HttpResponseMessage response = getTask.Result;
            Task<string> getContentTask = response.Content.ReadAsStringAsync();
            getContentTask.Wait();

            // JSONUtility cannot read root json arrays, put the array in an object
            string wrappedJson = $"{{\"items\":{getContentTask.Result}}}";
            GithubTagResponseArray responseJson = JsonUtility.FromJson<GithubTagResponseArray>(wrappedJson);
            if (responseJson.Items == null || responseJson.Items.Length == 0)
                return "";

            _versionCache = responseJson.Items[0].Name;
            return _versionCache;
        }

        public static bool UpdateAvailable(string version)
        {
            return version != GetLatestVersion();
        }
    }
}
#endif