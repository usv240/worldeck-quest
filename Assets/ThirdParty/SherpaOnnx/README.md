# sherpa-onnx C# API (ported for Unity)

On-device speech recognition for Worldeck's voice: nothing you say leaves the headset.

- Source: https://github.com/k2-fsa/sherpa-onnx, tag v2.0.1, `scripts/dotnet` (Apache-2.0, see LICENSE.txt).
- Only the files the offline recognizer and the voice activity detector need are kept (34 of 101).
- Ported to Unity's C# 9 by `port_sherpa.py`: C# 10 parameterless struct constructors became a static
  `Default()` factory, and `new T()` for those structs became `T.Default()`. Nothing else changed.
- Native libraries (`Assets/Plugins/Android/arm64-v8a/`): `libsherpa-onnx-c-api.so` (sherpa-onnx, Apache-2.0)
  and `libonnxruntime.so` (ONNX Runtime, MIT), from `sherpa-onnx-v2.0.1-android.tar.bz2`.
- Models (`Assets/StreamingAssets/Voice/`): Moonshine v2 base English, quantized, 2026-02-27 (MIT, see its
  LICENSE) and Silero VAD (MIT). `selftest.wav` is the model's own sample recording, used by the dev build's
  self-test.
