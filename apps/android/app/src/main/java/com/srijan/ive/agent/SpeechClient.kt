package com.srijan.ive.agent

import com.srijan.ive.data.IveConfig
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.DataOutputStream
import java.io.File
import java.net.HttpURLConnection
import java.net.URL

/** Speech-to-text via the OpenAI-compatible /audio/transcriptions endpoint (Groq whisper-large-v3-turbo by default). */
class SpeechClient {
    suspend fun transcribe(cfg: IveConfig, audio: File): String = withContext(Dispatchers.IO) {
        val key = cfg.sttApiKey.ifBlank { cfg.apiKey }
        require(key.isNotBlank()) { "Add your API key in Settings to use voice input." }
        val base = cfg.sttBaseUrl.ifBlank { cfg.baseUrl }.trimEnd('/')
        val boundary = "----ive${System.currentTimeMillis()}"

        val conn = (URL("$base/audio/transcriptions").openConnection() as HttpURLConnection).apply {
            requestMethod = "POST"
            connectTimeout = 15_000
            readTimeout = 60_000
            doOutput = true
            setChunkedStreamingMode(0)
            setRequestProperty("Authorization", "Bearer $key")
            setRequestProperty("Content-Type", "multipart/form-data; boundary=$boundary")
        }
        try {
            DataOutputStream(conn.outputStream).use { out ->
                fun field(name: String, value: String) {
                    out.writeBytes("--$boundary\r\nContent-Disposition: form-data; name=\"$name\"\r\n\r\n$value\r\n")
                }
                field("model", cfg.sttModel)
                field("response_format", "json")
                field("temperature", "0")
                out.writeBytes("--$boundary\r\nContent-Disposition: form-data; name=\"file\"; filename=\"voice.m4a\"\r\nContent-Type: audio/mp4\r\n\r\n")
                audio.inputStream().use { it.copyTo(out) }
                out.writeBytes("\r\n--$boundary--\r\n")
            }
            val code = conn.responseCode
            val body = (if (code in 200..299) conn.inputStream else conn.errorStream)?.bufferedReader()?.use { it.readText() }.orEmpty()
            if (code !in 200..299) throw ApiException(code, body.take(300))
            JSONObject(body).optString("text").trim()
        } finally {
            conn.disconnect()
        }
    }
}
