// Ported for Unity (C# 9) from sherpa-onnx v2.0.1 scripts/dotnet, Apache-2.0.
/// Copyright (c)  2024.5 by 东风破

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineCanaryModelConfig
    {
        /// <summary>The defaults the C# 10 constructor used to set (ported for Unity's C# 9).</summary>
        public static OfflineCanaryModelConfig Default()
        {
            var self = new OfflineCanaryModelConfig();
            self.Encoder = "";
            self.Decoder = "";
            self.SrcLang = "en";
            self.TgtLang = "en";
            self.UsePnc = 1;
            return self;
        }
        [MarshalAs(UnmanagedType.LPStr)]
        public string Encoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Decoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string SrcLang;

        [MarshalAs(UnmanagedType.LPStr)]
        public string TgtLang;

        public int UsePnc;
    }
}
