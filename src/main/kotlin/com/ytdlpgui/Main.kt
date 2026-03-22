package com.ytdlpgui

import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Window
import androidx.compose.ui.window.WindowPosition
import androidx.compose.ui.window.application
import androidx.compose.ui.window.rememberWindowState
import com.ytdlpgui.service.DownloadManager
import com.ytdlpgui.service.YtDlpService
import com.ytdlpgui.ui.App

fun main() = application {
    val windowState = rememberWindowState(
        size = DpSize(1050.dp, 900.dp),
        position = WindowPosition(Alignment.Center)
    )

    val ytDlpService = remember { YtDlpService() }
    val downloadManager = remember { DownloadManager(ytDlpService) }

    LaunchedEffect(Unit) {
        downloadManager.checkYtDlp()
    }

    DisposableEffect(Unit) {
        onDispose { downloadManager.dispose() }
    }

    Window(
        onCloseRequest = ::exitApplication,
        title = "yt-dlp GUI",
        state = windowState,
    ) {
        App(downloadManager)
    }
}
