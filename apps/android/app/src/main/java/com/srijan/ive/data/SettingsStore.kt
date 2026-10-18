package com.srijan.ive.data

import android.content.Context
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey

data class IveConfig(
    val apiKey: String = "",
    val baseUrl: String = "https://codecraftapi.com/v1",
    val model: String = "claude-opus-5-5",
    val requireApproval: Boolean = true,
) {
    val ready: Boolean get() = apiKey.isNotBlank()
}

/** API key lives in Keystore-backed EncryptedSharedPreferences; if that is unavailable it stays in memory only. */
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
        )
    }

    fun save(config: IveConfig) {
        memory = config
        prefs?.edit()
            ?.putString("apiKey", config.apiKey)
            ?.putString("baseUrl", config.baseUrl)
            ?.putString("model", config.model)
            ?.putBoolean("requireApproval", config.requireApproval)
            ?.apply()
    }
}
