# Third-party notices

Worldeck includes the following third-party work. Everything else (code, the props still built in code, sounds, music) was made for Worldeck.

| What | Used for | Licence | Source |
|---|---|---|---|
| sherpa-onnx v2.0.1 (C# API sources, `libsherpa-onnx-c-api.so`) | On-device speech recognition | Apache-2.0 | https://github.com/k2-fsa/sherpa-onnx |
| ONNX Runtime (`libonnxruntime.so`) | Runs the speech models | MIT | https://github.com/microsoft/onnxruntime |
| Moonshine v2 base English model | Speech to text | MIT | https://github.com/moonshine-ai/moonshine |
| Silero VAD | Detects when someone is speaking | MIT | https://github.com/snakers4/silero-vad |
| Sky photographs (22) | Skies and light | CC0 | https://polyhaven.com (list in `Assets/Worldeck/Skies/CREDITS.md`) |
| Ground textures (9 sets) | Ground detail | CC0 | https://polyhaven.com (list in `Assets/Worldeck/Grounds/CREDITS.md`) |
| Kenney game assets: Nature Kit, Holiday Kit, Space Kit, Space Station Kit, City Kit (Commercial, Roads), Furniture Kit | Trees, rocks, plants, buildings, lamps and set dressing in the worlds (`Assets/Worldeck/Resources/Kit/kit.bytes`, made by `Tools/kit/build-kit.py`) | CC0 | https://kenney.nl (licence files in `Tools/kit/licences/`) |
| Kenney Mini Characters | Mara's figure (`character-female-e`) and its animations; the people, knights and robots you can ask for | CC0 | https://kenney.nl/assets/mini-characters |
| Quaternius models on Poly Pizza: the dinosaur, animal and farm bundles (25 models) | Dinosaurs and animals you can ask for (`Tools/kit/made.py`, packed into `kit.bytes`) | CC0 | https://poly.pizza (list with ids in `Tools/kit/licences/polypizza-manifest.json`) |
| Kenney Cube Pets | Pets and small animals you can ask for | CC0 | https://kenney.nl/assets/cube-pets |
| Mara's voice: 170 lines recorded offline with Kokoro-82M (v0.19, speaker af_bella) through sherpa-onnx | Her lines over the radio (`Assets/Worldeck/Resources/MaraVoice`, made by `Tools/voice/bake-mara-kokoro.py`). Only the recordings ship, not the model | Apache-2.0 | https://huggingface.co/hexgrad/Kokoro-82M , https://github.com/k2-fsa/sherpa-onnx |
| Liberation Sans (Unity's built-in LegacyRuntime font) | All text | SIL Open Font License 1.1 | Unity; https://github.com/liberationfonts |
| Meta XR SDKs v207, Unity 6 | Engine and platform | Their own terms | Meta, Unity |

Licence texts: `Assets/ThirdParty/SherpaOnnx/LICENSE.txt`, `Assets/StreamingAssets/Voice/moonshine-base-en/LICENSE`.
