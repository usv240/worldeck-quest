using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Worldeck.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Worldeck
{
    /// <summary>
    /// Asks Juniper's helper (the relay) what to do about a sentence the app doesn't understand by itself. The answer
    /// is a short list of actions from a fixed set, read back through <see cref="ActAnswer"/> with nothing trusted.
    /// With no relay configured, or no internet, the app carries on with what it knows.
    /// </summary>
    public static class ActAi
    {
        public const int TimeoutSeconds = 14;
        /// <summary>For tests: answers in place of the network (sentence → the relay's JSON, or null for "no answer").</summary>
        public static Func<string, string> Canned;

        public static bool Ready => Canned != null || AppConfig.HasAi;

        public static IEnumerator Ask(string text, Recipe current, List<string> objects, List<string> places, Action<ActAnswer> done, string holding = "", string persona = "")
        {
            if (Canned != null)
            {
                yield return null;
                string canned = Canned(text);
                done(canned != null ? ActAnswer.Read(canned, Wishes.Knows) : new ActAnswer { Error = "no answer" });
                yield break;
            }
            if (!AppConfig.HasAi) { done(new ActAnswer { Error = "no AI configured" }); yield break; }
            if (!RelayGate.Open) { done(new ActAnswer { Error = "relay paused: " + RelayGate.Why, Source = "skipped" }); yield break; }
            var body = new Dictionary<string, object>
            {
                { "text", text.Length > 300 ? text.Substring(0, 300) : text },
                { "kit", Kit.Version },
                { "current", current != null ? (object)current.ToJson() : null },
                { "objects", new List<object>(objects.ConvertAll(x => (object)x)) },
                { "places", new List<object>(places.ConvertAll(x => (object)x)) },
                { "holding", holding ?? "" },
                { "persona", persona ?? "" }
            };
            float t0 = Time.realtimeSinceStartup;
            ActAnswer a;
            using (var req = new UnityWebRequest(AppConfig.ProxyUrl + "/v1/act", "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(Json.Write(body))) { contentType = "application/json" };
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("X-App-Key", AppConfig.AppKey);
                req.SetRequestHeader("X-Device", AppConfig.DeviceId);
                req.timeout = TimeoutSeconds;
                yield return req.SendWebRequest();
                RelayGate.Saw(req);
                if (req.result != UnityWebRequest.Result.Success) a = new ActAnswer { Error = "relay " + req.responseCode + " " + req.error };
                else a = ActAnswer.Read(req.downloadHandler.text, Wishes.Knows);
                a.Rid = req.GetResponseHeader("X-Request-Id") ?? "";
            }
            a.Ms = (Time.realtimeSinceStartup - t0) * 1000f;
            done(a);
        }
    }
}
