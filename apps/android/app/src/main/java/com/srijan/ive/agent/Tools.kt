package com.srijan.ive.agent

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.Locale
import kotlin.coroutines.cancellation.CancellationException

/** All file tools are confined to one app-private folder; paths can never escape it. */
class Sandbox(root: File) {
    val root: File = root.canonicalFile

    fun resolve(path: String): File {
        val f = File(root, path.trimStart('/', '\\')).canonicalFile
        require(f == root || f.path.startsWith(root.path + File.separator)) { "Path escapes the workspace" }
        return f
    }
}

object ToolSchemas {
    private fun fn(name: String, description: String, props: List<String>, required: List<String>): JSONObject {
        val properties = JSONObject()
        props.forEach { properties.put(it, JSONObject().put("type", "string")) }
        val params = JSONObject()
            .put("type", "object")
            .put("properties", properties)
            .put("required", JSONArray(required))
        return JSONObject()
            .put("type", "function")
            .put("function", JSONObject().put("name", name).put("description", description).put("parameters", params))
    }

    val all: JSONArray = JSONArray()
        .put(fn("list_dir", "List files and folders in the IVE workspace folder.", listOf("path"), listOf("path")))
        .put(fn("read_file", "Read a UTF-8 text file from the workspace (first 200 KB).", listOf("path"), listOf("path")))
        .put(fn("write_file", "Create or overwrite a text file in the workspace. Requires approval.", listOf("path", "content"), listOf("path", "content")))
        .put(fn("delete_file", "Delete a file in the workspace. Requires approval.", listOf("path"), listOf("path")))
        .put(fn("device_info", "Get device model, Android version, locale and current time.", emptyList(), emptyList()))
        .put(fn("open_url", "Open an http(s) link in the browser. Requires approval.", listOf("url"), listOf("url")))
}

class ToolRunner(
    private val context: Context,
    private val sandbox: Sandbox,
    private val requireApproval: Boolean,
    private val approve: suspend (title: String, detail: String) -> Boolean,
) {
    suspend fun run(name: String, args: JSONObject): JSONObject = try {
        dispatch(name, args)
    } catch (e: CancellationException) {
        throw e
    } catch (e: Exception) {
        JSONObject().put("error", e.message ?: e.toString())
    }

    private suspend fun allowed(title: String, detail: String) = !requireApproval || approve(title, detail)
    private fun denied() = JSONObject().put("error", "User denied this action.")

    private suspend fun dispatch(name: String, args: JSONObject): JSONObject {
        when (name) {
            "list_dir" -> {
                val dir = sandbox.resolve(args.optString("path", "."))
                val entries = JSONArray()
                dir.listFiles()?.sortedBy { it.name }?.take(500)?.forEach {
                    entries.put(JSONObject().put("name", it.name).put("type", if (it.isDirectory) "dir" else "file"))
                }
                return JSONObject().put("entries", entries)
            }
            "read_file" -> {
                val f = sandbox.resolve(args.getString("path"))
                val len = minOf(f.length(), MAX_READ.toLong()).toInt()
                val buf = ByteArray(len)
                f.inputStream().use { it.read(buf) }
                return JSONObject().put("content", String(buf, Charsets.UTF_8)).put("truncated", f.length() > MAX_READ)
            }
            "write_file" -> {
                val path = args.getString("path")
                val content = args.optString("content", "")
                val f = sandbox.resolve(path)
                if (!allowed("Write file: $path", content.take(600))) return denied()
                f.parentFile?.mkdirs()
                f.writeText(content)
                return JSONObject().put("ok", true)
            }
            "delete_file" -> {
                val path = args.getString("path")
                val f = sandbox.resolve(path)
                if (f.isDirectory) return JSONObject().put("error", "Refusing to delete a directory.")
                if (!allowed("Delete file: $path", "This permanently removes the file.")) return denied()
                return JSONObject().put("ok", f.delete())
            }
            "device_info" -> return JSONObject()
                .put("model", Build.MODEL)
                .put("android", Build.VERSION.RELEASE)
                .put("sdk", Build.VERSION.SDK_INT)
                .put("locale", Locale.getDefault().toString())
                .put("time", java.time.Instant.now().toString())
            "open_url" -> {
                val url = args.getString("url")
                require(url.startsWith("https://") || url.startsWith("http://")) { "Only http(s) links are allowed" }
                if (!allowed("Open link", url)) return denied()
                context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
                return JSONObject().put("ok", true)
            }
            else -> return JSONObject().put("error", "Unknown tool: $name")
        }
    }

    companion object {
        const val MAX_READ = 200 * 1024

        fun describe(name: String, args: JSONObject): String {
            val p = args.optString("path").substringAfterLast('/')
            return when (name) {
                "list_dir" -> "Listing ${p.ifEmpty { "workspace" }}"
                "read_file" -> "Reading $p"
                "write_file" -> "Writing $p"
                "delete_file" -> "Deleting $p"
                "device_info" -> "Checking device info"
                "open_url" -> "Opening link"
                else -> name
            }
        }
    }
}
