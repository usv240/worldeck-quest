using NUnit.Framework;
using UnityEngine;
using Worldeck.Core;

namespace Worldeck.Tests
{
    /// <summary>Mara's runway: lights along the top edges of each piece of furniture she steers by.</summary>
    public class RunwayTests
    {
        [Test]
        public void ASofaGetsARowOfLightsAllRoundItsTop()
        {
            var mb = new MeshBuilder();
            int bulbs = Mission.EdgeLights(mb, new Vector3(2f, 0.8f, 0.9f), Quaternion.Euler(0f, 30f, 0f));
            Assert.That(bulbs, Is.InRange(40, 60), "about one every 12 cm round a 2 m sofa");
            var b = mb.Bounds;
            Assert.Greater(Mathf.Max(b.size.x, b.size.z), 1.8f, "they go round its whole top, not a dot in the middle");
            Assert.Less(b.size.y, 0.12f, "a flat ring of lights on the top, not a tower");
        }

        [Test]
        public void TheRealTableIsKeptWhenTheEyesReadLowForAMoment()
        {
            var room = new RoomModel { FloorY = 0f, Forward = Vector3.forward };
            var desk = new RoomModel.Piece { Role = Role.Surface, Label = "TABLE", Center = new Vector3(0f, 0.385f, 0.55f), Size = new Vector3(1.17f, 0.77f, 0.63f) };
            var stool = new RoomModel.Piece { Role = Role.Surface, Label = "TABLE", Center = new Vector3(0.35f, 0.245f, 0.35f), Size = new Vector3(0.41f, 0.49f, 0.44f) };
            room.Pieces.Add(desk); room.Pieces.Add(stool);
            room.Choose(new Vector3(0f, 1.15f, 0f));
            Assert.AreSame(desk, room.Table, "seated normally: the desk");
            room.Choose(new Vector3(0f, 0.885f, 0f));
            Assert.AreSame(desk, room.Table, "eyes reading 88 cm for a moment: still the desk, not a stool or a virtual table");
        }

        [Test]
        public void ASmallTableStillGetsLightsOnEverySide()
        {
            Assert.GreaterOrEqual(Mission.EdgeLights(new MeshBuilder(), new Vector3(0.3f, 0.5f, 0.3f), Quaternion.identity), 4);
        }
    }
}
