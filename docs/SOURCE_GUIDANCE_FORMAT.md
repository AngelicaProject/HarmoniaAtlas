# Harmonia Source Guidance Format v1

Harmonia Source Guidance (HSG) is deterministic JSON metadata that grants translation permission for exact physical String occurrences in one source HXS. It is derived from multilingual source data and contains a sparse positive allowlist. An occurrence absent from the allowlist is read-only.

## Purpose

HSG identifies an occurrence by its physical HXS coordinate:

```text
sheet name + row_id + subrow_id + column_index
```

HSG stores the exact identity of the source HXS and lightweight fingerprints for the evidence used to compare languages. It does not store source text, translation state, semantic field labels, or blocked-cell records.

## File structure

An HSG v1 file has this shape:

```json
{
  "formatVersion": 1,
  "gameVersion": "2026.09.01.0000.0000",
  "scope": "full",
  "bundleId": "sha256:...",
  "source": {
    "language": "en",
    "contentId": "sha256:...",
    "snapshotId": "sha256:..."
  },
  "evidenceInputs": [
    {
      "language": "de",
      "evidenceId": "sha256:..."
    },
    {
      "language": "en",
      "evidenceId": "sha256:..."
    },
    {
      "language": "fr",
      "evidenceId": "sha256:..."
    },
    {
      "language": "ja",
      "evidenceId": "sha256:..."
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

`formatVersion` is `1`. `gameVersion` and `scope` are copied from the verified source and comparison inputs. Local paths are never persisted.

## Source identity

`source` identifies the HXS to which the allowlist applies:

- `language` is the source HXS `language`;
- `contentId` is the source HXS `content_id`;
- `snapshotId` is the source HXS `snapshot_id`.

The source identity is authoritative when a consumer checks whether HSG can be applied to an HXS.

## Evidence inputs and evidenceId

`evidenceInputs` records the languages and lightweight fingerprints used to derive the allowlist. The source language appears exactly once in this array. Every language is canonical and distinct.

An `evidenceId` is a SHA-256 fingerprint in the domain:

```text
HARMONIA-SOURCE-GUIDANCE-EVIDENCE-v1
```

The canonical evidence stream contains the game version, scope, and language, followed by every sheet in ordinal name order. Each sheet contributes its name, variant, and physical schema hash. Each physical row contributes `row_id` and `subrow_id`; each String occurrence contributes `column_index` and exact UTF-8 `macro_text` in column order.

Evidence IDs do not include raw bytes, raw hashes, stored macro hashes, technical values or payloads, row hashes, technical or String hashes, sheet content hashes, `contentId`, `snapshotId`, producer versions, paths, or timestamps. Therefore raw-only and technical-only changes do not change an evidence ID, while changes to macro text, coordinates, schema, language, game version, or scope do.

## Input requirements

The current guidance command fully verifies one source HXS and at least one comparison HXS before analysis. The files must use distinct canonical `hxs_meta.language` values from this set:

```text
en  ja  de  fr  zh-cn  zh-tw  ko
```

Language matching is exact and case-sensitive. Values such as `english`, `japanese`, and `zh_CN` are invalid. Filenames do not provide language. All inputs must have the same exact `game_version` and `scope`.

Comparison HXS files are used to derive evidence and permissions, but their HXS `contentId` and `snapshotId` values are not persisted in HSG.

## Sheet compatibility

A sheet is compatible only when it exists in every input and all inputs agree on:

- sheet variant;
- physical column indexes;
- column offsets;
- Harmonia column types;
- HXS schema hash.

Atlas emits a sheet entry for the union of sheet names. A missing or structurally mismatched sheet is `incompatible` and has an empty `translatable` array. Supported incompatibility reasons are:

```text
missingInInput
sheetVariantMismatch
columnDefinitionMismatch
schemaHashMismatch
rowTopologyMismatch
unreadableInInput
```

`unreadableInInput` means that an input lists the sheet as excluded or that the sheet failed while being read. A sheet that no input could read has no schema to report and is omitted from `sheets`.

A sheet whose effective specific language differs from the requested evidence language is incompatible and has an empty `translatable` array. Its physical String rows still participate in that input's `evidenceId`.

## Translatable occurrence rule

For a compatible sheet, Atlas compares exact `macro_text` values at each physical row/subrow/column coordinate across all selected evidence languages. An occurrence is added to `translatable` when:

1. its `macro_text` in the source language is not empty; and
2. the values are not all equal under ordinal comparison.

Otherwise it is absent and read-only. An empty source text has nothing to translate even when another language has text there.

The comparison is exact:

- no lowercasing;
- no trimming;
- no Unicode normalization;
- no visible-text projection;
- no text similarity;
- no identifier heuristics;
- raw-value differences alone do not grant permission.

An empty comparison value is a source value: a non-empty source text next to an empty comparison value is real variance. One differing language is sufficient.

## Applying guidance to a snapshot

An HSG bundle may grant permission to an HXS only when all global source checks pass:

1. `HSG.gameVersion` equals `HXS.game_version`.
2. `HSG.scope` equals `HXS.scope`.
3. `HSG.source.language` equals `HXS.language`.
4. `HSG.source.contentId` equals `HXS.content_id`.
5. `HSG.source.snapshotId` equals `HXS.snapshot_id`.

For each sheet whose allowlist is consumed, `HSG.schemaHash` must equal the HXS `schema_hash`. A failed global source check grants no permission anywhere. A failed sheet schema check grants no permission for that sheet.

`evidenceInputs` explains how permissions were derived. A consumer does not need the comparison HXS files to consume an HSG. Do not match by game version alone, language alone, a similar content ID, another snapshot from the same patch, or a same-named sheet with a different schema. HSG does not repair mismatches or use text similarity.

## Row topology

Rows are compared in ordered scans by exact `row_id` and `subrow_id`. String cells are matched by exact `column_index`. If any input has different row/subrow topology for a sheet, the whole sheet is incompatible and its allowlist is empty. Atlas does not guess correspondences from text or row hashes.

## Canonical ordering

Persisted arrays are canonicalized explicitly:

- `evidenceInputs` by `language`, ordinal ascending;
- `sheets` by `name`, ordinal ascending;
- `incompatibilityReasons` by contract order;
- `translatable` by `rowId`, then `subrowId`, then `columnIndex`.

Persisted JSON is compact UTF-8 without a BOM, uses stable camel-case enum values, and ends with exactly one LF newline. JSON whitespace is not part of `bundleId`. The formatted JSON example in this document is for readability.

## bundleId

`bundleId` is a lowercase `sha256:` string computed with the domain:

```text
HARMONIA-SOURCE-GUIDANCE-v1
```

The canonical framed hash covers:

- `gameVersion` and `scope`;
- source `language`, `contentId`, and `snapshotId`;
- sorted evidence-input `language` and `evidenceId`;
- every sorted sheet name, status, schema hash, and incompatibility reason;
- every sorted positive occurrence’s `rowId`, `subrowId`, and `columnIndex`.

The `bundleId` field is excluded from its own hash. Output paths, timestamps, machine state, and JSON whitespace are not hashed.

## Validation and publication

`SourceGuidanceReader` validates the format version, source identity, canonical language codes, evidence-input membership and ordering, required SHA-256 strings, sheet ordering, enum values, typed incompatibility reasons, coordinate ordering, incompatible-sheet emptiness, and the recomputed `bundleId`.

Generation writes compact JSON to `<output>.partial`, flushes it, reads it back through the validator, and atomically publishes the final output. A failed generation does not publish a final artifact and removes the temporary file where practical.

## CLI example

```bash
dotnet run --project src/HarmoniaAtlas -- guidance \
  --source source-en.hxs \
  --compare source-ja.hxs \
  --compare source-de.hxs \
  --compare source-fr.hxs \
  --output source-en.hsg.json
```
