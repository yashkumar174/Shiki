package app.shiki.phone

import android.Manifest
import android.content.ContentUris
import android.content.Context
import android.content.pm.PackageManager
import android.net.Uri
import android.provider.MediaStore
import android.provider.OpenableColumns
import java.util.Calendar

/** A photo, video or file on the phone, ready to be sent as it is. */
data class Item(val uri: Uri, val name: String, val size: Long, val taken: Long, val added: Long = 0)

/** The phone's photos and videos (MediaStore), always read as the original file. */
object Media {

    /** The newest camera photos, videos or screenshots, newest first. */
    fun recent(context: Context, count: Int, kind: String, today: Boolean): List<Item> {
        val since = if (today) startOfToday() / 1000 else 0
        val items = when (kind) {
            "video" -> query(context, video = true, where = Where.Camera, sinceSeconds = since, limit = count)
                .ifEmpty { query(context, video = true, where = Where.Anywhere, sinceSeconds = since, limit = count) }
            "screenshot" -> query(context, video = false, where = Where.Screenshots, sinceSeconds = since, limit = count)
            "any" -> (query(context, false, Where.Camera, since, count) + query(context, true, Where.Camera, since, count))
                .sortedByDescending { it.added }.take(count)
            else -> query(context, video = false, where = Where.Camera, sinceSeconds = since, limit = count)
                .ifEmpty { query(context, video = false, where = Where.NotScreenshots, sinceSeconds = since, limit = count) }
        }
        return items
    }

    /** Camera photos and videos added after a moment (seconds), oldest first: the ones to send by themselves. */
    fun newSince(context: Context, sinceSeconds: Long): List<Item> =
        (query(context, false, Where.Camera, sinceSeconds, 50, newerOnly = true) +
            query(context, true, Where.Camera, sinceSeconds, 50, newerOnly = true))
            .sortedBy { it.added }

    /** The newest camera photo or video's time (seconds), so sending by itself starts from now. */
    fun newestAdded(context: Context): Long =
        (query(context, false, Where.Camera, 0, 1) + query(context, true, Where.Camera, 0, 1)).maxOfOrNull { it.added }
            ?: (System.currentTimeMillis() / 1000)

    /** Something shared to Shiki: its name and size, as the app that shared it says. */
    fun shared(context: Context, uri: Uri): Item? {
        var name = uri.lastPathSegment ?: "file"
        var size = -1L
        try {
            context.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)?.use { c ->
                if (c.moveToFirst()) {
                    c.getString(0)?.let { name = it }
                    if (!c.isNull(1)) size = c.getLong(1)
                }
            }
        } catch (_: Exception) { }
        // A photo shared from the gallery: its original, with the place it was taken kept.
        return Item(original(context, uri), name, size, 0)
    }

    private enum class Where { Camera, Screenshots, NotScreenshots, Anywhere }

    private fun query(context: Context, video: Boolean, where: Where, sinceSeconds: Long, limit: Int, newerOnly: Boolean = false): List<Item> {
        val collection = if (video) MediaStore.Video.Media.getContentUri(MediaStore.VOLUME_EXTERNAL)
        else MediaStore.Images.Media.getContentUri(MediaStore.VOLUME_EXTERNAL)
        val columns = arrayOf(
            MediaStore.MediaColumns._ID, MediaStore.MediaColumns.DISPLAY_NAME, MediaStore.MediaColumns.SIZE,
            MediaStore.MediaColumns.DATE_TAKEN, MediaStore.MediaColumns.DATE_ADDED,
        )
        val clauses = mutableListOf("${MediaStore.MediaColumns.IS_PENDING} = 0")
        val args = mutableListOf<String>()
        when (where) {
            Where.Camera -> { clauses.add("${MediaStore.MediaColumns.RELATIVE_PATH} LIKE ?"); args.add("DCIM/%"); clauses.add("${MediaStore.MediaColumns.RELATIVE_PATH} NOT LIKE ?"); args.add("%Screenshot%") }
            Where.Screenshots -> { clauses.add("${MediaStore.MediaColumns.RELATIVE_PATH} LIKE ?"); args.add("%Screenshot%") }
            Where.NotScreenshots -> { clauses.add("${MediaStore.MediaColumns.RELATIVE_PATH} NOT LIKE ?"); args.add("%Screenshot%") }
            Where.Anywhere -> { }
        }
        if (sinceSeconds > 0) {
            clauses.add("${MediaStore.MediaColumns.DATE_ADDED} ${if (newerOnly) ">" else ">="} ?")
            args.add(sinceSeconds.toString())
        }
        val list = mutableListOf<Item>()
        try {
            context.contentResolver.query(collection, columns, clauses.joinToString(" AND "), args.toTypedArray(),
                "${MediaStore.MediaColumns.DATE_ADDED} DESC")?.use { c ->
                while (c.moveToNext() && list.size < limit) {
                    val uri = ContentUris.withAppendedId(collection, c.getLong(0))
                    val added = c.getLong(4)
                    val taken = if (!c.isNull(3) && c.getLong(3) > 0) c.getLong(3) else added * 1000
                    list.add(Item(original(context, uri), c.getString(1) ?: "photo", c.getLong(2), taken, added))
                }
            }
        } catch (_: SecurityException) {
            // Photos and videos not allowed yet.
        }
        return list
    }

    /** The file as it was taken: Android otherwise strips the location from a photo it hands over. */
    private fun original(context: Context, uri: Uri): Uri {
        if (context.checkSelfPermission(Manifest.permission.ACCESS_MEDIA_LOCATION) != PackageManager.PERMISSION_GRANTED) return uri
        if (uri.authority != MediaStore.AUTHORITY) return uri
        return try { MediaStore.setRequireOriginal(uri) } catch (_: Exception) { uri }
    }

    private fun startOfToday(): Long = Calendar.getInstance().apply {
        set(Calendar.HOUR_OF_DAY, 0); set(Calendar.MINUTE, 0); set(Calendar.SECOND, 0); set(Calendar.MILLISECOND, 0)
    }.timeInMillis
}
