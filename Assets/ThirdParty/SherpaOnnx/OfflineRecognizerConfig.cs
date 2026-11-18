// Ported for Unity (C# 9) from sherpa-onnx v2.0.1 scripts/dotnet, Apache-2.0.
/// Copyright (c)  2024.5 by 东风破

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineRecognizerConfig
    {
        /// <summary>The defaults the C# 10 constructor used to set (ported for Unity's C# 9).</summary>
        public static OfflineRecognizerConfig Default()
        {
            var self = new OfflineRecognizerConfig();
            self.FeatConfig = FeatureConfig.Default();
            self.ModelConfig = OfflineModelConfig.Default();
            self.LmConfig = OfflineLMConfig.Default();
            self.DecodingMethod = "greedy_search";
            self.MaxActivePaths = 4;
            self.HotwordsFile = "";
            self.HotwordsScore = 1.5F;
            self.RuleFsts = "";
            self.RuleFars = "";
            self.BlankPenalty = 0.0F;
            self.Hr = HomophoneReplacerConfig.Default();
            self.CtcFstDecoderConfig = OfflineCtcFstDecoderConfig.Default();
            return self;
        }
        public FeatureConfig FeatConfig;
        public OfflineModelConfig ModelConfig;
        public OfflineLMConfig LmConfig;

        [MarshalAs(UnmanagedType.LPStr)]
        public string DecodingMethod;

        public int MaxActivePaths;

        [MarshalAs(UnmanagedType.LPStr)]
        public string HotwordsFile;

        public float HotwordsScore;

        [MarshalAs(UnmanagedType.LPStr)]
        public string RuleFsts;

        [MarshalAs(UnmanagedType.LPStr)]
        public string RuleFars;

        public float BlankPenalty;

        public HomophoneReplacerConfig Hr;

        public OfflineCtcFstDecoderConfig CtcFstDecoderConfig;
    }
}
