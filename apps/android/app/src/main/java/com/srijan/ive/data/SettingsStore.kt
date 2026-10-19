package com.srijan.ive.data

import android.content.Context
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey

data class IveConfig(
    val apiKey: String = "",
    val baseUrl: String = "https://api.groq.com/openai/v1",
    val model: String = "llama-3.3-70b-versatile",
    val requireApproval: Boolean = true,
    // Voice input (Groq Whisper large-v3-turbo). Blank base URL / key = reuse the main ones.
    val sttModel: String = "whisper-large-v3-turbo",
    val sttBaseUrl: String = "",
    val sttApiKey: String = "",
    val voiceAutoSend: Boolean = true,
    // The music app the user chose (package name), e.g. com.spotify.music.
    val musicPackage: String = "",
) {
    val ready: Boolean get() = apiKey.isNotBlank()
    val voiceReady: Boolean get() = sttApiKey.isNotBlank() || apiKey.isNotBlank()
}

/** Secrets live in Keystore-backed EncryptedSharedPreferences; if that is unavailable they stay in memory only. */
class SettingsStore(context: Context) {
    private val prefs = runCatching {
        val key = MasterKey.Builder(context).setKeyScheme(MasterKey.KeyScheme.AES256_GCM).build()
        EncryptedSharedPreferences.create(
            context, "ive_secure", key,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
        )
    }.getOrNull()

    private var memory = IveConfig()

    fun load(): IveConfig {
        val p = prefs ?: return memory
        val d = IveConfig()
        return IveConfig(
            apiKey = p.getString("apiKey", "") ?: "",
            baseUrl = p.getString("baseUrl", d.baseUrl) ?: d.baseUrl,
            model = p.getString("model", d.model) ?: d.model,
            requireApproval = p.getBoolean("requireApproval", true),
            sttModel = p.getString("sttModel", d.sttModel) ?: d.sttModel,
            sttBaseUrl = p.getString("sttBaseUrl", "") ?: "",
            sttApiKey = p.getString("sttApiKey", "") ?: "",
            voiceAutoSend = p.getBoolean("voiceAutoSend", true),
            musicPackage = p.getString("musicPackage", "") ?: "",
        )
    }

    fun save(config: IveConfig) {
        memory = config
        prefs?.edit()
            ?.putString("apiKey", config.apiKey)
            ?.putString("baseUrl", config.baseUrl)
            ?.putString("model", config.model)
            ?.putBoolean("requireApproval", config.requireApproval)
            ?.putString("sttModel", config.sttModel)
            ?.putString("sttBaseUrl", config.sttBaseUrl)
            ?.putString("sttApiKey", config.sttApiKey)
            ?.putBoolean("voiceAutoSend", config.voiceAutoSend)
            ?.putString("musicPackage", config.musicPackage)
            ?.apply()
    }
}

/** Packages the user has allowed IVE to open and control. Asked once per app; revocable any time. */
class AppGrants(context: Context) {
    private val prefs = context.getSharedPreferences("ive_grants", Context.MODE_PRIVATE)

    fun all(): Set<String> = prefs.getStringSet("pkgs", emptySet()) ?: emptySet()
    fun isGranted(pkg: String): Boolean = pkg in all()
    fun grant(pkg: String) = prefs.edit().putStringSet("pkgs", all() + pkg).apply()
    fun revoke(pkg: String) = prefs.edit().putStringSet("pkgs", all() - pkg).apply()
}
