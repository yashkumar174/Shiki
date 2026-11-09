package app.shiki.phone

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Typeface
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.PowerManager
import android.provider.Settings
import android.text.InputType
import android.view.Gravity
import android.view.View
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Switch
import android.widget.TextView
import org.json.JSONObject

/** Connecting to the PC once, then what's going on and a few switches. */
class MainActivity : Activity() {
    private lateinit var store: Store
    private lateinit var root: LinearLayout
    private lateinit var status: TextView
    private val listener: () -> Unit = { runOnUiThread { if (store.paired) status.text = LinkState.status } }
    @Volatile private var searching = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        store = Store(this)
        root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(24), dp(36), dp(24), dp(24))
        }
        setContentView(ScrollView(this).apply { addView(root) })
        askPermissions()
        render()
    }

    override fun onResume() {
        super.onResume()
        LinkState.listeners = LinkState.listeners + listener
        if (store.paired) { LinkService.start(this); render() }
    }

    override fun onPause() {
        LinkState.listeners = LinkState.listeners - listener
        super.onPause()
    }

    private fun render() {
        root.removeAllViews()
        root.addView(text("Shiki", 30f, bold = true))
        status = text("", 16f).also { it.setPadding(0, dp(8), 0, dp(16)) }
        if (store.paired) renderPaired() else renderConnect()
    }

    // ---- Connected ----

    private fun renderPaired() {
        status.text = LinkState.status.ifEmpty { "Connecting to ${store.pcName}…" }
        root.addView(status)
        root.addView(text("Share anything (photos, videos, PDFs) and pick Send to PC. Or, on the PC, just ask: \"get my last photo from my phone\". Everything arrives at full quality.", 15f))
        root.addView(Switch(this).apply {
            text = "Send new photos and videos by themselves (on Wi-Fi)"
            textSize = 15f
            isChecked = store.autoSend
            setPadding(0, dp(20), 0, dp(8))
            setOnCheckedChangeListener { _, on ->
                store.autoSend = on
                // From now on: older photos aren't sent all at once.
                if (on) store.autoSince = Media.newestAdded(this@MainActivity)
            }
        })
        root.addView(button("Send files to the PC…") {
            @Suppress("DEPRECATION")
            startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
                type = "*/*"
                addCategory(Intent.CATEGORY_OPENABLE)
                putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true)
            }, PICK)
        })
        val power = getSystemService(PowerManager::class.java)
        if (!power.isIgnoringBatteryOptimizations(packageName)) {
            root.addView(text("So the PC can reach your phone while it's in your pocket, let Shiki run in the background.", 14f).also { it.setPadding(0, dp(16), 0, 0) })
            root.addView(button("Let Shiki run in the background") {
                try {
                    startActivity(Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:$packageName")))
                } catch (_: Exception) {
                    startActivity(Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS))
                }
            })
        }
        root.addView(button("Disconnect from ${store.pcName}") {
            stopService(Intent(this, LinkService::class.java))
            store.forget()
            render()
        }.also { (it.layoutParams as LinearLayout.LayoutParams).topMargin = dp(28) })
    }

    @Deprecated("Activity results")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode != PICK || resultCode != RESULT_OK || data == null) return
        val uris = mutableListOf<Uri>()
        data.clipData?.let { clip -> for (i in 0 until clip.itemCount) uris.add(clip.getItemAt(i).uri) }
        if (uris.isEmpty()) data.data?.let { uris.add(it) }
        for (uri in uris) try { contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION) } catch (_: Exception) { }
        if (uris.isNotEmpty()) {
            LinkService.send(this, uris)
            status.text = "Sending ${uris.size} to ${store.pcName}…"
        }
    }

    // ---- Connecting ----

    private fun renderConnect() {
        root.addView(text("Sends your photos, videos and files to your PC at full quality, over your Wi-Fi. Open Shiki on the PC (it's the cat), and keep this phone on the same Wi-Fi.", 15f))
        status.text = "Looking for your PC…"
        root.addView(status)
        val found = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        root.addView(found)
        root.addView(button("Look again") { search(found) })
        root.addView(text("Not found? Type the address shown on the PC (Settings, Phone, Connect a phone).", 14f).also { it.setPadding(0, dp(24), 0, 0) })
        val address = EditText(this).apply {
            hint = "192.168.1.5"
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI
        }
        root.addView(address)
        root.addView(button("Connect") {
            val host = address.text.toString().trim().removePrefix("http://").substringBefore('/').substringBefore(':')
            if (host.isNotEmpty()) pair(Link.Pc("", host, host, Link.LINK_PORT, ""))
        })
        search(found)
    }

    private fun search(found: LinearLayout) {
        if (searching) return
        searching = true
        status.text = "Looking for your PC…"
        Thread {
            val pcs = try { Link.find(null, 3000) } catch (_: Exception) { emptyList() }
            runOnUiThread {
                searching = false
                if (isFinishing || store.paired) return@runOnUiThread
                found.removeAllViews()
                status.text = when (pcs.size) {
                    0 -> "No PC with Shiki found on this Wi-Fi yet."
                    1 -> "Found it. Tap it to connect:"
                    else -> "Tap your PC to connect:"
                }
                for (pc in pcs) found.addView(button(pc.name) { pair(pc) })
            }
        }.start()
    }

    private fun pair(pc: Link.Pc) {
        status.text = "Connecting to ${pc.name}…"
        Thread {
            try {
                Link.open(pc.host, pc.port, null, readTimeoutMs = 150_000).use { conn ->
                    if (pc.fingerprint.isNotEmpty() && pc.fingerprint != conn.fingerprint) {
                        runOnUiThread { status.text = "Something else answered for ${pc.name}. Try again." }
                        return@Thread
                    }
                    val nonce = Link.randomHex(16)
                    val code = Link.pairCode(conn.fingerprint, nonce)
                    runOnUiThread { showCode(code, pc.name) }
                    conn.send(JSONObject().put("op", "pair").put("name", deviceName()).put("nonce", nonce))
                    val reply = conn.read()
                    if (!reply.optBoolean("ok")) {
                        val why = reply.optString("error")
                        runOnUiThread { render(); status.text = if (why == "declined") "Not paired (Not now on the PC, or no answer)." else why }
                        return@Thread
                    }
                    store.pcId = reply.optString("pc")
                    store.pcName = reply.optString("name", pc.name)
                    store.fingerprint = conn.fingerprint
                    store.phoneId = reply.optString("phone")
                    store.key = reply.optString("key")
                    store.host = pc.host
                    store.port = pc.port
                    store.autoSince = Media.newestAdded(this)
                }
                runOnUiThread {
                    LinkState.set("Connected to ${store.pcName}", true)
                    LinkService.start(this)
                    render()
                }
            } catch (e: Exception) {
                runOnUiThread { status.text = "Couldn't connect to ${pc.name}: ${e.message ?: "no answer"}" }
            }
        }.start()
    }

    private fun showCode(code: String, pcName: String) {
        root.removeAllViews()
        root.addView(text("Shiki", 30f, bold = true))
        root.addView(text("Check $pcName shows this code, then press Pair on the PC:", 16f).also { it.setPadding(0, dp(16), 0, dp(12)) })
        root.addView(text(code, 44f, bold = true).also { it.gravity = Gravity.CENTER; it.letterSpacing = 0.08f })
        root.addView(text("If the codes are different, press Not now on the PC.", 14f).also { it.setPadding(0, dp(16), 0, 0) })
        status = text("", 15f)
        root.addView(status)
    }

    // ---- Small pieces ----

    private fun askPermissions() {
        val wanted = mutableListOf<String>()
        if (Build.VERSION.SDK_INT >= 33) {
            wanted += Manifest.permission.READ_MEDIA_IMAGES
            wanted += Manifest.permission.READ_MEDIA_VIDEO
            wanted += Manifest.permission.POST_NOTIFICATIONS
        } else {
            wanted += Manifest.permission.READ_EXTERNAL_STORAGE
        }
        wanted += Manifest.permission.ACCESS_MEDIA_LOCATION
        val missing = wanted.filter { checkSelfPermission(it) != PackageManager.PERMISSION_GRANTED }
        if (missing.isNotEmpty()) requestPermissions(missing.toTypedArray(), 1)
    }

    private fun deviceName(): String =
        Settings.Global.getString(contentResolver, "device_name")?.takeIf { it.isNotBlank() }
            ?: "${Build.MANUFACTURER.replaceFirstChar { it.uppercase() }} ${Build.MODEL}"

    private fun text(value: String, size: Float, bold: Boolean = false) = TextView(this).apply {
        text = value
        textSize = size
        if (bold) typeface = Typeface.DEFAULT_BOLD
        setLineSpacing(0f, 1.15f)
    }

    private fun button(label: String, click: () -> Unit) = Button(this).apply {
        text = label
        isAllCaps = false
        textSize = 16f
        setOnClickListener { click() }
        layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(8) }
    }

    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()

    companion object {
        private const val PICK = 7
    }
}
