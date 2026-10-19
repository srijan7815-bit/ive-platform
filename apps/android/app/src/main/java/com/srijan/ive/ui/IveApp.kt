package com.srijan.ive.ui

import android.view.KeyEvent
import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.width
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.TextButton
import androidx.compose.ui.draw.scale
import androidx.core.content.ContextCompat
import com.srijan.ive.control.AppBridge
import com.srijan.ive.data.InstalledApps
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.spring
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.srijan.ive.IveViewModel
import com.srijan.ive.MediaKeys
import com.srijan.ive.data.IveConfig

@Composable
fun IveApp(vm: IveViewModel = viewModel()) {
    Box(Modifier.fillMaxSize()) {
        LiquidBackground()
        BoxWithConstraints(Modifier.fillMaxSize().safeDrawingPadding()) {
            // Auto-fit: one codebase, two layouts — phone (single column) and tablet/foldable/landscape (two panes).
            val wide = maxWidth >= 720.dp
            Column(Modifier.fillMaxSize()) {
                Box(Modifier.fillMaxWidth().padding(top = 6.dp)) {
                    Notch(
                        phase = vm.phase, approval = vm.approval, running = vm.running, needsKey = !vm.config.ready,
                        onApprove = { vm.respond(true) }, onDeny = { vm.respond(false) },
                        onTap = { if (!vm.config.ready) vm.showSettings = true },
                        modifier = Modifier.align(Alignment.TopCenter),
                    )
                    GlyphButton("⚙", Modifier.align(Alignment.TopEnd).padding(end = 16.dp), diameter = 38) { vm.showSettings = true }
                }
                Spacer(Modifier.height(8.dp))
                if (wide) {
                    Row(Modifier.fillMaxSize().padding(horizontal = 20.dp), horizontalArrangement = Arrangement.spacedBy(20.dp)) {
                        Column(
                            Modifier.weight(0.42f).fillMaxHeight().verticalScroll(rememberScrollState()),
                            verticalArrangement = Arrangement.spacedBy(16.dp),
                        ) {
                            Text("IVE", color = Ive.ink, fontSize = 34.sp, fontWeight = FontWeight.Bold)
                            MusicCard(vm)
                            Composer(vm)
                        }
                        Feed(vm, Modifier.weight(0.58f).fillMaxHeight(), showMusic = false)
                    }
                } else {
                    Feed(vm, Modifier.weight(1f).fillMaxWidth(), showMusic = true)
                    Composer(vm, Modifier.padding(12.dp))
                }
            }
        }
        vm.notice?.let { msg ->
            Box(
                Modifier.align(Alignment.BottomCenter).safeDrawingPadding().padding(start = 24.dp, end = 24.dp, bottom = 92.dp)
                    .glass(RoundedCornerShape(50), strong = true).padding(horizontal = 18.dp, vertical = 10.dp),
            ) { Text(msg, color = Ive.ink, fontSize = 13.sp) }
        }
        AnimatedVisibility(
            visible = vm.showSettings,
            enter = fadeIn() + scaleIn(initialScale = 0.92f, animationSpec = spring(dampingRatio = 0.6f, stiffness = Spring.StiffnessMediumLow)),
            exit = fadeOut() + scaleOut(targetScale = 0.95f),
        ) { SettingsSheet(vm) }
    }
}

@Composable
private fun Feed(vm: IveViewModel, modifier: Modifier, showMusic: Boolean) {
    LazyColumn(
        modifier,
        contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        if (showMusic) item { MusicCard(vm) }
        if (vm.tasks.isEmpty()) {
            item {
                Column(Modifier.fillMaxWidth().padding(vertical = 48.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("Nothing yet", color = Ive.ink2, fontSize = 18.sp, fontWeight = FontWeight.SemiBold)
                    Text("Ask IVE below to get started.", color = Ive.ink3, fontSize = 13.sp)
                }
            }
        }
        items(vm.tasks, key = { it.id }) { t -> TaskCard(t, onCancel = { vm.cancel(t) }) }
        if (vm.tasks.any { !it.active }) {
            item { Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) { PillButton("Clear finished") { vm.clearFinished() } } }
        }
    }
}

@Composable
private fun TaskCard(t: TaskUi, onCancel: () -> Unit) {
    var open by remember { mutableStateOf(false) }
    val (chip, chipColor) = when (t.status) {
        TaskStatus.Queued -> "Queued" to Ive.accent
        TaskStatus.Running -> "Working" to Ive.accent
        TaskStatus.Awaiting -> "Needs approval" to Ive.warn
        TaskStatus.Done -> "Done" to Ive.ok
        TaskStatus.Error -> "Failed" to Ive.bad
        TaskStatus.Cancelled -> "Cancelled" to Ive.ink3
    }
    Column(
        Modifier.fillMaxWidth().glass(RoundedCornerShape(22.dp))
            .animateContentSize(spring(dampingRatio = 0.7f, stiffness = Spring.StiffnessMediumLow))
            .padding(16.dp),
    ) {
        Row(verticalAlignment = Alignment.Top, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Text(t.prompt, color = Ive.ink, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
            Text(
                chip, color = chipColor, fontSize = 11.sp, fontWeight = FontWeight.Bold,
                modifier = Modifier.clip(RoundedCornerShape(50)).background(chipColor.copy(alpha = 0.16f)).padding(horizontal = 10.dp, vertical = 4.dp),
            )
        }
        t.summary?.let {
            Spacer(Modifier.height(8.dp))
            Text(it, color = if (t.status == TaskStatus.Error) Ive.bad else Ive.ink2, fontSize = 13.5.sp, lineHeight = 20.sp)
        }
        if (t.steps.isNotEmpty()) {
            Spacer(Modifier.height(8.dp))
            Text(
                "${t.steps.size} step${if (t.steps.size > 1) "s" else ""} ${if (open) "▴" else "▾"}",
                color = Ive.ink3, fontSize = 12.sp, modifier = Modifier.clickable { open = !open },
            )
            if (open) t.steps.forEach { Text("• $it", color = Ive.ink2, fontSize = 12.5.sp) }
        }
        if (t.active) {
            Spacer(Modifier.height(10.dp))
            PillButton("Cancel", onClick = onCancel)
        }
    }
}

@Composable
private fun MusicCard(vm: IveViewModel) {
    val ctx = LocalContext.current
    val pkg = vm.config.musicPackage
    val appName = remember(pkg) { if (pkg.isEmpty()) "" else InstalledApps.label(ctx, pkg) }
    Column(Modifier.fillMaxWidth().glass(RoundedCornerShape(26.dp)).padding(18.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Box(
                Modifier.size(54.dp).clip(RoundedCornerShape(16.dp))
                    .background(Brush.sweepGradient(listOf(Ive.accent, Ive.accent2, Color(0xFFF472B6), Ive.accent)))
                    .clickable { if (pkg.isNotEmpty()) InstalledApps.launch(ctx, pkg) else vm.showSettings = true },
            )
            Column(Modifier.weight(1f)) {
                Text("Music", color = Ive.ink, fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
                Text(
                    if (pkg.isEmpty()) "Tap to choose your music app" else "Controlling $appName",
                    color = Ive.ink3, fontSize = 12.sp, maxLines = 1,
                )
            }
            GlyphButton("⏮", diameter = 40) { MediaKeys.send(ctx, KeyEvent.KEYCODE_MEDIA_PREVIOUS, pkg) }
            GlyphButton("⏯", diameter = 40) { MediaKeys.send(ctx, KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE, pkg) }
            GlyphButton("⏭", diameter = 40) { MediaKeys.send(ctx, KeyEvent.KEYCODE_MEDIA_NEXT, pkg) }
        }
    }
}

@Composable
private fun Composer(vm: IveViewModel, modifier: Modifier = Modifier) {
    val ctx = LocalContext.current
    var text by remember { mutableStateOf("") }

    fun send() {
        if (text.isBlank()) return
        if (!vm.config.ready) {
            vm.showSettings = true
            return
        }
        vm.submit(text)
        text = ""
    }

    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) vm.startVoice() else vm.toast("Microphone permission is needed for voice input.")
    }

    fun micTap() {
        when (vm.voice) {
            VoiceState.Idle ->
                if (ContextCompat.checkSelfPermission(ctx, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) vm.startVoice()
                else permission.launch(Manifest.permission.RECORD_AUDIO)
            VoiceState.Recording -> vm.stopVoice { t ->
                text = (text + " " + t).trim()
                if (vm.config.voiceAutoSend) send()
            }
            VoiceState.Transcribing -> Unit
        }
    }

    val level by animateFloatAsState(vm.voiceLevel, label = "level")
    val recording = vm.voice == VoiceState.Recording
    val hint = when (vm.voice) {
        VoiceState.Recording -> "Listening… tap ■ when you're done"
        VoiceState.Transcribing -> "Transcribing…"
        VoiceState.Idle -> "Tell IVE what to do…"
    }

    Row(
        modifier.fillMaxWidth().glass(RoundedCornerShape(28.dp), strong = true).padding(start = 6.dp, end = 8.dp, top = 6.dp, bottom = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        TextField(
            value = text,
            onValueChange = { text = it },
            modifier = Modifier.weight(1f),
            placeholder = { Text(hint, color = Ive.ink3) },
            maxLines = 4,
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Send),
            keyboardActions = KeyboardActions(onSend = { send() }),
            colors = TextFieldDefaults.colors(
                focusedContainerColor = Color.Transparent,
                unfocusedContainerColor = Color.Transparent,
                focusedIndicatorColor = Color.Transparent,
                unfocusedIndicatorColor = Color.Transparent,
                cursorColor = Ive.accent,
                focusedTextColor = Color.White,
                unfocusedTextColor = Color.White,
            ),
        )
        Box(
            Modifier.size(44.dp).scale(if (recording) 1f + 0.3f * level else 1f).clip(CircleShape)
                .background(if (recording) Ive.bad else Color.White.copy(alpha = 0.14f))
                .clickable { micTap() },
            contentAlignment = Alignment.Center,
        ) {
            Text(
                when (vm.voice) { VoiceState.Idle -> "🎤"; VoiceState.Recording -> "■"; VoiceState.Transcribing -> "…" },
                color = if (recording) Ive.bg else Ive.ink, fontSize = 18.sp, fontWeight = FontWeight.Bold,
            )
        }
        Spacer(Modifier.width(8.dp))
        Box(
            Modifier.size(44.dp).clip(CircleShape)
                .background(Brush.linearGradient(listOf(Ive.accent, Ive.accent2)))
                .clickable { send() },
            contentAlignment = Alignment.Center,
        ) { Text("↑", color = Ive.bg, fontSize = 22.sp, fontWeight = FontWeight.Bold) }
    }
}

@Composable
private fun SectionTitle(text: String) {
    Text(text, color = Ive.ink, fontSize = 17.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 10.dp))
}

@Composable
private fun StatusRow(label: String, on: Boolean, action: String, onClick: () -> Unit) {
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        Text(if (on) "✓" else "○", color = if (on) Ive.ok else Ive.warn, fontSize = 16.sp, fontWeight = FontWeight.Bold)
        Text(label, color = Ive.ink, fontSize = 14.sp, modifier = Modifier.weight(1f))
        PillButton(if (on) "Manage" else action, onClick = onClick)
    }
}

@Composable
private fun SettingsSheet(vm: IveViewModel) {
    val ctx = LocalContext.current
    val cfg = vm.config
    var key by remember { mutableStateOf("") }
    var url by remember(cfg) { mutableStateOf(cfg.baseUrl) }
    var model by remember(cfg) { mutableStateOf(cfg.model) }
    var approval by remember(cfg) { mutableStateOf(cfg.requireApproval) }
    var sttModel by remember(cfg) { mutableStateOf(cfg.sttModel) }
    var sttKey by remember { mutableStateOf("") }
    var sttUrl by remember(cfg) { mutableStateOf(cfg.sttBaseUrl) }
    var autoSend by remember(cfg) { mutableStateOf(cfg.voiceAutoSend) }
    var music by remember(cfg) { mutableStateOf(cfg.musicPackage) }
    var picker by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    val tick = AppBridge.tick // re-read permission state whenever the app resumes
    val overlayOk = remember(tick) { Settings.canDrawOverlays(ctx) }
    val field = Modifier.fillMaxWidth()
    val shape = RoundedCornerShape(16.dp)

    Box(
        Modifier.fillMaxSize().background(Color.Black.copy(alpha = 0.6f)).safeDrawingPadding()
            .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { vm.showSettings = false },
        contentAlignment = Alignment.Center,
    ) {
        Column(
            Modifier.padding(16.dp).fillMaxWidth().glass(RoundedCornerShape(30.dp), strong = true)
                .verticalScroll(rememberScrollState())
                .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) {}
                .padding(22.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text("Settings", color = Ive.ink, fontSize = 24.sp, fontWeight = FontWeight.Bold)

            OutlinedTextField(
                value = key, onValueChange = { key = it }, singleLine = true, modifier = field,
                label = { Text(if (cfg.ready) "API key (saved — type to replace)" else "API key") },
                visualTransformation = PasswordVisualTransformation(), shape = shape,
            )
            OutlinedTextField(value = url, onValueChange = { url = it }, singleLine = true, modifier = field, label = { Text("API base URL (https)") }, shape = shape)
            OutlinedTextField(value = model, onValueChange = { model = it }, singleLine = true, modifier = field, label = { Text("Model") }, shape = shape)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Switch(checked = approval, onCheckedChange = { approval = it })
                Text("Ask before writing, deleting or opening links", color = Ive.ink2, fontSize = 13.sp, modifier = Modifier.weight(1f))
            }

            SectionTitle("Voice input")
            OutlinedTextField(value = sttModel, onValueChange = { sttModel = it }, singleLine = true, modifier = field, label = { Text("Speech model") }, shape = shape)
            OutlinedTextField(
                value = sttKey, onValueChange = { sttKey = it }, singleLine = true, modifier = field, shape = shape,
                label = { Text(if (cfg.sttApiKey.isNotEmpty()) "Voice key (saved) — optional" else "Voice key — optional, blank reuses the API key") },
                visualTransformation = PasswordVisualTransformation(),
            )
            OutlinedTextField(value = sttUrl, onValueChange = { sttUrl = it }, singleLine = true, modifier = field, label = { Text("Voice base URL — optional") }, shape = shape)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Switch(checked = autoSend, onCheckedChange = { autoSend = it })
                Text("Send automatically after I stop talking", color = Ive.ink2, fontSize = 13.sp, modifier = Modifier.weight(1f))
            }

            SectionTitle("Music app")
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Text(
                    if (music.isEmpty()) "Not set — pick the app IVE should control" else InstalledApps.label(ctx, music),
                    color = Ive.ink, fontSize = 14.sp, modifier = Modifier.weight(1f),
                )
                PillButton("Choose") { picker = true }
                if (music.isNotEmpty()) PillButton("Clear") { music = "" }
            }

            SectionTitle("App control")
            Text(
                "Let IVE open and operate other apps. You are asked before each app, and can revoke any time.",
                color = Ive.ink3, fontSize = 12.5.sp,
            )
            StatusRow("Accessibility service", AppBridge.a11yOn, "Enable") {
                ctx.startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
            }
            StatusRow("Display over other apps", overlayOk, "Allow") {
                ctx.startActivity(
                    Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:${ctx.packageName}")).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
                )
            }
            Text(
                "Installed from an APK and the switch is greyed out? Open Android Settings → Apps → IVE → ⋮ → Allow restricted settings, then enable Accessibility.",
                color = Ive.ink3, fontSize = 12.sp,
            )
            if (vm.grantedApps.isNotEmpty()) {
                Text("Apps IVE may control", color = Ive.ink2, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
                vm.grantedApps.forEach { pkg ->
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        Text(InstalledApps.label(ctx, pkg), color = Ive.ink, fontSize = 14.sp, modifier = Modifier.weight(1f), maxLines = 1)
                        PillButton("Revoke") { vm.revoke(pkg) }
                    }
                }
            }

            Text("Keys are encrypted on this device with the Android Keystore.", color = Ive.ink3, fontSize = 12.sp)
            error?.let { Text(it, color = Ive.bad, fontSize = 13.sp) }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp, Alignment.End)) {
                PillButton("Cancel") { vm.showSettings = false }
                PillButton("Save", fill = Ive.accent, textColor = Ive.bg) {
                    val cleanUrl = url.trim().trimEnd('/')
                    val cleanStt = sttUrl.trim().trimEnd('/')
                    when {
                        !cleanUrl.startsWith("https://") -> error = "Base URL must start with https://"
                        cleanStt.isNotEmpty() && !cleanStt.startsWith("https://") -> error = "Voice base URL must start with https://"
                        model.isBlank() -> error = "Model is required"
                        sttModel.isBlank() -> error = "Speech model is required"
                        else -> {
                            vm.saveConfig(
                                IveConfig(
                                    apiKey = key.trim().ifEmpty { cfg.apiKey },
                                    baseUrl = cleanUrl,
                                    model = model.trim(),
                                    requireApproval = approval,
                                    sttModel = sttModel.trim(),
                                    sttBaseUrl = cleanStt,
                                    sttApiKey = sttKey.trim().ifEmpty { cfg.sttApiKey },
                                    voiceAutoSend = autoSend,
                                    musicPackage = music,
                                ),
                            )
                            error = null
                            vm.showSettings = false
                        }
                    }
                }
            }
        }
    }

    if (picker) {
        val apps = remember { InstalledApps.launchable(ctx) }
        AlertDialog(
            onDismissRequest = { picker = false },
            confirmButton = {},
            dismissButton = { TextButton(onClick = { picker = false }) { Text("Cancel") } },
            title = { Text("Choose your music app") },
            text = {
                LazyColumn(Modifier.heightIn(max = 360.dp)) {
                    items(apps) { a ->
                        Text(
                            a.label, color = Ive.ink, fontSize = 15.sp,
                            modifier = Modifier.fillMaxWidth().clickable { music = a.pkg; picker = false }.padding(vertical = 12.dp),
                        )
                    }
                }
            },
        )
    }
}
