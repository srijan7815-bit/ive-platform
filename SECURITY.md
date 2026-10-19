# Security Policy

## Reporting a vulnerability
Please **do not** open a public issue. Use GitHub's private vulnerability reporting
(*Security → Report a vulnerability*) on this repository. You'll get a response within 7 days.

## Threat model (what IVE does and does not protect)
IVE is an agent that can act on your device on your behalf. Its safety model is deliberately simple and explicit:

| Control | Windows | Android |
|---|---|---|
| API key at rest | Encrypted with DPAPI (current Windows user) | Android Keystore (AES-256-GCM) via EncryptedSharedPreferences |
| Transport | HTTPS enforced (http only for loopback) | HTTPS enforced |
| Destructive actions | Explicit approval card (write / delete / shell) | Explicit approval card (write / delete / open link) |
| Filesystem scope | Workspace-relative; system folders hard-blocked | Confined to an app-private folder (path traversal rejected) |
| Shell | Approval-gated, 60 s timeout, output capped | Not available |
| Controlling other apps | n/a | Accessibility service; **asked once per app**, re-checked on every action against the live foreground app, revocable, password fields masked and never typed; on-screen Stop button |
| Voice | Audio is sent only to the speech endpoint you configure, only while you record | Same |

**The approval prompt is a user-experience control, not a sandbox.** Keep *Ask before acting* enabled
unless you fully trust the model endpoint you configured. Never commit API keys; `.env` files are git-ignored.
