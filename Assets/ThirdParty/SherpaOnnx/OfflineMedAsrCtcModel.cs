// Ported for Unity (C# 9) from sherpa-onnx v2.0.1 scripts/dotnet, Apache-2.0.
/// Copyright (c)  2025  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{

    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineMedAsrCtcModelConfig
    {
        /// <summary>The defaults the C# 10 constructor used to set (ported for Unity's C# 9).</summary>
        public static OfflineMedAsrCtcModelConfig Default()
        {
            var self = new OfflineMedAsrCtcModelConfig();
            self.Model = "";
            return self;
        }
        [MarshalAs(UnmanagedType.LPStr)]
        public string Model;
    }
}
