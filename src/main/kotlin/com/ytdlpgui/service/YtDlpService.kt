package com.ytdlpgui.service

import com.ytdlpgui.model.*
import kotlinx.coroutines.*
import kotlinx.serialization.json.*
import java.io.*
import java.net.HttpURLConnection
import java.net.URI
import java.nio.file.Files
import java.nio.file.StandardCopyOption

class YtDlpService {

    private val appDataDir: File by lazy {
        val dir = when {
            isWindows() -> File(System.getenv("LOCALAPPDATA") ?: System.getProperty("user.home"), "yt-dlp-gui")
            isMac() -> File(System.getProperty("user.home"), "Library/Application Support/yt-dlp-gui")
            else -> File(System.getProperty("user.home"), ".local/share/yt-dlp-gui")
        }
        dir.mkdirs()
        dir
    }

    private val ytDlpBinary: File by lazy { findBinary(if (isWindows()) "yt-dlp.exe" else "yt-dlp") }
    private val ffmpegBinary: File by lazy { findBinary(if (isWindows()) "ffmpeg.exe" else "ffmpeg") }

    private fun findBinary(name: String): File {
        // 1. Bundled with app (compose resources dir)
        val resourcesDir = System.getProperty("compose.application.resources.dir")
        if (resourcesDir != null) {
            val bundled = File(resourcesDir, name)
            if (bundled.exists()) return bundled
        }

        // 2. appResources dir (dev mode — check common locations)
        val devPaths = listOf(
            File("appResources", platformDir()),
            File(System.getProperty("user.dir"), "appResources/${platformDir()}")
        )
        for (dir in devPaths) {
            val f = File(dir, name)
            if (f.exists()) return f
        }

        // 3. App data directory (downloaded at runtime)
        val appData = File(appDataDir, name)
        if (appData.exists()) return appData

        // 4. Return app data path as default target
        return appData
    }

    private fun platformDir(): String = when {
        isWindows() -> "windows-x64"
        isMac() && isArm() -> "macos-arm64"
        isMac() -> "macos-x64"
        isLinux() && isArm() -> "linux-arm64"
        else -> "linux-x64"
    }

    fun isInstalled(): Boolean = ytDlpBinary.exists() && (isWindows() || ytDlpBinary.canExecute())
    fun isFfmpegAvailable(): Boolean {
        if (ffmpegBinary.exists()) return true
        return try {
            val p = ProcessBuilder(if (isWindows()) "where" else "which", "ffmpeg")
                .redirectErrorStream(true).start()
            p.waitFor() == 0
        } catch (_: Exception) { false }
    }

    fun ffmpegLocation(): String? {
        if (ffmpegBinary.exists()) return ffmpegBinary.parent
        return null
    }

    suspend fun getVersion(): String = withContext(Dispatchers.IO) {
        if (!isInstalled()) return@withContext ""
        try {
            val process = ProcessBuilder(ytDlpBinary.absolutePath, "--version")
                .redirectErrorStream(true).start()
            val output = process.inputStream.bufferedReader().readText().trim()
            process.waitFor()
            output
        } catch (_: Exception) { "" }
    }

    suspend fun getLatestVersion(): String = withContext(Dispatchers.IO) {
        try {
            val url = URI("https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest").toURL()
            val conn = url.openConnection() as HttpURLConnection
            conn.setRequestProperty("Accept", "application/vnd.github.v3+json")
            conn.connectTimeout = 10000
            conn.readTimeout = 10000
            val body = conn.inputStream.bufferedReader().readText()
            conn.disconnect()
            val json = Json { ignoreUnknownKeys = true }
            json.parseToJsonElement(body).jsonObject["tag_name"]?.jsonPrimitive?.content ?: ""
        } catch (_: Exception) { "" }
    }

    suspend fun checkStatus(): YtDlpInfo = withContext(Dispatchers.IO) {
        if (!isInstalled()) return@withContext YtDlpInfo(status = YtDlpStatus.NOT_INSTALLED)
        try {
            val current = getVersion()
            val latest = getLatestVersion()
            when {
                latest.isBlank() -> YtDlpInfo(status = YtDlpStatus.UP_TO_DATE, currentVersion = current)
                current != latest -> YtDlpInfo(
                    status = YtDlpStatus.UPDATE_AVAILABLE,
                    currentVersion = current, latestVersion = latest
                )
                else -> YtDlpInfo(status = YtDlpStatus.UP_TO_DATE, currentVersion = current, latestVersion = latest)
            }
        } catch (e: Exception) {
            YtDlpInfo(status = YtDlpStatus.ERROR, error = e.message ?: "Unknown error")
        }
    }

    suspend fun installOrUpdate(onProgress: (String) -> Unit = {}): Result<String> = withContext(Dispatchers.IO) {
        try {
            onProgress("Fetching release info...")
            val url = URI("https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest").toURL()
            val conn = url.openConnection() as HttpURLConnection
            conn.setRequestProperty("Accept", "application/vnd.github.v3+json")
            val body = conn.inputStream.bufferedReader().readText()
            conn.disconnect()

            val json = Json { ignoreUnknownKeys = true }
            val release = json.parseToJsonElement(body).jsonObject
            val assets = release["assets"]?.jsonArray ?: return@withContext Result.failure(Exception("No assets"))
            val tag = release["tag_name"]?.jsonPrimitive?.content ?: "unknown"

            val assetName = when {
                isWindows() -> "yt-dlp.exe"
                isMac() -> "yt-dlp_macos"
                isLinux() && isArm() -> "yt-dlp_linux_aarch64"
                else -> "yt-dlp_linux"
            }

            val asset = assets.firstOrNull { it.jsonObject["name"]?.jsonPrimitive?.content == assetName }
                ?: return@withContext Result.failure(Exception("No binary for this platform"))

            val downloadUrl = asset.jsonObject["browser_download_url"]?.jsonPrimitive?.content
                ?: return@withContext Result.failure(Exception("No download URL"))

            val dest = File(appDataDir, if (isWindows()) "yt-dlp.exe" else "yt-dlp")
            onProgress("Downloading yt-dlp $tag...")
            downloadFile(downloadUrl, dest)
            if (!isWindows()) dest.setExecutable(true)

            onProgress("Installed yt-dlp $tag")
            Result.success(tag)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    private fun downloadFile(urlStr: String, dest: File) {
        var currentUrl = urlStr
        var redirects = 0
        while (redirects < 10) {
            val conn = URI(currentUrl).toURL().openConnection() as HttpURLConnection
            conn.instanceFollowRedirects = false
            conn.connectTimeout = 30000
            conn.readTimeout = 60000
            val code = conn.responseCode
            if (code in 301..302 || code == 307 || code == 308) {
                currentUrl = conn.getHeaderField("Location")
                conn.disconnect()
                redirects++
                continue
            }
            conn.inputStream.use { input ->
                Files.copy(input, dest.toPath(), StandardCopyOption.REPLACE_EXISTING)
            }
            conn.disconnect()
            return
        }
    }

    fun buildCommand(job: DownloadJob, options: DownloadOptions): List<String> {
        val cmd = mutableListOf(ytDlpBinary.absolutePath)
        cmd.addAll(listOf("--newline", "--no-colors"))

        if (options.format.isVideo) {
            cmd.addAll(listOf("-f", options.videoQuality.formatArg))
            cmd.addAll(listOf("--merge-output-format", options.format.ext))
        } else {
            cmd.addAll(listOf("-x", "--audio-format", options.format.ext))
            if (options.audioQuality != AudioQuality.BEST) {
                cmd.addAll(listOf("--audio-quality", options.audioQuality.bitrateArg))
            }
        }

        if (options.subtitles) cmd.addAll(listOf("--write-subs", "--sub-langs", "all"))
        if (options.sponsorBlock) cmd.addAll(listOf("--sponsorblock-remove", "all"))
        if (!options.fullPlaylist) cmd.add("--no-playlist")

        cmd.addAll(listOf("-o", File(options.outputDir, "%(title)s.%(ext)s").absolutePath))

        ffmpegLocation()?.let { cmd.addAll(listOf("--ffmpeg-location", it)) }

        cmd.add(job.url)
        return cmd
    }

    data class ProgressUpdate(
        val title: String? = null,
        val progress: Float? = null,
        val speed: String? = null,
        val eta: String? = null,
        val state: DownloadState? = null,
        val error: String? = null,
        val filePath: String? = null
    )

    suspend fun executeDownload(
        job: DownloadJob,
        options: DownloadOptions,
        onUpdate: (ProgressUpdate) -> Unit
    ): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val cmd = buildCommand(job, options)
            val process = ProcessBuilder(cmd).redirectErrorStream(true).start()
            onUpdate(ProgressUpdate(state = DownloadState.FETCHING))

            process.inputStream.bufferedReader().forEachLine { line ->
                parseLine(line, onUpdate)
            }

            val exitCode = process.waitFor()
            if (exitCode == 0) {
                onUpdate(ProgressUpdate(state = DownloadState.FINISHED, progress = 1f))
                Result.success(Unit)
            } else {
                onUpdate(ProgressUpdate(state = DownloadState.ERROR, error = "yt-dlp exited with code $exitCode"))
                Result.failure(Exception("Exit code $exitCode"))
            }
        } catch (e: Exception) {
            onUpdate(ProgressUpdate(state = DownloadState.ERROR, error = e.message ?: "Unknown error"))
            Result.failure(e)
        }
    }

    private fun parseLine(line: String, onUpdate: (ProgressUpdate) -> Unit) {
        when {
            line.contains("[download]") && line.contains("%") -> {
                val progress = Regex("""(\d+\.?\d*)%""").find(line)?.groupValues?.get(1)?.toFloatOrNull()?.div(100f)
                val speed = Regex("""at\s+(\S+/s)""").find(line)?.groupValues?.get(1) ?: ""
                val eta = Regex("""ETA\s+(\S+)""").find(line)?.groupValues?.get(1) ?: ""
                onUpdate(ProgressUpdate(state = DownloadState.DOWNLOADING, progress = progress, speed = speed, eta = eta))
            }
            line.contains("[download] Destination:") -> {
                val path = line.substringAfter("[download] Destination:").trim()
                onUpdate(ProgressUpdate(title = File(path).nameWithoutExtension, filePath = path))
            }
            line.contains("[ExtractAudio]") || line.contains("[Merger]") ||
                    line.contains("[EmbedSubtitle]") || line.contains("[SponsorBlock]") ->
                onUpdate(ProgressUpdate(state = DownloadState.PROCESSING))
            line.contains("ERROR:") ->
                onUpdate(ProgressUpdate(state = DownloadState.ERROR, error = line.substringAfter("ERROR:").trim()))
            line.startsWith("[info]") && line.contains("Extracting URL") ->
                onUpdate(ProgressUpdate(state = DownloadState.FETCHING))
        }
    }

    fun cancelProcess(process: Process?) {
        process?.let {
            it.descendants().forEach { child -> child.destroyForcibly() }
            it.destroyForcibly()
        }
    }

    companion object {
        fun isWindows() = System.getProperty("os.name").lowercase().contains("win")
        fun isMac() = System.getProperty("os.name").lowercase().contains("mac")
        fun isLinux() = System.getProperty("os.name").lowercase().contains("linux")
        fun isArm() = System.getProperty("os.arch").lowercase().let {
            it.contains("aarch64") || it.contains("arm")
        }
    }
}
