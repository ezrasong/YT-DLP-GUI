package com.ytdlpgui.model

enum class DownloadState(val label: String) {
    QUEUED("Queued"),
    FETCHING("Fetching info"),
    DOWNLOADING("Downloading"),
    PROCESSING("Processing"),
    FINISHED("Finished"),
    ERROR("Error"),
    CANCELLED("Cancelled")
}

enum class FormatOption(val label: String, val isVideo: Boolean, val ext: String) {
    VIDEO_MP4("Video (MP4)", true, "mp4"),
    VIDEO_MKV("Video (MKV)", true, "mkv"),
    VIDEO_WEBM("Video (WEBM)", true, "webm"),
    AUDIO_MP3("Audio (MP3)", false, "mp3"),
    AUDIO_M4A("Audio (M4A)", false, "m4a"),
    AUDIO_FLAC("Audio (FLAC)", false, "flac"),
    AUDIO_WAV("Audio (WAV)", false, "wav"),
    AUDIO_OPUS("Audio (OPUS)", false, "opus")
}

enum class VideoQuality(val label: String, val formatArg: String) {
    BEST("Best", "bestvideo+bestaudio/best"),
    Q4K("4K (2160p)", "bestvideo[height<=2160]+bestaudio/best[height<=2160]"),
    Q1440("1440p", "bestvideo[height<=1440]+bestaudio/best[height<=1440]"),
    Q1080("1080p", "bestvideo[height<=1080]+bestaudio/best[height<=1080]"),
    Q720("720p", "bestvideo[height<=720]+bestaudio/best[height<=720]"),
    Q480("480p", "bestvideo[height<=480]+bestaudio/best[height<=480]"),
    Q360("360p", "bestvideo[height<=360]+bestaudio/best[height<=360]")
}

enum class AudioQuality(val label: String, val bitrateArg: String) {
    BEST("Best", "0"),
    Q320("320 kbps", "320"),
    Q256("256 kbps", "256"),
    Q192("192 kbps", "192"),
    Q128("128 kbps", "128")
}

data class DownloadJob(
    val id: String = java.util.UUID.randomUUID().toString(),
    val url: String,
    val title: String = "Fetching info...",
    val state: DownloadState = DownloadState.QUEUED,
    val progress: Float = 0f,
    val speed: String = "",
    val eta: String = "",
    val error: String = "",
    val filePath: String = ""
)

data class DownloadOptions(
    val format: FormatOption = FormatOption.VIDEO_MP4,
    val videoQuality: VideoQuality = VideoQuality.BEST,
    val audioQuality: AudioQuality = AudioQuality.BEST,
    val subtitles: Boolean = false,
    val sponsorBlock: Boolean = false,
    val fullPlaylist: Boolean = false,
    val outputDir: String = defaultDownloadDir()
)

enum class YtDlpStatus {
    NOT_INSTALLED,
    CHECKING,
    UP_TO_DATE,
    UPDATE_AVAILABLE,
    INSTALLING,
    UPDATING,
    ERROR
}

data class YtDlpInfo(
    val status: YtDlpStatus = YtDlpStatus.CHECKING,
    val currentVersion: String = "",
    val latestVersion: String = "",
    val error: String = ""
)

fun defaultDownloadDir(): String {
    val home = System.getProperty("user.home")
    val downloads = java.io.File(home, "Downloads")
    return if (downloads.exists()) downloads.absolutePath else home
}
