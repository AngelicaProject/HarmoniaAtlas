# Harmonia Atlas

Harmonia Atlas is the canonical source snapshot generator for the Harmonia ecosystem.

It reads a local FINAL FANTASY XIV installation through [Lumina](https://github.com/NotAdam/Lumina) and produces an immutable, versioned Harmonia Source Snapshot (`.hxs`). The snapshot is intended to be the source-of-truth input for downstream Harmonia tooling such as Harmonia Suite and future shared translation services.

```text
FINAL FANTASY XIV installation
            |
            v
      Harmonia Atlas
            |
            v
 Harmonia Source Snapshot (.hxs)
            |
            v
  Harmonia Suite / backend
```

## Status

Harmonia Atlas is in early development.

- Application version: `0.1.0`
- HXS format version: `1`
- Lumina: `7.7.0`
- Runtime: .NET 10

HXS has its own format version. Updating Harmonia Atlas or Lumina does not by itself change the HXS format version; incompatible HXS contract changes require a new format version.

## What Atlas does

Atlas is intentionally narrow and stateless:

- validates an explicitly supplied FFXIV installation path;
- reads the installed game version from `game/ffxivgame.ver`;
- enumerates the game's Excel/EXD sheets through Lumina;
- supports both normal rows and subrows;
- preserves Harmonia-owned column types and canonical technical values;
- stores every String cell as Lumina `ToMacroString()` text plus the original `ReadOnlySeString.Data` bytes;
- computes deterministic row, sheet, content, and snapshot hashes;
- writes a self-contained SQLite `.hxs` artifact atomically through a `.partial` file;
- independently verifies completed snapshots;
- exposes concise human-readable and JSON inspection output.

A canonical extraction is all-or-nothing. Unsupported column types, unreadable sheets, corrupt data, or other extraction failures abort the snapshot instead of silently omitting data.

## What Atlas does not do

Atlas does not contain translation state or product workflow state. In particular, an HXS does not contain:

- users or accounts;
- projects or workspaces;
- translations or review statuses;
- cross-version migration decisions;
- runtime plugin overrides;
- network/upload state;
- absolute local game paths or machine-specific identity.

Runtime coordinates and cross-version translation identity are deliberately separate concerns. Atlas records factual source data for one snapshot; later Harmonia components are responsible for rebasing or migrating translation work between snapshots.

## CLI

Run from source with the .NET SDK:

```bash
dotnet run --project src/HarmoniaAtlas -- extract \
  --game-path <ffxiv-installation-root> \
  --language en \
  --output source-en.hxs
```

The supplied game path must contain at least `game/sqpack` and `game/ffxivgame.ver`.

Supported requested language codes are currently:

```text
en  ja  de  fr  zh-cn  zh-tw  ko
```

Inspect a snapshot:

```bash
dotnet run --project src/HarmoniaAtlas -- inspect source-en.hxs
```

Machine-readable inspection:

```bash
dotnet run --project src/HarmoniaAtlas -- inspect source-en.hxs --json
```

Verify a snapshot by recomputing its schema expectations, hashes, counts, `contentId`, and `snapshotId`:

```bash
dotnet run --project src/HarmoniaAtlas -- verify source-en.hxs
```

Generate a deterministic Harmonia Source Guidance sidecar from at least two verified HXS snapshots of the same game version:

```bash
dotnet run --project src/HarmoniaAtlas -- guidance \
  --input source-en.hxs \
  --input source-ja.hxs \
  --input source-de.hxs \
  --input source-fr.hxs \
  --output ffxiv-2026.09.01.0000.0000.hsg.json
```

Guidance compares exact `macro_text` values at exact sheet/row/subrow/column coordinates. Only a column with positive official-language variance is `translatable`; `context`, `technical`, `unknown`, missing guidance, and incompatible guidance are read-only from a translation-safety perspective. A String cell is never writable merely because it is a String. Guidance is derived metadata: it does not change HXS identity, create translation identity, or become part of an `.hxs` file. It is generated fully offline and does not require EXDSchema or network access.

The sidecar format and its fail-closed rules are specified in [`docs/SOURCE_GUIDANCE_FORMAT.md`](docs/SOURCE_GUIDANCE_FORMAT.md).

Extraction also supports `--json` for structured command output. Diagnostics are written to stderr; command output is written to stdout.

## Building and testing

The repository pins the .NET SDK through `global.json`.

```bash
dotnet restore HarmoniaAtlas.slnx
dotnet build HarmoniaAtlas.slnx --no-restore
dotnet test HarmoniaAtlas.slnx --no-restore
```

Warnings are treated as errors and builds are deterministic.

The application project declares `win-x64` and `linux-x64` runtime identifiers. Release packaging is intentionally separate from the HXS format contract.

## HXS identity

HXS uses two related identities:

- `contentId` identifies the canonical extracted source content for the requested language. It does not include the game version or producer versions.
- `snapshotId` identifies that content as a snapshot of a specific installed game version.

Therefore two game builds may share a `contentId` when their extracted source content is identical, while still having different `snapshotId` values.

The exact SQLite schema, canonical encoding rules, numeric type codes, hash domains, ordering rules, and verification requirements are specified in [`docs/HXS_FORMAT.md`](docs/HXS_FORMAT.md).

## Repository structure

```text
src/HarmoniaAtlas/
  Cli/          command parsing and execution
  Game/         game installation and Lumina boundary
  Extraction/   extraction pipeline and canonical row conversion
  Hxs/          HXS schema, hashing, reader/writer, verifier, inspector
  Guidance/     multilingual eligibility analysis and HSG sidecar I/O
  Model/        Harmonia-owned source model

tests/HarmoniaAtlas.Tests/
  unit and synthetic HXS contract tests
```

Lumina is isolated behind the game/extraction boundary. Lumina enum numeric values are not used as persisted HXS type identifiers.

## License

Harmonia Atlas is licensed under the GNU Affero General Public License v3.0. See [`LICENSE`](LICENSE).

FINAL FANTASY XIV and related game data are property of their respective rights holders. Harmonia Atlas is an independent project and is not affiliated with or endorsed by Square Enix.
