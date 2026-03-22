package com.ytdlpgui.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.ytdlpgui.model.*
import com.ytdlpgui.service.DownloadManager
import com.ytdlpgui.theme.AppColors
import java.awt.Toolkit
import java.awt.datatransfer.DataFlavor

@Composable
fun DownloadScreen(downloadManager: DownloadManager) {
    val jobs = downloadManager.jobs
    val options = downloadManager.options
    val ytDlpInfo = downloadManager.ytDlpInfo

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(horizontal = 32.dp)
    ) {
        // === Fixed top section (does not scroll) ===
        Spacer(Modifier.height(24.dp))
        Header(ytDlpInfo, downloadManager)
        Spacer(Modifier.height(16.dp))
        UrlInputCard(downloadManager)
        Spacer(Modifier.height(16.dp))
        OptionsCard(options) { downloadManager.updateOptions(it) }
        Spacer(Modifier.height(16.dp))
        QueueHeader(jobs, downloadManager)
        Spacer(Modifier.height(12.dp))

        // === Queue section fills remaining space and scrolls ===
        Box(modifier = Modifier.weight(1f).fillMaxWidth()) {
            if (jobs.isEmpty()) {
                Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center
                ) {
                    Text(
                        "No downloads yet. Paste a URL and tap Add.",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.6f)
                    )
                }
            } else {
                val queueScroll = rememberScrollState()
                Column(
                    modifier = Modifier.fillMaxSize().verticalScroll(queueScroll),
                    verticalArrangement = Arrangement.spacedBy(12.dp)
                ) {
                    jobs.forEach { job ->
                        QueueItemCard(
                            job = job,
                            onCancel = { downloadManager.cancelJob(job.id) },
                            onRemove = { downloadManager.removeJob(job.id) },
                            onRetry = { downloadManager.retryJob(job.id) }
                        )
                    }
                    Spacer(Modifier.height(16.dp))
                }
            }
        }
    }
}

@Composable
private fun Header(ytDlpInfo: YtDlpInfo, downloadManager: DownloadManager) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.Top
    ) {
        Column {
            Text("yt-dlp GUI", style = MaterialTheme.typography.headlineMedium, color = MaterialTheme.colorScheme.onBackground)
            Spacer(Modifier.height(4.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                val ytdlpColor = when (ytDlpInfo.status) {
                    YtDlpStatus.UP_TO_DATE -> AppColors.Success
                    YtDlpStatus.UPDATE_AVAILABLE -> AppColors.Warning
                    YtDlpStatus.NOT_INSTALLED, YtDlpStatus.ERROR -> AppColors.Error
                    else -> MaterialTheme.colorScheme.onSurfaceVariant
                }
                val ytdlpText = when (ytDlpInfo.status) {
                    YtDlpStatus.NOT_INSTALLED -> "yt-dlp not installed"
                    YtDlpStatus.CHECKING -> "yt-dlp checking..."
                    YtDlpStatus.INSTALLING -> "yt-dlp installing..."
                    YtDlpStatus.UPDATING -> "yt-dlp updating..."
                    else -> "yt-dlp ${ytDlpInfo.currentVersion.ifBlank { "unknown" }}"
                }
                Text(ytdlpText, style = MaterialTheme.typography.bodySmall, color = ytdlpColor)
                Text("ffmpeg", style = MaterialTheme.typography.bodySmall,
                    color = if (downloadManager.isFfmpegAvailable) AppColors.Success else AppColors.Error)
            }
        }

        when (ytDlpInfo.status) {
            YtDlpStatus.NOT_INSTALLED -> {
                Button(
                    onClick = { downloadManager.installOrUpdateYtDlp() },
                    shape = RoundedCornerShape(50),
                    colors = ButtonDefaults.buttonColors(containerColor = AppColors.Blue, contentColor = Color.White)
                ) { Text("Install yt-dlp") }
            }
            YtDlpStatus.UPDATE_AVAILABLE -> {
                OutlinedButton(
                    onClick = { downloadManager.installOrUpdateYtDlp() },
                    shape = RoundedCornerShape(50),
                    border = BorderStroke(1.dp, AppColors.Outline),
                    colors = ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.onSurface)
                ) { Text("Update yt-dlp") }
            }
            YtDlpStatus.INSTALLING, YtDlpStatus.UPDATING, YtDlpStatus.CHECKING -> {
                CircularProgressIndicator(Modifier.size(24.dp), strokeWidth = 2.dp, color = AppColors.Blue)
            }
            else -> {
                OutlinedButton(
                    onClick = { downloadManager.checkYtDlp() },
                    shape = RoundedCornerShape(50),
                    border = BorderStroke(1.dp, AppColors.Outline),
                    colors = ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.onSurface)
                ) { Text("Check for updates") }
            }
        }
    }
}

@Composable
private fun UrlInputCard(downloadManager: DownloadManager) {
    var url by remember { mutableStateOf("") }

    Card {
        Text("Video / Playlist URL", style = MaterialTheme.typography.labelMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(bottom = 12.dp))

        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
            OutlinedTextField(
                value = url,
                onValueChange = { url = it },
                modifier = Modifier.weight(1f),
                placeholder = { Text("Paste a URL here...", color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.4f)) },
                shape = RoundedCornerShape(12.dp),
                colors = OutlinedTextFieldDefaults.colors(
                    focusedBorderColor = AppColors.Blue,
                    unfocusedBorderColor = AppColors.Outline.copy(alpha = 0.5f),
                    focusedContainerColor = AppColors.InputField,
                    unfocusedContainerColor = AppColors.InputField,
                    cursorColor = AppColors.Blue
                ),
                singleLine = true,
            )

            TextButton(onClick = {
                try {
                    val clip = Toolkit.getDefaultToolkit().systemClipboard
                    val text = clip.getData(DataFlavor.stringFlavor) as? String
                    if (!text.isNullOrBlank()) url = text.trim()
                } catch (_: Exception) {}
            }) { Text("Paste", color = MaterialTheme.colorScheme.onSurface) }

            Button(
                onClick = { downloadManager.addToQueue(url); url = "" },
                enabled = url.isNotBlank(),
                shape = RoundedCornerShape(50),
                colors = ButtonDefaults.buttonColors(
                    containerColor = AppColors.Blue, contentColor = Color.White,
                    disabledContainerColor = AppColors.Blue.copy(alpha = 0.4f),
                    disabledContentColor = Color.White.copy(alpha = 0.4f)
                ),
                modifier = Modifier.height(48.dp)
            ) { Text("Add", style = MaterialTheme.typography.labelLarge) }
        }
    }
}

@Composable
private fun OptionsCard(
    options: DownloadOptions,
    onUpdate: (DownloadOptions.() -> DownloadOptions) -> Unit
) {
    Card {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            FullWidthDropdown(
                label = "Format", selected = options.format, options = FormatOption.entries,
                displayText = { it.label },
                onSelected = { fmt ->
                    onUpdate { copy(format = fmt,
                        videoQuality = if (fmt.isVideo) videoQuality else VideoQuality.BEST,
                        audioQuality = if (!fmt.isVideo) audioQuality else AudioQuality.BEST) }
                },
                modifier = Modifier.weight(1f)
            )
            if (options.format.isVideo) {
                FullWidthDropdown("Quality", options.videoQuality, VideoQuality.entries, { it.label },
                    { q -> onUpdate { copy(videoQuality = q) } }, Modifier.weight(1f))
            } else {
                FullWidthDropdown("Quality", options.audioQuality, AudioQuality.entries, { it.label },
                    { q -> onUpdate { copy(audioQuality = q) } }, Modifier.weight(1f))
            }
        }

        Spacer(Modifier.height(16.dp))

        Text("Save to", style = MaterialTheme.typography.labelMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(bottom = 8.dp))
        Surface(
            onClick = {
                val chooser = javax.swing.JFileChooser().apply {
                    fileSelectionMode = javax.swing.JFileChooser.DIRECTORIES_ONLY
                    currentDirectory = java.io.File(options.outputDir)
                }
                if (chooser.showOpenDialog(null) == javax.swing.JFileChooser.APPROVE_OPTION) {
                    onUpdate { copy(outputDir = chooser.selectedFile.absolutePath) }
                }
            },
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(12.dp),
            color = AppColors.InputField,
        ) {
            Text(options.outputDir, style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurface, maxLines = 1,
                overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(horizontal = 16.dp, vertical = 14.dp))
        }

        Spacer(Modifier.height(8.dp))

        ToggleRow("Subtitles", options.subtitles, onCheckedChange = { onUpdate { copy(subtitles = it) } })
        ToggleRow("SponsorBlock", options.sponsorBlock, onCheckedChange = { onUpdate { copy(sponsorBlock = it) } })
        ToggleRow("Full Playlist", options.fullPlaylist, onCheckedChange = { onUpdate { copy(fullPlaylist = it) } })
    }
}

@Composable
private fun QueueHeader(jobs: List<DownloadJob>, downloadManager: DownloadManager) {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
        Text("Queue", style = MaterialTheme.typography.headlineSmall, color = MaterialTheme.colorScheme.onBackground)
        if (jobs.isNotEmpty()) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(
                    onClick = { downloadManager.startAll() }, shape = RoundedCornerShape(50),
                    colors = ButtonDefaults.buttonColors(containerColor = AppColors.Blue, contentColor = Color.White),
                    contentPadding = PaddingValues(horizontal = 20.dp, vertical = 8.dp)
                ) { Text("Download All") }
                OutlinedButton(
                    onClick = { downloadManager.cancelAll() }, shape = RoundedCornerShape(50),
                    border = BorderStroke(1.dp, AppColors.Outline),
                    colors = ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.onSurface),
                    contentPadding = PaddingValues(horizontal = 20.dp, vertical = 8.dp)
                ) { Text("Cancel All") }
                TextButton(onClick = { downloadManager.clearFinished() },
                    contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp)
                ) { Text("Clear", color = AppColors.Error) }
            }
        }
    }
}

@Composable
private fun QueueItemCard(job: DownloadJob, onCancel: () -> Unit, onRemove: () -> Unit, onRetry: () -> Unit) {
    Card {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Text(job.title, style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.onSurface,
                maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f).padding(end = 12.dp))
            StatusBadge(job.state)
        }
        Text(job.url, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
            maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(top = 2.dp))

        if (job.state == DownloadState.DOWNLOADING || job.state == DownloadState.PROCESSING) {
            Spacer(Modifier.height(12.dp))
            ProgressBar(job.progress)
            Row(Modifier.fillMaxWidth().padding(top = 6.dp), horizontalArrangement = Arrangement.SpaceBetween) {
                Text("${(job.progress * 100).toInt()}%", style = MaterialTheme.typography.labelSmall, color = AppColors.Blue)
                Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                    if (job.speed.isNotBlank()) Text(job.speed, style = MaterialTheme.typography.labelSmall, color = AppColors.OnSurfaceVariant)
                    if (job.eta.isNotBlank()) Text("ETA ${job.eta}", style = MaterialTheme.typography.labelSmall, color = AppColors.OnSurfaceVariant)
                }
            }
        }

        if (job.state == DownloadState.ERROR && job.error.isNotBlank()) {
            Text(job.error, style = MaterialTheme.typography.bodySmall, color = AppColors.Error,
                maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(top = 8.dp))
        }

        Row(Modifier.fillMaxWidth().padding(top = 8.dp), horizontalArrangement = Arrangement.End) {
            when (job.state) {
                DownloadState.QUEUED, DownloadState.FETCHING, DownloadState.DOWNLOADING, DownloadState.PROCESSING -> {
                    TextButton(onClick = onCancel) { Text("Cancel", color = AppColors.Error) }
                }
                DownloadState.ERROR, DownloadState.CANCELLED -> {
                    TextButton(onClick = onRetry) { Text("Retry", color = AppColors.Blue) }
                    IconButton(onClick = onRemove, modifier = Modifier.size(32.dp)) {
                        Icon(Icons.Rounded.Close, "Remove", tint = AppColors.OnSurfaceVariant, modifier = Modifier.size(16.dp))
                    }
                }
                DownloadState.FINISHED -> {
                    IconButton(onClick = onRemove, modifier = Modifier.size(32.dp)) {
                        Icon(Icons.Rounded.Close, "Remove", tint = AppColors.OnSurfaceVariant, modifier = Modifier.size(16.dp))
                    }
                }
            }
        }
    }
}
