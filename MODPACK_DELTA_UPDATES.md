# Modpack Delta Updates

This launcher now supports incremental modpack updates:
- If `deltaManifestUrl` is provided by API, launcher downloads only changed files.
- If delta update fails, launcher automatically falls back to full ZIP download.

## API configuration

Set environment variable on API:

- `Modpack__DeltaManifestUrl=https://.../modpack-delta-manifest.json`

`/api/modpack/version` now returns `deltaManifestUrl` when configured.

### Modpack display version

`/api/modpack/version` supports:

- `version`: internal update key (can be non-semver)
- `displayVersion`: semver shown in the launcher UI

Configure on API:

- `Modpack__DisplayVersion=1.0.0`

## Delta manifest format

```json
{
  "version": "1.0.7",
  "baseUrl": "https://cdn.example.com/modpack/1.0.7",
  "files": [
    {
      "path": "mods/example-mod.jar",
      "url": "https://cdn.example.com/modpack/1.0.7/mods/example-mod.jar",
      "sha256Hash": "abcdef123456...",
      "fileSizeBytes": 1234567
    }
  ],
  "deletePaths": [
    "mods/old-mod.jar"
  ]
}
```

Notes:
- `path` must be relative to Minecraft directory.
- `sha256Hash` is required for file integrity verification.
- `deletePaths` is optional; files/directories there will be removed.

## Manifest generation script

Use script:

`scripts/Generate-ModpackDeltaManifest.ps1`

Example:

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\Generate-ModpackDeltaManifest.ps1" `
  -ModpackRoot "D:\build\modpack-files" `
  -BaseUrl "https://github.com/Bloody965/srp-rp-launcher/releases/download/modpack-files-v1.0.7" `
  -Version "1.0.7" `
  -OutputPath ".\modpack-delta-manifest.json"
```

Then upload:
1. `modpack-delta-manifest.json`
2. all files referenced in `files[*].url`

## Recommended rollout

1. Upload full ZIP as before (safety fallback).
2. Upload delta files + manifest.
3. Set `Modpack__Version` to new version.
4. Set `Modpack__DeltaManifestUrl` to the manifest URL.
5. Deploy API.
