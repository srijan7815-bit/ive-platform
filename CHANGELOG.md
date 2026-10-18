# Changelog

All notable changes to this project are documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning: [SemVer](https://semver.org/).

## [1.0.0] — 2026-10-19

### Added
- **IVE for Windows** — native .NET 8 / WPF app: spring-physics notch, frosted-glass prompt box, control center with Windows 11 acrylic, system-wide gesture (native low-level hooks), global shortcut, tray, WinRT now-playing + transport control, DPAPI-encrypted API key.
- **IVE for Android** — native Kotlin / Jetpack Compose app: Dynamic-Island-style notch with approvals, liquid-glass UI, adaptive phone/tablet/foldable layouts, sandboxed on-device tools, Keystore-encrypted API key, system media control.
- Platform-neutral agent core (`Ive.Core`) with unit tests: tool-calling loop, task queue, approval broker, validated settings.
- CI workflows (build, test, release, CodeQL), Dependabot, issue/PR templates.
