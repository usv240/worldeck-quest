// Ported for Unity (C# 9) from sherpa-onnx v2.0.1 scripts/dotnet, Apache-2.0.
/// Copyright (c)  2024  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct VadModelConfig
    {
        /// <summary>The defaults the C# 10 constructor used to set (ported for Unity's C# 9).</summary>
        public static VadModelConfig Default()
        {
            var self = new VadModelConfig();
            self.SileroVad = SileroVadModelConfig.Default();
            self.SampleRate = 16000;
            self.NumThreads = 1;
            self.Provider = "cpu";
            self.Debug = 0;
            self.TenVad = TenVadModelConfig.Default();
            return self;
        }

        public SileroVadModelConfig SileroVad;

        public int SampleRate;

        public int NumThreads;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Provider;

        public int Debug;

        public TenVadModelConfig TenVad;
    }
}

