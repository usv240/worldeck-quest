using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Worldeck.Core;

namespace Worldeck.EditorTools
{
    /// <summary>
    /// Mara's recorded voice. <see cref="Export"/> lists every line she can say (Tools/voice/mara-lines.tsv:
    /// key, text); Tools/voice/bake-mara-kokoro.py records each with Kokoro-82M (Apache-2.0) into Resources/MaraVoice/key.wav.
    /// The clips import as small mono files that the app can read and put through her radio.
    /// </summary>
    public sealed class MaraVoiceBake : AssetPostprocessor
    {
        [MenuItem("Worldeck/Export Mara's lines")]
        public static void Export()
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "voice");
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            var lines = Story.EverythingSpoken();
            foreach (var l in lines) sb.Append(Story.VoiceKey(l)).Append('\t').Append(l.Replace('\n', ' ').Replace('\t', ' ')).Append('\n');
            File.WriteAllText(Path.Combine(dir, "mara-lines.tsv"), sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[Worldeck] Mara's lines: " + lines.Count + " written to " + Path.Combine(dir, "mara-lines.tsv"));
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/" + RadioVoice.Folder)) return;
            var ai = (AudioImporter)assetImporter;
            ai.forceToMono = true;
            ai.loadInBackground = false;
            var s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;   // so its samples can go through the radio
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.55f;
            s.preloadAudioData = false;
            ai.defaultSampleSettings = s;
        }
    }
}
