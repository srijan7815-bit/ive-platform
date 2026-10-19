package com.srijan.ive

import android.content.Context
import android.content.Intent
import android.media.AudioManager
import android.view.KeyEvent

/** Transport control. With a package it targets exactly the music app the user chose; otherwise whatever is playing. */
object MediaKeys {
    fun send(context: Context, keyCode: Int, pkg: String = "") {
        for (action in intArrayOf(KeyEvent.ACTION_DOWN, KeyEvent.ACTION_UP)) {
            val event = KeyEvent(action, keyCode)
            if (pkg.isNotEmpty()) {
                context.sendBroadcast(
                    Intent(Intent.ACTION_MEDIA_BUTTON).setPackage(pkg).putExtra(Intent.EXTRA_KEY_EVENT, event),
                )
            } else {
                (context.getSystemService(Context.AUDIO_SERVICE) as AudioManager).dispatchMediaKeyEvent(event)
            }
        }
    }
}
