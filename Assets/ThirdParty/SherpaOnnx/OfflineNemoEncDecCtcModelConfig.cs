// Ported for Unity (C# 9) from sherpa-onnx v2.0.1 scripts/dotnet, Apache-2.0.
/// Copyright (c)  2023  Xiaomi Corporation (authors: Fangjun Kuang)
/// Copyright (c)  2023 by manyeyes
/// Copyright (c)  2024.5 by 东风破

using System.Runtime.InteropServices;

namespace SherpaOnnx
{

    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineNemoEncDecCtcModelConfig
    {
        /// <summary>The defaults the C# 10 constructor used to set (ported for Unity's C# 9).</summary>
        public static OfflineNemoEncDecCtcModelConfig Default()
        {
            var self = new OfflineNemoEncDecCtcModelConfig();
            self.Model = "";
            return self;
        }
        [MarshalAs(UnmanagedType.LPStr)]
        public string Model;
    }
}