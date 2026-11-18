using System;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// In your own room, your real furniture is in front of what you make: each scanned piece is drawn into depth only
    /// (no colour), so a life-size lion behind your sofa is hidden by the real sofa, seen through passthrough. Off in a
    /// world, where the furniture has its own skins.
    /// </summary>
    public sealed class FurnitureMask : MonoBehaviour
    {
        public Material Mask;
        public Func<RoomModel> Room;
        public Func<bool> Active;

        RoomModel built;
        readonly List<GameObject> boxes = new List<GameObject>();
        public int Count => boxes.Count;
        public bool Showing { get; private set; }

        void Update()
        {
            var room = Room != null ? Room() : null;
            if (room != built) Build(room);
            bool on = room != null && Mask != null && boxes.Count > 0 && (Active == null || Active());
            if (on == Showing) return;
            Showing = on;
            foreach (var b in boxes) if (b != null) b.SetActive(on);
        }

        void Build(RoomModel room)
        {
            foreach (var b in boxes) if (b != null) Util.Kill(b);
            boxes.Clear();
            built = room;
            if (room == null || Mask == null) return;
            foreach (var p in room.Pieces)
            {
                if (p.Virtual || p.Role == Role.Door || p.Role == Role.Window) continue;
                var mb = new MeshBuilder();
                // A hair smaller than the scan, so things standing on a top are never cut by it.
                mb.Box(Vector3.zero, new Vector3(Mathf.Max(0.02f, p.Size.x - 0.02f), Mathf.Max(0.02f, p.Size.y - 0.015f), Mathf.Max(0.02f, p.Size.z - 0.02f)), Color.white);
                var go = Util.MeshObject("Furniture mask " + p.Label, transform, mb.ToMesh("furniture-mask"), Mask);
                go.transform.SetPositionAndRotation(p.Center + Vector3.down * 0.008f, p.Rotation);
                go.SetActive(Showing);
                boxes.Add(go);
            }
            Trace.Info("Furniture", "masks built", "count", boxes.Count);
        }
    }
}
