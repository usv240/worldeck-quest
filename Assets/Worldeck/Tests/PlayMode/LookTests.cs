using System.Collections;
using System.IO;
using System.Linq;
using Meta.XR.MRUtilityKit;
using NUnit.Framework;
using Worldeck.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Worldeck.PlayTests
{
    /// <summary>
    /// Pictures of what a player sees, taken from the head's camera at the moments that matter (the dock, the storm's
    /// rings, a beacon, the map), saved to Tools/captures/look-*.png so they can be looked at without a headset.
    /// </summary>
    public class LookTests
    {
        const string Scene = "Assets/Worldeck/Scenes/Worldeck.unity";
        Director d;
        Transform hand;

        IEnumerator Until(System.Func<bool> ok, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!ok() && Time.realtimeSinceStartup < end) yield return null;
        }

        void Shot(string name)
        {
            var cam = d.Head.GetComponent<Camera>() ?? Camera.main;
            if (cam == null) return;
            var rt = new RenderTexture(1280, 1280, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            float fov = cam.fieldOfView;
            cam.fieldOfView = 96f;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 1280, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 1280), 0, 0);
            tex.Apply();
            cam.targetTexture = prev;
            cam.fieldOfView = fov;
            RenderTexture.active = null;
            Directory.CreateDirectory("Tools/captures");
            File.WriteAllBytes("Tools/captures/look-" + name + ".png", tex.EncodeToPNG());
            Object.Destroy(rt); Object.Destroy(tex);
        }

        [UnityTest, Timeout(420000)]
        public IEnumerator WhatThePlayerSees()
        {
            LogAssert.ignoreFailingMessages = true;
            PlayerPrefs.DeleteAll();
            var progress = Path.Combine(Application.persistentDataPath, "worldeck-progress.json");
            if (File.Exists(progress)) File.Delete(progress);
            if (File.Exists(progress + ".tmp")) File.Delete(progress + ".tmp");
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(Scene, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            for (int i = 0; i < 5; i++) yield return null;
            d = Object.FindFirstObjectByType<Director>();
            Assert.NotNull(d);
            var rig = Object.FindFirstObjectByType<OVRCameraRig>();
            if (rig != null) rig.enabled = false;
            hand = new GameObject("Simulated hand").transform;
            d.Hands.SimulatedHand = hand;
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.mrutilitykit/Core/Rooms/Prefabs/LivingRoom/LivingRoom00.prefab");
            var task = MRUK.Instance.LoadSceneFromPrefab(prefab);
            float until = Time.realtimeSinceStartup + 20f;
            while (!task.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
#endif
            var room = MRUK.Instance.GetCurrentRoom();
            var table = room.Anchors.First(a => a.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE) && a.VolumeBounds.HasValue);
            var b = table.VolumeBounds.Value;
            Vector3 c = table.transform.TransformPoint(b.center);
            float top = c.y + b.size.z * 0.5f;
            Vector3 right = new Vector3(table.transform.right.x, 0, table.transform.right.z).normalized;
            Vector3 back = Vector3.Cross(right, Vector3.up);
            Vector3 head = new Vector3(c.x, top + 0.45f, c.z) - back * (b.size.y * 0.5f + 0.45f);
            if (room.IsPositionInSceneVolume(head, true, 0f)) head = new Vector3(c.x, top + 0.45f, c.z) + back * (b.size.y * 0.5f + 0.45f);
            d.Head.position = head;
            Quaternion level = Quaternion.LookRotation(new Vector3(c.x - head.x, 0f, c.z - head.z));
            d.Head.rotation = level * Quaternion.Euler(18f, 0f, 0f);
            d.Rooms.UseRoom(room);
            hand.position = head + Vector3.up * 3f;
            yield return null;

            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f);
            yield return Until(() => d.Gather.Running, 15f);
            yield return new WaitForSeconds(2.5f);
            Shot("1-campfire-task");
            d.Head.rotation = level * Quaternion.Euler(-8f, 0f, 0f);
            yield return new WaitForSeconds(0.5f);
            Shot("2-campfire-ahead");
            d.Head.rotation = level * Quaternion.Euler(30f, 0f, 0f);
            yield return new WaitForSeconds(1.2f);
            Shot("3-dock");
            // The storm: rings and meteors.
            d.Head.rotation = level * Quaternion.Euler(-6f, 0f, 0f);
            d.StartStorm("test");
            yield return Until(() => d.Emergency.Sim != null && d.Emergency.Sim.NextStrikeIn < 1.2f, 30f);
            Shot("4-storm-ring");
            hand.position = d.Emergency.NextTarget;
            yield return new WaitForSeconds(0.3f);
            Shot("5-storm-hand-in-ring");
            hand.position = head + Vector3.up * 3f;
            Time.timeScale = 6f;
            yield return Until(() => d.Emergency.Sim.State.Phase >= Sim.Aftermath, 60f);
            Time.timeScale = 1f;
            yield return new WaitForSeconds(1f);
            Shot("6-storm-end");
            d.Emergency.Stop();
            // The map, in a world.
            d.Handle("help", "test");
            yield return new WaitForSeconds(1f);
            Shot("7-help-card");
            d.Help.Hide();
            // Another world by day, standing view.
            d.Open(Recipe.For("meadow", 3), "test");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Biome == "meadow", 25f);
            yield return new WaitForSeconds(3f);
            Shot("8-meadow");
            {
                // Towards the sun and a little down: the shadows of the trees and benches fall this way.
                var was = d.Head.rotation;
                Vector3 sun = Shader.GetGlobalVector("_SwSunDir"); sun.y = 0f;
                if (sun.sqrMagnitude > 1e-4f)
                {
                    d.Head.rotation = Quaternion.LookRotation((sun.normalized + Vector3.down * 0.3f).normalized);
                    yield return new WaitForSeconds(1f);
                    Shot("24-shadows-on-the-meadow");
                }
                d.Head.rotation = was;
            }
            d.Open(Recipe.For("space", 3), "test");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Biome == "space", 25f);
            yield return new WaitForSeconds(3f);
            Shot("9-space");
            // Composed places: one word, a world of its own.
            foreach (var (id, shot) in new[] { ("volcano", "12-volcano"), ("mars", "13-mars"), ("haunted", "14-haunted-forest") })
            {
                d.Open(Places.Get(id).Recipe(3), "test");
                yield return Until(() => d.State == Director.Mode.World && d.Current.Look != null && d.Current.Look.Name == Places.Get(id).Look.Name, 25f);
                yield return new WaitForSeconds(3f);
                Shot(shot);
            }
            // Life in the worlds: the savanna's animals and the prehistoric jungle's dinosaurs, at their real size.
            foreach (var (id, shot) in new[] { ("savanna", "18-savanna-animals"), ("prehistoric", "19-prehistoric-dinosaurs") })
            {
                d.Open(Places.Get(id).Recipe(3), "test");
                yield return Until(() => d.State == Director.Mode.World && d.Current.Look != null && d.Current.Look.Name == Places.Get(id).Look.Name, 25f);
                d.Gather.Stop();
                d.Head.rotation = level * Quaternion.Euler(-3f, 0f, 0f);
                yield return new WaitForSeconds(3f);
                Shot(shot);
            }
            // Take me anywhere: real places, each with its landmark on the horizon.
            foreach (var (said, shot) in new[] { ("take me to Egypt", "25-egypt"), ("take me to Paris", "26-paris"), ("take me to India", "27-india"), ("take me to Japan", "28-japan"), ("take me to London", "29-london"), ("take me to Rome", "30-rome"),
                ("take me to Moscow", "31-moscow"), ("take me to the Great Wall", "32-great-wall"), ("take me to Istanbul", "33-istanbul"), ("take me to Chichen Itza", "34-chichen-itza") })
            {
                d.Handle(said, "test");
                yield return Until(() => d.State == Director.Mode.World && d.Builder.Landmark != null && d.Current.Look != null && d.Current.Look.Landmark.Length > 0, 25f);
                d.Gather.Stop();
                if (d.Builder.Landmark != null)
                {
                    var lb = d.Builder.Landmark.GetComponent<Renderer>().bounds;
                    d.Head.rotation = Quaternion.LookRotation((lb.center - d.Head.position).normalized);
                }
                yield return new WaitForSeconds(3f);
                d.Guide.Words.Clear();   // the place, not the moment its name is being said
                yield return null;
                Shot(shot);
                d.Head.rotation = level;
            }
            // "Make it dark" in India: the same place, by night.
            d.Handle("take me to India", "test");
            yield return Until(() => d.State == Director.Mode.World && d.Builder.Landmark != null && d.Current.Look != null && d.Current.Look.Landmark == "dome", 25f);
            d.Gather.Stop();
            d.Handle("make it dark", "test");
            yield return new WaitForSeconds(3f);
            if (d.Builder.Landmark != null) d.Head.rotation = Quaternion.LookRotation((d.Builder.Landmark.GetComponent<Renderer>().bounds.center - d.Head.position).normalized);
            d.Guide.Words.Clear();
            yield return new WaitForSeconds(1f);
            Shot("35-india-by-night");
            d.Head.rotation = level;
            // Say a world, someone's in it: the pirate cove's captain says hello when you look at him.
            d.Open(Places.Get("pirate_cove").Recipe(3), "test");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Look != null && d.Current.Look.Name == Places.Get("pirate_cove").Look.Name, 25f);
            d.Gather.Stop();
            yield return Until(() => d.Local != null, 5f);
            if (d.Local != null)
            {
                d.Head.rotation = Quaternion.LookRotation((d.Local.transform.position + Vector3.up * 1.1f - d.Head.position).normalized);
                yield return new WaitForSeconds(2.2f);
                Shot("23-someone-is-in-it");
            }
            // Things you make: a T-rex and a lion on your real table; the lion held up close; then life size.
            d.Open(Recipe.For("campfire", 5), "test");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Biome == "campfire", 25f);
            d.Gather.Stop();
            d.Head.rotation = level * Quaternion.Euler(24f, 0f, 0f);
            d.Handle("put a t rex on the table", "test");
            d.Handle("make me a fox on the table", "test");
            yield return Until(() => d.Maker.Things.Count >= 2, 10f);
            yield return new WaitForSeconds(1.5f);
            Shot("15-made-on-table");
            var lion = d.Maker.Things[0];   // the T-rex
            hand.position = lion.T.position + Vector3.up * (lion.Size * 0.4f);
            d.Hands.SimulatedPinch = false;
            yield return null; yield return null;
            d.Hands.SimulatedPinch = true;
            yield return null; yield return null; yield return null;
            d.Head.rotation = level * Quaternion.Euler(6f, 0f, 0f);
            Vector3 close = d.Head.position + d.Head.forward * 0.32f;
            for (int i = 0; i < 25; i++) { hand.position = Vector3.Lerp(hand.position, close, 0.25f); yield return null; }
            yield return new WaitForSeconds(0.9f);
            Shot("16-inspect-card");
            d.Hands.SimulatedPinch = false;
            yield return new WaitForSeconds(0.6f);
            hand.position = head + Vector3.up * 3f;
            d.Handle("make it life size", "test");
            Assert.AreEqual(12f, lion.Size, 0.1f, "the T. rex you held is life size, not the fox made last");
            d.Head.rotation = level * Quaternion.Euler(-14f, 0f, 0f);
            yield return new WaitForSeconds(1.5f);
            Shot("17-life-size-t-rex");
            d.Maker.Clear();
            Time.timeScale = 1f;
            // Mara: her call on your table, the choice, then the storm with runway lights on your furniture.
            d.Open(Recipe.For("campfire", 4), "test");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Biome == "campfire", 25f);
            d.Gather.Stop();
            d.StartMission("test");
            yield return Until(() => d.Mission.Now == Mission.Step.Answer, 25f);
            yield return new WaitForSeconds(0.5f);
            if (d.Mission.Mara != null)
            {
                Vector3 to = d.Mission.Mara.transform.position + Vector3.up * 0.2f - d.Head.position;
                d.Head.rotation = Quaternion.LookRotation(to.normalized);
            }
            yield return new WaitForSeconds(0.4f);
            Shot("10-mara-calls");
            d.Mission.Pick("m:guide");
            yield return Until(() => d.Mission.Now == Mission.Step.Pace, 10f);
            d.Mission.Pick("m:slow");
            d.Head.rotation = level * Quaternion.Euler(-4f, 0f, 0f);
            yield return Until(() => d.Emergency.Sim != null && d.Emergency.Sim.NextStrikeIn < 1.5f, 40f);
            Shot("11-mara-storm-runway");
            d.Close("test");
            yield return Until(() => d.State == Director.Mode.Room, 25f);
            // In your own room: a life-size horse behind your real sofa, and the sofa in front of it.
            var couch = d.Rooms.Room.Pieces.FirstOrDefault(p => !p.Virtual && p.Label == "COUCH");
            if (couch != null)
            {
                d.Handle("make me a horse", "test");
                yield return Until(() => d.Maker.Things.Count > 0, 10f);
                d.Handle("make it life size", "test");
                yield return new WaitForSeconds(0.5f);
                var horse = d.Maker.Things[d.Maker.Things.Count - 1];
                Vector3 away = couch.Center - d.Head.position; away.y = 0f; away.Normalize();
                horse.T.position = new Vector3(couch.Center.x, d.Rooms.Room.FloorY, couch.Center.z) + away * 0.9f;
                horse.T.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, away), Vector3.up);
                d.Head.rotation = Quaternion.LookRotation((couch.Center + Vector3.up * 0.45f - d.Head.position).normalized);
                yield return new WaitForSeconds(1.2f);
                Shot("20-sofa-hides-horse");
                d.Maker.Clear();
            }
            // The opening: a first-time player touches Juniper, picks a zebra, and it stands on the table.
            d.ResetProgress(new Progress());
            hand.position = head + Vector3.up * 3f;
            d.Head.rotation = level * Quaternion.Euler(14f, 0f, 0f);
            yield return null;
            d.Guide.Button.Press();
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "Choice first:zebra"), 5f);
            yield return new WaitForSeconds(0.6f);
            Shot("21-pick-an-animal");
            PokeButton.Live.First(b => b.Id == "Choice first:zebra").Press();
            yield return new WaitForSeconds(1.6f);
            d.Head.rotation = level * Quaternion.Euler(24f, 0f, 0f);
            yield return new WaitForSeconds(0.4f);
            Shot("22-zebra-on-your-table");
        }
    }
}
