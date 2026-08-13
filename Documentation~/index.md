# MNNH DHMGS Package Guide

## Assembly boundaries

| Assembly | Player build | Responsibility |
|---|---:|---|
| `Mnnh.Dhmgs.Runtime` | Yes | Motion data, SMPL-X rig application, playback clock and coordinator |
| `Mnnh.Dhmgs.UI` | Optional | UI controls and avatar inspection view for imported motion |
| `Mnnh.Dhmgs.EditorPreview` | No | Attach-safe Editor video overlay and synchronized video clock |
| `Mnnh.Dhmgs.Editor` | No | HTTP/archive import, decoding, asset writing, clip baking and preview |
| `Mnnh.Dhmgs.Editor.Tests` | No | Import contract tests |

The runtime assembly has no HTTP, ZIP, `UnityEditor`, UI, or video reference. Editor-produced `DhmgsMotionAsset` instances retain their serialized field layout and can be referenced directly by Player scenes and prefabs.

## Import flow

```text
internal HTTP or archive bytes
  -> DhmgsImportService immutable request + preflight
  -> bounded archive and schema validation
  -> cooperative SMPL-X Generic/Humanoid baking
  -> prepared motion + clips + project-local preview cache
  -> non-cancellable publish with stable GUIDs and rollback
  -> exact .asset/.anim output set under Assets/
```

`DhmgsImportService` is the only public import facade used by the Importer window, Full Demo, and custom Editor tools. It accepts either a server attempt or caller-provided archive bytes through the same immutable request and result contract. Low-level acquisition, decoding, baking, and `AssetDatabase` code is internal. Raw JSON and preview video are not build assets; only the motion asset and requested clips are published under `Assets/`. `DhmgsMotionPlayer` never invokes Editor import code.

The bounded archive contract accepts root `manifest.json`, `sequence.json`, optional `identity.json`, and optional `fitting-cut.mp4` entries. `identity.json` is ancillary server metadata: it is size/UTF-8 validated but is not required for motion conversion or copied into build-facing assets. Other root or nested entries remain rejected so a server contract change is explicit.

The source cache is stored under `Library/Mnnh.Dhmgs/source-{sha256}/`. `CacheIfPresent` treats video as an exact optional artifact and removes an older cached video when the new archive has none; `Ignore` leaves video cache state untouched. Importer window preferences live in the consuming project's `UserSettings` folder rather than package assets or global Editor preferences.

## Project setup

After package installation or update, the interactive Editor enables Insecure HTTP and validates the consuming project once for each local user, project-path, and package-version combination. On later domain reloads it still checks this required setting and restores it if project files were reset, while the one-time marker suppresses repeated validation warnings. This is a persistent, project-wide `PlayerSettings` change; removing the package does not restore the previous value. The completion marker is stored in that user's `EditorPrefs`, so a package update or another workstation receives a new marker. Batch-mode jobs skip the mutation and do not consume the marker.

The manual `DHMGS > Project > Validate` and `DHMGS > Project > Allow Insecure HTTP` commands remain available for explicit rechecks or recovery. Validation reports missing TMP Essential Resources, but the package never imports them automatically because they are needed only by optional UI. The Humanoid preview does not require a custom project layer.

## Registering a project SMPL-X avatar

Open `DHMGS > 새 SMPLX 아바타 등록...`, select the project FBX, confirm its display name and project-owned prefab path, then choose **등록**. The default output folder is `Assets/Mnnh.Dhmgs/Content/Avatars`. The source FBX is not modified: the command saves a wrapper prefab that keeps the original model, mesh, materials, body shape, and company blendshapes, and adds one `DhmgsRigDriver` configured for its canonical skeleton root. It does not create a Humanoid Avatar and does not add the parametric `SMPLX` component.

The bundled official Neutral FBX is the default structural reference; Female and Male references remain available under **고급 설정**. Validation requires the official `root` plus 55-joint SMPL-X hierarchy as a direct-parent subset. Extra hair, face, or attachment branches are allowed. Missing, duplicate, or reparented canonical joints and structurally invalid skinning block registration. The canonical `root` must also have an effective Unity scale of `(1, 1, 1)` because direct camera-space placement uses its local offsets as meters; FBXs that retain DCC unit conversion such as a 100x ancestor scale are rejected with an Apply/Freeze and re-export instruction. Bone lengths, local positions, bind-pose values, vertex count, topology, and artist materials are not compared. Rest-axis differences remain warnings for visual playback review.

The 506 standard names (`Shape000...009`, `Exp000...009`, and `Pose000...485`) are inspected but are not required for skeletal playback. Missing standard blendshapes are warnings; additional company blendshapes are preserved and reported as information. When the FBX importer has **Import BlendShapes** disabled, registration remains available with a warning, but Unity cannot expose either standard or company blendshapes until that import setting is enabled. `_Opacity` support is detected from the original materials and `DhmgsSkinOpacity` is added only when at least one material exposes that property.

When **DHMGS 전체 데모에 추가** is enabled, the imported sample scene must exist and have no unsaved changes. Registration appends one inactive wrapper instance to its `DhmgsMotionPlayer` avatar list without changing the default Neutral selection or adding the same prefab twice. Package sample reimport replaces the copied demo scene, so re-register project avatars after importing a new sample version; the project-owned wrapper prefab remains intact.

Use `DHMGS > 등록된 SMPLX 아바타 제거...` (or the shortcut at the bottom of the registration window) to reverse that operation without editing the Hierarchy or serialized avatar list by hand. The removal window lists only project-owned registrations, removes duplicate Full Demo list entries and matching scene instances, adjusts the default index, then moves the generated wrapper prefab to the system trash. It never deletes the source FBX, mesh, materials, textures, or other dependencies. Play Mode, a dirty Full Demo scene, other unsaved scenes or project assets that reference the wrapper, and package-owned built-in avatars are blocked. If another saved scene, prefab, or ScriptableObject references the wrapper, the window keeps the wrapper and offers to remove it from Full Demo only.

If a wrapper file was deleted manually while its Full Demo instance still exists, the same window exposes it as **래퍼 파일 없음 / 씬 정리 필요**. **남은 씬 등록 정리** removes only null avatar-list entries and missing prefab instances under the avatar rig container. Removal is saved immediately and is not a Unity Undo operation, so the confirmation dialog always shows the exact scene and wrapper scope first.

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
