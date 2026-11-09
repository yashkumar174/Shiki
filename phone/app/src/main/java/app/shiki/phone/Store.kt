package app.shiki.phone

import android.content.Context

/** What the phone remembers about its PC (in the app's own private storage). */
class Store(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences("shiki", Context.MODE_PRIVATE)

    var pcId: String? get() = prefs.getString("pcId", null); set(v) = prefs.edit().putString("pcId", v).apply()
    var pcName: String get() = prefs.getString("pcName", null) ?: "your PC"; set(v) = prefs.edit().putString("pcName", v).apply()
    /** The PC's certificate fingerprint (SHA-256): the link only talks to that PC. */
    var fingerprint: String? get() = prefs.getString("fp", null); set(v) = prefs.edit().putString("fp", v).apply()
    var phoneId: String? get() = prefs.getString("phoneId", null); set(v) = prefs.edit().putString("phoneId", v).apply()
    var key: String? get() = prefs.getString("key", null); set(v) = prefs.edit().putString("key", v).apply()
    /** Where the PC was last found on the network. */
    var host: String? get() = prefs.getString("host", null); set(v) = prefs.edit().putString("host", v).apply()
    var port: Int get() = prefs.getInt("port", Link.LINK_PORT); set(v) = prefs.edit().putInt("port", v).apply()
    /** New camera photos and videos go to the PC by themselves. */
    var autoSend: Boolean get() = prefs.getBoolean("autoSend", true); set(v) = prefs.edit().putBoolean("autoSend", v).apply()
    /** Camera photos and videos added after this (seconds) haven't been sent by themselves yet. */
    var autoSince: Long get() = prefs.getLong("autoSince", 0); set(v) = prefs.edit().putLong("autoSince", v).apply()

    val paired: Boolean get() = phoneId != null && key != null && fingerprint != null

    fun forget() {
        prefs.edit().remove("pcId").remove("pcName").remove("fp").remove("phoneId").remove("key").remove("host").remove("autoSince").apply()
    }
}

/** What the link is doing, for the app's screen and the notification. */
object LinkState {
    @Volatile var status: String = ""
    @Volatile var connected: Boolean = false
    @Volatile var listeners = listOf<() -> Unit>()

    fun set(text: String, isConnected: Boolean = connected) {
        status = text
        connected = isConnected
        listeners.forEach { it() }
    }
}
