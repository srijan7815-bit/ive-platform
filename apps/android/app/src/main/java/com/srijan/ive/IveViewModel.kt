package com.srijan.ive

import android.app.Application
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.srijan.ive.agent.Agent
import com.srijan.ive.agent.AiClient
import com.srijan.ive.agent.Sandbox
import com.srijan.ive.agent.ToolRunner
import com.srijan.ive.data.IveConfig
import com.srijan.ive.data.SettingsStore
import com.srijan.ive.ui.ApprovalUi
import com.srijan.ive.ui.Phase
import com.srijan.ive.ui.TaskStatus
import com.srijan.ive.ui.TaskUi
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.io.File
import kotlin.coroutines.cancellation.CancellationException

class IveViewModel(app: Application) : AndroidViewModel(app) {
    private val store = SettingsStore(app)
    private val agent = Agent(AiClient())
    private val sandbox = Sandbox(File(app.filesDir, "workspace").apply { mkdirs() })
    private val gate = Mutex()
    private val jobs = mutableMapOf<Long, Job>()
    private var nextId = 1L

    val tasks = mutableStateListOf<TaskUi>()
    var config by mutableStateOf(store.load()); private set
    var phase by mutableStateOf<Phase>(Phase.Idle); private set
    var approval by mutableStateOf<ApprovalUi?>(null); private set
    var showSettings by mutableStateOf(false)

    val running: Int get() = tasks.count { it.active }

    fun saveConfig(c: IveConfig) {
        store.save(c)
        config = c
    }

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
            }
        }
    }

    fun cancel(task: TaskUi) {
        jobs[task.id]?.cancel()
    }

    fun clearFinished() {
        tasks.removeAll { !it.active }
    }

    fun respond(approved: Boolean) {
        approval?.decision?.complete(approved)
    }

    private suspend fun execute(task: TaskUi) {
        task.status = TaskStatus.Running
        phase = Phase.Thinking
        try {
            val runner = ToolRunner(getApplication(), sandbox, config.requireApproval) { title, detail ->
                askApproval(task, title, detail)
            }
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
        }
        viewModelScope.launch {
            delay(3500)
            if (phase is Phase.Done || phase is Phase.Error) phase = Phase.Idle
        }
    }

    private suspend fun askApproval(task: TaskUi, title: String, detail: String): Boolean {
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
}
