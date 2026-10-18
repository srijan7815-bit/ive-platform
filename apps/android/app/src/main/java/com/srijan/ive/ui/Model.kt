package com.srijan.ive.ui

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import kotlinx.coroutines.CompletableDeferred

enum class TaskStatus { Queued, Running, Awaiting, Done, Error, Cancelled }

class TaskUi(val id: Long, val prompt: String) {
    var status by mutableStateOf(TaskStatus.Queued)
    var summary by mutableStateOf<String?>(null)
    val steps = mutableStateListOf<String>()
    val active: Boolean get() = status == TaskStatus.Queued || status == TaskStatus.Running || status == TaskStatus.Awaiting
}

sealed interface Phase {
    data object Idle : Phase
    data object Thinking : Phase
    data class Working(val text: String) : Phase
    data object Approval : Phase
    data object Done : Phase
    data object Error : Phase
}

class ApprovalUi(val title: String, val detail: String, val decision: CompletableDeferred<Boolean>)
