using System;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Settings that must not live in the source (speech never leaves the headset, so there is no speech key): the recipe
    /// proxy's address and its shared app key. The build writes them from environment variables into
    /// Resources/WorldeckConfig.json (git-ignored); the editor also reads the environment directly.
    /// Missing values simply turn the feature off: the app works with presets, hands and choices.
    /// </summary>
    public static class AppConfig
    {
        public static string ProxyUrl { get; private set; } = "";
        public static string AppKey { get; private set; } = "";
        public static bool Loaded { get; private set; }

        /// <summary>Speech is recognised on the headset (Ears): voice works with no token and no internet.</summary>
        public static bool OnDevice;
        public static bool HasVoice => OnDevice;
        public static bool HasAi => ProxyUrl.Length > 0;

        public const string ResourceName = "WorldeckConfig";

        public static void Load()
        {
            if (Loaded) return;
            Loaded = true;
            var asset = Resources.Load<TextAsset>(ResourceName);
            if (asset != null && Json.Parse(asset.text) is Dictionary<string, object> o)
            {
                ProxyUrl = Json.Str(o, "proxy_url", "").Trim().TrimEnd('/');
                AppKey = Json.Str(o, "app_key", "").Trim();
            }
#if UNITY_EDITOR
            if (ProxyUrl.Length == 0) ProxyUrl = (Environment.GetEnvironmentVariable("WORLDECK_PROXY_URL") ?? "").Trim().TrimEnd('/');
            if (AppKey.Length == 0) AppKey = (Environment.GetEnvironmentVariable("WORLDECK_APP_KEY") ?? "").Trim();
#endif
            if (ProxyUrl.Length > 0 && !ProxyUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ProxyUrl = "";
        }

        /// <summary>For tests: set values directly.</summary>
        public static void Set(string wit, string proxy, string key)
        {
            Loaded = true;
            ProxyUrl = (proxy ?? "").TrimEnd('/'); AppKey = key ?? "";   // wit: unused (speech never leaves the headset)
        }

        /// <summary>A random id for this install (never the Meta account or the headset's serial).</summary>
        public static string DeviceId
        {
            get
            {
                const string k = "worldeck_install_id";
                string id = PlayerPrefs.GetString(k, "");
                if (id.Length == 0) { id = Guid.NewGuid().ToString("N"); PlayerPrefs.SetString(k, id); PlayerPrefs.Save(); }
                return id;
            }
        }
    }
}
