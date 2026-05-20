# Asset Slimming Log (2026-03-18)

## Baseline

- Project root size (approx):
  - `Assets`: 4404.32 MB
  - `Library`: 3878.30 MB
- Build scenes:
  - `Assets/Scenes/StartScene.unity`
  - `Assets/Scenes/GameScene.unity`

## High-confidence quarantine batch

Moved (not deleted) to quarantine (initially under `Assets/_Quarantine/2026-03-18/...`):

- `Assets/Model/unused/FemaleCharacterPBR.prefab`
- `Assets/Model/unused/MaleCharacterPBR.prefab`
- `Assets/Model/unused/Lu.prefab`
- `Assets/Model/unused/Adam.prefab`
- `Assets/Model/unused/Ranged_1.prefab`
- `Assets/Model/unused/DefaultMalePBR.prefab` (demo-only reference)
- `Assets/Model/unused/DefaultFemalePBR.prefab` (demo-only reference)
- `Assets/Video/win.mp4`
- `Assets/Plugins/Zenject/OptionalExtras/SampleGame1 (Beginner)/Media/Meshes/ship.fbx`

For each moved asset, its `.meta` file was moved together to preserve GUID consistency.

Then quarantine was moved outside `Assets` to:

- `Archive/Quarantine/2026-03-18/Assets/...`
- Quarantine payload size: `1.71 MB`

## Package source cleanup (non-runtime archives)

Moved all package source files out of `Assets` to:

- `Archive/asset_sources_2026-03-18/Assets/...`
- Included file types: `*.zip`, `*.unitypackage`
- Moved payload size: `365.31 MB`

## Texture import optimization

Applied conservative importer downscale on the largest heavy texture group:

- Updated `maxTextureSize` to `1024` for selected large texture metas under:
  - `Assets/Import/Altar_Ruins_FREE/Art/Textures/...`
  - `Assets/Import/Horseman/Textures/...`
  - `Assets/Import/Cardboard_Fat_Man/Textures/...`
  - `Assets/Import/Knight Statue/Textures/...`

## Verification

- Build scenes remain:
  - `Assets/Scenes/StartScene.unity`
  - `Assets/Scenes/GameScene.unity`
- Quarantined asset GUIDs were checked against both build scenes and found no direct references.
- No linter errors reported in modified files.

## Size delta

- `Assets`: `4404.32 MB` -> `4037.31 MB` (`-367.01 MB`)
- `Import` folder: `4034.51 MB` -> `3669.89 MB` (`-364.62 MB`)
- `Library`: now `0 MB` (folder content already removed/rebuilt externally)

## Revert

To restore any quarantined asset, move the asset and its `.meta` file back to the original path.
