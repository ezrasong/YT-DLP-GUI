package com.ytdlpgui.ui

import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import com.ytdlpgui.service.DownloadManager
import com.ytdlpgui.theme.AppTheme

@Composable
fun App(downloadManager: DownloadManager) {
    AppTheme {
        Surface(
            modifier = Modifier.fillMaxSize(),
            color = MaterialTheme.colorScheme.background
        ) {
            DownloadScreen(downloadManager)
        }
    }
}
