using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Worldeck.Tests
{
    public class SceneTests
    {
        const string Scene = "Assets/Worldeck/Scenes/Worldeck.unity";

        /// <summary>
        /// Unity saves a component in a scene only if its class lives in a file of the same name; otherwise the
        /// scene keeps a broken script link and the component is missing at runtime (Nov 11: BuildSite, Pokes and
        /// MicCapture were). Every script in the saved scene must point at a script asset.
        /// </summary>
        [Test]
        public void EverySavedComponentHasItsScript()
        {
            Assert.IsTrue(File.Exists(Scene), "scene built");
            string text = File.ReadAllText(Scene);
            var broken = Regex.Matches(text, @"m_Script: \{fileID: -?\d+\}");
            Assert.AreEqual(0, broken.Count, "components saved without a script asset (class name must match its file name)");
        }

        [Test]
        public void EveryRuntimeComponentIsInAFileOfItsName()
        {
            foreach (var path in Directory.GetFiles("Assets/Worldeck/Scripts/Runtime", "*.cs"))
            {
                string file = Path.GetFileNameWithoutExtension(path);
                foreach (Match m in Regex.Matches(File.ReadAllText(path), @"public sealed class (\w+) : MonoBehaviour"))
                {
                    string cls = m.Groups[1].Value;
                    // Components only ever added at runtime may share a file; the rest must not.
                    if (cls == "GatherItem" || cls == "Billboard" || cls == "AuroraRibbon" || cls == "FaceHead") continue;
                    Assert.AreEqual(file, cls, $"{cls} lives in {file}.cs");
                }
            }
        }
    }
}
