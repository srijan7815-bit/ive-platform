package com.srijan.ive.control

import android.accessibilityservice.AccessibilityService
import android.accessibilityservice.GestureDescription
import android.content.Intent
import android.graphics.Path
import android.graphics.Rect
import android.os.Bundle
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo
import kotlinx.coroutines.delay
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlin.coroutines.resume

/**
 * The hands of IVE on Android. Once the user enables it in Settings → Accessibility it can read any app's UI tree
 * and tap / type / scroll / swipe inside it. The agent layer (ToolRunner) gates every call behind a per-app grant.
 */
class IveAccessibilityService : AccessibilityService() {
    private var nodes: Map<Int, AccessibilityNodeInfo> = emptyMap()
    private var overlay: AgentOverlay? = null

    override fun onServiceConnected() {
        super.onServiceConnected()
        instance = this
        overlay = AgentOverlay(this)
        AppBridge.a11yOn = true
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) = Unit

    override fun onInterrupt() = Unit

    override fun onUnbind(intent: Intent?): Boolean {
        shutdown()
        return super.onUnbind(intent)
    }

    override fun onDestroy() {
        shutdown()
        super.onDestroy()
    }

    private fun shutdown() {
        if (instance === this) instance = null
        AppBridge.a11yOn = false
        overlay?.hideAll()
    }

    // ---- overlay passthroughs --------------------------------------------------------------

    fun overlayStatus(text: String) = overlay?.status(text)
    fun overlayClear() = overlay?.clearStatus()
    suspend fun askOverlay(title: String, detail: String): Boolean = overlay?.ask(title, detail) ?: false

    // ---- reading -----------------------------------------------------------------------------

    fun foregroundPackage(): String? = rootInActiveWindow?.packageName?.toString()

    suspend fun awaitForegroundPackage(): String? {
        repeat(8) {
            foregroundPackage()?.let { return it }
            delay(150)
        }
        return null
    }

    /** Compact text snapshot of the visible UI. Element ids are valid until the next call. */
    fun readScreen(): String {
        val root = rootInActiveWindow ?: return "error: no active window"
        val map = LinkedHashMap<Int, AccessibilityNodeInfo>()
        val sb = StringBuilder("package=${root.packageName}\n")
        walk(root, 0, sb, map)
        nodes = map
        if (map.isEmpty()) sb.append("(nothing readable — the app may still be loading or blocks accessibility)")
        return sb.toString()
    }

    private fun walk(n: AccessibilityNodeInfo, depth: Int, sb: StringBuilder, map: MutableMap<Int, AccessibilityNodeInfo>) {
        if (map.size >= MAX_NODES || !n.isVisibleToUser) return
        val text = if (n.isPassword) "••••" else n.text?.toString()?.replace('\n', ' ')?.take(120)
        val desc = n.contentDescription?.toString()?.replace('\n', ' ')?.take(80)
        val interactive = n.isClickable || n.isLongClickable || n.isEditable || n.isScrollable || n.isCheckable
        if (interactive || !text.isNullOrBlank() || !desc.isNullOrBlank()) {
            val id = map.size + 1
            map[id] = n
            val r = Rect().also { n.getBoundsInScreen(it) }
            sb.append("  ".repeat(minOf(depth, 6))).append('[').append(id).append("] ")
                .append(n.className?.toString()?.substringAfterLast('.') ?: "View")
            if (!text.isNullOrBlank()) sb.append(" \"").append(text).append('"')
            if (!desc.isNullOrBlank()) sb.append(" desc=\"").append(desc).append('"')
            if (n.isClickable) sb.append(" clickable")
            if (n.isEditable) sb.append(" editable")
            if (n.isScrollable) sb.append(" scrollable")
            if (n.isCheckable) sb.append(if (n.isChecked) " checked" else " unchecked")
            if (n.isFocused) sb.append(" focused")
            sb.append(" @").append(r.centerX()).append(',').append(r.centerY()).append('\n')
        }
        for (i in 0 until n.childCount) n.getChild(i)?.let { walk(it, depth + 1, sb, map) }
    }

    // ---- acting (each returns "ok" or an error message) ------------------------------------

    private fun clickableAncestor(start: AccessibilityNodeInfo): AccessibilityNodeInfo? {
        var cur: AccessibilityNodeInfo? = start
        var hops = 0
        while (cur != null && !cur.isClickable && hops < 6) {
            cur = cur.parent
            hops++
        }
        return if (cur != null && cur.isClickable) cur else null
    }

    private suspend fun clickNode(n: AccessibilityNodeInfo): String {
        n.refresh()
        if (clickableAncestor(n)?.performAction(AccessibilityNodeInfo.ACTION_CLICK) == true) return "ok"
        val r = Rect().also { n.getBoundsInScreen(it) }
        return tapXY(r.centerX().toFloat(), r.centerY().toFloat())
    }

    suspend fun tapElement(id: Int): String {
        val n = nodes[id] ?: return "unknown element $id — call screen_read again"
        return clickNode(n)
    }

    suspend fun tapText(text: String): String {
        if (text.isBlank()) return "text is required"
        val root = rootInActiveWindow ?: return "no active window"
        val hits = root.findAccessibilityNodeInfosByText(text)
        val best = hits.firstOrNull { it.text?.toString().equals(text, true) || it.contentDescription?.toString().equals(text, true) } ?: hits.firstOrNull()
        return if (best == null) "no element containing \"$text\" — call screen_read" else clickNode(best)
    }

    suspend fun tapXY(x: Float, y: Float): String =
        if (gesture(Path().apply { moveTo(x, y) }, 60)) "ok" else "gesture was cancelled or blocked by the app"

    suspend fun longPress(id: Int): String {
        val n = nodes[id] ?: return "unknown element $id — call screen_read again"
        n.refresh()
        if (n.performAction(AccessibilityNodeInfo.ACTION_LONG_CLICK)) return "ok"
        val r = Rect().also { n.getBoundsInScreen(it) }
        return if (gesture(Path().apply { moveTo(r.centerX().toFloat(), r.centerY().toFloat()) }, 700)) "ok" else "long press failed"
    }

    fun typeText(text: String, id: Int?): String {
        val target = (if (id != null) nodes[id] else null)
            ?: rootInActiveWindow?.findFocus(AccessibilityNodeInfo.FOCUS_INPUT)
            ?: rootInActiveWindow?.let { firstEditable(it) }
            ?: return "no text field found — tap one first"
        if (target.isPassword) return "refusing to type into a password field — ask the user to enter it themselves"
        val args = Bundle().apply { putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE, text) }
        return if (target.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, args)) "ok" else "the field rejected the text"
    }

    private fun firstEditable(n: AccessibilityNodeInfo): AccessibilityNodeInfo? {
        if (n.isEditable && n.isVisibleToUser) return n
        for (i in 0 until n.childCount) n.getChild(i)?.let { c -> firstEditable(c)?.let { return it } }
        return null
    }

    private fun firstScrollable(n: AccessibilityNodeInfo): AccessibilityNodeInfo? {
        if (n.isScrollable && n.isVisibleToUser) return n
        for (i in 0 until n.childCount) n.getChild(i)?.let { c -> firstScrollable(c)?.let { return it } }
        return null
    }

    suspend fun scroll(direction: String): String {
        val forward = direction.lowercase() in setOf("down", "right", "forward")
        val sc = rootInActiveWindow?.let { firstScrollable(it) }
        val action = if (forward) AccessibilityNodeInfo.ACTION_SCROLL_FORWARD else AccessibilityNodeInfo.ACTION_SCROLL_BACKWARD
        if (sc != null && sc.performAction(action)) return "ok"
        val m = resources.displayMetrics
        val w = m.widthPixels.toFloat()
        val h = m.heightPixels.toFloat()
        val p = Path()
        when (direction.lowercase()) {
            "up" -> { p.moveTo(w / 2, h * 0.3f); p.lineTo(w / 2, h * 0.7f) }
            "left" -> { p.moveTo(w * 0.3f, h / 2); p.lineTo(w * 0.7f, h / 2) }
            "right" -> { p.moveTo(w * 0.7f, h / 2); p.lineTo(w * 0.3f, h / 2) }
            else -> { p.moveTo(w / 2, h * 0.7f); p.lineTo(w / 2, h * 0.3f) }
        }
        return if (gesture(p, 320)) "ok" else "scroll failed"
    }

    suspend fun swipe(x1: Float, y1: Float, x2: Float, y2: Float): String {
        val p = Path().apply { moveTo(x1, y1); lineTo(x2, y2) }
        return if (gesture(p, 320)) "ok" else "swipe failed"
    }

    fun pressKey(key: String): String {
        val action = when (key.lowercase()) {
            "back" -> GLOBAL_ACTION_BACK
            "home" -> GLOBAL_ACTION_HOME
            "recents" -> GLOBAL_ACTION_RECENTS
            "notifications" -> GLOBAL_ACTION_NOTIFICATIONS
            "quick_settings" -> GLOBAL_ACTION_QUICK_SETTINGS
            else -> return "unknown key \"$key\" (back, home, recents, notifications, quick_settings)"
        }
        return if (performGlobalAction(action)) "ok" else "the system refused that action"
    }

    private suspend fun gesture(path: Path, durationMs: Long): Boolean = suspendCancellableCoroutine { cont ->
        val g = GestureDescription.Builder().addStroke(GestureDescription.StrokeDescription(path, 0, durationMs)).build()
        val dispatched = dispatchGesture(
            g,
            object : GestureResultCallback() {
                override fun onCompleted(gestureDescription: GestureDescription?) {
                    if (cont.isActive) cont.resume(true)
                }

                override fun onCancelled(gestureDescription: GestureDescription?) {
                    if (cont.isActive) cont.resume(false)
                }
            },
            null,
        )
        if (!dispatched && cont.isActive) cont.resume(false)
    }

    companion object {
        private const val MAX_NODES = 220

        @Volatile
        var instance: IveAccessibilityService? = null
    }
}
