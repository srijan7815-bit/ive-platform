# Enabling CI & releases

GitHub only accepts pushes that touch `.github/workflows/` from tokens with the **`workflow`** scope, so the
workflow files ship in `ci/workflows/`. To activate them (30 seconds):

**Option A — web UI:** *Add file → Create new file* → name it `.github/workflows/ci.yml` → paste the contents of
`ci/workflows/ci.yml` → commit. Repeat for `release.yml` and `codeql.yml`.

**Option B — git:** edit your classic PAT (*Settings → Developer settings → Tokens*), tick **workflow**, then:
```bash
mkdir -p .github && git mv ci/workflows .github/workflows && git commit -m "ci: enable workflows" && git push
```

## What you get
| Workflow | Trigger | Output |
|---|---|---|
| `ci.yml` | push / PR | Core unit tests, `IVE.exe` (win-x64, self-contained single file), debug `IVE.apk` as artifacts |
| `release.yml` | tag `v*` | GitHub Release with the `.exe` and `.apk` attached + generated notes |
| `codeql.yml` | push / PR / weekly | Static security analysis (C#, Kotlin) |

Cut a release: `git tag v1.0.0 && git push --tags`.
