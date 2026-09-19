# Harmonia Source Guidance (HSG) Format v1

Harmonia Source Guidance is a deterministic, immutable JSON sidecar derived from multiple official-language Harmonia Source Snapshots (HXS) for one exact FFXIV game version. Its purpose is to tell downstream translation tooling which physical String columns have positive localization evidence.

Only `translatable` is affirmative permission to edit. `context`, `technical`, `unknown`, missing guidance, and incompatible guidance are all read-only from a translation-safety perspective.

## 1. Independent architecture

HSG is not HXS Format v2. HXS remains the canonical immutable game snapshot. Guidance is regenerated from already-produced HXS files and is not embedded in SQLite or `.hxs`.

The eligibility pipeline is intentionally independent from semantic enrichment:

```text
verified multilingual HXS snapshots
            |
            v
   SourceGuidanceAnalyzer
            |
            v
Translatable / Context / Technical / Unknown
            +
            v
      HSG eligibility bundle
            ^
            |
        semantics: null
```

This version does not use EXDSchema, GitHub, or any network service. It succeeds with valid multilingual HXS files even when no EXDSchema files are present. HSG does not create TranslationUnit IDs, Aeria state, or any other durable translation identity.

## 2. Format and bundle structure

The current format version is `1`. A bundle contains:

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
    }
  ],
  "eligibility": {
    "version": 1,
    "sheets": [
      {
        "name": "Item",
        "status": "compatible",
        "schemaHash": "sha256:...",
        "columns": [
          {
            "columnIndex": 0,
            "role": "translatable",
            "evidence": {
              "kind": "officialLanguageVariance",
              "languages": ["de", "en"],
              "comparableOccurrences": 52801,
              "varyingOccurrences": 52700
            }
          }
        ],
        "incompatibilityReasons": []
      }
    ]
  },
  "semantics": null
}
```

The persisted model contains physical column indexes and eligibility evidence only. It intentionally does not add labels such as `Name`, `Description`, `Singular`, or `Plural`.

## 3. Determinism and identity

The artifact is UTF-8 JSON with stable camel-case enum values, indentation, and a final LF newline. It contains no timestamps, absolute input paths, machine names, random IDs, or filesystem-order data.

Canonical ordering is explicit:

- `inputs` by `language`, ordinal ascending;
- `eligibility.sheets` by `name`, ordinal ascending;
- `columns` by numeric `columnIndex` ascending;
- evidence `languages` by ordinal ascending;
- incompatibility reasons by their stable contract order.

`bundleId` is a textual SHA-256 hash with the dedicated domain `HARMONIA-SOURCE-GUIDANCE-v1`. The canonical framed hash covers:

- `gameVersion` and `scope`;
- sorted input languages, `contentId`, and `snapshotId`;
- eligibility version;
- each sorted sheet name, status, schema hash, and incompatibility reason;
- each physical column index and role;
- each evidence kind, sorted evidence language list, occurrence counters, and optional namespace prefix.

The `bundleId` field is not included in its own hash. JSON whitespace, output paths, producer versions, and object enumeration order are not identity inputs.

## 4. Input validation

The `guidance` command accepts repeatable `--input` values and requires `--output`. At least two inputs are required. Duplicate paths and duplicate `hxs_meta.language` values are rejected.

Before any analysis, Atlas runs the existing complete HXS verification contract for every input. A valid SQLite marker alone is insufficient. Every input must have:

- the supported current HXS format version;
- a valid SQLite schema, integrity check, foreign-key check, and canonical payloads;
- valid sheet, row, String-cell, schema, row, sheet, `contentId`, and `snapshotId` hashes;
- the same exact `game_version` as every other input;
- the same exact `scope` as every other input;
- a distinct canonical source language taken from `hxs_meta.language`.

Input filenames never supply or override language. `contentId` and `snapshotId` do not need to match between languages; they are persisted as provenance because language-specific content intentionally produces language-specific identities.

## 5. Sheet compatibility

Atlas takes the union of sheet names across the verified inputs and emits a deterministic entry for every sheet. A sheet is `compatible` only when it exists in every input and all inputs have:

- the same physical sheet variant;
- the same physical columns;
- the same column indexes;
- the same column offsets;
- the same Harmonia column types;
- the same HXS schema hash.

Values are never compared across incompatible physical layouts. An absent or incompatible sheet is emitted as `incompatible`, with `incompatibleSourceLayout` evidence and no affirmative role. Its String columns are `unknown`. Typed reasons may include `missingInInput`, `sheetVariantMismatch`, `columnDefinitionMismatch`, `schemaHashMismatch`, and `rowTopologyMismatch`.

Only physical String columns are listed in a sheet's `columns` array. Technical columns are not translation candidates and are not implicitly writable by being absent from the guidance.

## 6. Row topology and bounded scanning

For a compatible sheet, Atlas compares rows by exact physical coordinate:

```text
row_id + subrow_id + column_index
```

It does not use text similarity, source text, or row hashes as cross-language identity. If row/subrow topology differs, the sheet is marked `incompatible` and no `translatable` result is emitted.

HXS verification is completed before analysis. The read-only HXS scan then enumerates sheet metadata and performs one ordered SQLite scan per input for the current sheet. Rows are compared in lockstep and only the current row's String cells are retained. The analyzer does not load every String cell from every language for the complete game into memory and does not issue one SQL query per cell.

## 7. Exact official-language variance

For each compatible physical String column, Atlas reads the exact HXS `macro_text` at each matching coordinate. It compares the strings with ordinal equality:

- no lowercasing;
- no trimming;
- no Unicode normalization;
- no visible-text projection;
- macros are not ignored;
- `raw_value` differences alone are not evidence.

`comparableOccurrences` is the number of row/subrow coordinates compared for that column across all selected languages. `varyingOccurrences` is the number of those coordinates where not all selected language `macro_text` values are exactly equal. Empty strings are retained. Therefore an empty value versus a non-empty value is a real variance.

If `varyingOccurrences > 0`, the column is `translatable` with `officialLanguageVariance` evidence. The evidence lists the selected languages and both counters so a downstream consumer can answer why editing was allowed.

## 8. Conservative `TEXT_` context rule

The only structural classifier in HSG v1 is the exact uppercase namespace rule. A compatible String column is `context` with `knownTechnicalNamespace` evidence and `prefix = "TEXT_"` only when:

1. at least one non-empty value is observed; and
2. every non-empty `macro_text` observed in every selected language begins with the exact ordinal prefix `TEXT_`.

The rule is case-sensitive, does not trim, and does not match fuzzy variants. If one non-empty value does not match, the entire column is not Context through this rule. No other identifier prefix or heuristic is classified in v1.

Classification precedence is deterministic:

1. incompatible source layout → `unknown`;
2. all-non-empty-values `TEXT_` namespace → `context`;
3. positive exact macro-text variance → `translatable`;
4. otherwise → `unknown` with `noOfficialLanguageVariance` evidence.

Invariant values are not classified as `technical`. Equal values are only a lack of positive language-variance evidence; a legitimate localized field can happen to be equal in the selected snapshots. HSG v1 has no broad automatic Technical classifier, although `technical` remains a reserved role value in the contract.

## 9. Semantics reservation

HSG v1 always emits:

```json
"semantics": null
```

This nullable section is reserved for a separate EXDSchema enrichment implementation. The intended future flow is:

```text
HXS game_version
    -> exact EXDSchema branch ver/<game_version>
    -> exact commit pin
    -> validate semantic layout against physical HXS schema
    -> populate semantics
```

That future work must not become a prerequisite for eligibility. Semantic failure must be able to degrade to “eligibility available, semantics unavailable.” It must not change HXS v1 identity or make network availability a requirement for HSG generation.

## 10. Atomic publication and verification

Generation serializes the complete logical bundle to a sibling `.partial` file, flushes it, reopens and validates the persisted contract, and then atomically moves it to the requested final path. A final artifact is never overwritten halfway through generation. On failure Atlas removes the temporary partial file where practical and does not publish a successful-looking final artifact.

`SourceGuidanceReader` validates the format version, required hashes, enum values, canonical ordering, evidence consistency, nullable semantics reservation, and recomputed `bundleId`. The artifact is therefore not write-only.

Normal HXS commands remain unchanged:

```text
extract
inspect
verify
```

Guidance is optional derived metadata. HXS v1 schema, format version, schema hashes, row hashes, `contentId`, `snapshotId`, and producer metadata semantics are unchanged.
