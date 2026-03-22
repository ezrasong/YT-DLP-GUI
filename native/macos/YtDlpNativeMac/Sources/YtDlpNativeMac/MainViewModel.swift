import Combine
import Foundation

enum StatusTone {
    case neutral
    case success
    case warning
    case danger
}

@MainActor
final class MainViewModel: ObservableObject {
    let videoFormats = ["Video (MP4)", "Video (MKV)", "Video (WEBM)"]
    let audioFormats = ["Audio (MP3)", "Audio (M4A)", "Audio (FLAC)", "Audio (WAV)", "Audio (OPUS)"]
    let videoQualities = ["Best", "4K", "1440p", "1080p", "720p", "480p", "360p"]
    let audioQualities = ["Best", "320kbps", "256kbps", "192kbps", "128kbps"]

    @Published var url: String = ""
    @Published var selectedFormat: String = "Video (MP4)" {
        didSet {
            selectedQuality = "Best"
        }
    }
    @Published var selectedQuality: String = "Best"
    @Published var outputDirectory: String
    @Published var subtitles: Bool = false
    @Published var sponsorBlock: Bool = false
    @Published var playlist: Bool = false
    @Published var jobs: [DownloadJob] = []

    @Published var checkingUpdates: Bool = false
    @Published var updating: Bool = false
    @Published var binaryStatusText: String = "yt-dlp unknown"
    @Published var binaryTone: StatusTone = .neutral
    @Published var updateStatusText: String = "Update status unknown"
    @Published var updateTone: StatusTone = .neutral
    @Published var footerText: String = "Ready"

    private let service = YtDlpService()
    private var updateStatus: UpdateStatus?
    private var initialized = false
    private var nextId = 0

    init() {
        outputDirectory = service.defaultDownloadDirectory.path
    }

    var formatOptions: [String] {
        videoFormats + audioFormats
    }

    var qualityOptions: [String] {
        selectedFormat.hasPrefix("Audio") ? audioQualities : videoQualities
    }

    var updateActionLabel: String {
        if updating {
            return updateStatus?.installed == true ? "Updating..." : "Installing..."
        }
        guard let status = updateStatus else {
            return "Install yt-dlp"
        }
        if !status.installed {
            return "Install yt-dlp"
        }
        if status.updateAvailable {
            return "Update yt-dlp"
        }
        return "Up to date"
    }

    var updateActionEnabled: Bool {
        if updating || checkingUpdates {
            return false
        }
        guard let status = updateStatus else {
            return true
        }
        return !status.installed || status.updateAvailable
    }

    func onLaunch() {
        guard !initialized else {
            return
        }
        initialized = true
        Task {
            await refreshInstalledVersion()
            await checkUpdates()
        }
    }

    func addToQueue() {
        let trimmedURL = url.trimmingCharacters(in: .whitespacesAndNewlines)
        let trimmedOutput = outputDirectory.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmedURL.isEmpty, !trimmedOutput.isEmpty else {
            return
        }

        nextId += 1
        let job = DownloadJob(
            id: "dl-\(nextId)",
            url: trimmedURL,
            format: selectedFormat,
            quality: selectedQuality,
            outputDirectory: trimmedOutput,
            subtitles: subtitles,
            sponsorBlock: sponsorBlock,
            playlist: playlist
        )
        jobs.append(job)
        url = ""
        updateFooter()
    }

    func startAll() {
        for job in jobs where job.status == .queued {
            start(job: job)
        }
    }

    func start(job: DownloadJob) {
        guard !job.isActive else {
            return
        }
        if job.status == .finished {
            return
        }

        job.task = Task {
            do {
                try await service.runDownload(job: job) { [weak self] progress in
                    Task { @MainActor in
                        self?.apply(progress: progress, to: job)
                    }
                }
            } catch {
                job.status = .error
                job.error = error.localizedDescription
                updateFooter()
            }
            job.task = nil
            job.process = nil
            updateFooter()
        }
    }

    func cancel(job: DownloadJob) {
        job.task?.cancel()
        job.process?.terminate()
        if job.status == .queued {
            job.status = .cancelled
        }
        updateFooter()
    }

    func remove(job: DownloadJob) {
        cancel(job: job)
        jobs.removeAll { $0.id == job.id }
        updateFooter()
    }

    func clearDone() {
        jobs.removeAll { $0.isTerminal }
        updateFooter()
    }

    func openFolder(job: DownloadJob) {
        service.openFolder(path: job.outputDirectory)
    }

    func checkUpdates() async {
        checkingUpdates = true
        updateStatusText = "Checking latest release..."
        updateTone = .warning

        let status = await service.checkForUpdate()
        updateStatus = status
        checkingUpdates = false

        if !status.error.isEmpty {
            updateStatusText = "Could not verify latest version"
            updateTone = .warning
            return
        }
        if !status.installed {
            updateStatusText = status.latestVersion.isEmpty
                ? "yt-dlp missing"
                : "yt-dlp missing (latest: \(status.latestVersion))"
            updateTone = .danger
            return
        }
        if status.updateAvailable {
            updateStatusText = "Update available: \(status.currentVersion) -> \(status.latestVersion)"
            updateTone = .warning
            return
        }
        updateStatusText = "Up to date (\(status.currentVersion))"
        updateTone = .success
    }

    func installOrUpdate() async {
        guard !updating else {
            return
        }
        updating = true
        updateStatusText = updateStatus?.installed == true ? "Updating yt-dlp..." : "Installing yt-dlp..."
        updateTone = .warning
        do {
            let version = try await service.installOrUpdate()
            binaryStatusText = "yt-dlp \(version)"
            binaryTone = .success
        } catch {
            updateStatusText = "Install/update failed: \(error.localizedDescription)"
            updateTone = .danger
        }
        updating = false
        await checkUpdates()
    }

    private func refreshInstalledVersion() async {
        let version = await service.installedVersion()
        if version.isEmpty {
            binaryStatusText = "yt-dlp missing"
            binaryTone = .danger
            return
        }
        binaryStatusText = "yt-dlp \(version)"
        binaryTone = .success
    }

    private func apply(progress: DownloadProgress, to job: DownloadJob) {
        if !progress.title.isEmpty {
            job.title = progress.title
        }
        job.progress = max(0, min(100, progress.percent))
        job.speed = progress.speed
        job.eta = progress.eta
        job.error = progress.error
        job.status = progress.status
        updateFooter()
    }

    private func updateFooter() {
        let active = jobs.filter { $0.isActive }.count
        let queued = jobs.filter { $0.status == .queued }.count
        let done = jobs.filter { $0.status == .finished }.count
        let failed = jobs.filter { $0.status == .error }.count

        var chunks: [String] = []
        if active > 0 { chunks.append("\(active) active") }
        if queued > 0 { chunks.append("\(queued) queued") }
        if done > 0 { chunks.append("\(done) done") }
        if failed > 0 { chunks.append("\(failed) failed") }

        footerText = chunks.isEmpty ? "Ready" : chunks.joined(separator: " | ")
    }
}
