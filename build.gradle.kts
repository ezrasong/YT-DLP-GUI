import org.jetbrains.compose.desktop.application.dsl.TargetFormat
import java.net.HttpURLConnection
import java.net.URI
import java.util.zip.ZipInputStream

plugins {
    kotlin("jvm") version "2.0.21"
    id("org.jetbrains.compose") version "1.7.3"
    id("org.jetbrains.kotlin.plugin.compose") version "2.0.21"
    kotlin("plugin.serialization") version "2.0.21"
}

group = "com.ytdlpgui"
version = "2.0.0"

repositories {
    mavenCentral()
    maven("https://maven.pkg.jetbrains.space/public/p/compose/dev")
    google()
}

dependencies {
    implementation(compose.desktop.currentOs)
    implementation(compose.material3)
    implementation(compose.materialIconsExtended)
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-core:1.9.0")
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.7.3")
}

val osName: String = System.getProperty("os.name").lowercase()
val osArch: String = System.getProperty("os.arch").lowercase()
val isWin = osName.contains("win")
val isMac = osName.contains("mac")
val isLinux = osName.contains("linux")
val isArm = osArch.contains("aarch64") || osArch.contains("arm")
val platform = when {
    isWin -> "windows-x64"
    isMac && isArm -> "macos-arm64"
    isMac -> "macos-x64"
    isLinux && isArm -> "linux-arm64"
    else -> "linux-x64"
}

val appResourcesDir = layout.projectDirectory.dir("appResources")

fun fetchUrl(url: String, dest: File) {
    var currentUrl = url
    var redirects = 0
    while (redirects < 10) {
        val conn = URI(currentUrl).toURL().openConnection() as HttpURLConnection
        conn.instanceFollowRedirects = false
        conn.connectTimeout = 30000
        conn.readTimeout = 120000
        conn.setRequestProperty("User-Agent", "yt-dlp-gui-build")
        val code = conn.responseCode
        if (code in 301..302 || code == 307 || code == 308) {
            currentUrl = conn.getHeaderField("Location")
            conn.disconnect()
            redirects++
            continue
        }
        conn.inputStream.use { input -> dest.outputStream().use { output -> input.copyTo(output) } }
        conn.disconnect()
        return
    }
    throw RuntimeException("Too many redirects for $url")
}

tasks.register("downloadBinaries") {
    group = "setup"
    description = "Downloads yt-dlp and ffmpeg binaries for the current platform"

    doLast {
        val outputDir = appResourcesDir.dir(platform).asFile
        outputDir.mkdirs()

        // yt-dlp
        val ytdlpName = if (isWin) "yt-dlp.exe" else "yt-dlp"
        val ytdlpDest = File(outputDir, ytdlpName)
        if (!ytdlpDest.exists()) {
            val asset = when {
                isWin -> "yt-dlp.exe"
                isMac -> "yt-dlp_macos"
                isLinux && isArm -> "yt-dlp_linux_aarch64"
                else -> "yt-dlp_linux"
            }
            println("Downloading yt-dlp ($asset)...")
            fetchUrl("https://github.com/yt-dlp/yt-dlp/releases/latest/download/$asset", ytdlpDest)
            if (!isWin) ytdlpDest.setExecutable(true)
            println("  -> ${ytdlpDest.absolutePath}")
        }

        // ffmpeg
        val ffmpegName = if (isWin) "ffmpeg.exe" else "ffmpeg"
        val ffmpegDest = File(outputDir, ffmpegName)
        if (!ffmpegDest.exists()) {
            when {
                isWin -> {
                    println("Downloading ffmpeg for Windows...")
                    val zipFile = File(outputDir, "ffmpeg.zip")
                    fetchUrl(
                        "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip",
                        zipFile
                    )
                    ZipInputStream(zipFile.inputStream().buffered()).use { zis ->
                        var entry = zis.nextEntry
                        while (entry != null) {
                            if (!entry.isDirectory && entry.name.endsWith("bin/ffmpeg.exe")) {
                                ffmpegDest.outputStream().use { out -> zis.copyTo(out) }
                                break
                            }
                            entry = zis.nextEntry
                        }
                    }
                    zipFile.delete()
                    println("  -> ${ffmpegDest.absolutePath}")
                }
                isLinux -> {
                    println("Downloading ffmpeg for Linux...")
                    val tarFile = File(outputDir, "ffmpeg.tar.xz")
                    val archiveName = if (isArm) "ffmpeg-master-latest-linuxarm64-gpl.tar.xz"
                    else "ffmpeg-master-latest-linux64-gpl.tar.xz"
                    fetchUrl(
                        "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/$archiveName",
                        tarFile
                    )
                    exec {
                        workingDir = outputDir
                        commandLine("tar", "-xf", tarFile.name, "--wildcards", "*/bin/ffmpeg", "--strip-components=2")
                    }
                    tarFile.delete()
                    ffmpegDest.setExecutable(true)
                    println("  -> ${ffmpegDest.absolutePath}")
                }
                isMac -> {
                    println("macOS: ffmpeg not bundled. Install via: brew install ffmpeg")
                }
            }
        }
    }
}

compose.desktop {
    application {
        mainClass = "com.ytdlpgui.MainKt"

        nativeDistributions {
            targetFormats(TargetFormat.Dmg, TargetFormat.Msi, TargetFormat.Deb)
            packageName = "YT-DLP GUI"
            packageVersion = "2.0.0"
            description = "A modern yt-dlp desktop GUI"
            vendor = "YT-DLP GUI"
            appResourcesRootDir.set(appResourcesDir)

            windows {
                menuGroup = "YT-DLP GUI"
                upgradeUuid = "e4c8b3a1-5d2f-4e6a-9b7c-1a2d3e4f5678"
            }

            macOS {
                bundleID = "com.ytdlpgui.app"
            }

            linux {
                packageName = "yt-dlp-gui"
            }
        }
    }
}
