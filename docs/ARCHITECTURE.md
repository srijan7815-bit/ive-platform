# Architecture

IVE is two **fully native** apps that share a design language and a behavioral contract, not a runtime.
There is no web view, no Electron, no cross-platform UI toolkit.

```mermaid
flowchart LR
  subgraph Windows [IVE for Windows — .NET 8 / WPF]
    GH[Native low-level hooks<br/>keyboard + mouse] --> P[PromptWindow<br/>frosted glass]
    HK[RegisterHotKey] --> P
    P --> TM
    TM[TaskManager<br/>queue · approvals] --> AR[AgentRunner]
    AR --> TB[Toolbox<br/>read · list · write · delete · shell]
    AR --> AI[AiClient]
    TM --> NW[NotchWindow<br/>spring physics]
    MS[MediaService<br/>WinRT SMTC] --> NW
    ST[SettingsStore<br/>DPAPI] --> AI
  end
  subgraph Android [IVE for Android — Kotlin / Compose]
    UI[Compose UI<br/>Notch · Feed · Composer] --> VM[IveViewModel]
    VM --> AG[Agent] --> TR[ToolRunner<br/>sandboxed] 
    AG --> AC[AiClient]
    VM --> SS[SettingsStore<br/>Keystore]
    UI --> MK[MediaKeys]
  end
  AI & AC --> LLM[(OpenAI-compatible<br/>chat-completions endpoint)]
```

## Principles
1. **Logic below the UI.** On Windows, everything testable lives in `Ive.Core` (no WPF reference). Windows are thin views.
2. **Approvals are first-class.** The agent can only reach destructive tools through an approval broker; cancelling a task denies its pending approvals.
3. **Interruptible motion.** The notch is driven by real spring physics stepped per frame (velocity survives retargeting), with velocity-based squash-and-stretch on the detached "task bubble". Storyboard animations use a damped-spring easing.
4. **Auto-fit, not device sniffing.**
   - *Windows*: PerMonitorV2 DPI; UI scale derived from the target display's logical width (`√(w/1920)`, clamped); window reflows from side rail to bottom tab bar under 700 DIP; windows are placed per-monitor in physical pixels.
   - *Android*: edge-to-edge with `safeDrawing` insets (cutouts, gesture bars, IME); single column on phones, two panes ≥ 720 dp (tablets, foldables, landscape); notch width = `min(420 dp, screen − 24 dp)`.
5. **Real glass where the OS allows it.** Control center uses DWM acrylic. The prompt box blurs an actual snapshot of the desktop behind it.

## Behavioral parity
| Capability | Windows | Android |
|---|---|---|
| Invoke | Hold trigger + right-drag (swallows the right-click), `Ctrl+Alt+Space`, tray | Open app / composer |
| Notch | Top-center overlay, click-through | In-app Dynamic-Island surface |
| Tools | files + shell (approval-gated) | sandboxed files, device info, open link |
| Music | WinRT now-playing + transport | Media-key transport |
| Secrets | DPAPI | Keystore |
