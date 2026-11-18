// Ported for Unity (C# 9) from sherpa-onnx v2.0.1 scripts/dotnet, Apache-2.0.
/// Copyright (c)  2025  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineFireRedAsrModelConfig
    {
        /// <summary>The defaults the C# 10 constructor used to set (ported for Unity's C# 9).</summary>
        public static OfflineFireRedAsrModelConfig Default()
        {
            var self = new OfflineFireRedAsrModelConfig();
            self.Encoder = "";
            self.Decoder = "";
            return self;
        }
        [MarshalAs(UnmanagedType.LPStr)]
        public string Encoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Decoder;
    }
}
