using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Meta.XR.MRUtilityKit;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using Worldeck.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace Worldeck.EditorTools
{
    /// <summary>
    /// Headless project setup, scene build, APK builds and offline world captures for Worldeck.
    ///   Unity -batchmode -projectPath . -executeMethod Worldeck.EditorTools.WorldeckSetup.Setup -quit
    ///   ... WorldeckSetup.BuildApk | BuildJudgeApk | SceneAndApk | CaptureWorlds (no -nographics)
    /// The project's Unity and Meta settings started as a copy of the author's earlier Cyclopea project
    /// (same competition window); everything here is configured from code so it can be rebuilt anywhere.
    /// </summary>
    public static class WorldeckSetup
    {
        const string Root = "Assets/Worldeck";
        const string SettingsDir = Root + "/Settings";
        const string MaterialsDir = Root + "/Materials";
        const string AudioDir = Root + "/Audio";
        const string ScenePath = Root + "/Scenes/Worldeck.unity";
        const string ConfigPath = Root + "/Resources/" + AppConfig.ResourceName + ".json";
        public const string PackageId = "com.ujwal240.worldeck";
        public const string ProductName = "Worldeck";

        // ---------- entry points ----------

        [MenuItem("Worldeck/1. Configure project and build scene")]
        public static void Setup()
        {
            var report = new StringBuilder();
            Directory.CreateDirectory(SettingsDir);
            Directory.CreateDirectory(MaterialsDir);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Directory.CreateDirectory("Tools");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            ConfigureUrp(report);
            ConfigurePlayer(report);
            ConfigureMetaProjectConfig(report);
            ActivateXrOperator(report);
            RunMetaFixes(report);
            ConfigureOpenXR(report);
            ConfigureAudio(report);
            ConfigureVoicePlugins(report);
            SetImmersiveDebugger(true, report);
            BuildScene(report);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Tools/setup-report.txt", report.ToString());
            Debug.Log("[Worldeck] Setup complete\n" + report);
        }

        [MenuItem("Worldeck/2. Rebuild scene")]
        public static void BuildSceneMenu() { var r = new StringBuilder(); BuildScene(r); AssetDatabase.SaveAssets(); Debug.Log("[Worldeck] " + r); }

        public static void SceneAndApk() { BuildSceneMenu(); BuildApk(); }

        [MenuItem("Worldeck/3. Build dev APK")]
        public static void BuildApk()
        {
            var sb = new StringBuilder();
            SetImmersiveDebugger(true, sb);
            WriteConfig();
            ConfigureVoicePlugins(sb);
            // A release build stopped half way leaves the release key selected without its password: the dev build never uses it.
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.keystoreName = ""; PlayerSettings.Android.keyaliasName = "";
            Build("Builds/Worldeck-dev.apk", BuildOptions.Development, "Tools/build-report.txt");
        }

        /// <summary>The build judges get: release (Meta's build processor drops the XR Operator AAR and its screen capture permission), debugger off.</summary>
        [MenuItem("Worldeck/3b. Build judge APK (release)")]
        public static void BuildJudgeApk()
        {
            var sb = new StringBuilder();
            SetImmersiveDebugger(false, sb);
            WriteConfig();
            ConfigureVoicePlugins(sb);
            bool ok = false;
            bool signed = UseReleaseKey();
            try { ok = Build("Builds/Worldeck-judge.apk", BuildOptions.None, "Tools/build-judge-report.txt", exitOnFailure: false); }
            finally
            {
                // Nothing about the key stays in the project.
                PlayerSettings.Android.useCustomKeystore = false;
                PlayerSettings.Android.keystoreName = ""; PlayerSettings.Android.keyaliasName = "";
                PlayerSettings.Android.keystorePass = ""; PlayerSettings.Android.keyaliasPass = "";
                SetImmersiveDebugger(true, sb); AssetDatabase.SaveAssets();
            }
            Debug.Log($"[Worldeck] judge build signing: {(signed ? "release key" : "DEBUG key (not uploadable)")}, version code {PlayerSettings.Android.bundleVersionCode}");
            if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
        }

        /// <summary>
        /// Signs the judge build with the owner's release key (the store refuses debug-signed uploads). The key file is
        /// outside the repo (WORLDECK_KEYSTORE, or worldeck-release.keystore in the user's home folder) and its password
        /// comes from WORLDECK_KEYSTORE_PASS. Each signed build takes the next version code (uploads must always go up).
        /// </summary>
        static bool UseReleaseKey()
        {
            string Env(string k) => (Environment.GetEnvironmentVariable(k) ?? "").Trim();
            string pass = Env("WORLDECK_KEYSTORE_PASS");
            string file = Env("WORLDECK_KEYSTORE");
            if (file.Length == 0) file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "worldeck-release.keystore");
            if (pass.Length == 0 || !File.Exists(file)) return false;
            string alias = Env("WORLDECK_KEY_ALIAS");
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = file.Replace('\\', '/');
            PlayerSettings.Android.keystorePass = pass;
            PlayerSettings.Android.keyaliasName = alias.Length > 0 ? alias : "worldeck";
            PlayerSettings.Android.keyaliasPass = pass;
            const string counter = "Tools/judge-version-code.txt";
            int code = 2;
            if (File.Exists(counter)) int.TryParse(File.ReadAllText(counter).Trim(), out code);
            code = Math.Max(code, PlayerSettings.Android.bundleVersionCode) + 1;
            File.WriteAllText(counter, code + "\n");
            PlayerSettings.Android.bundleVersionCode = code;
            return true;
        }

        /// <summary>Writes the git-ignored config from environment variables (empty values turn the feature off).</summary>
        static void WriteConfig()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            string Env(string k) => (Environment.GetEnvironmentVariable(k) ?? "").Trim();
            var o = new Dictionary<string, object>
            {
                { "wit_token", Env("WORLDECK_WIT_TOKEN") }, { "proxy_url", Env("WORLDECK_PROXY_URL") }, { "app_key", Env("WORLDECK_APP_KEY") }
            };
            File.WriteAllText(ConfigPath, Json.Write(o));
            AssetDatabase.ImportAsset(ConfigPath);
            Debug.Log($"[Worldeck] config: voice {(Env("WORLDECK_WIT_TOKEN").Length > 0 ? "on" : "off")}, AI {(Env("WORLDECK_PROXY_URL").Length > 0 ? "on" : "off")}");
        }

        static bool Build(string path, BuildOptions opts, string reportPath, bool exitOnFailure = true)
        {
            Directory.CreateDirectory("Builds");
            AssetDatabase.SaveAssets();
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = path, target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android, options = opts
            };
            BuildReport r = BuildPipeline.BuildPlayer(options);
            string msg = $"[Worldeck] Build {r.summary.result}: {r.summary.outputPath} " +
                         $"{(File.Exists(path) ? new FileInfo(path).Length : 0) / (1024 * 1024)} MB, {r.summary.totalErrors} errors, {r.summary.totalTime}";
            Debug.Log(msg);
            File.WriteAllText(reportPath, msg + "\n");
            bool ok = r.summary.result == BuildResult.Succeeded;
            if (exitOnFailure && Application.isBatchMode && !ok) EditorApplication.Exit(1);
            return ok;
        }

        // ---------- project ----------

        static void ConfigureUrp(StringBuilder report)
        {
            string rendererPath = SettingsDir + "/Worldeck_Renderer.asset";
            string assetPath = SettingsDir + "/Worldeck_URP.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null) { renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(renderer, rendererPath); }
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (urp == null) { urp = UniversalRenderPipelineAsset.Create(renderer); AssetDatabase.CreateAsset(urp, assetPath); }
            // Quest: MSAA 4x (flat-shaded edges crawl without it), HDR off (keeps passthrough alpha), no shadows.
            urp.msaaSampleCount = 4;
            urp.supportsHDR = false;
            urp.renderScale = 1f;
            urp.supportsCameraDepthTexture = false;
            urp.supportsCameraOpaqueTexture = false;
            var so = new SerializedObject(urp);
            SetBool(so, "m_MainLightShadowsSupported", false);
            SetBool(so, "m_AdditionalLightShadowsSupported", false);
            SetBool(so, "m_UseSRPBatcher", true);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);
            GraphicsSettings.defaultRenderPipeline = urp;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = urp; }
            QualitySettings.SetQualityLevel(current, false);
            report.AppendLine("URP: " + assetPath + " (MSAA 4x, HDR off, shadows off)");
        }

        static void ConfigurePlayer(StringBuilder report)
        {
            PlayerSettings.companyName = "Ujwal240";
            PlayerSettings.productName = ProductName;
            PlayerSettings.bundleVersion = "0.2.0";
            PlayerSettings.Android.bundleVersionCode = 2;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            // Stack traces only for errors and exceptions: every trace line is a Debug.Log, and capturing a stack for each costs frame time.
            PlayerSettings.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            PlayerSettings.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            // Speech and the world AI need the network; everything else works offline.
            PlayerSettings.Android.forceInternetPermission = true;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (ps != null)
            {
                var pso = new SerializedObject(ps);
                var input = pso.FindProperty("activeInputHandler");
                if (input != null) { input.intValue = 2; pso.ApplyModifiedPropertiesWithoutUndo(); }
            }
            report.AppendLine($"Player: {PackageId}, IL2CPP, ARM64, API 32+, Vulkan, Linear, ASTC, internet");
        }

        static void ConfigureMetaProjectConfig(StringBuilder report)
        {
            var cfg = OVRProjectConfig.CachedProjectConfig;
            cfg.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            cfg.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            cfg.requiresSystemKeyboard = true;   // typing instead of talking (TypeBox)
            cfg.sceneSupport = OVRProjectConfig.FeatureSupport.Supported;
            cfg.anchorSupport = OVRProjectConfig.AnchorSupport.Enabled;
            cfg.isPassthroughCameraAccessEnabled = false;   // no camera access needed: fewer permissions
            OVRProjectConfig.CommitProjectConfig(cfg);
            report.AppendLine("OVRProjectConfig: hands+controllers, passthrough, scene, anchors (no camera access)");
        }

        static void RunMetaFixes(StringBuilder report)
        {
            var t = typeof(OVRProjectSetup);
            var fix = t.GetMethod("FixTasks", BindingFlags.NonPublic | BindingFlags.Static);
            var logEnum = t.GetNestedType("LogMessages", BindingFlags.NonPublic);
            if (fix == null || logEnum == null) { report.AppendLine("Meta fixes: FixTasks not found"); return; }
            object logAll = Enum.Parse(logEnum, "Changed");
            for (int pass = 0; pass < 3; pass++)
            {
                try { fix.Invoke(null, new object[] { BuildTargetGroup.Android, null, logAll, true, null, false }); }
                catch (Exception e) { report.AppendLine("Meta fixes pass " + pass + " threw: " + (e.InnerException ?? e).Message); }
            }
            report.AppendLine("Meta Project Setup Tool: FixTasks(Android) x3");
        }

        static void ConfigureOpenXR(StringBuilder report)
        {
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            bool hasLoader = general != null && general.Manager != null && general.Manager.activeLoaders.Any(l => l is OpenXRLoader);
            report.AppendLine("OpenXR loader on Android: " + hasLoader);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null) { report.AppendLine("OpenXR settings: missing"); return; }
            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            var wanted = new[] { "MetaXRFeature", "MetaQuestFeature", "HandTracking", "MetaHandTrackingAim",
                                 "OculusTouchControllerProfile", "HandInteractionProfile", "MetaXRFoveationFeature" };
            var enabled = new List<string>();
            foreach (var f in settings.GetFeatures())
            {
                if (wanted.Contains(f.GetType().Name)) f.enabled = true;
                if (f.enabled) enabled.Add(f.GetType().Name);
            }
            EditorUtility.SetDirty(settings);
            report.AppendLine("OpenXR features on: " + string.Join(", ", enabled));
        }

        /// <summary>
        /// On-device voice (sherpa-onnx + ONNX Runtime): Android ARM64 only, never loaded by the editor or a
        /// standalone player (the voice code checks Ears.Supported before touching them).
        /// </summary>
        static void ConfigureVoicePlugins(StringBuilder report)
        {
            foreach (var path in new[] { "Assets/Plugins/Android/arm64-v8a/libsherpa-onnx-c-api.so", "Assets/Plugins/Android/arm64-v8a/libonnxruntime.so" })
            {
                var imp = AssetImporter.GetAtPath(path) as PluginImporter;
                if (imp == null) { report.AppendLine("Voice plugin missing: " + path); continue; }
                imp.SetCompatibleWithAnyPlatform(false);
                imp.SetCompatibleWithEditor(false);
                imp.SetCompatibleWithPlatform(BuildTarget.Android, true);
                imp.SetPlatformData(BuildTarget.Android, "CPU", "ARM64");
                imp.SaveAndReimport();
                report.AppendLine("Voice plugin: " + Path.GetFileName(path) + " (Android ARM64)");
            }
        }

        static void ConfigureAudio(StringBuilder report)
        {
            const string name = "Meta XR Audio";
            bool available = AudioSettings.GetSpatializerPluginNames().Contains(name);
            if (available) AudioSettings.SetSpatializerPluginName(name);
            // Setting it at runtime doesn't reach the saved project settings in batch mode (found Nov 11: the
            // file still had no spatializer, so 3D sound was off). Write it into AudioManager.asset too.
            var am = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset").FirstOrDefault();
            if (am != null && available)
            {
                var so = new SerializedObject(am);
                var p = so.FindProperty("m_SpatializerPlugin");
                if (p != null) { p.stringValue = name; so.ApplyModifiedPropertiesWithoutUndo(); }
                EditorUtility.SetDirty(am);
                AssetDatabase.SaveAssets();
            }
            report.AppendLine("Spatializer: " + (available ? name : "default"));
        }

        static void SetImmersiveDebugger(bool on, StringBuilder report)
        {
            Type t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Meta.XR.ImmersiveDebugger.RuntimeSettings")).FirstOrDefault(x => x != null);
            var inst = t?.GetProperty("Instance", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var prop = t?.GetProperty("ImmersiveDebuggerEnabled", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (inst == null || prop == null) { report.AppendLine("Immersive Debugger: settings API not found"); return; }
            prop.SetValue(inst, on);
            var so = new SerializedObject((UnityEngine.Object)inst);
            SetBool(so, "enableOnlyInDebugBuild", true);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty((UnityEngine.Object)inst);
            AssetDatabase.SaveAssets();
            report.AppendLine("Immersive Debugger: " + (on ? "on" : "off"));
        }

        static void ActivateXrOperator(StringBuilder report)
        {
            Type t = FindType("Meta.XR.Editor.MetaXROperatorActivator");
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
            if (t == null) { report.AppendLine("XR Operator: activator not found"); return; }
            try { t.GetMethod("Activate", F)?.Invoke(null, new object[] { "worldeck-setup" }); }
            catch (Exception e) { report.AppendLine("XR Operator: activate threw " + (e.InnerException ?? e).Message); }
            report.AppendLine("XR Operator: activated " + t.GetProperty("IsActivated", F)?.GetValue(null));
        }

        static Type FindType(string fullName) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(x => x != null);

        static void NoEditableCopy(object wizard) =>
            wizard.GetType().GetMethod("InjectOptionalGenerateAsEditableCopy", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(wizard, new object[] { false });

        /// <summary>Meta's comprehensive interaction rig (hand data and hand meshes), locomotion off, hand meshes as passthrough masks.</summary>
        static GameObject AddInteractionRig(OVRCameraRig cameraRig, Material handMask, StringBuilder report)
        {
            Type wizard = FindType("Oculus.Interaction.OVR.Editor.QuickActions.OVRComprehensiveInteractionRigWizard");
            Type baseWizard = FindType("Oculus.Interaction.Editor.QuickActions.QuickActionsWizard");
            var create = baseWizard?.GetMethod("CreateWithDefaults", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (wizard == null || create == null) { report.AppendLine("ISDK rig: wizard API not found"); return null; }
            var inject = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(wizard),
                typeof(WorldeckSetup).GetMethod(nameof(NoEditableCopy), BindingFlags.NonPublic | BindingFlags.Static));
            var made = ((IEnumerable<GameObject>)create.MakeGenericMethod(wizard).Invoke(null, new object[] { null, true, inject })).ToList();
            GameObject rig = made.FirstOrDefault(g => g != null && g.GetComponentInChildren<OVRCameraRigRef>(true) != null) ?? made.FirstOrDefault();
            if (rig == null) { report.AppendLine("ISDK rig: wizard created nothing"); return null; }
            foreach (var r in rig.GetComponentsInChildren<OVRCameraRigRef>(true)) r.InjectAllOVRCameraRigRef(cameraRig, true);
            var off = new List<string>();
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                // Seated: no locomotion. ISDK's own interactors stay (they feed the hand data and have nothing of
                // ours to target, so they stay invisible); Cyclopea ran on device this way.
                if (n.Contains("Locomot") || n.Contains("Teleport") || n.Contains("Turner") || n.Contains("Microgesture"))
                {
                    if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); off.Add(n); }
                }
            }
            int masked = 0;
            foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!smr.name.Contains("Hand") && !smr.transform.parent.name.Contains("Hand")) continue;
                smr.sharedMaterials = Enumerable.Repeat(handMask, Math.Max(1, smr.sharedMaterials.Length)).ToArray();
                smr.shadowCastingMode = ShadowCastingMode.Off;
                masked++;
            }
            report.AppendLine($"ISDK rig: {rig.name}, off: {off.Count} objects, hand meshes masked: {masked}");
            return rig;
        }

        // ---------- materials ----------

        public sealed class Mats
        {
            public Material World, Sky, Silhouette, Glow, GlowFlat, Cut, CutHold, Fade, Panel, HandMask, Ghost, Vignette, Fire, Ground, Pad, HandGlow, Holo, Figure, Shadow, SunCaster;
        }

        public static Mats MakeMaterials()
        {
            Directory.CreateDirectory(MaterialsDir);
            var m = new Mats
            {
                World = Mat("World", "Worldeck/World", null),
                Sky = Mat("Sky", "Worldeck/Sky", null),
                Silhouette = Mat("Silhouette", "Worldeck/Silhouette", null),
                Glow = Mat("Glow", "Worldeck/Glow", x => { x.SetFloat("_Radial", 1f); x.SetFloat("_Intensity", 1f); }),
                GlowFlat = Mat("GlowFlat", "Worldeck/Glow", x => { x.SetFloat("_Radial", 0f); x.SetFloat("_Intensity", 1f); }),
                Cut = Mat("RoomCut", "Worldeck/RoomCut", x => x.SetFloat("_Hold", 0f)),
                CutHold = Mat("RoomCutHold", "Worldeck/RoomCut", x => x.SetFloat("_Hold", 1f)),
                Fade = Mat("Fade", "Worldeck/Fade", null),
                // 92% opaque: at 78% the text over a bright sky or snow fell to about 3.5:1 contrast.
                Panel = Mat("Panel", "Worldeck/RoundedPanel", x => x.SetColor("_Color", new Color(0.05f, 0.06f, 0.11f, 0.92f))),
                HandMask = Mat("HandMask", "Worldeck/HandOccluder", null),
                Ghost = Mat("Ghost", "Worldeck/Ghost", null),
                Vignette = Mat("Vignette", "Worldeck/Vignette", null),
                Fire = Mat("Fire", "Worldeck/Fire", null),
                HandGlow = Mat("HandGlow", "Worldeck/Ghost", x => { x.SetColor("_Tint", new Color(0.62f, 0.86f, 1f, 1f)); x.SetFloat("_Fill", 0.22f); x.SetFloat("_RimPower", 1.8f); x.SetFloat("_Alpha", 1f); }),
                Pad = Mat("Controller", "Worldeck/World", x => { x.SetColor("_Tint", new Color(0.55f, 0.56f, 0.6f, 1f)); x.SetFloat("_EmissiveBoost", 0f); }),
                Ground = Mat("Ground", "Worldeck/Ground", null),
                Shadow = Mat("Shadow", "Worldeck/Shadow", null),
                SunCaster = Mat("SunCaster", "Worldeck/SunCaster", null),
                Holo = Mat("Holo", "Worldeck/Ghost", x => { x.SetColor("_Tint", new Color(1f, 0.74f, 0.42f, 1f)); x.SetFloat("_Fill", 0.14f); x.SetFloat("_RimPower", 2.4f); x.SetFloat("_Alpha", 1f); }),
                Figure = Mat("Figure", "Worldeck/Figure", x => { x.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Worldeck/Models/Mara/mara-colormap.png")); x.SetColor("_Tint", new Color(1f, 0.74f, 0.42f, 1f)); }),
            };
            m.World.enableInstancing = true;
            m.Glow.renderQueue = 3000; m.GlowFlat.renderQueue = 3000; m.Ghost.renderQueue = 3010; m.Holo.renderQueue = 3012;
            m.CutHold.SetFloat("_Hold", 1f);
            m.GlowFlat.SetFloat("_Radial", 0f);
            AssetDatabase.SaveAssets();
            return m;
        }

        static Material Mat(string name, string shader, Action<Material> init)
        {
            string path = MaterialsDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (sh == null) throw new Exception("Shader missing: " + shader);
            if (m == null) { m = new Material(sh); init?.Invoke(m); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            init?.Invoke(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------- scene ----------

        static void BuildScene(StringBuilder report)
        {
            var mats = MakeMaterials();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab"));
            rig.name = "OVRCameraRig";
            var cameraRig = rig.GetComponent<OVRCameraRig>();
            var manager = rig.GetComponent<OVRManager>() ?? rig.AddComponent<OVRManager>();
            manager.isInsightPassthroughEnabled = true;
            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            var mso = new SerializedObject(manager);
            SetBool(mso, "requestScenePermissionOnStartup", true);
            SetBool(mso, "requestPassthroughCameraAccessPermissionOnStartup", false);
            mso.ApplyModifiedPropertiesWithoutUndo();
            var passthrough = rig.AddComponent<OVRPassthroughLayer>();

            Transform center = cameraRig.transform.Find("TrackingSpace/CenterEyeAnchor");
            var cam = center.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 900f;
            center.gameObject.tag = "MainCamera";
            var camData = cam.GetComponent<UniversalAdditionalCameraData>() ?? cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = false;
            camData.antialiasing = AntialiasingMode.None;

            var handPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab");
            var leftOvr = MakeHand(handPrefab, cameraRig.transform.Find("TrackingSpace/LeftHandAnchor"), true);
            var rightOvr = MakeHand(handPrefab, cameraRig.transform.Find("TrackingSpace/RightHandAnchor"), false);
            AddInteractionRig(cameraRig, mats.HandMask, report);

            var mrukPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.mrutilitykit/Core/Tools/MRUK.prefab");
            if (mrukPrefab != null)
            {
                var mruk = (GameObject)PrefabUtility.InstantiatePrefab(mrukPrefab);
                var comp = mruk.GetComponent<MRUK>();
                var kso = new SerializedObject(comp);
                SetEnum(kso, "SceneSettings.DataSource", "Device");
                SetBool(kso, "SceneSettings.LoadSceneOnStartup", false);   // RoomSource loads it and handles every outcome
                kso.ApplyModifiedPropertiesWithoutUndo();
            }

            // Fade quad in front of the eyes (safety fade, freeze).
            var fade = Util.MeshObject("Fade", center, Util.Quad(Color.black, "fade"), mats.Fade);
            fade.transform.localPosition = new Vector3(0, 0, 0.25f);
            fade.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
            fade.GetComponent<Renderer>().enabled = false;

            var app = new GameObject("Worldeck");
            var hands = app.AddComponent<HandInput>();
            hands.LeftSystem = leftOvr; hands.RightSystem = rightOvr;
            Transform track = center.parent;
            hands.Head = center;
            hands.LeftController = track.Find("LeftHandAnchor/LeftControllerAnchor") ?? track.Find("LeftHandAnchor");
            hands.RightController = track.Find("RightHandAnchor/RightControllerAnchor") ?? track.Find("RightHandAnchor");
            AddControllerModel(hands.LeftController, true, mats.Pad, report);
            AddControllerModel(hands.RightController, false, mats.Pad, report);

            var sfx = app.AddComponent<Sfx>();
            FillSounds(sfx, report);
            var log = app.AddComponent<PlayLog>();
            var rooms = app.AddComponent<RoomSource>();
            rooms.Head = center;
            var look = app.AddComponent<WorldLook>();
            look.Sky = mats.Sky;
            var skies = app.AddComponent<Skies>();
            FillSkies(skies, report);
            look.Photos = skies;

            var worldGo = new GameObject("World");
            worldGo.transform.SetParent(app.transform, false);
            var builder = worldGo.AddComponent<WorldBuilder>();
            builder.World = mats.World; builder.Sky = mats.Sky; builder.Silhouette = mats.Silhouette; builder.Glow = mats.Glow; builder.GlowFlat = mats.GlowFlat;
            builder.Look = look; builder.Head = center; builder.Blob = mats.Shadow;
            var grounds = app.AddComponent<Grounds>();
            FillGrounds(grounds, report);
            builder.Grounds = grounds; builder.GroundMat = mats.Ground;

            var shell = app.AddComponent<RoomShell>();
            shell.Cut = mats.Cut; shell.CutHold = mats.CutHold;
            var skinsGo = new GameObject("Skins");
            skinsGo.transform.SetParent(app.transform, false);
            var skins = skinsGo.AddComponent<RoomSkins>();
            skins.World = mats.World; skins.Shell = shell;
            // Shadows on the ground from everything that stands in the world.
            var sunShadows = app.AddComponent<SunShadows>();
            sunShadows.Builder = builder; sunShadows.Skins = skins; sunShadows.Look = look; sunShadows.Head = center; sunShadows.Caster = mats.SunCaster;

            var siteGo = new GameObject("Build site");
            siteGo.transform.SetParent(app.transform, false);
            var site = siteGo.AddComponent<BuildSite>();
            site.World = mats.World; site.Glow = mats.Glow; site.Look = look; site.Fire = mats.Fire;

            var gatherGo = new GameObject("Gather");
            gatherGo.transform.SetParent(app.transform, false);
            var gather = gatherGo.AddComponent<Gather>();
            gather.Hands = hands; gather.Head = center; gather.World = mats.World; gather.Glow = mats.Glow; gather.Site = site;

            var pokes = app.AddComponent<Pokes>();
            pokes.Hands = hands; pokes.Head = center; pokes.Glow = mats.GlowFlat;

            var panelGo = new GameObject("Hand panel");
            panelGo.transform.SetParent(app.transform, false);
            var panel = panelGo.AddComponent<HandPanel>();
            panel.Hands = hands; panel.Head = center; panel.Panel = mats.Panel; panel.Font = font;

            var guideGo = new GameObject("Guide");
            guideGo.transform.SetParent(app.transform, false);
            var guide = guideGo.AddComponent<Guide>();
            guide.Head = center; guide.Hands = hands; guide.World = mats.World; guide.Glow = mats.Glow; guide.Panel = mats.Panel; guide.Font = font;

            var shelfGo = new GameObject("Souvenirs");
            shelfGo.transform.SetParent(app.transform, false);
            var shelf = shelfGo.AddComponent<Souvenirs>();
            shelf.World = mats.World; shelf.Glow = mats.Glow; shelf.Panel = mats.Panel; shelf.Font = font; shelf.Head = center;

            var ripplesGo = new GameObject("Touch ripples");
            ripplesGo.transform.SetParent(app.transform, false);
            var ripples = ripplesGo.AddComponent<TouchRipples>();
            ripples.Hands = hands; ripples.Skins = skins; ripples.Glow = mats.Glow;

            var campGo = new GameObject("Campfire play");
            campGo.transform.SetParent(app.transform, false);
            var campfire = campGo.AddComponent<Campfire>();
            campfire.Hands = hands; campfire.Head = center; campfire.World = mats.World; campfire.Glow = mats.Glow; campfire.Look = look;
            var finaleGo = new GameObject("Finale");
            finaleGo.transform.SetParent(app.transform, false);
            var finale = finaleGo.AddComponent<Finale>();
            finale.Head = center; finale.Glow = mats.Glow; finale.GlowFlat = mats.GlowFlat;

            var mic = app.AddComponent<MicCapture>();
            var watch = app.AddComponent<Watch>();
            watch.Head = center; watch.Hands = hands;

            var director = app.AddComponent<Director>();
            director.Head = center; director.Rooms = rooms; director.Builder = builder; director.Look = look; director.Shell = shell;
            director.Skins = skins; director.Site = site; director.Gather = gather; director.Hands = hands; director.Pokes = pokes;
            director.Panel = panel; director.Guide = guide; director.Mic = mic; director.Shelf = shelf; director.Watch = watch;
            director.Log = log; director.FadeQuad = fade.GetComponent<Renderer>(); director.GlowFlat = mats.GlowFlat; director.Glow = mats.Glow;
            director.Passthrough = passthrough;
            director.Ripples = ripples;
            director.Campfire = campfire; director.Finale = finale;
            var light = app.AddComponent<PassthroughLight>();
            light.Layer = passthrough; light.Look = look;
            director.Light = light;
            var freeze = app.AddComponent<FrozenTouch>();
            freeze.Hands = hands; freeze.Builder = builder; freeze.Extra = siteGo.transform; freeze.Glow = mats.Glow;
            director.Freeze = freeze;
            var verbsGo = new GameObject("Room verbs");
            verbsGo.transform.SetParent(app.transform, false);
            var verbs = verbsGo.AddComponent<RoomVerbs>();
            verbs.Head = center; verbs.Panel = mats.Panel; verbs.Font = font;
            director.Verbs = verbs;
            var mapGo = new GameObject("World map");
            mapGo.transform.SetParent(app.transform, false);
            var map = mapGo.AddComponent<WorldMap>();
            map.Head = center; map.World = mats.World; map.Glow = mats.Glow; map.Panel = mats.Panel; map.Font = font;
            director.Map = map;
            var helpGo = new GameObject("Help card");
            helpGo.transform.SetParent(app.transform, false);
            var help = helpGo.AddComponent<HelpCard>();
            help.Head = center; help.Panel = mats.Panel; help.Font = font;
            director.Help = help;
            var coachGo = new GameObject("Coach");
            coachGo.transform.SetParent(app.transform, false);
            var coach = coachGo.AddComponent<HandCoach>();
            coach.Head = center; coach.Hands = hands; coach.Ghost = mats.Ghost; coach.Panel = mats.Panel; coach.Font = font;
            director.Coach = coach;
            var hintsGo = new GameObject("Hints");
            hintsGo.transform.SetParent(app.transform, false);
            var hints = hintsGo.AddComponent<Hints>();
            hints.Head = center; hints.Font = font; hints.Panel = mats.Panel;
            director.Hints = hints;
            var wristGo = new GameObject("Wrist menu");
            wristGo.transform.SetParent(app.transform, false);
            var wrist = wristGo.AddComponent<WristMenu>();
            wrist.Hands = hands; wrist.Head = center; wrist.Panel = mats.Panel; wrist.Font = font;
            director.Wrist = wrist;
            var looseGo = new GameObject("Loose things");
            looseGo.transform.SetParent(app.transform, false);
            var loose = looseGo.AddComponent<Loose>();
            loose.Hands = hands; loose.Head = center; loose.World = mats.World; loose.Glow = mats.Glow;
            director.Loose = loose;
            loose.Rig = cameraRig.transform;
            var makerGo = new GameObject("Maker");
            makerGo.transform.SetParent(app.transform, false);
            var maker = makerGo.AddComponent<Maker>();
            maker.Hands = hands; maker.Head = center; maker.World = mats.World; maker.Glow = mats.Glow; maker.Shadow = mats.Shadow; maker.Ghost = mats.Holo;
            director.Maker = maker;
            var inspect = makerGo.AddComponent<Inspect>();
            inspect.Maker = maker; inspect.Head = center; inspect.Font = font; inspect.Panel = mats.Panel; inspect.GlowFlat = mats.GlowFlat; inspect.Holo = mats.Holo;
            director.Inspect = inspect;
            var furniture = makerGo.AddComponent<FurnitureMask>();
            furniture.Mask = mats.HandMask;
            director.Furniture = furniture;
            var missionGo = new GameObject("Mission");
            missionGo.transform.SetParent(app.transform, false);
            var mission = missionGo.AddComponent<Mission>();
            mission.Head = center; mission.Guide = guide; mission.Glow = mats.Glow; mission.Panel = mats.Panel; mission.Font = font; mission.Holo = mats.Holo; mission.World = mats.World; mission.Figure = mats.Figure; mission.GlowFlat = mats.GlowFlat;
            director.Mission = mission;
            app.AddComponent<MetaPlatform>();
            Oculus.Platform.PlatformSettings.MobileAppID = MetaPlatform.AppId;
            Oculus.Platform.PlatformSettings.AppID = MetaPlatform.AppId;
            EditorUtility.SetDirty(Oculus.Platform.PlatformSettings.Instance);
            var typeBox = app.AddComponent<TypeBox>();
            typeBox.Guide = guide; typeBox.Head = center; typeBox.Font = font; typeBox.Panel = mats.Panel;
            director.Type = typeBox;
            var padsGo = new GameObject("Controller labels");
            padsGo.transform.SetParent(app.transform, false);
            var pads = padsGo.AddComponent<ControllerLabels>();
            pads.Hands = hands; pads.Head = center; pads.Font = font; pads.Panel = mats.Panel;
            director.Pads = pads;
            var ears = app.AddComponent<Ears>();
            director.Ears = ears;
            var giftsGo = new GameObject("Gifts");
            giftsGo.transform.SetParent(app.transform, false);
            var gifts = giftsGo.AddComponent<Gifts>();
            gifts.World = mats.World; gifts.Glow = mats.Glow; gifts.Panel = mats.Panel; gifts.Font = font; gifts.Head = center;
            director.Gifts = gifts;
            var dockGo = new GameObject("Dock");
            dockGo.transform.SetParent(app.transform, false);
            var dock = dockGo.AddComponent<Dock>();
            dock.Head = center; dock.Panel = mats.Panel; dock.Font = font;
            director.Dock = dock;
            var emGo = new GameObject("Emergency");
            emGo.transform.SetParent(app.transform, false);
            var em = emGo.AddComponent<Emergency>();
            em.Hands = hands; em.Head = center; em.World = mats.World; em.Glow = mats.Glow; em.GlowFlat = mats.GlowFlat; em.Panel = mats.Panel; em.Font = font; em.Look = look;
            director.Emergency = em;
            app.AddComponent<Autopilot>().Director = director;
            app.AddComponent<DemoPilot>().Director = director;   // development builds only, started by demo.txt
            var handLook = app.AddComponent<HandLook>();
            handLook.Mask = mats.HandMask; handLook.Glow = mats.HandGlow;
            handLook.Hands = UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(r => r.sharedMaterial == mats.HandMask).Cast<Renderer>().ToArray();
            report.AppendLine("Hand renderers: " + handLook.Hands.Length);
            director.HandLook = handLook;
            var moveGo = new GameObject("Locomotion");
            moveGo.transform.SetParent(app.transform, false);
            var move = moveGo.AddComponent<Locomotion>();
            move.Rig = cameraRig.transform; move.Head = center; move.Hands = hands; move.Builder = builder; move.Glow = mats.Glow; move.Vignette = mats.Vignette;
            director.Move = move;
            var wondersGo = new GameObject("Wonders");
            wondersGo.transform.SetParent(app.transform, false);
            var wonders = wondersGo.AddComponent<Wonders>();
            wonders.Head = center; wonders.World = mats.World; wonders.Glow = mats.Glow;
            director.Wonders = wonders;
            watch.Rig = cameraRig.transform;
            var heartbeat = app.AddComponent<Heartbeat>();
            heartbeat.Director = director; heartbeat.Hands = hands; heartbeat.Head = center;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            report.AppendLine("Scene: " + ScenePath);
        }

        static OVRHand MakeHand(GameObject prefab, Transform anchor, bool isLeft)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, anchor);
            go.name = isLeft ? "LeftHand" : "RightHand";
            var hand = go.GetComponent<OVRHand>();
            var hso = new SerializedObject(hand);
            SetEnum(hso, "HandType", isLeft ? "HandLeft" : "HandRight");
            hso.ApplyModifiedPropertiesWithoutUndo();
            var skel = go.GetComponent<OVRSkeleton>();
            if (skel != null) { var sso = new SerializedObject(skel); SetEnum(sso, "_skeletonType", isLeft ? "HandLeft" : "HandRight"); sso.ApplyModifiedPropertiesWithoutUndo(); }
            var mesh = go.GetComponent<OVRMesh>();
            if (mesh != null) { var m2 = new SerializedObject(mesh); SetEnum(m2, "_meshType", isLeft ? "HandLeft" : "HandRight"); m2.ApplyModifiedPropertiesWithoutUndo(); }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            return hand;
        }

        /// <summary>
        /// The controllers you hold, visible inside a world (Meta's own controller model, which picks the right
        /// shape for the headset and hides itself when the controller is put down), in a plain grey lit by the world.
        /// </summary>
        static void AddControllerModel(Transform anchor, bool left, Material mat, StringBuilder report)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.core/Prefabs/OVRControllerPrefab.prefab");
            if (prefab == null || anchor == null) { report.AppendLine("Controller model: prefab not found"); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, anchor);
            go.name = left ? "Left controller model" : "Right controller model";
            var helper = go.GetComponent<OVRControllerHelper>();
            if (helper != null)
            {
                var so = new SerializedObject(helper);
                SetEnum(so, "m_controller", left ? "LTouch" : "RTouch");
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterials = Enumerable.Repeat(mat, Math.Max(1, r.sharedMaterials.Length)).ToArray();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            report.AppendLine("Controller model: " + go.name);
        }

        /// <summary>The photographic ground sets (Tools/make_grounds.py) and their average colours.</summary>
        static void FillGrounds(Grounds grounds, StringBuilder report)
        {
            var albedo = new List<Texture2D>();
            var normal = new List<Texture2D>();
            string dir = "Assets/Worldeck/Grounds";
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir))
                {
                    if (!(f.EndsWith(".jpg") || f.EndsWith(".png"))) continue;
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(f.Replace(Path.DirectorySeparatorChar, '/'));
                    if (tex == null) continue;
                    if (tex.name.EndsWith("_normal")) normal.Add(tex); else albedo.Add(tex);
                }
            grounds.Albedo = albedo.ToArray();
            grounds.Normal = normal.ToArray();
            grounds.Meta = AssetDatabase.LoadAssetAtPath<TextAsset>(dir + "/grounds.json");
            report.AppendLine("Grounds: " + albedo.Count + " sets");
        }

        /// <summary>The photographed skies (Tools/make_skies.py) and what was measured from them.</summary>
        static void FillSkies(Skies skies, StringBuilder report)
        {
            var list = new List<Texture2D>();
            string dir = "Assets/Worldeck/Skies";
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.jpg"))
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(f.Replace('\\', '/'));
                    if (tex != null) list.Add(tex);
                }
            skies.Photos = list.ToArray();
            skies.Meta = AssetDatabase.LoadAssetAtPath<TextAsset>(dir + "/skies.json");
            report.AppendLine("Skies: " + list.Count + (skies.Meta != null ? " (with measurements)" : " (no skies.json)"));
        }

        static void FillSounds(Sfx sfx, StringBuilder report)
        {
            var list = new List<Sfx.Entry>();
            if (Directory.Exists(AudioDir))
                foreach (var f in Directory.GetFiles(AudioDir, "*.wav"))
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(f.Replace('\\', '/'));
                    if (clip != null) list.Add(new Sfx.Entry { Name = Path.GetFileNameWithoutExtension(f), Clip = clip });
                }
            sfx.Clips = list.ToArray();
            report.AppendLine("Sounds: " + list.Count);
        }

        // ---------- offline captures ----------

        /// <summary>
        /// Renders every world from a seated eye in a virtual room with typical furniture (a desk in front, a sofa
        /// to the right, a shelf and a lamp to the left), plus the reveal half-way, to Tools/captures/*.png, so
        /// the look can be checked without a headset.
        /// </summary>
        [MenuItem("Worldeck/4. Capture worlds (offline)")]
        public static void CaptureWorlds()
        {
            var mats = MakeMaterials();
            string outDir = "Tools/captures";
            Directory.CreateDirectory(outDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Capture camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.2f, 0.22f, 1f);   // stands in for the passthrough room
            cam.nearClipPlane = 0.03f; cam.farClipPlane = 900f; cam.fieldOfView = 90f;
            camGo.tag = "MainCamera";
            var head = new Vector3(0f, 1.18f, 0f);
            camGo.transform.position = head;
            camGo.transform.rotation = Quaternion.Euler(14f, 0f, 0f);

            var room = SampleRoom(head);
            var app = new GameObject("App");
            var look = app.AddComponent<WorldLook>(); look.Sky = mats.Sky;
            var builder = app.AddComponent<WorldBuilder>();
            builder.World = mats.World; builder.Sky = mats.Sky; builder.Silhouette = mats.Silhouette; builder.Glow = mats.Glow; builder.GlowFlat = mats.GlowFlat;
            builder.Look = look; builder.Head = camGo.transform;
            var shell = app.AddComponent<RoomShell>(); shell.Cut = mats.Cut; shell.CutHold = mats.CutHold;
            var skins = new GameObject("Skins").AddComponent<RoomSkins>(); skins.World = mats.World; skins.Shell = shell;
            var site = new GameObject("Site").AddComponent<BuildSite>(); site.World = mats.World; site.Glow = mats.Glow; site.Look = look;

            var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var report = new StringBuilder("# World captures\n\n| World | Build ms | Triangles | Renderers | Skin triangles |\n|---|---|---|---|---|\n");
            var views = new[] { ("front", Quaternion.Euler(14f, 0f, 0f)), ("left", Quaternion.Euler(4f, -70f, 0f)), ("up", Quaternion.Euler(-35f, 25f, 0f)) };
            foreach (var def in Kit.Biomes)
            {
                var r = Recipe.For(def.Id, 7);
                if (def.Id == "campfire") r.Extras.Add(Extras.Aurora);
                look.Show(r, snap: true); look.Apply();
                builder.BuildNow(r, room);
                skins.Build(room, r); skins.Show(true, instant: true);
                site.transform.position = room.Table.TopCenter; site.transform.rotation = room.Table.Rotation;
                site.Make(r, 0.3f); site.Finish(); site.Snap();
                look.Apply();
                shell.SetOrigin(head + Vector3.forward * 2f); shell.Build(room); shell.Radius = 100f; shell.HoldAll(false); shell.Apply();
                foreach (var (name, rot) in views)
                {
                    camGo.transform.rotation = rot;
                    Render(cam, rt);
                    Save(rt, $"{outDir}/{def.Id}-{name}.png");
                }
                report.AppendLine($"| {def.Id} | {builder.LastBuildMs:0} | {builder.Triangles} | {builder.Renderers} | {skins.Triangles} |");
            }
            // The reveal half-way, and the room before it (the grey stands for the passthrough room).
            {
                var r = Recipe.For("campfire", 7);
                look.Show(r, snap: true); look.Apply();
                builder.BuildNow(r, room);
                skins.Build(room, r); skins.Show(false, instant: true);
                shell.SetOrigin(head + Vector3.forward * 2.2f); shell.Build(room);
                camGo.transform.rotation = Quaternion.Euler(6f, 0f, 0f);
                foreach (var (name, radius) in new[] { ("reveal-0", -1f), ("reveal-1", 1.2f), ("reveal-2", 2.6f) })
                {
                    shell.Radius = radius; shell.Apply();
                    Render(cam, rt);
                    Save(rt, $"{outDir}/{name}.png");
                }
            }
            File.WriteAllText(outDir + "/captures.md", report.ToString());
            cam.targetTexture = null;
            rt.Release();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Debug.Log("[Worldeck] captures written to " + outDir + "\n" + report);
        }

        /// <summary>A 4.2 × 3.8 m room: a desk in front of the seat, a sofa to the right, a shelf and a lamp to the left, a plant behind.</summary>
        public static RoomModel SampleRoom(Vector3 head)
        {
            var room = RoomModel.Virtual(head, Vector3.forward, 0f, "editor");
            room.Pieces.Clear();
            RoomModel.Piece P(string label, Role role, Vector3 center, Vector3 size, float yaw) =>
                new RoomModel.Piece { Label = label, Role = role, Center = center, Size = size, Rotation = Quaternion.Euler(0, yaw, 0), Key = label.ToLowerInvariant() };
            var desk = P("TABLE", Role.Surface, new Vector3(0f, 0.37f, 0.62f), new Vector3(1.2f, 0.74f, 0.6f), 0f);
            room.Pieces.Add(desk);
            room.Pieces.Add(P("COUCH", Role.Seat, new Vector3(1.35f, 0.22f, 0.3f), new Vector3(1.9f, 0.44f, 0.85f), 90f));
            room.Pieces.Add(P("STORAGE", Role.Tall, new Vector3(-1.5f, 0.9f, 0.9f), new Vector3(0.8f, 1.8f, 0.4f), 90f));
            room.Pieces.Add(P("LAMP", Role.Light, new Vector3(-1.4f, 0.75f, -0.3f), new Vector3(0.35f, 1.5f, 0.35f), 0f));
            room.Pieces.Add(P("PLANT", Role.Plant, new Vector3(0.9f, 0.45f, -1.3f), new Vector3(0.5f, 0.9f, 0.5f), 0f));
            room.Pieces.Add(P("OTHER", Role.Clutter, new Vector3(-0.8f, 0.2f, -1.2f), new Vector3(0.5f, 0.4f, 0.4f), 20f));
            room.Pieces.Add(P("DOOR_FRAME", Role.Door, new Vector3(-0.6f, 1.02f, 1.86f), new Vector3(0.9f, 2.04f, 0.08f), 180f));
            room.Table = desk;
            return room;
        }

        static void Render(Camera cam, RenderTexture rt)
        {
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else cam.Render();
        }

        static void Save(RenderTexture rt, string path)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static void SetBool(SerializedObject so, string prop, bool v)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.boolValue = v; else Debug.LogWarning("[Worldeck] property not found: " + prop);
        }

        static void SetEnum(SerializedObject so, string prop, string name)
        {
            var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning("[Worldeck] property not found: " + prop); return; }
            int idx = Array.IndexOf(p.enumNames, name);
            if (idx >= 0) p.enumValueIndex = idx; else Debug.LogWarning($"[Worldeck] enum value {name} not in {prop}");
        }
    }
}
