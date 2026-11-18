// Ported for Unity (C# 9) from sherpa-onnx v2.0.1 scripts/dotnet, Apache-2.0.
/// Copyright (c)  2024.5 by 东风破

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineWhisperModelConfig
    {
        /// <summary>The defaults the C# 10 constructor used to set (ported for Unity's C# 9).</summary>
        public static OfflineWhisperModelConfig Default()
        {
            var self = new OfflineWhisperModelConfig();
            self.Encoder = "";
            self.Decoder = "";
            self.Language = "";
            self.Task = "transcribe";
            self.TailPaddings = -1;
            self.EnableTokenTimestamps = 0;
            self.EnableSegmentTimestamps = 0;
            return self;
        }
        [MarshalAs(UnmanagedType.LPStr)]
        public string Encoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Decoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Language;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Task;

        public int TailPaddings;
        public int EnableTokenTimestamps;
        public int EnableSegmentTimestamps;
    }

}
