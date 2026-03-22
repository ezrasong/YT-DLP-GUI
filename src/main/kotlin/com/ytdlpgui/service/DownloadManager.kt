package com.ytdlpgui.service

import androidx.compose.runtime.*
import com.ytdlpgui.model.*
import kotlinx.coroutines.*
import java.util.concurrent.ConcurrentHashMap

class DownloadManager(private val ytDlpService: YtDlpService) {

    private val _jobs = mutableStateListOf<DownloadJob>()
    val jobs: List<DownloadJob> get() = _jobs

    var options by mutableStateOf(DownloadOptions())
        private set

    var ytDlpInfo by mutableStateOf(YtDlpInfo())
        private set

    var installProgress by mutableStateOf("")
        private set

    var isFfmpegAvailable by mutableStateOf(false)
        private set

    private val scope = CoroutineScope(Dispatchers.Default + SupervisorJob())
    private val activeJobs = ConcurrentHashMap<String, Job>()
    private var activeDownloadCount = 0
    private val maxConcurrent = 3

    fun updateOptions(transform: DownloadOptions.() -> DownloadOptions) {
        options = options.transform()
    }

    fun checkYtDlp() {
        ytDlpInfo = ytDlpInfo.copy(status = YtDlpStatus.CHECKING)
        scope.launch {
            ytDlpInfo = ytDlpService.checkStatus()
            isFfmpegAvailable = ytDlpService.isFfmpegAvailable()
        }
    }

    fun installOrUpdateYtDlp() {
        val newStatus = if (ytDlpService.isInstalled()) YtDlpStatus.UPDATING else YtDlpStatus.INSTALLING
        ytDlpInfo = ytDlpInfo.copy(status = newStatus)
        installProgress = ""

        scope.launch {
            ytDlpService.installOrUpdate { msg -> installProgress = msg }.fold(
                onSuccess = {
                    installProgress = ""
                    ytDlpInfo = ytDlpService.checkStatus()
                    isFfmpegAvailable = ytDlpService.isFfmpegAvailable()
                },
                onFailure = { e ->
                    installProgress = ""
                    ytDlpInfo = ytDlpInfo.copy(status = YtDlpStatus.ERROR, error = e.message ?: "Failed")
                }
            )
        }
    }

    fun addToQueue(url: String) {
        if (url.isBlank()) return
        url.lines().map { it.trim() }.filter { it.isNotBlank() }.forEach { u ->
            _jobs.add(0, DownloadJob(url = u))
        }
        processQueue()
    }

    fun startAll() {
        // Reset queued items and restart
        _jobs.forEachIndexed { i, job ->
            if (job.state == DownloadState.QUEUED || job.state == DownloadState.CANCELLED || job.state == DownloadState.ERROR) {
                _jobs[i] = job.copy(state = DownloadState.QUEUED, progress = 0f, speed = "", eta = "", error = "")
            }
        }
        processQueue()
    }

    fun removeJob(jobId: String) {
        cancelJob(jobId)
        _jobs.removeAll { it.id == jobId }
    }

    fun cancelJob(jobId: String) {
        activeJobs.remove(jobId)?.cancel()
        val index = _jobs.indexOfFirst { it.id == jobId }
        if (index >= 0) {
            val current = _jobs[index]
            if (current.state != DownloadState.FINISHED && current.state != DownloadState.ERROR) {
                _jobs[index] = current.copy(state = DownloadState.CANCELLED)
                activeDownloadCount = (activeDownloadCount - 1).coerceAtLeast(0)
            }
        }
    }

    fun retryJob(jobId: String) {
        val index = _jobs.indexOfFirst { it.id == jobId }
        if (index >= 0) {
            _jobs[index] = _jobs[index].copy(
                state = DownloadState.QUEUED, progress = 0f, speed = "", eta = "", error = ""
            )
            processQueue()
        }
    }

    fun clearFinished() {
        _jobs.removeAll { it.state == DownloadState.FINISHED || it.state == DownloadState.CANCELLED }
    }

    fun cancelAll() {
        val toCancel = _jobs.filter {
            it.state in listOf(DownloadState.DOWNLOADING, DownloadState.FETCHING, DownloadState.PROCESSING)
        }
        toCancel.forEach { cancelJob(it.id) }
    }

    private fun processQueue() {
        if (!ytDlpService.isInstalled()) return
        val queued = _jobs.filter { it.state == DownloadState.QUEUED }
        val slots = maxConcurrent - activeDownloadCount
        queued.take(slots).forEach { startDownload(it) }
    }

    private fun startDownload(job: DownloadJob) {
        activeDownloadCount++
        activeJobs[job.id] = scope.launch {
            ytDlpService.executeDownload(job, options) { update ->
                val index = _jobs.indexOfFirst { it.id == job.id }
                if (index >= 0) {
                    val c = _jobs[index]
                    _jobs[index] = c.copy(
                        title = update.title ?: c.title,
                        progress = update.progress ?: c.progress,
                        speed = update.speed ?: c.speed,
                        eta = update.eta ?: c.eta,
                        state = update.state ?: c.state,
                        error = update.error ?: c.error,
                        filePath = update.filePath ?: c.filePath
                    )
                }
            }
            activeDownloadCount = (activeDownloadCount - 1).coerceAtLeast(0)
            activeJobs.remove(job.id)
            processQueue()
        }
    }

    fun dispose() {
        cancelAll()
        scope.cancel()
    }
}
