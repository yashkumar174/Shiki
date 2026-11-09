package app.shiki.phone

import org.json.JSONObject
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.ByteArrayOutputStream
import java.io.Closeable
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.Socket
import java.net.SocketTimeoutException
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.cert.X509Certificate
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocket
import javax.net.ssl.X509TrustManager

/**
 * Talking to Shiki on the PC: find it with a broadcast on the Wi-Fi, then one TLS connection per
 * request. Each request is a line of JSON (and, for a file, its bytes after it); the answer is a
 * line of JSON (and, for a file, its bytes). The PC is trusted by its certificate's fingerprint,
 * remembered at pairing, never by a certificate authority.
 */
object Link {
    const val FIND_PORT = 47811
    const val LINK_PORT = 47812

    data class Pc(val id: String, val name: String, val host: String, val port: Int, val fingerprint: String)

    /** PCs running Shiki on this network (one PC by its id, or any). */
    fun find(wanted: String? = null, waitMs: Int = 2500): List<Pc> {
        val found = linkedMapOf<String, Pc>()
        DatagramSocket().use { socket ->
            socket.broadcast = true
            socket.soTimeout = 300
            val message = "SHIKI-FIND ${wanted ?: "*"}".toByteArray()
            val targets = broadcastAddresses()
            val deadline = System.currentTimeMillis() + waitMs
            var nextSend = 0L
            val buffer = ByteArray(2048)
            while (System.currentTimeMillis() < deadline) {
                if (System.currentTimeMillis() >= nextSend) {
                    for (target in targets) {
                        try { socket.send(DatagramPacket(message, message.size, target, FIND_PORT)) } catch (_: IOException) { }
                    }
                    nextSend = System.currentTimeMillis() + 800
                }
                try {
                    val packet = DatagramPacket(buffer, buffer.size)
                    socket.receive(packet)
                    val json = JSONObject(String(packet.data, 0, packet.length))
                    val id = json.optString("id")
                    if (id.isEmpty() || (wanted != null && id != wanted)) continue
                    found[id] = Pc(id, json.optString("name", "PC"), packet.address.hostAddress ?: continue,
                        json.optInt("port", LINK_PORT), json.optString("fp"))
                    if (wanted != null) break
                } catch (_: SocketTimeoutException) {
                } catch (_: Exception) {
                }
            }
        }
        return found.values.toList()
    }

    private fun broadcastAddresses(): List<InetAddress> {
        val list = mutableListOf<InetAddress>(InetAddress.getByName("255.255.255.255"))
        try {
            for (net in NetworkInterface.getNetworkInterfaces()) {
                if (!net.isUp || net.isLoopback) continue
                for (address in net.interfaceAddresses) {
                    if (address.address is Inet4Address) address.broadcast?.let { list.add(it) }
                }
            }
        } catch (_: Exception) { }
        return list.distinct()
    }

    /** A connection to the PC. With a pinned fingerprint, a different certificate is refused. */
    class Conn(private val socket: SSLSocket) : Closeable {
        val input: InputStream = BufferedInputStream(socket.inputStream, 256 * 1024)
        val output: OutputStream = BufferedOutputStream(socket.outputStream, 256 * 1024)
        val fingerprint: String = sha256Hex(socket.session.peerCertificates[0].encoded)

        fun send(json: JSONObject) {
            output.write((json.toString() + "\n").toByteArray())
            output.flush()
        }

        fun read(): JSONObject {
            val bytes = ByteArrayOutputStream()
            while (true) {
                val b = input.read()
                if (b < 0) throw IOException("The PC closed the connection")
                if (b == '\n'.code) break
                if (b != '\r'.code) bytes.write(b)
                if (bytes.size() > 1_000_000) throw IOException("Answer too long")
            }
            return JSONObject(bytes.toString("UTF-8"))
        }

        fun timeout(ms: Int) { socket.soTimeout = ms }

        override fun close() { try { socket.close() } catch (_: Exception) { } }
    }

    fun open(host: String, port: Int, pinned: String?, readTimeoutMs: Int = 30_000): Conn {
        val raw = Socket()
        raw.connect(InetSocketAddress(host, port), 4000)
        raw.soTimeout = readTimeoutMs
        raw.tcpNoDelay = true
        val tls = SSLContext.getInstance("TLS")
        tls.init(null, arrayOf(AnyCertificate), SecureRandom())
        val socket = tls.socketFactory.createSocket(raw, host, port, true) as SSLSocket
        socket.startHandshake()
        val conn = Conn(socket)
        if (pinned != null && conn.fingerprint != pinned) {
            conn.close()
            throw IOException("That isn't your PC (its certificate changed)")
        }
        return conn
    }

    /** Six digits from the PC's certificate and the phone's random number, as the PC shows them. */
    fun pairCode(fingerprint: String, nonce: String): String {
        val hash = MessageDigest.getInstance("SHA-256").digest("$fingerprint:$nonce".toByteArray())
        // The first four bytes as a little-endian unsigned number, as the PC reads them.
        val n = (hash[0].toLong() and 0xff) or ((hash[1].toLong() and 0xff) shl 8) or
            ((hash[2].toLong() and 0xff) shl 16) or ((hash[3].toLong() and 0xff) shl 24)
        val digits = String.format(java.util.Locale.ROOT, "%06d", n % 1_000_000)
        return digits.substring(0, 3) + " " + digits.substring(3)
    }

    fun sha256Hex(bytes: ByteArray): String =
        MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) }

    fun randomHex(bytes: Int): String {
        val b = ByteArray(bytes)
        SecureRandom().nextBytes(b)
        return b.joinToString("") { "%02x".format(it) }
    }

    /** The check happens after the handshake: the certificate's fingerprint against the one remembered. */
    private object AnyCertificate : X509TrustManager {
        override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) {}
        override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {}
        override fun getAcceptedIssuers(): Array<X509Certificate> = arrayOf()
    }
}
