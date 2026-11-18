using System;
using Oculus.Interaction;
using Oculus.Interaction.Locomotion;
using Oculus.Interaction.Surfaces;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Getting around with Meta's own hand teleport (Interaction SDK): the gesture, arc and snap turns Quest players
    /// already know from Meta's apps. Its events land here and become our moves (<see cref="Locomotion.JumpTo"/>,
    /// <see cref="Locomotion.TurnBy"/>), so camp, the edge of the world, the comfort blink and the safety fade all
    /// still apply. The world's ground is a surface its arc can land on (<see cref="GroundSurface"/>). Meta's
    /// interactors are only switched on while you may move (in a world, exploring, nothing else going on).
    /// </summary>
    public sealed class MetaTeleport : MonoBehaviour, ILocomotionEventHandler
    {
        public Locomotion Move;
        [Tooltip("Meta's hand teleport and turn interactors (switched on only while moving is allowed).")]
        public GameObject[] Interactors = Array.Empty<GameObject>();
        public TeleportInteractable Ground;

        public event Action<LocomotionEvent, Pose> WhenLocomotionEventHandled = delegate { };
        public int Handled { get; private set; }
        bool wasOn;

        void Update()
        {
            bool on = Move != null && Move.Allowed && Move.UseMetaHands;
            if (on == wasOn) return;
            wasOn = on;
            foreach (var g in Interactors) if (g != null) g.SetActive(on);
            if (Ground != null) Ground.enabled = on;
            Trace.Info("Move", on ? "meta teleport on" : "meta teleport off");
        }

        public void HandleLocomotionEvent(LocomotionEvent e)
        {
            if (Move == null || !Move.Allowed) return;
            bool did = false;
            if (e.Translation == LocomotionEvent.TranslationType.Absolute || e.Translation == LocomotionEvent.TranslationType.AbsoluteEyeLevel)
                did = Move.JumpTo(e.Pose.position, "meta-hand");
            if (e.Rotation == LocomotionEvent.RotationType.Relative)
            {
                float yaw = e.Pose.rotation.eulerAngles.y;
                if (yaw > 180f) yaw -= 360f;
                if (Mathf.Abs(yaw) > 1f) { Move.TurnBy(yaw, "meta-turn"); did = true; }
            }
            if (did) Handled++;
            WhenLocomotionEventHandled(e, e.Pose);
        }
    }
}
