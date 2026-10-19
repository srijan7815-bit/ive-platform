package com.srijan.ive.control

import android.accessibilityservice.AccessibilityService
import android.content.Context
import android.graphics.PixelFormat
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Handler
import android.os.Looper
import android.view.Gravity
import android.view.View
import android.view.WindowManager
import android.widget.LinearLayout
import android.widget.TextView
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlin.coroutines.resume

/**
 * Floating UI drawn by the accessibility service (no extra permission needed): a status pill with a Stop button
 * while IVE drives another app, and an Allow/Deny card so approvals still work when IVE itself is not on screen.
 */
class AgentOverlay(private val service: AccessibilityService) {
    private val wm = service.getSystemService(Context.WINDOW_SERVICE) as WindowManager
    private val ui = Handler(Looper.getMainLooper())
    private val density = service.resources.displayMetrics.density
    private var pill: View? = null
    private var pillText: TextView? = null
    private var card: View? = null

    private fun dp(v: Int) = (v * density).toInt()

    private fun params(width: Int, topDp: Int) = WindowManager.LayoutParams(
        width,
        WindowManager.LayoutParams.WRAP_CONTENT,
        WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
        WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL,
        PixelFormat.TRANSLUCENT,
    ).apply {
        gravity = Gravity.TOP or Gravity.CENTER_HORIZONTAL
        y = dp(topDp)
    }

    private fun bg(color: Int, radiusDp: Int, strokeColor: Int = 0x33FFFFFF) = GradientDrawable().apply {
        setColor(color)
        cornerRadius = dp(radiusDp).toFloat()
        setStroke(dp(1), strokeColor)
    }

    private fun label(text: String, sp: Float, color: Int = 0xF2FFFFFF.toInt(), bold: Boolean = false) = TextView(service).apply {
        this.text = text
        textSize = sp
        setTextColor(color)
        if (bold) setTypeface(typeface, Typeface.BOLD)
    }

    private fun button(text: String, fill: Int, textColor: Int, onClick: () -> Unit) = TextView(service).apply {
        this.text = text
        textSize = 14f
        setTextColor(textColor)
        setTypeface(typeface, Typeface.BOLD)
        gravity = Gravity.CENTER
        setPadding(dp(18), dp(10), dp(18), dp(10))
        background = bg(fill, 100, 0)
        setOnClickListener { onClick() }
    }

    fun status(text: String) {
        ui.post {
            if (pill == null) {
                val t = label(text, 13f, bold = true)
                val stop = button("Stop", 0x33F87171, 0xFFFECACA.toInt()) { AppBridge.onStop?.invoke() }
                val row = LinearLayout(service).apply {
                    orientation = LinearLayout.HORIZONTAL
                    gravity = Gravity.CENTER_VERTICAL
                    setPadding(dp(18), dp(6), dp(6), dp(6))
                    background = bg(0xF2050507.toInt(), 100)
                    addView(label("●  ", 12f, 0xFF7DD3FC.toInt()))
                    addView(t, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
                    addView(stop)
                }
                pill = row
                pillText = t
                runCatching { wm.addView(row, params(minOf(service.resources.displayMetrics.widthPixels - dp(32), dp(360)), 36)) }
            }
            pillText?.text = text
        }
    }

    fun clearStatus() {
        ui.post {
            pill?.let { runCatching { wm.removeView(it) } }
            pill = null
            pillText = null
        }
    }

    fun hideAll() {
        clearStatus()
        ui.post { removeCard() }
    }

    private fun removeCard() {
        card?.let { runCatching { wm.removeView(it) } }
        card = null
    }

    suspend fun ask(title: String, detail: String): Boolean = suspendCancellableCoroutine { cont ->
        ui.post {
            removeCard()
            fun finish(ok: Boolean) {
                removeCard()
                if (cont.isActive) cont.resume(ok)
            }
            val buttons = LinearLayout(service).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.END
                addView(button("Deny", 0x24FFFFFF, 0xF2FFFFFF.toInt()) { finish(false) })
                addView(View(service), LinearLayout.LayoutParams(dp(10), 1))
                addView(button("Allow", 0xFF4ADE80.toInt(), 0xFF04210F.toInt()) { finish(true) })
            }
            val body = LinearLayout(service).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(dp(20), dp(18), dp(20), dp(16))
                background = bg(0xF5101420.toInt(), 28)
                addView(label(title, 16f, bold = true))
                addView(label(detail.take(500), 12.5f, 0xB3FFFFFF.toInt()).apply {
                    setPadding(0, dp(8), 0, dp(14))
                    if (detail.startsWith("{") || detail.contains('\n')) typeface = Typeface.MONOSPACE
                })
                addView(buttons)
            }
            card = body
            runCatching { wm.addView(body, params(minOf(service.resources.displayMetrics.widthPixels - dp(32), dp(420)), 72)) }
                .onFailure { if (cont.isActive) cont.resume(false) }
        }
        cont.invokeOnCancellation { ui.post { removeCard() } }
    }
}
