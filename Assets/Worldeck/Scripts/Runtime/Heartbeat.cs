using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// A heartbeat every 10 s (5 s while a world is opening): state, world, fps, head height over the floor,
    /// hands, mic, pieces placed, memory, battery. One line that says whether the app is healthy.
    /// </summary>
    public sealed class Heartbeat : MonoBehaviour
    {
        public Director Director;
        public HandInput Hands;
        public Transform Head;
        public float Seconds = 10f;
        float next;
        readonly float[] recent = new float[72];
        int ri;

        /// <summary>Android's thermal status (0 none … 6 shutdown), or -1 where it can't be read. Throttling explains drops.</summary>
        static int Thermal()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var power = activity.Call<AndroidJavaObject>("getSystemService", "power"))
                    return power.Call<int>("getCurrentThermalStatus");
            }
            catch { return -1; }
#else
            return -1;
#endif
        }

        void Update()
        {
            recent[ri] = Time.unscaledDeltaTime; ri = (ri + 1) % recent.Length;
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + (Director != null && Director.Busy ? 5f : Seconds);
            float sum = 0f; foreach (var d in recent) sum += d;
            float fps = sum > 0f ? recent.Length / sum : 0f;
            var room = Director != null && Director.Rooms != null ? Director.Rooms.Room : null;
            float headOverFloor = Head != null && room != null ? Head.position.y - room.FloorY : -1f;
            string hands = "none";
            if (Hands != null)
            {
                var l = Hands[0]; var r = Hands[1];
                hands = (l.Tracked ? (l.Controller ? "L:pad" : "L:hand") : "L:-") + " " + (r.Tracked ? (r.Controller ? "R:pad" : "R:hand") : "R:-");
            }
            Trace.Info("Perf", "heartbeat",
                "fps", Mathf.Round(fps), "hz", OVRManager.display != null ? OVRManager.display.displayFrequency : 0f,
                "world", Director != null && Director.Current != null ? Director.Current.Line() : "-",
                "room", room != null ? room.Source : "-", "head_over_floor_m", headOverFloor, "hands", hands,
                "frozen", Director != null && Director.Frozen, "unsafe", Director != null && Director.Watch != null && Director.Watch.Unsafe,
                "placed", Director != null && Director.Gather != null ? Director.Gather.PlacedCount + "/" + Director.Gather.Total : "-",
                "mem_mb", UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024),
                "mono_mb", UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / (1024 * 1024), "gc", System.GC.CollectionCount(0),
                "thermal", Thermal(), "charging", SystemInfo.batteryStatus == BatteryStatus.Charging,
                "battery", SystemInfo.batteryLevel >= 0f ? Mathf.RoundToInt(SystemInfo.batteryLevel * 100f) : -1,
                "mounted", OVRManager.instance == null || OVRManager.isHmdPresent, "focus", Application.isFocused);
        }
    }
}
