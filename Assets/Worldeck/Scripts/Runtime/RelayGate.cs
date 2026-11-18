using UnityEngine;
using UnityEngine.Networking;

namespace Worldeck
{
    /// <summary>
    /// After the relay fails (no connection, a timeout, a 5xx, or "too many requests"), the app stops asking for a while
    /// and goes straight to what it knows offline, instead of making the player wait 10-14 s on every request.
    /// 429 and 503 say how long (Retry-After); anything else waits a minute.
    /// </summary>
    public static class RelayGate
    {
        static float closedUntil = -1f;
        static int failures;
        public static string Why { get; private set; } = "";
        public static bool Open => Time.realtimeSinceStartup >= closedUntil;

        /// <summary>Looks at a finished request: a failure closes the gate.</summary>
        public static void Saw(UnityWebRequest req)
        {
            if (req == null) return;
            if (req.result == UnityWebRequest.Result.Success) { failures = 0; return; }
            long code = req.responseCode;
            if (code >= 400 && code < 500 && code != 429) return;   // our own mistake (a bad request): no reason to stop asking
            // One failure can be a blip: a short pause. Again soon after: a minute.
            failures++;
            float wait = failures <= 1 ? 15f : 60f;
            if (code == 429 || code == 503)
            {
                if (float.TryParse(req.GetResponseHeader("Retry-After") ?? "", out float after) && after > 0f) wait = after;
            }
            wait = Mathf.Min(wait, 900f);
            closedUntil = Time.realtimeSinceStartup + wait;
            Why = code == 0 ? "no connection" : "relay " + code;
            Trace.Info("AI", "relay paused", "seconds", wait, "why", Why);
        }

        public static void Reset() { closedUntil = -1f; Why = ""; failures = 0; }
    }
}
