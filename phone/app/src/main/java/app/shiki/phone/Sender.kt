package app.shiki.phone

import android.content.ContentValues
import android.content.Context
import android.net.Uri
import android.provider.MediaStore
import org.json.JSONObject
import java.io.IOException
import java.io.InputStream
import java.security.MessageDigest

/** Sends files to the PC (and fetches the ones it offers), byte for byte, carrying on after a drop. */
class Sender(private val context: Context, private val store: Store) {

    /** One request to the PC: finds it again on the network if it has moved. */
    fun request(json: JSONObject, readTimeoutMs: Int = 30_000, body: ((Link.Conn) -> Unit)? = null): JSONObject {
        val conn = connect(readTimeoutMs)
        conn.use {
            it.send(signed(json))
            body?.invoke(it)
            return it.read()
        }
    }

    fun connect(readTimeoutMs: Int = 30_000): Link.Conn {
        val pinned = store.fingerprint ?: throw IOException("Not paired")
        store.host?.let { host ->
            try { return Link.open(host, store.port, pinned, readTimeoutMs) } catch (e: IOException) {
                if (e.message?.contains("certificate") == true) throw e
            }
        }
        // Not where it was: ask the network where it is now.
        val pc = Link.find(store.pcId, 2500).firstOrNull() ?: throw IOException("Can't find ${store.pcName} on this Wi-Fi")
        store.host = pc.host
        store.port = pc.port
        return Link.open(pc.host, pc.port, pinned, readTimeoutMs)
    }

    private fun signed(json: JSONObject): JSONObject {
        if (json.optString("op") !in setOf("hello", "pair")) {
            json.put("phone", store.phoneId).put("key", store.key)
        }
        return json
    }

    /** Tells the PC how a batch is going (its total first, then that it's done). */
    fun report(job: String, total: Int? = null, done: Boolean = false, failed: Int = 0, note: String? = null) {
        val json = JSONObject().put("op", "job").put("job", job).put("done", done).put("failed", failed)
        total?.let { json.put("total", it) }
        note?.let { json.put("note", it) }
        try { request(json) } catch (_: Exception) { }
    }

    /** Sends one file; true once the PC has it, checked. Tries three times, carrying on each time. */
    fun upload(item: Item, job: String?, progress: (Long, Long) -> Unit = { _, _ -> }): Boolean {
        val (sha, size) = hash(item)
        for (attempt in 1..3) {
            try {
                val check = request(JSONObject().put("op", "check").put("sha", sha).put("size", size).put("name", item.name).also { if (job != null) it.put("job", job) })
                if (check.optBoolean("done")) return true
                val offset = check.optLong("have", 0).coerceIn(0, size)
                val put = JSONObject().put("op", "put").put("sha", sha).put("size", size).put("offset", offset)
                    .put("name", item.name).put("taken", item.taken)
                if (job != null) put.put("job", job)
                // The answer comes once the PC has read the file back and checked it: give big files time.
                val reply = request(put, readTimeoutMs = 120_000) { conn ->
                    open(item).use { input ->
                        skipFully(input, offset)
                        val buffer = ByteArray(256 * 1024)
                        var sent = offset
                        while (sent < size) {
                            val read = input.read(buffer, 0, minOf(buffer.size.toLong(), size - sent).toInt())
                            if (read < 0) throw IOException("The file got shorter")
                            conn.output.write(buffer, 0, read)
                            sent += read
                            progress(sent, size)
                        }
                        conn.output.flush()
                    }
                }
                if (reply.optBoolean("ok")) return true
                if (reply.optString("error") == "unpaired") throw Unpaired()
                // A checksum that didn't match starts again from the beginning.
            } catch (e: Unpaired) {
                throw e
            } catch (e: IOException) {
                if (attempt == 3) return false
                Thread.sleep(1500L * attempt)
            }
        }
        return false
    }

    /** Fetches a file the PC offered into Download/Shiki; true if it arrived whole and matches. */
    fun download(token: String, name: String, size: Long, sha: String): Boolean {
        val resolver = context.contentResolver
        val values = ContentValues().apply {
            put(MediaStore.MediaColumns.DISPLAY_NAME, name)
            put(MediaStore.MediaColumns.RELATIVE_PATH, "Download/Shiki")
            put(MediaStore.MediaColumns.IS_PENDING, 1)
        }
        val uri: Uri = resolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values) ?: return false
        try {
            val digest = MessageDigest.getInstance("SHA-256")
            val conn = connect(60_000)
            conn.use {
                it.send(signed(JSONObject().put("op", "get").put("file", token)))
                val head = it.read()
                if (!head.optBoolean("ok")) throw IOException(head.optString("error"))
                val total = head.optLong("size", size)
                resolver.openOutputStream(uri)!!.use { out ->
                    val buffer = ByteArray(256 * 1024)
                    var got = 0L
                    while (got < total) {
                        val read = it.input.read(buffer, 0, minOf(buffer.size.toLong(), total - got).toInt())
                        if (read < 0) throw IOException("The PC stopped sending")
                        out.write(buffer, 0, read)
                        digest.update(buffer, 0, read)
                        got += read
                    }
                }
            }
            val got = digest.digest().joinToString("") { b -> "%02x".format(b) }
            if (got != sha) throw IOException("The file didn't match")
            resolver.update(uri, ContentValues().apply { put(MediaStore.MediaColumns.IS_PENDING, 0) }, null, null)
            return true
        } catch (e: Exception) {
            try { resolver.delete(uri, null, null) } catch (_: Exception) { }
            return false
        }
    }

    private fun hash(item: Item): Pair<String, Long> {
        val digest = MessageDigest.getInstance("SHA-256")
        var size = 0L
        open(item).use { input ->
            val buffer = ByteArray(256 * 1024)
            while (true) {
                val read = input.read(buffer)
                if (read < 0) break
                digest.update(buffer, 0, read)
                size += read
            }
        }
        return digest.digest().joinToString("") { "%02x".format(it) } to size
    }

    /** The original (with its location kept) when Android allows it, otherwise the file as shared. */
    private fun open(item: Item): InputStream {
        val resolver = context.contentResolver
        return try {
            resolver.openInputStream(item.uri) ?: throw IOException("Can't open ${item.name}")
        } catch (e: Exception) {
            if (e is IOException && item.uri.query == null) throw e
            val plain = item.uri.buildUpon().clearQuery().build()
            resolver.openInputStream(plain) ?: throw IOException("Can't open ${item.name}")
        }
    }

    private fun skipFully(input: InputStream, count: Long) {
        var left = count
        val buffer = ByteArray(64 * 1024)
        while (left > 0) {
            val skipped = input.skip(left)
            if (skipped > 0) { left -= skipped; continue }
            val read = input.read(buffer, 0, minOf(buffer.size.toLong(), left).toInt())
            if (read < 0) throw IOException("The file got shorter")
            left -= read
        }
    }

    class Unpaired : IOException("This phone was removed on the PC")
}
