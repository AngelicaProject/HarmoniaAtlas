# Harmonia Source Snapshot (HXS) Format v2

This document specifies the HXS v2 format implemented by Harmonia Atlas.

HXS is an immutable, source-only artifact representing one complete Harmonia extraction of a local FINAL FANTASY XIV installation for one requested language. It is designed to be portable between local Harmonia Suite installations and future shared Harmonia services.

## 1. Versioning

HXS format versioning is independent from Harmonia Atlas application versioning and from Lumina versioning.

For HXS v2:

```text
format version:       2
SQLite application_id: 0x4841544C
SQLite user_version:   2
```

HXS v2 adds the `excluded_sheets` table, the `hxs_meta.excluded_sheet_count`
column, and the v2 `contentId` formula. Row, sheet, and snapshot hashes are
unchanged from v1. Atlas writes and verifies v2 only; v1 files are read by
earlier Atlas releases.

`0x4841544C` is the Harmonia Atlas HXS SQLite marker (`HATL` in ASCII bytes).

A Harmonia Atlas application update or Lumina update does not require an HXS format bump by itself. Any incompatible change to the persisted schema, canonical encoding, numeric type codes, identity rules, or hash semantics requires a new HXS format version.

Current producer metadata is:

```text
Harmonia Atlas: 0.3.0
Lumina:          7.7.0
```

These producer versions are provenance only. They are not part of `contentId`.

## 2. Scope and invariants

An HXS v2 artifact is:

- immutable after successful creation;
- source-only;
- user-independent;
- project-independent;
- translation-independent;
- self-contained in one SQLite file;
- deterministic at the logical-data/hash level.

Current canonical snapshots use:

```text
scope = "full"
```

A full extraction enumerates the sheet catalog exposed by Lumina. Every sheet of the catalog is either stored or listed in `excluded_sheets` with a reason; no sheet is silently omitted.

A sheet is excluded when it cannot be represented or read:

| Code | Reason |
| ---: | --- |
| `1` | Unsupported sheet variant |
| `2` | Unsupported column type |
| `3` | Unreadable data: the sheet header, a data page, or a row cannot be read or canonicalized |

Rows already written for an excluded sheet are rolled back, so an excluded sheet has no rows, columns, or String cells. Extraction still aborts on SQLite or file-system failures, on failures that are not attributable to one sheet, and when no sheet of a non-empty catalog can be read.

HXS does not define cross-version translation identity. Coordinates in an HXS identify source occurrences inside that snapshot.

## 3. Snapshot coordinate model

The source coordinate for a cell is:

```text
sheet name / row_id / subrow_id / column_index
```

For a normal-row sheet:

```text
subrow_id = 0
```

For a subrow sheet, `row_id` and `subrow_id` are both preserved from Lumina.

`sheet_id` is an internal SQLite surrogate key. It is not part of the semantic coordinate and is not included in canonical hashes.

Rows and columns are identified from source data, not from physical SQLite insertion order.

## 4. Requested and effective language

The snapshot-level `hxs_meta.language` is the language explicitly requested for extraction.

Atlas currently produces requested language codes:

```text
en
ja
de
fr
zh-cn
zh-tw
ko
```

Each sheet also stores `effective_language`.

Lumina may satisfy a requested localized sheet with a language-neutral sheet. In that case HXS records:

```text
effective_language = "none"
```

Therefore this is valid:

```text
snapshot language:       en
sheet effective_language: none
```

The effective language is part of `contentId`.

## 5. SQLite requirements

A completed HXS v2 file contains exactly these user-defined tables:

```text
hxs_meta
sheets
excluded_sheets
columns
rows
string_cells
```

HXS v2 defines no user views, triggers, or indexes.

SQLite-owned objects whose names begin with `sqlite_` are not part of the HXS user schema.

Atlas verifies the schema structurally. Unexpected user-defined tables, views, triggers, indexes, visible columns, generated columns, or hidden columns cause verification failure.

Writers enable foreign keys. Atlas creates the artifact using SQLite `journal_mode=DELETE`, so a completed HXS does not depend on a sidecar WAL or SHM file.

## 6. SQLite schema

The logical HXS v2 schema is:

```sql
CREATE TABLE hxs_meta (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    format_version INTEGER NOT NULL,
    game_version TEXT NOT NULL,
    language TEXT NOT NULL,
    scope TEXT NOT NULL,
    content_id TEXT NOT NULL,
    snapshot_id TEXT NOT NULL,
    extractor_version TEXT NOT NULL,
    lumina_version TEXT NOT NULL,
    sheet_count INTEGER NOT NULL,
    row_count INTEGER NOT NULL,
    string_cell_count INTEGER NOT NULL,
    excluded_sheet_count INTEGER NOT NULL
);

CREATE TABLE sheets (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    variant INTEGER NOT NULL CHECK (variant IN (0, 1)),
    effective_language TEXT NOT NULL,
    column_count INTEGER NOT NULL CHECK (column_count >= 0),
    row_count INTEGER NOT NULL CHECK (row_count >= 0),
    schema_hash BLOB NOT NULL,
    technical_hash BLOB NOT NULL,
    string_hash BLOB NOT NULL,
    content_hash BLOB NOT NULL
);

CREATE TABLE excluded_sheets (
    name TEXT PRIMARY KEY,
    reason INTEGER NOT NULL CHECK (reason IN (1, 2, 3))
);

CREATE TABLE columns (
    sheet_id INTEGER NOT NULL,
    column_index INTEGER NOT NULL CHECK (column_index >= 0),
    offset INTEGER NOT NULL CHECK (offset >= 0),
    type INTEGER NOT NULL,
    PRIMARY KEY (sheet_id, column_index),
    FOREIGN KEY (sheet_id) REFERENCES sheets(id)
);

CREATE TABLE "rows" (
    sheet_id INTEGER NOT NULL,
    row_id INTEGER NOT NULL CHECK (row_id >= 0),
    subrow_id INTEGER NOT NULL CHECK (subrow_id >= 0),
    technical_payload BLOB NOT NULL,
    row_hash BLOB NOT NULL,
    technical_hash BLOB NOT NULL,
    string_hash BLOB NOT NULL,
    PRIMARY KEY (sheet_id, row_id, subrow_id),
    FOREIGN KEY (sheet_id) REFERENCES sheets(id)
);

CREATE TABLE string_cells (
    sheet_id INTEGER NOT NULL,
    row_id INTEGER NOT NULL,
    subrow_id INTEGER NOT NULL,
    column_index INTEGER NOT NULL,
    macro_text TEXT NOT NULL,
    raw_value BLOB,
    macro_hash BLOB NOT NULL,
    raw_hash BLOB,
    PRIMARY KEY (sheet_id, row_id, subrow_id, column_index),
    FOREIGN KEY (sheet_id, row_id, subrow_id)
        REFERENCES "rows" (sheet_id, row_id, subrow_id)
);
```

`hxs_meta` must contain exactly one row with `id = 1`. `sheet_count` counts stored sheets and `excluded_sheet_count` counts rows of `excluded_sheets`. A sheet name must not appear in both `sheets` and `excluded_sheets`.

All SHA-256 hash BLOBs are exactly 32 bytes.

## 7. Sheet variants

HXS owns its persisted variant codes:

| Code | Meaning |
| ---: | --- |
| `0` | Default rows |
| `1` | Subrows |

Lumina enum numeric values are not part of the HXS persistence contract.

## 8. Column type codes

HXS owns its persisted column type codes:

| Code | Type |
| ---: | --- |
| `1` | String |
| `2` | Bool |
| `10` | Int8 |
| `11` | UInt8 |
| `12` | Int16 |
| `13` | UInt16 |
| `14` | Int32 |
| `15` | UInt32 |
| `16` | Int64 |
| `17` | UInt64 |
| `20` | Float32 |
| `30` | PackedBool0 |
| `31` | PackedBool1 |
| `32` | PackedBool2 |
| `33` | PackedBool3 |
| `34` | PackedBool4 |
| `35` | PackedBool5 |
| `36` | PackedBool6 |
| `37` | PackedBool7 |

Unknown Lumina column types are extraction errors. Atlas does not silently omit them.

## 9. String cells

Every String column in every stored row has one corresponding `string_cells` row.

`macro_text` is produced with:

```csharp
ReadOnlySeString.ToMacroString()
```

Ordinary `.ToString()` is not the HXS macro representation.

Atlas also stores the original bytes exposed by:

```csharp
ReadOnlySeString.Data
```

in `raw_value`.

`raw_value` and `raw_hash` form an optional pair at the format level:

- both may be present;
- both may be null;
- one present without the other is invalid.

Current Atlas game extraction stores raw bytes when Lumina provides them.

The macro and raw representations have independent hashes so downstream tools can reason about editable macro text and exact source bytes separately.

## 10. Technical payload

String cells are excluded from `rows.technical_payload`.

Every non-String column appears exactly once in the technical payload, ordered by ascending `column_index`.

Each technical cell is encoded as:

```text
u32 column_index
u32 HXS type code
u32 value_length
value bytes
```

All integer framing is little-endian.

Canonical value encoding is:

| Type | Canonical value |
| --- | --- |
| Bool | one byte, `00` or `01` |
| Int8 | one two's-complement byte |
| UInt8 | one byte |
| Int16 | 2-byte little-endian two's-complement |
| UInt16 | 2-byte little-endian |
| Int32 | 4-byte little-endian two's-complement |
| UInt32 | 4-byte little-endian |
| Int64 | 8-byte little-endian two's-complement |
| UInt64 | 8-byte little-endian |
| Float32 | exact IEEE-754 32-bit representation, little-endian |
| PackedBool0..7 | normalized logical byte, `00` or `01` |

Float32 values are never canonicalized through decimal text. Bit distinctions such as `+0.0` versus `-0.0` are preserved.

## 11. Canonical framing primitives

HXS hashes logical canonical data, not SQLite file bytes.

The canonical hasher uses:

```text
byte        = 1 raw byte
u16         = 2-byte little-endian
u32         = 4-byte little-endian
u64         = 8-byte little-endian
utf8(s)     = u32 UTF-8 byte length + UTF-8 bytes
bytes(b)    = u32 byte length + raw bytes
hash(h)     = 32 raw SHA-256 bytes
```

Hash domain strings are written as their exact UTF-8 bytes without a length prefix or terminator. All following variable-length text/byte fields are explicitly framed.

Canonical ordering uses ordinal string ordering, never locale-sensitive ordering.

## 12. String hashes

### Macro hash

```text
SHA256(
    "HARMONIA-HXS-V1-MACRO"
    + utf8(macro_text)
)
```

### Raw string hash

When `raw_value` is present:

```text
SHA256(
    "HARMONIA-HXS-V1-RAW-STRING"
    + bytes(raw_value)
)
```

## 13. Row hashes

Rows are canonically ordered by:

```text
row_id ASC
subrow_id ASC
```

### Row technical hash

```text
SHA256(
    "HARMONIA-HXS-V1-ROW-TECHNICAL"
    + utf8(sheet_name)
    + u32(row_id)
    + u32(subrow_id)
    + each technical cell in column_index order
)
```

Each technical cell uses the same framing as `technical_payload`:

```text
u32 column_index
u32 type
u32 value_length
value bytes
```

### Row string hash

```text
SHA256(
    "HARMONIA-HXS-V1-ROW-STRINGS"
    + utf8(sheet_name)
    + u32(row_id)
    + u32(subrow_id)
    + each String cell in column_index order
)
```

Each String-cell contribution is:

```text
u32 column_index
hash(macro_hash)
byte raw_hash_present
hash(raw_hash) if present
```

`raw_hash_present` is exactly `00` or `01`.

### Row hash

```text
SHA256(
    "HARMONIA-HXS-V1-ROW"
    + utf8(sheet_name)
    + u32(row_id)
    + u32(subrow_id)
    + hash(technical_hash)
    + hash(string_hash)
)
```

## 14. Sheet hashes

Sheets are canonically ordered by ordinal `name`.

Columns are canonically ordered by ascending `column_index`.

### Schema hash

```text
SHA256(
    "HARMONIA-HXS-V1-SCHEMA"
    + utf8(sheet_name)
    + u32(variant)
    + for each column:
        u32(column_index)
        u32(offset)
        u32(type)
)
```

### Sheet technical hash

```text
SHA256(
    "HARMONIA-HXS-V1-SHEET-TECHNICAL"
    + utf8(sheet_name)
    + for each row:
        u32(row_id)
        u32(subrow_id)
        hash(row.technical_hash)
)
```

### Sheet string hash

```text
SHA256(
    "HARMONIA-HXS-V1-SHEET-STRINGS"
    + utf8(sheet_name)
    + for each row:
        u32(row_id)
        u32(subrow_id)
        hash(row.string_hash)
)
```

### Sheet content hash

```text
SHA256(
    "HARMONIA-HXS-V1-SHEET"
    + utf8(sheet_name)
    + u32(variant)
    + hash(schema_hash)
    + hash(technical_hash)
    + hash(string_hash)
)
```

`effective_language` intentionally does not change the lower-level sheet hashes. It is included at snapshot content identity level.

## 15. contentId

`contentId` identifies canonical extracted source content for a requested snapshot language.

It is computed as:

```text
SHA256(
    "HARMONIA-HXS-CONTENT-v2"
    + utf8(snapshot_language)
    + u32(stored sheet count)
    + for each stored sheet ordered by ordinal name:
        utf8(sheet.name)
        utf8(sheet.effective_language)
        hash(sheet.schema_hash)
        hash(sheet.content_hash)
    + u32(excluded sheet count)
    + for each excluded sheet ordered by ordinal name:
        utf8(name)
        u32(reason)
)
```

The public textual representation is:

```text
sha256:<64 lowercase hexadecimal characters>
```

`contentId` does not include:

```text
game_version
extractor_version
lumina_version
timestamp
output filename
output path
hostname
username
SQLite page layout
SQLite surrogate sheet_id
```

Consequences:

- identical canonical content for the same requested language produces the same `contentId`;
- changing technical data changes `contentId`;
- changing String data changes `contentId`;
- changing schema changes `contentId`;
- changing sheet effective language changes `contentId`;
- excluding a sheet, or changing an exclusion reason, changes `contentId`;
- changing only game version does not change `contentId`.

## 16. snapshotId

`snapshotId` identifies canonical content as belonging to a specific installed game version.

It is computed as:

```text
SHA256(
    "HARMONIA-HXS-SNAPSHOT-v1"
    + utf8(game_version)
    + utf8(snapshot_language)
    + utf8(contentId)
)
```

Its textual representation is also:

```text
sha256:<64 lowercase hexadecimal characters>
```

Therefore two distinct game versions with identical source content may have:

```text
same contentId
different snapshotId
```

This distinction is intentional.

## 17. Game version

Atlas reads the installed game version from:

```text
game/ffxivgame.ver
```

The file is read as UTF-8, trimmed, and normalized to Unicode NFC.

A missing, empty, or malformed version file is an extraction error. Atlas does not guess a game version.

## 18. Atomic creation

Atlas writes to a sibling partial path first:

```text
target.hxs.partial
```

The high-level completion flow is:

```text
validate game installation
open Lumina
create partial SQLite database
set HXS SQLite identity
create HXS schema
begin transaction
extract and write all sheets/rows/cells
write metadata
commit transaction
close SQLite resources
flush partial file to disk
run full HXS verification on the partial file
rename partial file to target.hxs
```

Atlas does not silently overwrite an existing final target.

On normal managed failure/disposal, Atlas performs best-effort rollback/cleanup and deletes the partial file. A `.partial` file may remain after abnormal process or system termination where cleanup could not run.

## 19. Verification

`harmonia-atlas verify` does not trust stored identity values.

Verification includes:

- file existence and read-only SQLite opening;
- `application_id`;
- `user_version`;
- exact allowed HXS user schema objects;
- exact table column sets through `PRAGMA table_xinfo`;
- rejection of hidden/generated columns;
- SQLite `integrity_check`;
- SQLite `foreign_key_check`;
- exactly one metadata row;
- supported HXS format version and `scope = "full"`;
- valid exclusion reason codes, no sheet both stored and excluded, and the excluded sheet count;
- sheet column counts;
- valid sheet variant codes;
- valid HXS column type codes;
- technical payload canonicality and complete non-String coverage;
- complete String-column coverage per row;
- macro hashes;
- raw-value/raw-hash pairing and raw hashes;
- row technical/string/combined hashes;
- sheet schema/technical/string/content hashes;
- metadata sheet/row/string counts;
- recomputed `contentId`;
- recomputed `snapshotId`.

The current verifier requires language metadata to be non-empty and hashes it as part of identity. Atlas itself emits the canonical language codes described above.

## 20. Security and untrusted HXS files

HXS is a SQLite container and should be treated as untrusted input when received from another machine or user.

A service ingesting uploaded HXS files should:

1. open them read-only;
2. verify SQLite/HXS identity and exact schema;
3. enforce external file-size and row/count limits appropriate to the service;
4. recompute all HXS hashes and identities;
5. reject unsupported future format versions;
6. ingest validated logical data into service-owned storage rather than treating an uploaded SQLite file as an operational database.

Do not trust a claimed `contentId` or `snapshotId` without recomputation.

## 21. Format evolution

HXS v2 constants are format-level commitments:

- SQLite `application_id`;
- `user_version`;
- table/column contract;
- sheet variant codes;
- column type codes;
- canonical primitive encodings;
- hash domain strings;
- hash field order;
- canonical ordering rules;
- `contentId` and `snapshotId` semantics.

A change that breaks interpretation or identity compatibility requires a new HXS format version and new hash-domain/version semantics where appropriate.

Producer implementation changes that preserve the HXS v2 contract do not require a format bump.
