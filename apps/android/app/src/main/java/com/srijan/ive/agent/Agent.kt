package com.srijan.ive.agent

import com.srijan.ive.data.IveConfig
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import org.json.JSONArray
import org.json.JSONObject

private const val SYSTEM = """You are IVE, an agent running on the user's own Android phone with their permission.
FILES: you can list/read files in the IVE workspace freely; writing/deleting needs approval.
APPS: you can operate other apps. Workflow: list_apps -> open_app -> screen_read -> act (tap, tap_text, type_text, scroll, swipe, press_key) -> screen_read to verify. Element ids are valid only for the most recent screen_read. The user is asked before IVE touches each app; if they deny, stop and say so.
SAFETY: never type passwords, card numbers or one-time codes; if you meet a login, payment or permission prompt, stop and tell the user what is needed. Do not send messages, make purchases or delete data unless the user's request clearly asked for it.
MUSIC: use music_control for the user's chosen music app; for specific songs open the app and search using the UI tools.
Be decisive and finish in as few steps as possible. When completely done, reply with a short plain-text summary (no tool call)."""

private const val MAX_TURNS = 30

class Agent(private val ai: AiClient) {
    suspend fun run(prompt: String, cfg: IveConfig, tools: ToolRunner, onStep: (String) -> Unit): String {
        val messages = JSONArray()
            .put(JSONObject().put("role", "system").put("content", SYSTEM))
            .put(JSONObject().put("role", "user").put("content", prompt))

        for (turn in 0 until MAX_TURNS) {
            currentCoroutineContext().ensureActive()
            val msg = ai.chat(cfg, messages, ToolSchemas.all)
            messages.put(msg)

            val calls = msg.optJSONArray("tool_calls")
            if (calls == null || calls.length() == 0) {
                return if (msg.isNull("content")) "(no response)" else msg.getString("content")
            }
            for (i in 0 until calls.length()) {
                val call = calls.getJSONObject(i)
                val fn = call.getJSONObject("function")
                val name = fn.getString("name")
                val args = runCatching { JSONObject(fn.optString("arguments", "{}").ifBlank { "{}" }) }.getOrDefault(JSONObject())
                onStep(ToolRunner.describe(name, args))
                val result = tools.run(name, args).toString().take(9_000)
                messages.put(
                    JSONObject().put("role", "tool").put("tool_call_id", call.getString("id")).put("content", result),
                )
            }
        }
        throw IllegalStateException("Task exceeded the maximum number of steps.")
    }
}
