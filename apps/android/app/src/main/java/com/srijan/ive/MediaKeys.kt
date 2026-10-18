package com.srijan.ive

import android.content.Context
import android.media.AudioManager
import android.view.KeyEvent

/** Transport control for whatever app is playing — no special permission needed. */
object MediaKeys {
    fun send(context: Context, keyCode: Int) {
        val am = context.getSystemService(Context.AUDIO_SERVICE) as AudioManager
        am.dispatchMediaKeyEvent(KeyEvent(KeyEvent.ACTION_DOWN, keyCode))
        am.dispatchMediaKeyEvent(KeyEvent(KeyEvent.ACTION_UP, keyCode))
    }
}
