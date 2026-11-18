// Ported for Unity (C# 9) from sherpa-onnx v2.0.1 scripts/dotnet, Apache-2.0.
/// Copyright (c)  2026  Xiaomi Corporation

using System.Runtime.InteropServices;

namespace SherpaOnnx
{

    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineQwen3AsrModelConfig
    {
        /// <summary>The defaults the C# 10 constructor used to set (ported for Unity's C# 9).</summary>
        public static OfflineQwen3AsrModelConfig Default()
        {
            var self = new OfflineQwen3AsrModelConfig();
            self.ConvFrontend = "";
            self.Encoder = "";
            self.Decoder = "";
            self.Tokenizer = "";
            self.MaxTotalLen = 512;
            self.MaxNewTokens = 128;
            self.Temperature = 1e-6F;
            self.TopP = 0.8F;
            self.Seed = 42;
            self.Hotwords = "";
            return self;
        }

        [MarshalAs(UnmanagedType.LPStr)]
        public string ConvFrontend;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Encoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Decoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Tokenizer;

        public int MaxTotalLen;
        public int MaxNewTokens;
        public float Temperature;
        public float TopP;
        public int Seed;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Hotwords;
    }
}
