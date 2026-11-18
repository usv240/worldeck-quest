// Ported for Unity (C# 9) from sherpa-onnx v2.0.1 scripts/dotnet, Apache-2.0.
/// Copyright (c)  2026  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineCtcFstDecoderConfig
    {
        /// <summary>The defaults the C# 10 constructor used to set (ported for Unity's C# 9).</summary>
        public static OfflineCtcFstDecoderConfig Default()
        {
            var self = new OfflineCtcFstDecoderConfig();
            self.Graph = "";
            self.MaxActive = 3000;
            return self;
        }

        [MarshalAs(UnmanagedType.LPStr)]
        public string Graph;

        /// <summary>
        /// Decoder max-active setting. 0 means 3000.
        /// </summary>
        public int MaxActive;
    }
}
