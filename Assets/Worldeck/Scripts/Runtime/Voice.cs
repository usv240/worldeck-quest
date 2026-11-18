using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Worldeck.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Worldeck
{

    /// <summary>What the recipe proxy (and the AI behind it) answered.</summary>
    public sealed class AiAnswer
    {
        public Recipe Recipe;
        public bool Declined;
        public string Clarify = "";
        public string[] Choices = Array.Empty<string>();
        public string Source = "";
        /// <summary>The relay's request id (X-Request-Id): the same id is in its log.</summary>
        public string Rid = "";
        public float Ms;
        public string Error;
    }

    /// <summary>
    /// Asks the recipe proxy about a place the offline parser doesn't know. Times out at 3.5 s so the room
    /// never waits long; every answer is read back through <see cref="Recipe.FromJson"/> and clamped.
    /// </summary>
    public static class RecipeAi
    {
        public static IEnumerator Ask(string text, Recipe current, Action<AiAnswer> done, int timeoutSeconds = 10)
        {
            var a = new AiAnswer();
            if (!AppConfig.HasAi) { a.Error = "no AI configured"; done(a); yield break; }
            if (!RelayGate.Open) { a.Error = "relay paused: " + RelayGate.Why; a.Source = "skipped"; done(a); yield break; }
            var body = new Dictionary<string, object>
            {
                { "text", text.Length > 300 ? text.Substring(0, 300) : text },
                { "current", current != null ? (object)current.ToJson() : null },
                { "kit", Kit.Version }
            };
            float t0 = Time.realtimeSinceStartup;
            using (var req = new UnityWebRequest(AppConfig.ProxyUrl + "/v1/recipe", "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(Json.Write(body))) { contentType = "application/json" };
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("X-App-Key", AppConfig.AppKey);
                req.SetRequestHeader("X-Device", AppConfig.DeviceId);
                req.timeout = timeoutSeconds;
                yield return req.SendWebRequest();
                a.Ms = (Time.realtimeSinceStartup - t0) * 1000f;
                a.Rid = req.GetResponseHeader("X-Request-Id") ?? "";
                RelayGate.Saw(req);
                if (req.result != UnityWebRequest.Result.Success) { a.Error = "proxy " + req.responseCode + " " + req.error; done(a); yield break; }
                Read(req.downloadHandler.text, a);
            }
            done(a);
        }

        /// <summary>Reads the proxy's JSON (public for tests).</summary>
        public static void Read(string json, AiAnswer a)
        {
            var o = Json.FirstObject(json);
            if (o == null) { a.Error = "unreadable answer"; return; }
            a.Declined = Json.Str(o, "safety", "ok") == "decline";
            a.Recipe = Recipe.FromJson(Json.Obj(o, "recipe"));
            string q = Json.Str(o, "clarify", "");
            q = q.Trim();
            a.Clarify = q.Length <= 90 && Words.IsClean(q) && q.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0 ? q : "";
            var choices = new List<string>();
            foreach (var c in Json.Strings(o, "choices", 3)) if (Kit.Get(c) != null) choices.Add(c);
            a.Choices = choices.ToArray();
            a.Source = Json.Str(o, "source", "");
            if (a.Recipe == null && a.Choices.Length == 0) a.Error = "no recipe";
        }
    }
}
