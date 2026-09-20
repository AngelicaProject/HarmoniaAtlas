# Harmonia Source Package Format v1

## Purpose

A Harmonia Source Package (`.hsp`) is the validated Atlas handoff artifact for one source-language snapshot and its multilingual source guidance. It is a ZIP container. ZIP is transport only; the package identity is independent of ZIP timestamps and compression metadata.

## Archive structure

An HSP v1 archive contains exactly one manifest and the files listed by that manifest:

```text
manifest.json
guidance/source-guidance.json
source/source.hxs
```

The manifest is compact UTF-8 without a BOM and ends with one LF. Entries have fixed timestamps and are written in this order: `manifest.json`, then component paths in ordinal path order. Directory entries are not used.

## Manifest

The current manifest shape is:

```json
{
  "formatVersion": 1,
  "packageId": "sha256:...",
  "gameVersion": "2026.09.01.0000.0000",
  "scope": "full",
  "source": {
    "language": "en",
    "contentId": "sha256:...",
    "snapshotId": "sha256:..."
  },
  "components": [
    {
      "id": "guidance",
      "kind": "sourceGuidance",
      "formatVersion": 1,
      "required": true,
      "path": "guidance/source-guidance.json",
      "size": 12345,
      "sha256": "sha256:..."
    },
    {
      "id": "source",
      "kind": "sourceHxs",
      "formatVersion": 1,
      "required": true,
      "path": "source/source.hxs",
      "size": 123456789,
      "sha256": "sha256:..."
    }
  ]
}
```

Component IDs and paths are unique. Paths are normalized relative paths using `/`; absolute paths, backslashes, `.` segments, and `..` segments are invalid. `size` is the exact uncompressed byte size and `sha256` is the SHA-256 hash of the exact component bytes.

The required v1 components are exactly one `sourceHxs` component at `source/source.hxs` and exactly one `sourceGuidance` component at `guidance/source-guidance.json`.

## Source identity

The manifest source language, game version, scope, content ID, and snapshot ID must match the embedded HXS metadata. The embedded HSG must contain the same game version, scope, and source identity. The HSG must pass Source Guidance v1 validation, including its source evidence identity matching the embedded HXS.

## Package identity

`packageId` is the logical package identity. Atlas hashes the domain `HARMONIA-SOURCE-PACKAGE-v1` followed by canonical typed values for `formatVersion`, `gameVersion`, `scope`, source language/content/snapshot IDs, and component descriptors sorted by component ID. Each descriptor contributes `id`, `kind`, `formatVersion`, `required`, `path`, `size`, and `sha256`.

The `packageId` field itself is excluded from this hash. ZIP timestamps, compression settings, archive order, filesystem paths, temporary paths, and wall-clock time are excluded.

## Validation

A reader validates the ZIP, duplicate entry names, the single manifest, manifest version, component uniqueness and path safety, required components, archive membership, sizes, hashes, package ID, embedded HXS, embedded HSG, and their relationships. Every compatible HSG sheet must exist in the embedded HXS with the same schema hash. It materializes only the two manifest-approved current components into a controlled temporary directory while validating them; arbitrary ZIP entries are never extracted.

Unknown optional component kinds are integrity-checked and may be ignored. Unknown required component kinds are rejected. This permits optional components to be added without changing the HXS or HSG contracts.

## Compatibility

HSP v1 embeds HXS Format v1 and Source Guidance Format v1. It does not add EXDSchema or semantic metadata.

## Publication

Atlas builds components in owned temporary storage, writes `<output>.partial`, validates the completed archive, and only then atomically publishes the requested `.hsp` path. Generation failures remove the partial archive and owned temporary files. A stale partial is removed before the next invocation and is never treated as a valid package. A hard process termination may leave owned temporary state for later cleanup.

## CLI example

```bash
harmonia-atlas package \
  --game-path <ffxiv-path> \
  --language en \
  --output source-en.hsp \
  --events jsonl
```

The package product path currently supports the global FFXIV evidence family `en`, `ja`, `de`, and `fr`. The selected source language is included in the evidence set. Other canonical Atlas language codes remain supported by standalone HXS and HSG commands, but the package path fails safely until an evidence policy exists for them.
