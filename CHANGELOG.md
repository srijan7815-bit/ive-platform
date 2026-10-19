# Changelog

All notable changes to this project are documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning: [SemVer](https://semver.org/).

## [1.1.0] — 2026-10-21

### Added
- **Voice input** on both platforms using Groq **whisper-large-v3-turbo** (`SpeechClient`, tested). Windows: mic button + `Ctrl+Alt+V` push-to-talk; Android: mic button with runtime permission. Optional auto-send.
- **Android full app control**: `AccessibilityService` that reads any app's UI and taps / types / scrolls / swipes; per-app *ask first* grants (revocable), overlay approval card and Stop pill while IVE is in the background, password fields never read or typed.
- **User-configured music app**: pick the app IVE controls (Windows: detected media sources + launch target; Android: installed-app picker, targeted media-button broadcast). New `music_control` agent tool.
- Groq as the default provider (single key for chat + voice).

### Changed
- CI is live: build + test on every push, release on tags.

## [1.0.0] — 2026-10-19

### Added
- **IVE for Windows** — native .NET 8 / WPF app: spring-physics notch, frosted-glass prompt box, control center with Windows 11 acrylic, system-wide gesture (native low-level hooks), global shortcut, tray, WinRT now-playing + transport control, DPAPI-encrypted API key.
- **IVE for Android** — native Kotlin / Jetpack Compose app: Dynamic-Island-style notch with approvals, liquid-glass UI, adaptive phone/tablet/foldable layouts, sandboxed on-device tools, Keystore-encrypted API key, system media control.
- Platform-neutral agent core (`Ive.Core`) with unit tests: tool-calling loop, task queue, approval broker, validated settings.
- CI workflows (build, test, release, CodeQL), Dependabot, issue/PR templates.
