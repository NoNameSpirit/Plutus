# CLI reference

Plutus is one command per job. Bundling takes one or more local project paths or supported remote repository URLs,
followed by options.

```text
plutus bundle <project-path> [<project-path> ...] [options]
plutus init [--output <path>] [--force]
```

When running from the repository, use `dotnet run --project Plutus.CLI -- <arguments>`.

## Commands

| Command | Description |
|---|---|
| `bundle` | Collect the given project paths into a single document. |
| `init` | Write a `.plutusrc` profile with every option already in it, set to the value a run would use anyway. |

A command line that names no command is read as `bundle`, so the shorter `plutus <project-path> [options]` keeps
working and both spellings mean the same thing:

```bash
dotnet run --project Plutus.CLI -- bundle ./my-project -o context.md
dotnet run --project Plutus.CLI -- ./my-project -o context.md          # the same run
```

`init` writes every profile key with the value a run would use anyway, so the generated file is a starting point to
edit rather than a schema to learn, and generating it cannot change what a bundle contains. It refuses to replace an
existing profile unless `--force` is given, and `--output` writes somewhere other than
`<current directory>/.plutusrc`. See [Configuration profiles](CONFIGURATION.md).

```bash
dotnet run --project Plutus.CLI -- init
dotnet run --project Plutus.CLI -- init -o ci/plutus.ci.plutusrc
dotnet run --project Plutus.CLI -- init --force
```

## Options

The options below belong to `bundle`.

| Option | Default | Description |
|---|---|---|
| `-o`, `--output <path>` | Generated in the current working directory | Output file path. If omitted, Plutus derives a bundle name from the first input and selected format. |
| `--profile <path...>` | Discovered automatically | Use these `.plutusrc` profiles instead of searching for one; repeatable, later profiles override earlier ones. Overrides the `PLUTUS_PROFILE` environment variable. See [Configuration profiles](CONFIGURATION.md). |
| `--no-profile` | Off | Ignore any `.plutusrc` profile (and `PLUTUS_PROFILE`) and run with the command line and built-in defaults only. |
| `--format <md\|json\|xml>` | `md` | Output serialization format. |
| `--max-file-size <size>` | `1M` | Maximum size of an individual file eligible for reading and inclusion. Examples: `512K`, `1M`, `2MB`. Oversized files are represented as skipped in the bundle's tree/metadata rather than embedded as source content. |
| `--max-tokens <count>` | Disabled | Optional positive integer token budget for estimated source content. See [Token budget](TOKEN_BUDGET.md). |
| `--token-overflow-policy <fail\|warn\|select>` | `fail` | Behavior when estimated eligible source content exceeds `--max-tokens`. |
| `--token-selection-strategy <path-order\|largest-first>` | `path-order` | Whole-file selection strategy used when the overflow policy is `select`. |
| `--max-concurrency <number>` | `8` | Maximum number of concurrent file-read operations. Must be at least `1`. |
| `--encoding <name>` | `utf-8` | Text encoding used for reading and writing. Examples: `utf-8`, `windows-1251`, `koi8-r`. The encoding must be supported by .NET. |
| `-m`, `--minify` | Off | Apply the configured source minifier before writing file contents: comments are removed and whitespace-only lines are collapsed. Uses the same comment scanner as `--strip-comments`; the transformations depend on the language. |
| `--strip-comments` | Off | Remove comments only, for languages with a maintained comment syntax. Whitespace, indentation, and line endings are preserved, and a space is inserted when removing a comment would otherwise join two tokens. See [Comment stripping](COMMENT_STRIPPING.md). |
| `--preserve-doc-comments` | Off | Keep documentation comments (`///`, `//!`, `/** */`, `/*! */`) while stripping or minifying. Off by default: documentation comments are removed like any other comment. No effect without `--strip-comments` or `--minify`. |
| `--comment-strategy <auto\|lexical\|antlr>` | `auto` | How comments are located. `auto` uses ANTLR token channels for languages a project-maintained grammar covers and lexical scanning elsewhere; `lexical` forces the character scanner; `antlr` requires a maintained grammar and fails when none exists (no silent fallback). See [ANTLR grammar inventory](ANTLR_GRAMMAR_INVENTORY.md). |
| `--signatures-only` | Off | Reduce supported files to their signatures: a parse-tree pass replaces implementation bodies (method bodies, `=>` expression bodies, property accessor bodies) with a structural terminator, keeping declarations, attributes, generics and constraints. Independent of `--strip-comments` and `--minify`. See [Signature extraction](SIGNATURE_EXTRACTION.md). |
| `--no-gitignore` | Off | Do not read the root `.gitignore`. |
| `--use-default-excludes` | On | Use built-in exclusions for common generated/junk paths. |
| `--exclude <pattern...>` / `-e` | None | Global gitignore-style patterns that can exclude files and directories. Can be repeated. |
| `--exclude-files <pattern...>` | None | Exclude matching files. |
| `--exclude-dirs <pattern...>` | None | Exclude matching directories. Patterns can target a name or a path, depending on matcher semantics. |
| `--include-files <pattern...>` | None | Inverted filter: when set, only files matching the include patterns are retained. |
| `--include-dirs <pattern...>` | None | Inverted filter for directory/module paths. |
| `--allow-dangerous-files` | Off | Allow files normally excluded because their names/extensions look sensitive, such as `.env` or private-key files. Use only when intentional. |
| `--diff-only` | Off | Emit the change itself rather than the source. The bundle keeps its header, the list of changed paths and one fenced diff per file, and **omits all file contents** (and the directory tree). The working tree is compared with `HEAD`. |
| `--diff-from <ref>` | None | Restrict the bundle to files changed relative to this branch, tag, or commit, using merge-base semantics, and **keep their contents**. Cannot be combined with `--branch` or `--commit`. |
| `--diff-to <ref>` | None | Other end of an explicit commit range. Together with `--diff-from` it compares the two revisions directly, so any commit range can be bundled. Requires `--diff-from`. |
| `--diff-include-untracked` | Off | With `--diff-only` or `--diff-from`, also cover files git does not track yet, excluding anything `.gitignore` already covers. A real new-file diff is generated for them. |
| `--branch <name>` | Provider default | Remote branch or tag to download. Cannot be combined with `--commit`. |
| `--commit <hash>` | Provider default | Specific remote commit hash, 7–40 hexadecimal characters. Cannot be combined with `--branch`. |
| `--max-download-size <size>` | `100MB` | Maximum allowed download size for remote archives. Archives exceeding this limit are rejected before extraction. Examples: `50M`, `100MB`. |
| `--max-extracted-size <size>` | `500MB` | Maximum allowed total extracted size (sum of all file sizes after decompression). Protects against decompression bombs (zip bombs). Examples: `200M`, `500MB`. |
| `--max-archive-file-count <number>` | `10000` | Maximum number of files allowed in a remote archive. Must be at least 101. |
| `--max-archive-depth <number>` | `16` | Maximum nesting depth of directories in a remote archive. Must be between 1 and 100. |
| `--reject-duplicate-archive-paths` / `--reject-duplicates` | On | Reject archives containing duplicate entry paths. The first duplicate causes the entire extraction to fail. |
| `--reject-symlinks-in-archives` / `--reject-symlinks` | On | Reject archives containing symlinks (stored as directory entries with non-zero length). |
| `--http-timeout <timespan>` | `2m` | Maximum time allowed for each HTTP operation (download, extraction request). Examples: `30s`, `5m`. |
| `--max-retries <number>` | `3` | Maximum number of retry attempts for transient network failures (5xx, 429, connection resets). |
| `--retry-base-delay <timespan>` | `500ms` | Base delay for exponential backoff between retries. Each retry doubles this value with jitter. Examples: `100ms`, `1s`. |
| `--retry-max-delay <timespan>` | `30s` | Maximum delay between retries, regardless of backoff calculation. Prevents excessively long waits. Examples: `10s`, `1m`. |

> The CLI is the source of truth for accepted values. Run `dotnet run --project Plutus.CLI -- --help` from the repository to inspect the help generated by the current build.

## Profiles

A `.plutusrc` file in the working directory or any parent up to the repository root supplies defaults for the flags
above, so a long command line full of exclusion targets becomes a checked-in preset:

```jsonc
{
  // .plutusrc at the repository root
  "excludeDirs": ["bin", "obj", "node_modules", "dist"],
  "includeExtensions": [".cs", ".razor"],
  "format": "md"
}
```

Archive extraction limits can also be configured in a profile:

```jsonc
{
  // Limit remote archive downloads
  "maxDownloadSize": "200MB",
  "maxExtractedSize": "1GB",
  "maxArchiveFileCount": 5000,
  "maxArchiveDepth": 20
}
```

An explicit flag always wins, and a flag replaces the profile's value for that key rather than adding to it:

```bash
# The profile's excludeDirs, includeExtensions and format apply here
dotnet run --project Plutus.CLI -- bundle . -o context.md

# ...and here --format json overrides the profile while its exclusions still apply
dotnet run --project Plutus.CLI -- bundle . -o context.json --format json

# Layer two profiles: base first, then a review-specific override
dotnet run --project Plutus.CLI -- bundle . --profile base review
```

Every run that used a profile says so, together with the keys that decided the run and the ones the command line
replaced:

```text
Configuration
  profile  /repo/.plutusrc — applied: excludeDirs, includeExtensions; overridden by the command line: format
```

`applied` names the keys that stated something other than the default; keys that merely restate a default are counted
(`restated defaults: 13`), so a generated profile that lists every option does not bury the settings that matter.

The full schema, the search rules and the precedence are in [Configuration profiles](CONFIGURATION.md).

## Common recipes

### Bundle a local project

```bash
dotnet run --project Plutus.CLI -- bundle ./my-project -o ./my-project-context.md
```

### Include C# files and omit test directories

```bash
dotnet run --project Plutus.CLI -- bundle ./my-project --include-files "*.cs" --exclude-dirs "*Test*"
```

### Exclude multiple generated or irrelevant paths

```bash
dotnet run --project Plutus.CLI -- bundle ./my-project \
  --exclude-dirs "bin" "obj" "node_modules" \
  --exclude-files "*.generated.cs" "*.min.js"
```

### Bundle more than one root

```bash
dotnet run --project Plutus.CLI -- bundle ./src ./tests -o ./combined-context.md
```

### Choose a remote ref

```bash
dotnet run --project Plutus.CLI -- bundle "https://github.com/owner/repository" --branch "feature/new-parser"
dotnet run --project Plutus.CLI -- bundle "https://gitlab.com/group/subgroup/repository" --commit "a1b2c3d4e5f6789012345678901234567890abcd"
```

`--branch` and `--commit` are for remote repository inputs and are mutually exclusive. Plutus includes provider adapters for GitHub, GitLab, and Bitbucket. Access to private repositories is not documented as supported by the current public-URL download flow; do not assume authenticated access.

### Set a size threshold and source-token budget

```bash
dotnet run --project Plutus.CLI -- bundle ./src --max-file-size 512K --max-tokens 80000 --token-overflow-policy select --token-selection-strategy path-order
```

### Change output format

```bash
dotnet run --project Plutus.CLI -- bundle ./src --format json -o ./artifacts/context.json
dotnet run --project Plutus.CLI -- bundle ./src --format xml -o ./artifacts/context.xml
```

### Bundle only what changed

```bash
dotnet run --project Plutus.CLI -- bundle . --diff-only -o ./artifacts/change-review.md
dotnet run --project Plutus.CLI -- bundle . --diff-only --diff-include-untracked -o ./artifacts/change-review-full.md
dotnet run --project Plutus.CLI -- bundle . --diff-from main -o ./artifacts/branch-context.md
dotnet run --project Plutus.CLI -- bundle . --diff-only --diff-from v1.0.0 --diff-to v1.1.0 -o ./artifacts/release-review.md
dotnet run --project Plutus.CLI -- bundle . --diff-from a1b2c3d --diff-to 9f8e7d6 -o ./artifacts/two-commits.md
dotnet run --project Plutus.CLI -- bundle . --diff-only --format json -o ./artifacts/change-review.json
```

The flags answer different questions:

| | Scope | File contents | Use when |
|---|---|---|---|
| `--diff-only` | working tree vs `HEAD` | **omitted** | You want to read the change itself: a diff to review, not the surrounding source. |
| `--diff-from <ref>` | merge base of `<ref>` and `HEAD`, vs working tree | included | You want the changed files **with** their full contents as context, e.g. to hand a branch to a model. |
| `--diff-from <a> --diff-to <b>` | `<a>` vs `<b>`, no working tree | included | You want a specific commit or release range, including already-committed history. |

All of them require a local git repository, and every project path must live inside it. Directories that contain no changed file are marked as ignored, so unchanged modules stay out of the file list.

A `--diff-only` bundle contains the header, a `## Changed Files` list of paths and a `## Changes against <ref>` section with **one fenced diff per file**. Nothing else — the artifact is the change. A `--diff-from` bundle additionally embeds the content of every changed file, plus the directory tree, and ends with the same diff section.

Only tracked content is visible to `git diff`, so by default a file that was never `git add`ed is **not** part of the diff scope. Add `--diff-include-untracked` to cover it: the paths are listed in a "New files git does not track yet" block and a real new-file diff is generated for each one (`git diff --no-index`). `.gitignore`d files are still excluded.

### Limits apply per file

`--max-file-size` and `--max-tokens` bound each diff independently, exactly as they bound whole files on the content path. A diff is the biggest thing a change bundle carries, so without this one huge change could make the artifact unusable.

```bash
dotnet run --project Plutus.CLI -- bundle . --diff-only --max-file-size 256K --max-tokens 60000 --token-overflow-policy select
```

A change that is left out is reported in place rather than dropped silently, and it does not take the rest of the change set with it:

```markdown
### DIFF: `src/Large.cs`

Diff omitted: omitted by token budget (60000 tokens; strategy: PathOrder)
```

The token budget uses the same policies and selection strategies as the content path (`fail`, `warn`, `select`; `path-order`, `largest-first`), and the bundle header reports the diff numbers. `--exclude`/`--include` filters and sensitive-file protection apply to the diff scope as well.

## Archive extraction safety

When bundling from a remote repository (GitHub, GitLab, Bitbucket), Plutus downloads a ZIP archive and validates every entry before extraction. The following options control these safety limits:

```bash
# Use custom download and extraction size limits
dotnet run --project Plutus.CLI -- bundle "https://github.com/owner/repository" \
  --max-download-size 200MB --max-extracted-size 1GB

# Restrict file count and nesting depth
dotnet run --project Plutus.CLI -- bundle "https://gitlab.com/group/project" \
  --max-archive-file-count 5000 --max-archive-depth 20

# Disable duplicate/symlink rejection (not recommended)
dotnet run --project Plutus.CLI -- bundle "https://bitbucket.org/workspace/project" \
  --reject-duplicates false --reject-symlinks false
```

The safety checks include:

- **Download size limit** — rejects archives larger than `--max-download-size` before extraction begins.
- **Entry-by-entry validation** — each file is checked for path traversal (`..`, absolute paths), depth limits, and Windows reserved device names.
- **Decompression bomb protection** — total extracted size is tracked and rejected if it exceeds `--max-extracted-size`.
- **File count limit** — extraction stops if the archive contains more than `--max-archive-file-count` files.
- **Directory depth limit** — entries deeper than `--max-archive-depth` levels are rejected.
- **Symlink rejection** — directory entries with non-zero length (a common symlink encoding) are rejected when `--reject-symlinks-in-archives` is enabled.
- **Duplicate path rejection** — when `--reject-duplicate-archive-paths` is enabled, duplicate entry paths cause extraction to fail.

All limits can also be set in a `.plutusrc` profile using the keys `maxDownloadSize`, `maxExtractedSize`, `maxArchiveFileCount`, `maxArchiveDepth`, `rejectDuplicateArchivePaths`, and `rejectSymlinksInArchives`.

## Signature extraction

`--signatures-only` replaces implementation bodies with a structural `;` using a real parse tree, so a bundle can
carry a project's shape instead of its code:

```bash
dotnet run --project Plutus.CLI -- bundle ./src --signatures-only -o ./artifacts/api-surface.md
dotnet run --project Plutus.CLI -- bundle ./src --signatures-only --strip-comments
dotnet run --project Plutus.CLI -- bundle ./src --signatures-only --minify --include-files "*.cs"
```

```csharp
public sealed class WidgetService : IWidgetService
{
    private readonly Dictionary<string, Widget> _widgets = new();

    public WidgetService(IEnumerable<Widget> widgets);

    public int Count;

    public Widget? Find(string id);

    public string Describe<T>(T value) where T : notnull;
}
```

Declarations, attributes, documentation comments, generic parameters and constraints survive. Interface members,
abstract members, auto-properties and field initializers are already signatures or are part of the contract, so they
are left alone. Only C# has a provider today; any other language is passed through unchanged and counted in the run
report:

```text
Bundle created:  /work/artifacts/api-surface.md
Files included:  12
Total size:      240.5 KB
Elapsed time:    0.42s (420 ms)

Source processing
  signatures  provider=roslyn-csharp
              files=12, bodies=48 (accessor=6, constructor=3, method=30, property=9), without a provider=2
```

The order is always comments, then signatures, then whitespace, so `--signatures-only` composes with
`--strip-comments`, `--preserve-doc-comments` and `--minify`. The output is a skeleton for reading and for model
context, not compilable code; details, guarantees and limitations are in
[Signature extraction](SIGNATURE_EXTRACTION.md).

## Comment stripping

`--strip-comments` removes comments without touching anything else, and `--minify` removes them and collapses
whitespace-only lines. Both use the same language-aware lexical scanner; neither rewrites the contents of string,
raw-string, template, regex, or markup-attribute literals, and neither merges two tokens when a comment between them
disappears.

```bash
dotnet run --project Plutus.CLI -- bundle ./src --strip-comments -o ./artifacts/context.md
dotnet run --project Plutus.CLI -- bundle ./src --strip-comments --preserve-doc-comments
dotnet run --project Plutus.CLI -- bundle ./src --minify --comment-strategy lexical
```

The default for documentation comments is explicit: **they are removed** unless `--preserve-doc-comments` is
given. Languages without a distinct documentation form (Go, Python, SQL, C, HTML, the config formats) ignore that
flag, because a plain `//` or `#` comment cannot be classified as documentation.

`--comment-strategy antlr` selects real token-channel processing and therefore requires a project-maintained
grammar. This build maintains none, so the flag fails with the manifest path it probed instead of degrading to
lexical scanning:

```text
Error: --comment-strategy antlr requires at least one project-maintained ANTLR grammar, and this build ships none
(none (probed '/app/grammars/grammar-manifest.json')). Provide a manifest and its .g4/lexer files, or use
--comment-strategy lexical.
```

Every run that processes source prints what happened, and every service in the run reports through the same output,
so the wording does not depend on which layer is speaking:

```text
Bundle created:  /work/artifacts/context.md
Files included:  12
Total size:      240.5 KB
Elapsed time:    0.42s (420 ms)

Source processing
  comments    strategy=lexical (requested: auto) — lexical scanning; no project-maintained grammar
              files=12, removed=37 (inline 20, block 12, documentation 5), documentation preserved=3, languages: csharp=9, javascript=3
  signatures  provider=roslyn-csharp
              files=12, bodies=48 (accessor=6, constructor=3, method=30, property=9), without a provider=2
```

Warnings and errors go to standard error as `Warning: …` and `Error: …`; results go to standard output, framed as
`label:  value`. The supported languages, comment forms, guarantees, and known limitations are documented in
[Comment stripping](COMMENT_STRIPPING.md).

## Filtering notes

- `.gitignore` processing is enabled by default; `--no-gitignore` disables reading the root `.gitignore`.
- Built-in exclusions are enabled by default. `--use-default-excludes` explicitly keeps them enabled; omit that option to use the default behavior. The current CLI does not expose a `--no-default-excludes` flag.
- `--exclude` is the broadest user-supplied exclusion. Use the more specific `--exclude-files` and `--exclude-dirs` when the target type matters.
- Include filters narrow the bundle. Test patterns on a small directory first when combining multiple include/exclude rules.
- Patterns are not shell-quoted by Plutus itself; quote wildcard patterns so the shell passes them literally.

## Sensitive files

The default sensitive-file matcher excludes known high-risk filename patterns. `--allow-dangerous-files` overrides this safeguard and can cause secrets, credentials, private keys, or environment settings to appear in the generated artifact. Only enable it for a deliberate local workflow, and inspect the output before sharing it.
