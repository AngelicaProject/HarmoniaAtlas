# Harmonia Source Guidance Format v1

Harmonia Source Guidance (HSG) is a deterministic JSON sidecar generated from two or more verified Harmonia Source Snapshots (HXS) for one game version. It is a sparse positive allowlist of exact physical String occurrences that have multilingual localization evidence.

An occurrence is writable only when it is present in the allowlist. Absence means read-only.

## Purpose

HSG identifies source occurrences using the physical HXS coordinate:

```text
sheet name + row_id + subrow_id + column_index
```

It does not persist source text, translation state, semantic field labels, or UI presentation information. It is derived metadata and does not change HXS identity.

## File structure

An HSG v1 file has this shape:

```json
{
  "formatVersion": 1,
  "gameVersion": "2026.09.01.0000.0000",
  "scope": "full",
  "bundleId": "sha256:...",
  "inputs": [
    {
      "language": "de",
      "contentId": "sha256:...",
      "snapshotId": "sha256:..."
    },
    {
      "language": "en",
      "contentId": "sha256:...",
      "snapshotId": "sha256:..."
    }
  ],
  "sheets": [
    {
      "name": "Item",
      "schemaHash": "sha256:...",
      "status": "compatible",
      "translatable": [
        {
          "rowId": 2,
          "subrowId": 0,
          "columnIndex": 0
        }
      ],
      "incompatibilityReasons": []
    }
  ]
}
```

`formatVersion` is `1`. `gameVersion` and `scope` are copied from the verified inputs. Each input records its HXS language, `contentId`, and `snapshotId`; local paths are never persisted.

Each sheet records the representative physical `schemaHash`, a `compatible` or `incompatible` status, a sorted `translatable` allowlist, and typed incompatibility reasons. An incompatible sheet always has an empty allowlist.

## Input snapshots

Before comparison, Atlas fully verifies every HXS input. Verification includes the supported HXS format and SQLite contract, integrity and foreign-key checks, canonical payloads, String-cell coverage, schema/row/sheet hashes, counts, `contentId`, and `snapshotId`.

At least two inputs are required. Their `hxs_meta.language` values must be distinct. All inputs must have the same exact `game_version` and `scope`. Input filenames do not provide language. Language-specific `contentId` and `snapshotId` values do not need to match.

## Sheet compatibility

A sheet is compatible only when it exists in every input and the inputs agree on:

- sheet variant;
- physical column indexes;
- column offsets;
- Harmonia column types;
- HXS schema hash.

Atlas emits a sheet entry for the union of sheet names. A missing or structurally mismatched sheet is `incompatible`, has an empty `translatable` array, and is not analyzed for values. Supported incompatibility reasons are:

```text
missingInInput
sheetVariantMismatch
columnDefinitionMismatch
schemaHashMismatch
rowTopologyMismatch
```

## Translatable occurrence rule

For a compatible sheet, Atlas compares every physical String occurrence at the exact row/subrow/column coordinate across all selected inputs. The occurrence is added to `translatable` exactly when the selected languages do not all have equal `macro_text` values under ordinal comparison.

The comparison is exact:

- no lowercasing;
- no trimming;
- no Unicode normalization;
- no visible-text projection;
- no text similarity;
- no identifier heuristics;
- raw-value differences alone do not grant permission.

An empty string is a source value. Empty versus non-empty is a real variance and adds that occurrence to the allowlist. Every language does not need to differ; one exact difference is sufficient.

For example, in one String column:

```text
row 1: LogChatBubbleShoutFontColor / LogChatBubbleShoutFontColor
row 2: Hello / こんにちは
```

only row 2 is allowlisted. A localized occurrence in one row never grants permission to another row in the same column.

## Row topology

Rows are compared in ordered scans by exact `row_id` and `subrow_id`. String cells are matched by exact `column_index`. If any input has a different row/subrow topology for a sheet, the whole sheet is incompatible and its allowlist is empty. Atlas does not guess correspondences from text or row hashes.

## Canonical ordering

Persisted arrays are canonicalized explicitly:

- `inputs` by `language`, ordinal ascending;
- `sheets` by `name`, ordinal ascending;
- `incompatibilityReasons` by their contract order;
- `translatable` by `rowId`, then `subrowId`, then `columnIndex`.

JSON is UTF-8, indented, uses stable camel-case enum values, and ends with one LF newline. The artifact contains no timestamps, absolute paths, machine names, random IDs, or filesystem-order data.

## bundleId

`bundleId` is a lowercase `sha256:` string computed with the dedicated domain:

```text
HARMONIA-SOURCE-GUIDANCE-v1
```

The canonical framed hash covers:

- `gameVersion` and `scope`;
- sorted input `language`, `contentId`, and `snapshotId`;
- every sorted sheet name, status, schema hash, and incompatibility reason;
- every positive occurrence’s `rowId`, `subrowId`, and `columnIndex`.

The `bundleId` field is excluded from its own hash. Output paths, timestamps, machine state, JSON whitespace, and dictionary enumeration order are not hashed.

## Validation and publication

`SourceGuidanceReader` validates format version, required SHA-256 strings, input and sheet ordering, enum values, typed incompatibility reasons, coordinate ordering, incompatible-sheet emptiness, and the recomputed `bundleId`.

Generation writes the complete JSON to `<output>.partial`, flushes it, reads it back through the validator, and atomically publishes the final output. A failed generation does not publish a final artifact and removes the temporary file where practical.

The command is:

```bash
dotnet run --project src/HarmoniaAtlas -- guidance \
  --input source-en.hxs \
  --input source-ja.hxs \
  --output source.hsg.json
```
