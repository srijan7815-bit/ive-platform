package com.srijan.ive.agent

import com.srijan.ive.data.IveConfig
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL

class ApiException(val code: Int, detail: String) : Exception("AI request failed ($code): $detail") {
    val retryable: Boolean get() = code == 408 || code == 429 || code >= 500
}

/** OpenAI-compatible chat-completions client with tool calling and bounded retry/backoff. */
class AiClient {
    suspend fun chat(cfg: IveConfig, messages: JSONArray, tools: JSONArray): JSONObject = withContext(Dispatchers.IO) {
        var last: Exception? = null
        for (attempt in 0..2) {
            currentCoroutineContext().ensureActive()
            try {
                return@withContext once(cfg, messages, tools)
            } catch (e: ApiException) {
                last = e
                if (!e.retryable) throw e
            } catch (e: IOException) {
                last = e
            }
            if (attempt < 2) delay(600L * (1 shl attempt))
        }
        throw last ?: IllegalStateException("AI request failed")
    }

    private fun once(cfg: IveConfig, messages: JSONArray, tools: JSONArray): JSONObject {
        val conn = (URL(cfg.baseUrl.trimEnd('/') + "/chat/completions").openConnection() as HttpURLConnection).apply {
            requestMethod = "POST"
            connectTimeout = 15_000
            readTimeout = 90_000
            doOutput = true
            setRequestProperty("Content-Type", "application/json")
            setRequestProperty("Authorization", "Bearer ${cfg.apiKey}")
        }
        try {
            val body = JSONObject()
                .put("model", cfg.model)
                .put("messages", messages)
                .put("tools", tools)
                .put("tool_choice", "auto")
                .put("max_tokens", 4096)
            conn.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }

            val code = conn.responseCode
            val stream = if (code in 200..299) conn.inputStream else conn.errorStream
            val text = stream?.bufferedReader()?.use { it.readText() }.orEmpty()
            if (code !in 200..299) throw ApiException(code, text.take(300))

            val choice = JSONObject(text).optJSONArray("choices")?.optJSONObject(0)
                ?: throw ApiException(0, "response had no choices")
            return choice.getJSONObject("message")
        } finally {
            conn.disconnect()
        }
    }
}
