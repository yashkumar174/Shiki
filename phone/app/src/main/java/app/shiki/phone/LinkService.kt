package app.shiki.phone

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.net.Uri
import android.net.wifi.WifiManager
import android.os.IBinder
import android.os.PowerManager
import org.json.JSONObject
import java.util.concurrent.Executors

/**
 * Keeps Shiki connected to the PC while the phone is on Wi-Fi: asks the PC "anything for me?"
 * (so the PC can ask for photos), sends what's shared, and sends new camera photos and videos by
 * themselves. Runs as a foreground service, with a quiet notification.
 */
class LinkService : Service() {
    companion object {
        const val ACTION_SEND = "app.shiki.phone.SEND"
        private const val CHANNEL = "link"
        private const val NOTE = 1

        fun start(context: Context) {
            context.startForegroundService(Intent(context, LinkService::class.java))
        }

        /** Sends files (shared to Shiki, or picked in the app) to the PC. */
        fun send(context: Context, uris: List<Uri>) {
            val intent = Intent(context, LinkService::class.java).setAction(ACTION_SEND)
                .putParcelableArrayListExtra("uris", ArrayList(uris))
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            // The permission to read them comes along with the clip.
            if (uris.isNotEmpty()) {
                val clip = android.content.ClipData.newRawUri("files", uris[0])
                uris.drop(1).forEach { clip.addItem(android.content.ClipData.Item(it)) }
                intent.clipData = clip
            }
            context.startForegroundService(intent)
        }
    }

    private lateinit var store: Store
    private lateinit var sender: Sender
    private val jobs = Executors.newSingleThreadExecutor()
    @Volatile private var running = true
    @Volatile private var busy = 0
    private var poll: Thread? = null
    private var lastAutoCheck = 0L

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        store = Store(this)
        sender = Sender(this, store)
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(NotificationChannel(CHANNEL, "Connection to your PC", NotificationManager.IMPORTANCE_LOW))
        startForeground(NOTE, note("Connecting to ${store.pcName}…"), ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
        poll = Thread(::pollLoop, "shiki-link").also { it.start() }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_SEND) {
            @Suppress("DEPRECATION")
            val uris = intent.getParcelableArrayListExtra<Uri>("uris") ?: arrayListOf()
            if (uris.isNotEmpty()) jobs.submit { sendShared(uris) }
        }
        return START_STICKY
    }

    override fun onDestroy() {
        running = false
        poll?.interrupt()
        jobs.shutdownNow()
        LinkState.set("Not connected", false)
        super.onDestroy()
    }

    // ---- Asking the PC "anything for me?" ----

    private fun pollLoop() {
        var backoff = 2000L
        while (running) {
            if (!store.paired) { stopSelf(); return }
            if (!onWifi()) {
                show("Waiting for Wi-Fi", false)
                sleep(20_000)
                continue
            }
            try {
                val reply = sender.request(JSONObject().put("op", "wait"), readTimeoutMs = 40_000)
                if (!reply.optBoolean("ok") && reply.optString("error") == "unpaired") {
                    store.forget()
                    show("Removed on the PC. Open Shiki to connect again.", false)
                    stopSelf()
                    return
                }
                if (busy == 0) show("Connected to ${store.pcName}", true)
                when (reply.optString("cmd")) {
                    "send-recent" -> {
                        val job = reply.optString("job")
                        val count = reply.optInt("count", 1)
                        val kind = reply.optString("kind", "photo")
                        val today = reply.optBoolean("today")
                        jobs.submit { sendRecent(job, count, kind, today) }
                    }
                    "get" -> {
                        val job = reply.optString("job")
                        val files = reply.optJSONArray("files")
                        jobs.submit { fetch(job, files) }
                    }
                }
                backoff = 2000L
                maybeSendNew()
            } catch (e: Exception) {
                if (!running) return
                show("Looking for ${store.pcName}…", false)
                // Next time, look for it on the network again.
                store.host = null
                sleep(backoff)
                backoff = minOf(backoff * 2, 30_000L)
            }
        }
    }

    // ---- The work ----

    private fun sendShared(uris: List<Uri>) {
        val items = uris.mapNotNull { Media.shared(this, it) }
        sendBatch("share-" + Link.randomHex(5), items, "Sending")
    }

    private fun sendRecent(job: String, count: Int, kind: String, today: Boolean) {
        val items = Media.recent(this, count, kind, today)
        if (items.isEmpty()) {
            val allowed = checkSelfPermission(android.Manifest.permission.READ_MEDIA_IMAGES) == android.content.pm.PackageManager.PERMISSION_GRANTED
            sender.report(job, total = 0, done = true,
                note = if (allowed) "There's nothing like that on the phone${if (today) " from today" else ""}." else "Shiki on the phone isn't allowed to see photos and videos yet: open Shiki on the phone and allow it.")
            return
        }
        sendBatch(job, items, "Sending")
    }

    /** New camera photos and videos, about once a minute, while on this Wi-Fi. */
    private fun maybeSendNew() {
        if (!store.autoSend || busy > 0 || System.currentTimeMillis() - lastAutoCheck < 60_000) return
        lastAutoCheck = System.currentTimeMillis()
        if (store.autoSince == 0L) { store.autoSince = Media.newestAdded(this); return }
        val items = Media.newSince(this, store.autoSince)
        if (items.isEmpty()) return
        busy++
        jobs.submit {
            try {
                sendBatch("auto-" + Link.randomHex(5), items, "Sending new", countBusy = false)
                // The ones that went (or failed three times) aren't tried again by themselves.
                store.autoSince = maxOf(store.autoSince, items.maxOf { it.added })
            } finally { busy-- }
        }
    }

    private fun sendBatch(job: String, items: List<Item>, verb: String, countBusy: Boolean = true) {
        if (countBusy) busy++
        val locks = hold()
        try {
            sender.report(job, total = items.size)
            var failed = 0
            items.forEachIndexed { i, item ->
                val label = if (items.size == 1) item.name else "${i + 1} of ${items.size}"
                show("$verb $label to ${store.pcName}…", true)
                if (!sender.upload(item, job) { sent, size -> if (size > 20_000_000) show("$verb $label: ${sent * 100 / size}%", true) }) failed++
            }
            sender.report(job, done = true, failed = failed, note = if (failed > 0) "$failed didn't go through" else null)
            show(if (failed == 0) "Sent ${items.size} to ${store.pcName}" else "Sent ${items.size - failed} of ${items.size} to ${store.pcName}", true)
        } catch (e: Sender.Unpaired) {
            store.forget()
            show("Removed on the PC. Open Shiki to connect again.", false)
        } catch (e: Exception) {
            show("Couldn't send: ${e.message}", false)
        } finally {
            release(locks)
            if (countBusy) busy--
        }
    }

    private fun fetch(job: String, files: org.json.JSONArray?) {
        if (files == null) return
        busy++
        val locks = hold()
        try {
            sender.report(job, total = files.length())
            var failed = 0
            for (i in 0 until files.length()) {
                val f = files.getJSONObject(i)
                show("Getting ${f.optString("name")} from ${store.pcName}…", true)
                if (!sender.download(f.optString("token"), f.optString("name"), f.optLong("size"), f.optString("sha"))) failed++
            }
            sender.report(job, done = true, failed = failed, note = if (failed > 0) "the phone couldn't save them" else null)
            show("Got ${files.length() - failed} from ${store.pcName} (Download/Shiki)", true)
        } finally {
            release(locks)
            busy--
        }
    }

    // ---- Small pieces ----

    /** Keeps the Wi-Fi fast and the phone awake while files move. */
    private fun hold(): Pair<WifiManager.WifiLock, PowerManager.WakeLock> {
        val wifi = (applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager)
            .createWifiLock(WifiManager.WIFI_MODE_FULL_HIGH_PERF, "shiki:send")
        val wake = getSystemService(PowerManager::class.java).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "shiki:send")
        wifi.acquire()
        wake.acquire(60 * 60 * 1000L)
        return wifi to wake
    }

    private fun release(locks: Pair<WifiManager.WifiLock, PowerManager.WakeLock>) {
        try { if (locks.first.isHeld) locks.first.release() } catch (_: Exception) { }
        try { if (locks.second.isHeld) locks.second.release() } catch (_: Exception) { }
    }

    private fun onWifi(): Boolean {
        val cm = getSystemService(ConnectivityManager::class.java)
        @Suppress("DEPRECATION")
        return cm.allNetworks.any {
            val caps = cm.getNetworkCapabilities(it)
            caps != null && (caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) || caps.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET))
        }
    }

    private fun show(text: String, connected: Boolean) {
        LinkState.set(text, connected)
        getSystemService(NotificationManager::class.java).notify(NOTE, note(text))
    }

    private fun note(text: String): Notification {
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
        return Notification.Builder(this, CHANNEL)
            .setSmallIcon(R.drawable.ic_cat)
            .setContentTitle("Shiki")
            .setContentText(text)
            .setContentIntent(open)
            .setOngoing(true)
            .build()
    }

    private fun sleep(ms: Long) {
        try { Thread.sleep(ms) } catch (_: InterruptedException) { }
    }
}
