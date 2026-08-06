# MNNH DHMGS Package Guide

## Assembly boundaries

| Assembly | Player build | Responsibility |
|---|---:|---|
| `Mnnh.Dhmgs.Runtime` | Yes | Motion data, SMPL-X rig application, playback clock and coordinator |
| `Mnnh.Dhmgs.UI` | Optional | UI controls and avatar inspection view for imported motion |
| `Mnnh.Dhmgs.Editor` | No | HTTP/archive import, decoding, asset writing, clip baking and preview |
| `Mnnh.Dhmgs.Editor.Tests` | No | Import contract tests |

The runtime assembly has no HTTP, ZIP, `UnityEditor`, UI, or video reference. Editor-produced `DhmgsMotionAsset` instances retain their serialized field layout and can be referenced directly by Player scenes and prefabs.

## Import flow

```text
internal HTTP or archive bytes
  -> DhmgsImportPipeline
  -> validated manifest + sequence
  -> DhmgsMotionAsset in memory
  -> DhmgsAssetWriter
  -> .asset and optional AnimationClips under Assets/
```

`DhmgsImportPipeline` owns acquisition and decoding. `DhmgsAssetWriter` owns `AssetDatabase` writes and clip baking. `DhmgsMotionPlayer` never invokes either one.

## Build contract

A Player build may consume:

- `DhmgsMotionAsset`
- baked `AnimationClip` assets
- `DhmgsMotionPlayer`, `DhmgsRigDriver`, and `DhmgsRealtimeClock`
- optional `Mnnh.Dhmgs.UI` components

A Player build does not consume:

- server URLs or credentials
- ZIP/JSON source parsing
- `AssetDatabase` writes
- preview MP4 files or `VideoPlayer` synchronization
- AnimationClip baking

Reference generated assets from a build scene/prefab or register them with the consuming project's chosen dynamic asset system.

## Release checklist

1. Parse every `.json` and `.asmdef` file.
2. Run `Mnnh.Dhmgs.Editor.Tests` in EditMode.
3. Compile Editor and a representative Player target in Unity 6000.5.1f1.
4. Verify a clean project can install the private Git URL tag.
5. Keep only Female/Male/Neutral base FBX files in `SmplxRig/Raw`.
6. Confirm `package.json` version and Git tag match.
