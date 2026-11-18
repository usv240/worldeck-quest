using System.Collections.Generic;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Every sound in the app, by name (Assets/Worldeck/Audio, made by Tools/make_sounds.py). One-shots
    /// come from a small pool of spatialised sources (Meta XR Audio) placed where the thing happened, or a
    /// plain 2D source for interface earcons. The ambience bed is four quiet emitters around the player
    /// (not head-locked stereo: research D §4). A missing clip is silent, never an error.
    /// </summary>
    public sealed class Sfx : MonoBehaviour
    {
        [System.Serializable]
        public struct Entry { public string Name; public AudioClip Clip; }

        public Entry[] Clips = new Entry[0];
        public float Master = 2.2f;
        [Tooltip("Ambience bed level (linear).")]
        public float BedVolume = 0.3f;
        /// <summary>Set every frame by the owner: under the music the bed is a faint layer (one thing in front at a
        /// time: Nov 15 on the headset, waves, birds and music together were too much).</summary>
        public float BedDuck = 1f;
        /// <summary>How loud the bed is now (0 … its volume), before the master level.</summary>
        public float BedLevel => bedLevel;
        /// <summary>Clips that are music themselves: background music never plays over them.</summary>
        static readonly HashSet<string> Musical = new HashSet<string> { "jukebox_music", "amb_city_jazz" };

        readonly Dictionary<string, AudioClip> map = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        AudioSource ui, loopA;
        readonly AudioSource[] bed = new AudioSource[4];
        int next;
        float bedTarget, bedLevel;
        string bedClip = "";
        bool bedSingle;
        /// <summary>Bed sources playing now (for tests: a musical bed is one).</summary>
        public int BedSources { get { int n = 0; foreach (var b in bed) if (b != null && b.isPlaying) n++; return n; } }
        float duckUntil;

        public static Sfx Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            map.Clear();
            foreach (var e in Clips) if (e.Clip != null && !string.IsNullOrEmpty(e.Name)) map[e.Name] = e.Clip;
            Trace.Info("Audio", "sounds loaded", "count", map.Count, "spatializer", AudioSettings.GetSpatializerPluginName());
            ui = gameObject.AddComponent<AudioSource>();
            ui.playOnAwake = false; ui.spatialBlend = 0f;
            for (int i = 0; i < 8; i++)
            {
                var go = new GameObject("Sfx " + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.spatialize = true;
                s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.minDistance = 0.35f; s.maxDistance = 12f;
                TryMetaSpatial(go);
                pool.Add(s);
            }
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Bed " + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false; s.loop = true; s.volume = 0f;
                s.spatialBlend = 1f; s.spatialize = true;
                s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 8f; s.maxDistance = 40f;
                TryMetaSpatial(go);
                bed[i] = s;
            }
            var lg = new GameObject("Loop");
            lg.transform.SetParent(transform, false);
            loopA = lg.AddComponent<AudioSource>();
            loopA.playOnAwake = false; loopA.loop = true; loopA.spatialBlend = 1f; loopA.spatialize = true;
            loopA.rolloffMode = AudioRolloffMode.Logarithmic; loopA.minDistance = 0.5f; loopA.maxDistance = 10f;
            TryMetaSpatial(lg);
        }

        static void TryMetaSpatial(GameObject go)
        {
            var mxa = go.AddComponent<MetaXRAudioSource>();
            mxa.EnableSpatialization = true;
        }

        public bool Has(string name) => map.ContainsKey(name);
        public AudioClip Clip(string name) => map.TryGetValue(name, out var c) ? c : null;

        /// <summary>A sound where something happened.</summary>
        public void At(string name, Vector3 where, float volume = 1f, float pitch = 1f)
        {
            if (!map.TryGetValue(name, out var clip)) { if (Trace.Every("sfx-missing-" + name, 60f)) Trace.Warn("Audio", "missing clip", "name", name); return; }
            var s = pool[next];
            next = (next + 1) % pool.Count;
            s.transform.position = where;
            s.pitch = pitch;
            s.PlayOneShot(clip, volume * Master);
        }

        /// <summary>An interface earcon (not spatial).</summary>
        public void Ui(string name, float volume = 1f)
        {
            if (map.TryGetValue(name, out var clip)) ui.PlayOneShot(clip, volume * Master);
        }

        /// <summary>Starts the ambience bed of a world around <paramref name="center"/> (fades over 1.5 s).</summary>
        public void Bed(string clipName, Vector3 center)
        {
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + 0.4f;
                bed[i].transform.position = center + new Vector3(Mathf.Cos(a) * 10f, 1.5f, Mathf.Sin(a) * 10f);
            }
            bedCentre = center; bedPlaced = true;
            if (clipName != bedClip && map.TryGetValue(clipName ?? "", out var clip))
            {
                bedClip = clipName;
                // A bed that is music (the city's jazz) plays from one source: four copies a quarter apart were four
                // chords at once (review 31).
                bedSingle = Musical.Contains(clipName);
                for (int i = 0; i < 4; i++)
                {
                    bed[i].clip = clip;
                    if (bedSingle && i > 0) { bed[i].Stop(); continue; }
                    bed[i].time = bedSingle ? 0f : clip.length * i / 4f;
                    bed[i].Play();
                }
            }
            bedTarget = BedVolume;
        }

        public void BedOff() => bedTarget = 0f;

        /// <summary>Lowers the bed for a moment (while the player speaks or the room replies).</summary>
        public void Duck(float seconds) => duckUntil = Time.unscaledTime + seconds;

        /// <summary>A looping sound at a place (the fire, the jukebox); null name stops it.</summary>
        public void Loop(string name, Vector3 where, float volume)
        {
            if (string.IsNullOrEmpty(name) || !map.TryGetValue(name, out var clip)) { loopA.Stop(); loopA.clip = null; return; }
            loopA.transform.position = where;
            loopBase = volume; loopLevel = volume * Mathf.Clamp01(LoopDuck);
            loopA.volume = loopLevel * Master;
            if (loopA.clip != clip) { loopA.clip = clip; loopA.Play(); }
            else if (!loopA.isPlaying) loopA.Play();
        }

        public void LoopVolume(float v) { loopBase = v; }

        /// <summary>Set every frame by the owner: 1 as it is, less to dip the loop (a jukebox under Mara's radio).</summary>
        public float LoopDuck = 1f;
        float loopBase, loopLevel;
        /// <summary>How loud the loop is now, before the master level.</summary>
        public float LoopLevel => loopLevel;

        /// <summary>A world's own music is playing as its loop (the jukebox): the background music waits.</summary>
        public bool LoopIsMusic => loopA != null && loopA.isPlaying && loopA.clip != null && Musical.Contains(loopA.clip.name);
        /// <summary>The world's own ambience is music (the city's jazz): the background music waits.</summary>
        public bool BedIsMusic => bedTarget > 0f && Musical.Contains(bedClip);

        AudioSource music;
        float musicBase, musicLevel;
        public bool MusicPlaying => music != null && music.isPlaying;
        /// <summary>The tune playing (or fading out), "" for none.</summary>
        public string MusicName => music != null && music.clip != null && music.isPlaying ? music.clip.name : "";
        /// <summary>How loud the music is now (0 … its own volume), before the master level.</summary>
        public float MusicLevel => musicLevel;
        /// <summary>Set every frame by the owner: 1 as it is, less to dip it (under a voice), 0 to fade it away.</summary>
        public float MusicDuck = 1f;

        /// <summary>A tune, around you (null name fades it out). A new tune fades in over two seconds; nothing starts
        /// or stops suddenly.</summary>
        public void Music(string name, Vector3 where, float volume = 0.45f)
        {
            if (music == null)
            {
                var go = new GameObject("Music");
                go.transform.SetParent(transform, false);
                music = go.AddComponent<AudioSource>();
                music.loop = true;
                music.playOnAwake = false;
                music.spatialBlend = 0.15f;   // around you, a little from the table: background, not a speaker in the room
                music.minDistance = 3f;
            }
            if (string.IsNullOrEmpty(name) || !map.TryGetValue(name, out var clip)) { musicBase = 0f; return; }
            music.transform.position = where;
            musicBase = volume;
            if (music.clip != clip) { music.clip = clip; musicLevel = 0f; music.volume = 0f; music.Play(); }
            else if (!music.isPlaying) { musicLevel = 0f; music.Play(); }
        }

        void UpdateMusic()
        {
            if (music == null) return;
            float target = musicBase * Mathf.Clamp01(MusicDuck);
            // In or out over about two seconds (a fade out goes at the speed of the level it starts from).
            musicLevel = Mathf.MoveTowards(musicLevel, target, Time.unscaledDeltaTime * Mathf.Max(0.05f, Mathf.Max(musicBase, musicLevel)) / 2f);
            music.volume = musicLevel * Master;
            if (musicBase <= 0f && musicLevel <= 0.0001f && music.isPlaying) music.Stop();
        }

        Vector3 bedCentre;
        bool bedPlaced;

        void Update()
        {
            var cam = Camera.main;
            if (cam != null && bedPlaced && (cam.transform.position - bedCentre).sqrMagnitude > 9f)
            {
                bedCentre = cam.transform.position;
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.PI * 0.5f + 0.4f;
                    if (bed[i] != null) bed[i].transform.position = bedCentre + new Vector3(Mathf.Cos(a) * 10f, 1.5f, Mathf.Sin(a) * 10f);
                }
            }
            UpdateMusic();
            if (loopA != null && loopA.clip != null)
            {
                loopLevel = Mathf.MoveTowards(loopLevel, loopBase * Mathf.Clamp01(LoopDuck), Time.unscaledDeltaTime * Mathf.Max(0.05f, Mathf.Max(loopBase, loopLevel)) / 1.5f);
                loopA.volume = loopLevel * Master;
            }
            float target = bedTarget * Mathf.Clamp01(BedDuck) * (Time.unscaledTime < duckUntil ? 0.5f : 1f);
            bedLevel = Mathf.MoveTowards(bedLevel, target, Time.unscaledDeltaTime / 1.5f);
            for (int i = 0; i < 4; i++)
            {
                if (bed[i] == null) continue;
                // One source carries what four did (four unrelated copies add up to about twice the level).
                bed[i].volume = bedLevel * Master * (bedSingle ? 2f : 1f);
                if (bedLevel <= 0.0001f && bed[i].isPlaying && bedTarget <= 0f) { bed[i].Stop(); bedClip = ""; }
            }
        }
    }
}
