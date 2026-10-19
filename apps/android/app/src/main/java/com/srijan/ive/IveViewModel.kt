package com.srijan.ive

import android.app.Application
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.srijan.ive.agent.Agent
import com.srijan.ive.agent.AiClient
import com.srijan.ive.agent.Sandbox
import com.srijan.ive.agent.SpeechClient
import com.srijan.ive.agent.ToolRunner
import com.srijan.ive.audio.VoiceRecorder
import com.srijan.ive.control.AppBridge
import com.srijan.ive.control.IveAccessibilityService
import com.srijan.ive.data.AppGrants
import com.srijan.ive.data.IveConfig
import com.srijan.ive.data.SettingsStore
import com.srijan.ive.ui.ApprovalUi
import com.srijan.ive.ui.Phase
import com.srijan.ive.ui.TaskStatus
import com.srijan.ive.ui.TaskUi
import com.srijan.ive.ui.VoiceState
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.io.File
import kotlin.coroutines.cancellation.CancellationException

class IveViewModel(app: Application) : AndroidViewModel(app) {
    private val store = SettingsStore(app)
    private val grants = AppGrants(app)
    private val agent = Agent(AiClient())
    private val speech = SpeechClient()
    private val recorder = VoiceRecorder(app)
    private val sandbox = Sandbox(File(app.filesDir, "workspace").apply { mkdirs() })
    private val gate = Mutex()
    private val jobs = mutableMapOf<Long, Job>()
    private var nextId = 1L
    private var levelJob: Job? = null

    val tasks = mutableStateListOf<TaskUi>()
    var config by mutableStateOf(store.load()); private set
    var phase by mutableStateOf<Phase>(Phase.Idle); private set
    var approval by mutableStateOf<ApprovalUi?>(null); private set
    var showSettings by mutableStateOf(false)
    var voice by mutableStateOf(VoiceState.Idle); private set
    var voiceLevel by mutableFloatStateOf(0f); private set
    var notice by mutableStateOf<String?>(null); private set
    var grantedApps by mutableStateOf(grants.all().toList().sorted()); private set

    val running: Int get() = tasks.count { it.active }

    init {
        AppBridge.onStop = { stopAll() }
    }

    override fun onCleared() {
        AppBridge.onStop = null
        recorder.cancel()
        super.onCleared()
    }

    fun saveConfig(c: IveConfig) {
        store.save(c)
        config = c
    }

    fun revoke(pkg: String) {
        grants.revoke(pkg)
        grantedApps = grants.all().toList().sorted()
    }

    fun toast(msg: String) {
        notice = msg
        viewModelScope.launch {
            delay(3500)
            if (notice == msg) notice = null
        }
    }

    // ---- tasks -----------------------------------------------------------------------------

    fun submit(text: String) {
        val prompt = text.trim().take(4000)
        if (prompt.isEmpty()) return
        if (!config.ready) { showSettings = true; return }

        val task = TaskUi(nextId++, prompt)
        tasks.add(0, task)
        jobs[task.id] = viewModelScope.launch {
            try {
                gate.withLock { execute(task) }
            } catch (e: CancellationException) {
                task.status = TaskStatus.Cancelled
                if (phase !is Phase.Approval) phase = Phase.Idle
                throw e
            } finally {
                jobs.remove(task.id)
                if (jobs.isEmpty()) IveAccessibilityService.instance?.overlayClear()
            }
        }
    }

    fun cancel(task: TaskUi) {
        jobs[task.id]?.cancel()
    }

    fun stopAll() {
        approval?.decision?.complete(false)
        jobs.values.toList().forEach { it.cancel() }
    }

    fun clearFinished() {
        tasks.removeAll { !it.active }
    }

    fun respond(approved: Boolean) {
        approval?.decision?.complete(approved)
    }

    private fun overlayStatus(text: String) {
        if (!AppBridge.activityVisible) IveAccessibilityService.instance?.overlayStatus(text)
    }

    private suspend fun execute(task: TaskUi) {
        task.status = TaskStatus.Running
        phase = Phase.Thinking
        try {
            val runner = ToolRunner(
                getApplication(), sandbox, config.requireApproval, grants,
                musicPackage = { config.musicPackage },
                onStatus = { overlayStatus(it) },
            ) { title, detail -> askApproval(task, title, detail) }
            val result = agent.run(task.prompt, config, runner) { step ->
                task.steps.add(step)
                phase = Phase.Working(step)
            }
            task.summary = result
            task.status = TaskStatus.Done
            phase = Phase.Done
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            task.summary = e.message ?: e.toString()
            task.status = TaskStatus.Error
            phase = Phase.Error
        } finally {
            grantedApps = grants.all().toList().sorted()
        }
        viewModelScope.launch {
            delay(3500)
            if (phase is Phase.Done || phase is Phase.Error) phase = Phase.Idle
        }
    }

    private suspend fun askApproval(task: TaskUi, title: String, detail: String): Boolean {
        // When IVE is not on screen (it is driving another app), ask through the accessibility overlay instead.
        if (!AppBridge.activityVisible) {
            IveAccessibilityService.instance?.let { return it.askOverlay(title, detail) }
        }
        val decision = CompletableDeferred<Boolean>()
        approval = ApprovalUi(title, detail, decision)
        task.status = TaskStatus.Awaiting
        phase = Phase.Approval
        return try {
            decision.await()
        } finally {
            approval = null
            if (task.status == TaskStatus.Awaiting) task.status = TaskStatus.Running
            if (phase is Phase.Approval) phase = Phase.Thinking
        }
    }

    // ---- voice -----------------------------------------------------------------------------

    fun startVoice() {
        if (voice != VoiceState.Idle) return
        if (!config.voiceReady) { showSettings = true; toast("Add your API key to use voice."); return }
        try {
            recorder.start()
        } catch (e: Exception) {
            toast("Could not start the microphone: ${e.message}")
            return
        }
        voice = VoiceState.Recording
        levelJob = viewModelScope.launch {
            while (isActive) {
                voiceLevel = recorder.level()
                delay(70)
            }
        }
    }

    fun stopVoice(onText: (String) -> Unit) {
        if (voice != VoiceState.Recording) return
        levelJob?.cancel()
        voiceLevel = 0f
        val file = recorder.stop()
        if (file == null) {
            voice = VoiceState.Idle
            toast("I didn't catch anything — try again.")
            return
        }
        voice = VoiceState.Transcribing
        viewModelScope.launch {
            try {
                val text = speech.transcribe(config, file)
                voice = VoiceState.Idle
                if (text.isBlank()) toast("I didn't catch anything — try again.") else onText(text)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                voice = VoiceState.Idle
                toast(e.message ?: "Transcription failed")
            } finally {
                file.delete()
            }
        }
    }
}
