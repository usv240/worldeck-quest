using System;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Loads the player's room from the Quest scan and keeps a <see cref="RoomModel"/> of it. Without a
    /// scan (declined, none yet, editor) it serves a virtual room and says so; the player can ask for
    /// Space Setup from the panel. MRUK's own startup load is off so every outcome is handled here.
    /// </summary>
    public sealed class RoomSource : MonoBehaviour
    {
        public Transform Head;
        [Tooltip("Ask Quest to scan the room on the very first launch if there is no scan yet.")]
        public bool RequestScanFirstTime = false;   // never open Quest's room setup unasked (the menu's "Use my furniture" does)
        public const string AskedKey = "worldeck_scan_asked";

        public RoomModel Room { get; private set; }
        public bool Loading { get; private set; }
        public string LastResult { get; private set; } = "";
        public event Action<RoomModel> Changed;

        void Start()
        {
            if (Room == null) UseVirtual("loading");
            _ = Load(RequestScanFirstTime && PlayerPrefs.GetInt(AskedKey, 0) == 0);
        }

        /// <summary>Loads (or reloads) the scan; <paramref name="askForScan"/> opens Space Setup when there is none.</summary>
        public async Task Load(bool askForScan)
        {
            if (Loading) return;
            Loading = true;
            try
            {
                var mruk = MRUK.Instance;
                if (mruk == null) { Finish(null, "no scene kit"); return; }
                if (askForScan) { PlayerPrefs.SetInt(AskedKey, 1); PlayerPrefs.Save(); }
                Trace.Info("Room", "loading the scan", "open_setup_if_none", askForScan, "scene_permission", OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene), "attempt", retries + 1);
                MRUK.LoadDeviceResult result;
                try { result = await mruk.LoadSceneFromDevice(askForScan); }
                catch (Exception e) { Trace.Error("Room", "load threw", "error", e.Message); result = MRUK.LoadDeviceResult.Failure; }
                LastResult = result.ToString();
                Trace.Info("Room", "scan load result", "result", LastResult);
                var room = result == MRUK.LoadDeviceResult.Success ? mruk.GetCurrentRoom() : null;
                Finish(room, Why(result));
                // The permission prompt may still be open on the first launch, or the scan still syncing: try again
                // once permission is granted (for up to a minute), so nobody is left in the virtual room by timing.
                if (room == null && (result == MRUK.LoadDeviceResult.NoScenePermission || result == MRUK.LoadDeviceResult.DiscoveryOngoing) && retries < 30)
                {
                    retries++;
                    Loading = false;
                    float until = Time.realtimeSinceStartup + 2f;
                    while (Time.realtimeSinceStartup < until) await System.Threading.Tasks.Task.Yield();
                    if (this != null && (result == MRUK.LoadDeviceResult.DiscoveryOngoing ||
                                         OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene) || retries % 5 == 0))
                        await Load(false);
                    else if (this != null) { Loading = false; await RetryLater(); }
                }
            }
            finally { Loading = false; }
        }

        int retries;
        float nextLook;
        /// <summary>The owner says when a quiet look for the scan is welcome (in the room, not mid-world).</summary>
        public bool LookQuietly = true;

        /// <summary>Looks for the scan at the next quiet moment (the headset was just put on, or the app is back in
        /// focus): a scan that wasn't there while the headset slept usually is now.</summary>
        public void LookSoon() { nextLook = 0f; }

        /// <summary>In a world the room can't change under it: a scan found there is held until the owner applies it
        /// (Nov 15, build 51: launched while the headset slept, the scan loaded only once the player went home, so Mara's
        /// chapter in between had none of their furniture).</summary>
        public bool HoldFound;
        MRUKRoom held;
        public bool HasHeld => held != null;
        /// <summary>Uses a scan found while in a world (it becomes <see cref="Room"/>); false if there is none.</summary>
        public bool ApplyHeld()
        {
            if (held == null) return false;
            var r = held; held = null;
            if (r == null) return false;   // (an MRUK room destroyed since it was found)
            Trace.Info("Room", "scan found in a world: used now");
            Finish(r, "scan");
            return true;
        }

        void Update()
        {
            if (!LookQuietly || Loading || held != null || Room == null || Room.Scanned || Time.realtimeSinceStartup < nextLook) return;
            if (LastResult != "NoRoomsFound" && LastResult != "DiscoveryOngoing" && LastResult != "Failure") return;
            nextLook = Time.realtimeSinceStartup + 6f;
            _ = Look();
        }

        /// <summary>Looks for the scan again without changing anything unless it is found.</summary>
        async Task Look()
        {
            var mruk = MRUK.Instance;
            if (mruk == null) return;
            Loading = true;
            try
            {
                MRUK.LoadDeviceResult result;
                try { result = await mruk.LoadSceneFromDevice(false); }
                catch (Exception) { return; }
                if (this != null && result != MRUK.LoadDeviceResult.Success && Trace.Every("room-quiet-look", 60f))
                    Trace.Info("Room", "quiet look: no scan yet", "result", result.ToString());
                if (this == null || result != MRUK.LoadDeviceResult.Success) return;
                var room = mruk.GetCurrentRoom();
                if (room == null) return;
                LastResult = result.ToString();
                Trace.Info("Room", "scan found on a later look", "after_s", Mathf.Round(Time.realtimeSinceStartup), "held", HoldFound);
                if (HoldFound) { held = room; return; }
                Finish(room, "scan");
            }
            finally { Loading = false; }
        }

        async System.Threading.Tasks.Task RetryLater()
        {
            while (retries < 30 && this != null && !OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene))
            {
                retries++;
                float until = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < until) await System.Threading.Tasks.Task.Yield();
            }
            if (this != null && Room != null && !Room.Scanned) await Load(false);
        }

        /// <summary>Uses an already-loaded MRUK room (tests and editor captures load sample rooms this way).</summary>
        public void UseRoom(MRUKRoom room) => Finish(room, "editor");
        /// <summary>Tests: no scan yet (the virtual room), then one turns up while in a world.</summary>
        public void NoScanForTests() { UseVirtual("no scan"); LastResult = "test"; Changed?.Invoke(Room); }
        public void FoundInWorldForTests(MRUKRoom room) => held = room;

        static string Why(MRUK.LoadDeviceResult r) => r switch
        {
            MRUK.LoadDeviceResult.Success => "scan",
            MRUK.LoadDeviceResult.NoScenePermission => "no permission",
            MRUK.LoadDeviceResult.NoRoomsFound => "no scan",
            _ => "scan failed (" + r + ")"
        };

        void Finish(MRUKRoom room, string why)
        {
            held = null;   // whatever was held is older than this
            Vector3 head = Head != null ? Head.position : new Vector3(0, 1.2f, 0);
            Vector3 fwd = Head != null ? Head.forward : Vector3.forward;
            if (room != null)
            {
                Room = RoomModel.FromScan(room, head, fwd);
                int furniture = Room.Pieces.Count - (Room.Table != null && Room.Table.Virtual ? 1 : 0);
                Health.Ok(Health.Room, $"scan: {Room.Walls.Count} walls, {furniture} pieces, table {(Room.Table.Virtual ? "virtual" : Room.Table.Label.ToLowerInvariant())}", Time.realtimeSinceStartup);
                var roles = new System.Text.StringBuilder();
                foreach (var p in Room.Pieces) if (!p.Virtual) roles.Append(p.Label.ToLowerInvariant()).Append('>').Append(p.Role.ToString().ToLowerInvariant()).Append(' ');
                Trace.Info("Room", "scan ready", "walls", Room.Walls.Count, "pieces", furniture, "floor_y", Room.FloorY, "ceiling_m", Room.CeilingY - Room.FloorY,
                           "table", Room.Table.Virtual ? "virtual" : Room.Table.Label, "seat", Room.Seat != null ? Room.Seat.Label : "-", "roles", roles.ToString().Trim(),
                           "head_over_floor_m", head.y - Room.FloorY);
            }
            else UseVirtual(why);
            Changed?.Invoke(Room);
        }

        void UseVirtual(string why)
        {
            Vector3 head = Head != null ? Head.position : new Vector3(0, 1.2f, 0);
            Vector3 fwd = Head != null ? Head.forward : Vector3.forward;
            // No scan: the floor is unknown. A seated head is about 1.2 m above it (the tracking origin is not
            // always at floor level: the Nov 11 run had the floor 1.11 m above it).
            float floor = Room != null && Room.Scanned ? Room.FloorY : head.y - 1.2f;
            Room = RoomModel.Virtual(head, fwd, floor, why);
            if (why != "loading")
            {
                Health.Set(Health.Room, Health.Level.Fallback, why, Time.realtimeSinceStartup,
                           "No room scan, so your furniture stays as it is. Scan your room in Quest settings to bring it into the world.");
                Trace.Warn("Room", "virtual room", "why", why, "floor_y", Room.FloorY, "head_over_floor_m", head.y - Room.FloorY);
            }
        }

        /// <summary>Re-reads the nearest table and seat for where the player is now.</summary>
        public void Recentre()
        {
            if (Room == null || Head == null) return;
            if (!Room.Scanned) { UseVirtual(Room.Source == "loading" ? LastResult.Length > 0 ? Why(System.Enum.TryParse(LastResult, out MRUK.LoadDeviceResult lr) ? lr : MRUK.LoadDeviceResult.Failure) : "no scan" : Room.Source); Changed?.Invoke(Room); return; }
            Room.Head = Head.position;
            Room.Forward = new Vector3(Head.forward.x, 0, Head.forward.z).normalized;
            Room.Pieces.RemoveAll(p => p.Virtual);
            Room.Choose(Head.position);
            Changed?.Invoke(Room);
        }
    }
}
