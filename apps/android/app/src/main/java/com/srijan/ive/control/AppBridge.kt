package com.srijan.ive.control

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue

/** Process-wide state shared between the UI, the view model and the accessibility service. */
object AppBridge {
    /** True while IVE's own activity is on screen (approvals then use the in-app notch, otherwise the overlay card). */
    @Volatile var activityVisible = false

    /** Invoked by the overlay's Stop button. */
    @Volatile var onStop: (() -> Unit)? = null

    /** Observable: is the accessibility service connected? */
    var a11yOn by mutableStateOf(false)

    /** Bumped on every resume so permission status rows re-evaluate. */
    var tick by mutableIntStateOf(0)
}
