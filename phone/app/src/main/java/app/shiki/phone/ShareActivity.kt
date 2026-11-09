package app.shiki.phone

import android.app.Activity
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.widget.Toast

/** Share → Send to PC: hands the files to the link and gets out of the way at once. */
class ShareActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val store = Store(this)
        if (!store.paired) {
            Toast.makeText(this, "Connect Shiki to your PC first", Toast.LENGTH_LONG).show()
            startActivity(Intent(this, MainActivity::class.java))
            finish()
            return
        }
        val uris = mutableListOf<Uri>()
        @Suppress("DEPRECATION")
        when (intent?.action) {
            Intent.ACTION_SEND -> intent.getParcelableExtra<Uri>(Intent.EXTRA_STREAM)?.let { uris.add(it) }
            Intent.ACTION_SEND_MULTIPLE -> intent.getParcelableArrayListExtra<Uri>(Intent.EXTRA_STREAM)?.let { uris.addAll(it) }
        }
        if (uris.isEmpty()) intent?.clipData?.let { clip -> for (i in 0 until clip.itemCount) clip.getItemAt(i).uri?.let { uris.add(it) } }
        if (uris.isEmpty()) {
            Toast.makeText(this, "Only files can be sent to the PC", Toast.LENGTH_SHORT).show()
        } else {
            LinkService.send(this, uris)
            Toast.makeText(this, if (uris.size == 1) "Sending to ${store.pcName}" else "Sending ${uris.size} to ${store.pcName}", Toast.LENGTH_SHORT).show()
        }
        finish()
    }
}
