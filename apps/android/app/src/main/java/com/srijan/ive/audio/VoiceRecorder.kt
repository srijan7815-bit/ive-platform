package com.srijan.ive.audio

import android.content.Context
import android.media.MediaRecorder
import android.os.Build
import java.io.File

/** Mic capture to AAC/m4a (16 kHz mono) — a format Groq Whisper accepts directly. */
class VoiceRecorder(private val context: Context) {
    private var recorder: MediaRecorder? = null
    private var file: File? = null

    val isRecording: Boolean get() = recorder != null

    @Suppress("DEPRECATION")
    private fun newRecorder(): MediaRecorder =
        if (Build.VERSION.SDK_INT >= 31) MediaRecorder(context) else MediaRecorder()

    fun start() {
        if (recorder != null) return
        val f = File(context.cacheDir, "voice.m4a").also { it.delete() }
        val r = newRecorder()
        try {
            r.setAudioSource(MediaRecorder.AudioSource.MIC)
            r.setOutputFormat(MediaRecorder.OutputFormat.MPEG_4)
            r.setAudioEncoder(MediaRecorder.AudioEncoder.AAC)
            r.setAudioSamplingRate(16_000)
            r.setAudioEncodingBitRate(48_000)
            r.setAudioChannels(1)
            r.setMaxDuration(120_000)
            r.setOutputFile(f.absolutePath)
            r.prepare()
            r.start()
        } catch (e: Exception) {
            runCatching { r.release() }
            throw e
        }
        recorder = r
        file = f
    }

    /** 0..1 input level for the UI pulse. */
    fun level(): Float = ((recorder?.maxAmplitude ?: 0) / 12_000f).coerceIn(0f, 1f)

    /** Returns the recorded file, or null if nothing usable was captured. */
    fun stop(): File? {
        val r = recorder ?: return null
        recorder = null
        val ok = try {
            r.stop()
            true
        } catch (e: RuntimeException) {
            false // stop() throws if nothing was recorded
        } finally {
            runCatching { r.release() }
        }
        val f = file
        if (!ok) f?.delete()
        return if (ok && f != null && f.length() > 1500) f else null
    }

    fun cancel() {
        stop()?.delete()
    }
}
