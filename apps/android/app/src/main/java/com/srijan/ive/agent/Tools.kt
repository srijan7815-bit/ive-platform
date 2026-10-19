package com.srijan.ive.agent

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.view.KeyEvent
import com.srijan.ive.MediaKeys
import com.srijan.ive.control.IveAccessibilityService
import com.srijan.ive.data.AppGrants
import com.srijan.ive.data.InstalledApps
import kotlinx.coroutines.delay
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.Locale
import kotlin.coroutines.cancellation.CancellationException

/** File tools are confined to one app-private folder; paths can never escape it. */
class Sandbox(root: File) {
    val root: File = root.canonicalFile

    fun resolve(path: String): File {
        val f = File(root, path.trimStart('/', '\\')).canonicalFile
        require(f == root || f.path.startsWith(root.path + File.separator)) { "Path escapes the workspace" }
        return f
    }
}

object ToolSchemas {
    /** props entries are "name" (string) or "name:integer". */
    private fun fn(name: String, description: String, props: List<String> = emptyList(), required: List<String> = emptyList()): JSONObject {
        val properties = JSONObject()
        props.forEach {
            val (n, t) = if (':' in it) it.split(':').let { p -> p[0] to p[1] } else it to "string"
            properties.put(n, JSONObject().put("type", t))
        }
        val params = JSONObject().put("type", "object").put("properties", properties).put("required", JSONArray(required))
        return JSONObject().put("type", "function")
            .put("function", JSONObject().put("name", name).put("description", description).put("parameters", params))
    }

    val all: JSONArray = JSONArray().apply {
        put(fn("list_dir", "List files and folders in the IVE workspace folder.", listOf("path"), listOf("path")))
        put(fn("read_file", "Read a UTF-8 text file from the workspace (first 200 KB).", listOf("path"), listOf("path")))
        put(fn("write_file", "Create or overwrite a text file in the workspace. Requires approval.", listOf("path", "content"), listOf("path", "content")))
        put(fn("delete_file", "Delete a file in the workspace. Requires approval.", listOf("path"), listOf("path")))
        put(fn("device_info", "Get device model, Android version, locale and current time."))
        put(fn("open_url", "Open an http(s) link in the browser. Requires approval.", listOf("url"), listOf("url")))
        put(fn("list_apps", "List installed, launchable apps (name + package)."))
        put(fn("open_app", "Open an installed app by package or name. The user is asked to allow each app the first time.", listOf("package", "name")))
        put(fn("screen_read", "Read the visible UI of the foreground app as a numbered list of elements. Call this before acting and again after to verify."))
        put(fn("tap", "Tap an element by its id from the latest screen_read.", listOf("id:integer"), listOf("id")))
        put(fn("tap_text", "Tap the element whose text or description matches.", listOf("text"), listOf("text")))
        put(fn("tap_xy", "Tap screen coordinates (use the @x,y shown by screen_read).", listOf("x:integer", "y:integer"), listOf("x", "y")))
        put(fn("long_press", "Long-press an element by id.", listOf("id:integer"), listOf("id")))
        put(fn("type_text", "Replace the text of the focused field (or element id) with text. Never use for passwords.", listOf("text", "id:integer"), listOf("text")))
        put(fn("scroll", "Scroll the foreground app. direction = up | down | left | right.", listOf("direction"), listOf("direction")))
        put(fn("swipe", "Swipe between two screen points.", listOf("x1:integer", "y1:integer", "x2:integer", "y2:integer"), listOf("x1", "y1", "x2", "y2")))
        put(fn("press_key", "System navigation: back | home | recents | notifications | quick_settings.", listOf("key"), listOf("key")))
        put(fn("wait", "Wait for the UI to settle (milliseconds, 100-5000).", listOf("ms:integer")))
        put(fn("music_control", "Control the user's chosen music app. action = play_pause | next | previous | open.", listOf("action"), listOf("action")))
    }
}

class ToolRunner(
    private val context: Context,
    private val sandbox: Sandbox,
    private val requireApproval: Boolean,
    private val grants: AppGrants,
    private val musicPackage: () -> String,
    private val onStatus: (String) -> Unit,
    private val approve: suspend (title: String, detail: String) -> Boolean,
) {
    suspend fun run(name: String, args: JSONObject): JSONObject = try {
        onStatus(describe(name, args))
        dispatch(name, args)
    } catch (e: CancellationException) {
        throw e
    } catch (e: Exception) {
        err(e.message ?: e.toString())
    }

    private fun err(m: String) = JSONObject().put("error", m)
    private fun ok() = JSONObject().put("ok", true)

    private suspend fun allowed(title: String, detail: String) = !requireApproval || approve(title, detail)

    private suspend fun dispatch(name: String, args: JSONObject): JSONObject = when (name) {
        "list_dir" -> listDir(args)
        "read_file" -> readFile(args)
        "write_file" -> writeFile(args)
        "delete_file" -> deleteFile(args)
        "device_info" -> JSONObject()
            .put("model", Build.MODEL).put("android", Build.VERSION.RELEASE).put("sdk", Build.VERSION.SDK_INT)
            .put("locale", Locale.getDefault().toString()).put("time", java.time.Instant.now().toString())
        "open_url" -> openUrl(args)
        "list_apps" -> listApps()
        "open_app" -> openApp(args)
        "music_control" -> music(args)
        "wait" -> { delay(args.optLong("ms", 800).coerceIn(100, 5000)); ok() }
        "screen_read", "tap", "tap_text", "tap_xy", "long_press", "type_text", "scroll", "swipe", "press_key" -> control(name, args)
        else -> err("Unknown tool: $name")
    }

    // ---- files -------------------------------------------------------------------------------

    private fun listDir(a: JSONObject): JSONObject {
        val dir = sandbox.resolve(a.optString("path", "."))
        val entries = JSONArray()
        dir.listFiles()?.sortedBy { it.name }?.take(500)?.forEach {
            entries.put(JSONObject().put("name", it.name).put("type", if (it.isDirectory) "dir" else "file"))
        }
        return JSONObject().put("entries", entries)
    }

    private fun readFile(a: JSONObject): JSONObject {
        val f = sandbox.resolve(a.getString("path"))
        val len = minOf(f.length(), MAX_READ.toLong()).toInt()
        val buf = ByteArray(len)
        f.inputStream().use { it.read(buf) }
        return JSONObject().put("content", String(buf, Charsets.UTF_8)).put("truncated", f.length() > MAX_READ)
    }

    private suspend fun writeFile(a: JSONObject): JSONObject {
        val path = a.getString("path")
        val content = a.optString("content", "")
        val f = sandbox.resolve(path)
        if (!allowed("Write file: $path", content.take(600))) return err("User denied this action.")
        f.parentFile?.mkdirs()
        f.writeText(content)
        return ok()
    }

    private suspend fun deleteFile(a: JSONObject): JSONObject {
        val path = a.getString("path")
        val f = sandbox.resolve(path)
        if (f.isDirectory) return err("Refusing to delete a directory.")
        if (!allowed("Delete file: $path", "This permanently removes the file.")) return err("User denied this action.")
        return JSONObject().put("ok", f.delete())
    }

    private suspend fun openUrl(a: JSONObject): JSONObject {
        val url = a.getString("url")
        require(url.startsWith("https://") || url.startsWith("http://")) { "Only http(s) links are allowed" }
        if (!allowed("Open link", url)) return err("User denied this action.")
        context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        return ok()
    }

    // ---- apps --------------------------------------------------------------------------------

    private fun listApps(): JSONObject {
        val arr = JSONArray()
        InstalledApps.launchable(context).take(250).forEach { arr.put(JSONObject().put("name", it.label).put("package", it.pkg)) }
        return JSONObject().put("apps", arr)
    }

    private fun resolvePackage(a: JSONObject): String? {
        val apps = InstalledApps.launchable(context)
        val p = a.optString("package")
        if (p.isNotBlank()) return apps.firstOrNull { it.pkg == p }?.pkg
        val n = a.optString("name").trim().lowercase()
        if (n.isEmpty()) return null
        return (apps.firstOrNull { it.label.lowercase() == n } ?: apps.firstOrNull { it.label.lowercase().contains(n) })?.pkg
    }

    /** "Ask first": the user must allow each app once. Always asks, regardless of the file-approval setting. */
    private suspend fun ensureGranted(pkg: String): Boolean {
        if (pkg == context.packageName || grants.isGranted(pkg)) return true
        val label = InstalledApps.label(context, pkg)
        val yes = approve(
            "Allow IVE to control $label?",
            "IVE will be able to read the screen and tap, type and scroll inside $label until you revoke access in IVE → Settings → App access.",
        )
        if (yes) grants.grant(pkg)
        return yes
    }

    private suspend fun openApp(a: JSONObject): JSONObject {
        val pkg = resolvePackage(a) ?: return err("No installed app matches that. Call list_apps.")
        if (!ensureGranted(pkg)) return err("The user did not allow IVE to open or control this app.")
        if (!InstalledApps.launch(context, pkg)) return err("That app cannot be launched.")
        val svc = IveAccessibilityService.instance
        if (svc == null) {
            delay(900)
            return ok().put("package", pkg).put("note", "App control is off, so IVE cannot see or operate the app.")
        }
        repeat(10) {
            if (svc.foregroundPackage() == pkg) return ok().put("package", pkg)
            delay(250)
        }
        // Android can block launches from the background — fall back to the launcher UI.
        svc.pressKey("home")
        delay(700)
        if (svc.tapText(InstalledApps.label(context, pkg)) == "ok") {
            repeat(8) {
                if (svc.foregroundPackage() == pkg) return ok().put("package", pkg)
                delay(250)
            }
        }
        return err("Launched, but the app did not come to the foreground.")
    }

    private suspend fun control(name: String, a: JSONObject): JSONObject {
        val svc = IveAccessibilityService.instance
            ?: return err("App control is off. Ask the user to enable IVE in Settings → Accessibility, then retry.")
        val pkg = svc.awaitForegroundPackage() ?: return err("No app is in the foreground.")
        if (!ensureGranted(pkg)) return err("The user did not allow IVE to control this app.")

        val result: String = when (name) {
            "screen_read" -> return JSONObject().put("screen", svc.readScreen())
            "tap" -> svc.tapElement(a.optInt("id", -1))
            "tap_text" -> svc.tapText(a.optString("text"))
            "tap_xy" -> svc.tapXY(a.optDouble("x").toFloat(), a.optDouble("y").toFloat())
            "long_press" -> svc.longPress(a.optInt("id", -1))
            "type_text" -> svc.typeText(a.optString("text"), if (a.has("id")) a.optInt("id") else null)
            "scroll" -> svc.scroll(a.optString("direction", "down"))
            "swipe" -> svc.swipe(a.optDouble("x1").toFloat(), a.optDouble("y1").toFloat(), a.optDouble("x2").toFloat(), a.optDouble("y2").toFloat())
            "press_key" -> svc.pressKey(a.optString("key"))
            else -> "unsupported"
        }
        delay(350) // let the UI settle before the model looks again
        return if (result == "ok") ok().put("package", svc.foregroundPackage() ?: pkg) else err(result)
    }

    // ---- music -------------------------------------------------------------------------------

    private fun music(a: JSONObject): JSONObject {
        val pkg = musicPackage()
        return when (a.optString("action")) {
            "play_pause", "play", "pause" -> { MediaKeys.send(context, KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE, pkg); ok() }
            "next" -> { MediaKeys.send(context, KeyEvent.KEYCODE_MEDIA_NEXT, pkg); ok() }
            "previous", "prev" -> { MediaKeys.send(context, KeyEvent.KEYCODE_MEDIA_PREVIOUS, pkg); ok() }
            "open" -> if (pkg.isNotEmpty() && InstalledApps.launch(context, pkg)) ok() else err("No music app is set. Choose one in IVE → Settings.")
            else -> err("Unknown music action.")
        }
    }

    companion object {
        const val MAX_READ = 200 * 1024

        fun describe(name: String, a: JSONObject): String {
            val p = a.optString("path").substringAfterLast('/')
            return when (name) {
                "list_dir" -> "Listing ${p.ifEmpty { "workspace" }}"
                "read_file" -> "Reading $p"
                "write_file" -> "Writing $p"
                "delete_file" -> "Deleting $p"
                "device_info" -> "Checking device info"
                "open_url" -> "Opening link"
                "list_apps" -> "Looking at installed apps"
                "open_app" -> "Opening ${a.optString("name").ifEmpty { a.optString("package") }}"
                "screen_read" -> "Reading the screen"
                "tap" -> "Tapping element ${a.optInt("id")}"
                "tap_text" -> "Tapping “${a.optString("text")}”"
                "tap_xy" -> "Tapping the screen"
                "long_press" -> "Long-pressing"
                "type_text" -> "Typing"
                "scroll" -> "Scrolling ${a.optString("direction")}"
                "swipe" -> "Swiping"
                "press_key" -> "Pressing ${a.optString("key")}"
                "wait" -> "Waiting"
                "music_control" -> "Music: ${a.optString("action")}"
                else -> name
            }
        }
    }
}
