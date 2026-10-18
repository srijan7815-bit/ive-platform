package com.srijan.ive.agent

import com.srijan.ive.data.IveConfig
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import org.json.JSONArray
import org.json.JSONObject

private const val SYSTEM = """You are IVE, a background agent running on the user's Android phone with their permission.
You can list/read files in the IVE workspace folder freely. Writing or deleting files and opening links require the user's approval — call those tools when needed; a denial comes back as an error you must respect.
Be decisive and finish in as few steps as possible. When completely done, reply with a short plain-text summary (no tool call)."""

private const val MAX_TURNS = 12

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
                val args = runCatching { JSONObject(fn.optString("arguments", "{}")) }.getOrDefault(JSONObject())
                onStep(ToolRunner.describe(name, args))
                val result = tools.run(name, args).toString().take(12_000)
                messages.put(
                    JSONObject().put("role", "tool").put("tool_call_id", call.getString("id")).put("content", result),
                )
            }
        }
        throw IllegalStateException("Task exceeded the maximum number of steps.")
    }
}
