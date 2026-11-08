# Configuration profiles (`.plutusrc`)

A profile is a small file at the repository root that states the defaults for a project, so the usual command line
stops being a list of exclusions:

```bash
# Without a profile
dotnet run --project Plutus.CLI -- bundle . --exclude-dirs bin obj node_modules dist --exclude-files "*.g.cs" \
  --include-files "*.cs" "*.razor" --format md -o artifacts/context.md

# With one
dotnet run --project Plutus.CLI -- bundle .
```

## Start from a template

Nobody has to learn the schema by heart: `init` writes a profile with every supported key already in it, set to the
value a run would use anyway, so filling it in means editing values instead of writing JSON from memory.

```bash
dotnet run --project Plutus.CLI -- init
```

```text
Profile template written: /repo/.plutusrc
Every supported key is present with the value a run would use anyway; edit what this repository needs.
A run from this directory or below picks it up automatically; --no-profile ignores it.
```

```jsonc
{
  "includeFiles": [],
  "includeDirs": [],
  "includeExtensions": [],
  "exclude": [],
  "excludeDirs": ["bin", "obj", "node_modules", "dist", ".vs"],
  "excludeFiles": [],
  "useGitignore": true,
  "useDefaultExcludes": true,
  "allowDangerousFiles": false,
  "output": null,
  "format": "md",
  "encoding": "utf-8",
  "maxFileSize": "1M",
  "maxTokens": null,
  "tokenOverflowPolicy": "fail",
  "tokenSelectionStrategy": "path-order",
  "maxConcurrency": 8,
  "includeFileContents": true,
  "minify": false,
  "stripComments": false,
  "preserveDocComments": false,
  "commentStrategy": "auto",
  "signaturesOnly": false
}
```

Because the values are the ones a run would have used anyway, generating the file cannot change what a bundle
contains — a run that finds it prints the profile and reports no key it had to take from it. The one thing the
starter states outright is `excludeDirs` with the junk trees that `useDefaultExcludes` already skips, because
excluding generated and vendored directories is what a profile is usually for. `null` means "not set": leave it, or
put a value in.

| Option | Default | Description |
|---|---|---|
| `-o`, `--output <path>` | `<current directory>/.plutusrc` | Where to write the template. |
| `--force` | Off | Replace an existing profile instead of refusing to touch it. |

## Where the profile is found

Plutus searches for a base profile from the current working directory upwards,
stopping at the repository root. It uses the first `.plutusrc` or
`.plutusrc.json` file it finds. Profiles from different directories are not
merged.

The directory containing the discovered base profile is used to resolve
`default.plutusrc` and named profiles. If no base profile is found, the current
working directory is used instead.

Configuration files are loaded in this order:

1. `default.plutusrc`, when present;
2. the discovered `.plutusrc` or `.plutusrc.json`, when present;
3. named profiles specified by `--profile`, each resolving to
   `<name>.plutusrc` in the selected configuration directory.

## Cascading profiles

`--profile` can be given more than once, or with several names in one token; the profiles are applied in the order
they are stated, and a later profile overrides a key an earlier one set:

```bash
dotnet run --project Plutus.CLI -- bundle . --profile base review-diff
dotnet run --project Plutus.CLI -- bundle . --profile base --profile ci
```

Each name resolves to `<name>.plutusrc` in the selected configuration directory,
so `--profile base review-diff` reads `base.plutusrc` and then
`review-diff.plutusrc`.

Both spellings mean the same: `base` first, then `review-diff` / `ci`. Strings, numbers, enums and flags follow
"the later profile wins". Array keys (`excludeDirs`, `includeFiles`, …) are **unioned** across all configuration
layers, including environment variables, instead of being replaced, because excluding generated directories is
additive in practice.

Environment variables provide the lowest-precedence configuration values. Configuration files override them,
and each later file or named profile overrides the previous ones. Explicit command-line flags have the highest
precedence:

```text
built-in defaults  <  PLUTUS_* env  <  default.plutusrc  <  .plutusrc  <  --profile base  <  --profile review-diff  <  explicit CLI flags
```

A named profile that does not exist fails the run and names the file; the chain is never silently shortened.

## Precedence

| Source | Wins over |
|---|---|
| An explicit command-line flag | the profile and the built-in default |
| A key stated by a later profile (string, number, enum, flag) | an earlier profile and the built-in default |
| An array key stated by any configuration layer | unioned across all configuration layers |
| The built-in default of the option | — |
| A value from a configuration file | a PLUTUS_* environment variable for the same key |

A flag **replaces** the profile's value for the same key rather than adding to it, and the run report states what
happened:

```text
Configuration
  profile  /repo/.plutusrc — applied: excludeDirs, includeExtensions; overridden by the command line: format
```

`applied` lists the keys that stated something other than the built-in default — the ones that decided the run. A
key that merely restates the default is counted as `restated defaults: N` instead of being listed, which is what
keeps the report readable for a generated profile that states every key:

```text
Configuration
  profile  /repo/.plutusrc — applied: excludeDirs; restated defaults: 13; overridden by the command line: output
```

So `--exclude-dirs node_modules` in a repository whose profile lists `bin`, `obj`, `node_modules` and `dist` means
*only* `node_modules` is excluded for that run. Combining the two sources is a different feature (it needs its own
flag to be honest about it) and is not implemented.

An option that is a flag can also be turned off explicitly, which is what overrides a profile that enables it:

```bash
dotnet run --project Plutus.CLI -- bundle . --minify false   # the profile says minify: true
```

## Schema

Every key is optional; a profile states only what it changes. Unknown keys are an error.

### Path rules

| Key | Type | Equivalent flag |
|---|---|---|
| `includeFiles` | string[] | `--include-files` |
| `includeDirs` | string[] | `--include-dirs` |
| `includeExtensions` | string[] | `--include-files` (see below) |
| `exclude` | string[] | `--exclude` |
| `excludeDirs` | string[] | `--exclude-dirs` |
| `excludeFiles` | string[] | `--exclude-files` |
| `useGitignore` | bool | `--no-gitignore` (inverted) |
| `useDefaultExcludes` | bool | `--use-default-excludes` |
| `allowDangerousFiles` | bool | `--allow-dangerous-files` |

`includeExtensions` is the friendly form of an inclusive filter: each entry becomes the pattern that selects it at
any depth, so `".cs"`, `"cs"` and `"*.cs"` all mean the same, and `[".cs", ".ts"]` is `--include-files "*.cs" "*.ts"`.
An entry that names a directory (`"src/*.cs"`) is rejected with a pointer at `includeFiles`, because that is the key
for patterns.

### Output preferences

| Key | Type | Equivalent flag |
|---|---|---|
| `output` | string | `--output` |
| `format` | `"md"` \| `"json"` \| `"xml"` | `--format` |
| `encoding` | string | `--encoding` |
| `maxFileSize` | string (`"512K"`, `"2M"`) | `--max-file-size` |
| `maxTokens` | positive integer | `--max-tokens` |
| `tokenOverflowPolicy` | `"fail"` \| `"warn"` \| `"select"` | `--token-overflow-policy` |
| `tokenSelectionStrategy` | `"path-order"` \| `"largest-first"` | `--token-selection-strategy` |
| `maxConcurrency` | integer ≥ 1 | `--max-concurrency` |
| `includeFileContents` | bool | `--diff-only` (inverted) |

A relative `output` is resolved against the **profile's own directory**, not the directory the command runs in: a
checked-in preset writes next to the repository it describes. An `output` given on the command line stays relative to
the working directory, as before.

### Source processing

| Key | Type | Equivalent flag |
|---|---|---|
| `minify` | bool | `--minify` |
| `stripComments` | bool | `--strip-comments` |
| `preserveDocComments` | bool | `--preserve-doc-comments` |
| `commentStrategy` | `"auto"` \| `"lexical"` \| `"antlr"` | `--comment-strategy` |
| `signaturesOnly` | bool | `--signatures-only` |

The comment strategy is still resolved against the maintained grammars before any file is read, so a profile cannot
make `antlr` succeed where the build ships no grammar.

What a profile cannot set: the project paths, `--branch` / `--commit`, and the diff scope (`--diff-only`,
`--diff-from`, `--diff-to`, `--diff-include-untracked`). Those identify *what* is being bundled rather than *how*, and
a preset that silently changed the target would be a trap.

### HTTP timeouts and retries

| Key | Type | Equivalent flag |
|---|---|---|
| `timeout` | object | CLI flags (see below) |
| `timeout.operationTimeout` | string (`"120s"`, `"5m"`) | `--http-timeout` |
| `timeout.maxRetries` | integer ≥ 0 | `--max-retries` |
| `timeout.retryBaseDelay` | string (`"500ms"`, `"1s"`) | `--retry-base-delay` |
| `timeout.retryMaxDelay` | string (`"30s"`, `"1m"`) | `--retry-max-delay` |

`timeout` controls how HTTP operations behave when fetching remote repositories:

- **`operationTimeout`** — total time allowed for a single HTTP operation. Default: `120s`.
- **`maxRetries`** — maximum number of retry attempts for transient failures (5xx responses, connection errors). Default: `3`.
- **`retryBaseDelay`** — base delay for exponential backoff (`baseDelay * 2^attempt`). Default: `500ms`.
- **`retryMaxDelay`** — maximum delay between retries, capping the exponential growth. Default: `30s`.

The retry policy respects the `Retry-After` header when present: if a server returns a `Retry-After` value greater
than the calculated backoff delay, the policy waits for the server-specified duration instead.

```jsonc
{
  "timeout": {
    "operationTimeout": "120s",
    "maxRetries": 3,
    "retryBaseDelay": "500ms",
    "retryMaxDelay": "30s"
  }
}
```

## Example

```jsonc
{
  // .plutusrc at the repository root
  "excludeDirs": ["bin", "obj", "node_modules", "dist", ".vs"],
  "excludeFiles": ["*.g.cs", "*.Designer.cs", "*.min.js"],
  "includeExtensions": [".cs", ".razor", ".json"],

  "output": "artifacts/context.md",
  "format": "md",
  "maxFileSize": "2M",
  "maxTokens": 200000,
  "tokenOverflowPolicy": "select",
  "tokenSelectionStrategy": "path-order",

  "stripComments": true,
  "preserveDocComments": true
}
```

## Failure messages

A profile problem names the file and the key, and stops the run before anything is read:

```text
Error: profile '/repo/.plutusrc' has unknown key(s): 'excludeDir'. Known keys are: allowDangerousFiles,
commentStrategy, encoding, exclude, excludeDirs, excludeFiles, format, includeDirs, includeExtensions,
includeFileContents, includeFiles, maxConcurrency, maxFileSize, maxTokens, minify, output, preserveDocComments,
signaturesOnly, stripComments, tokenOverflowPolicy, tokenSelectionStrategy, useDefaultExcludes, useGitignore.
```

## Tests

- `Plutus.Test/Configuration/PlutusProfileLoaderTests.cs` — binding of every key, comments and trailing commas,
  unknown keys, wrong types, malformed JSON, a missing explicit path, and the search: nearest wins, walk up, stop at
  the repository root, disable.
- `Plutus.Test/Configuration/RunOptionsResolverTests.cs` — defaults without a profile, profile values applied,
  explicit flags overriding (including turning a flag off), inclusive patterns from extensions, the profile-relative
  output path, token budget and diff limits from the profile, and the provenance the report prints.
- `Plutus.Test/Configuration/ProfileTemplateTests.cs` — the generated template writes every key the loader accepts,
  comes back unchanged through the loader, and states the values a run would use anyway.
- `Plutus.Test/Configuration/PlutusConfigurationBuilderTests.cs` — cascading named profiles: later ones override
  earlier ones for strings and flags, arrays are unioned across layers, and a missing profile is an error.
- `Plutus.Test/Cli/CommandLineReaderTests.cs` — the "stated or not stated" distinction the precedence depends on
  (including `--minify false` and `--no-gitignore`), and the command surface: bundling with an `init` subcommand.
- `Plutus.Test/Configuration/ProfileScaffolderTests.cs` — the write: template content, no byte-order mark, creating
  the directory, refusing to replace an existing profile, `--force`, and a write failure reported as one.
- `Plutus.Test/Diagnostics/RunReportWriterTests.cs` — the `Configuration` block, and that a run without a profile
  prints none.
