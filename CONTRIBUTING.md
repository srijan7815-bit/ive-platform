# Contributing

Thanks for helping improve IVE! 

## Workflow
1. Fork, branch from `main`: `feat/…`, `fix/…`, `docs/…`.
2. Use [Conventional Commits](https://www.conventionalcommits.org/) (`feat(windows): …`, `fix(android): …`).
3. Keep PRs focused; update `CHANGELOG.md` for user-visible changes.
4. All checks must pass before review.

## Local checks
```bash
# Windows + core (Windows or Linux for the core tests)
dotnet test apps/windows/tests/Ive.Core.Tests/Ive.Core.Tests.csproj
# Android
cd apps/android && ./gradlew :app:testDebugUnitTest :app:assembleDebug
```

## Code style
`.editorconfig` is authoritative. UI on Windows is built in code (no runtime XAML) so every error is a compile error.
Business logic belongs in `Ive.Core` (platform-neutral, unit-tested), never in a window class.
