// Ported for Unity (C# 9) from sherpa-onnx v2.0.1 scripts/dotnet, Apache-2.0.
/// Copyright (c)  2024-2026  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

// For Moonshine v1, you need four models:
//  - preprocessor, encoder, cached_decoder, uncached_decoder
//
// For Moonshine v2, you need 2 models:
//  - encoder, merged_decoder
namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineMoonshineModelConfig
    {
        /// <summary>The defaults the C# 10 constructor used to set (ported for Unity's C# 9).</summary>
        public static OfflineMoonshineModelConfig Default()
        {
            var self = new OfflineMoonshineModelConfig();
            self.Preprocessor = "";
            self.Encoder = "";
            self.UncachedDecoder = "";
            self.CachedDecoder = "";
            self.MergedDecoder = "";
            return self;
        }
        [MarshalAs(UnmanagedType.LPStr)]
        public string Preprocessor;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Encoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string UncachedDecoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string CachedDecoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string MergedDecoder;
    }
}
