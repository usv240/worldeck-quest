// Ported for Unity (C# 9) from sherpa-onnx v2.0.1 scripts/dotnet, Apache-2.0.
/// Copyright (c)  2025  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct TenVadModelConfig
    {
        /// <summary>The defaults the C# 10 constructor used to set (ported for Unity's C# 9).</summary>
        public static TenVadModelConfig Default()
        {
            var self = new TenVadModelConfig();
            self.Model = "";
            self.Threshold = 0.5F;
            self.MinSilenceDuration = 0.5F;
            self.MinSpeechDuration = 0.25F;
            self.WindowSize = 256;
            self.MaxSpeechDuration = 5.0F;
            return self;
        }

        [MarshalAs(UnmanagedType.LPStr)]
        public string Model;

        public float Threshold;

        public float MinSilenceDuration;

        public float MinSpeechDuration;

        public int WindowSize;

        public float MaxSpeechDuration;
    }
}
