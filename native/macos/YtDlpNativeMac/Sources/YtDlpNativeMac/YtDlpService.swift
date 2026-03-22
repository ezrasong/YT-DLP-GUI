import AppKit
import Foundation

final class YtDlpService {
    private let ytdlpDownloadURL = URL(string: "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_macos")!
    private let latestReleaseApiURL = URL(string: "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest")!

    private let fileManager = FileManager.default
    private let session: URLSession
    private let binDirectory: URL
    private let ytdlpBinaryURL: URL
    private let ffmpegBinaryURL: URL

    private let percentRegex = try! NSRegularExpression(pattern: #"\[download\]\s+([\d.]+)%"#)
    private let detailRegex = try! NSRegularExpression(pattern: #"at\s+(.+?)\s+ETA\s+(.+)"#)
    private let destinationRegex = try! NSRegularExpression(pattern: #"\[download\] Destination:\s+(.+)"#)
    private let alreadyRegex = try! NSRegularExpression(pattern: #"\[download\]\s+(.+) has already been downloaded"#)
    private let processingRegex = try! NSRegularExpression(pattern: #"^\[(ffmpeg|ExtractAudio|Merger|Merge|SponsorBlock|ModifyChapters)\]"#)
    private let errorRegex = try! NSRegularExpression(pattern: #"^ERROR:\s+(.+)$"#)

    init() {
        let appSupport = fileManager.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        self.binDirectory = appSupport
            .appendingPathComponent("YtDlpGuiNative", isDirectory: true)
            .appendingPathComponent("bin", isDirectory: true)
        self.ytdlpBinaryURL = binDirectory.appendingPathComponent("yt-dlp", isDirectory: false)
        self.ffmpegBinaryURL = binDirectory.appendingPathComponent("ffmpeg", isDirectory: false)

        let config = URLSessionConfiguration.default
        config.httpAdditionalHeaders = [
            "User-Agent": "YtDlpGuiNative/1.0",
            "Accept": "application/vnd.github+json",
        ]
        self.session = URLSession(configuration: config)
    }

    var defaultDownloadDirectory: URL {
        fileManager.urls(for: .downloadsDirectory, in: .userDomainMask).first
            ?? fileManager.homeDirectoryForCurrentUser
    }

    func installedVersion() async -> String {
        await runBlocking {
            self.version(at: self.ytdlpBinaryURL)
        }
    }

    func checkForUpdate() async -> UpdateStatus {
        let current = await installedVersion()
        let installed = !current.isEmpty

        do {
            let latest = try await latestVersion()
            let available = installed && isNewerVersion(current: current, latest: latest)
            return UpdateStatus(
                installed: installed,
                currentVersion: current,
                latestVersion: latest,
                updateAvailable: available,
                error: ""
            )
        } catch {
            return UpdateStatus(
                installed: installed,
                currentVersion: current,
                latestVersion: "",
                updateAvailable: false,
                error: error.localizedDescription
            )
        }
    }

    func installOrUpdate() async throws -> String {
        try await ensureYtDlpInstalled(forceDownload: true)
        let version = await installedVersion()
        if version.isEmpty {
            throw NSError(domain: "YtDlpNativeMac", code: 1, userInfo: [NSLocalizedDescriptionKey: "yt-dlp install finished but version check failed."])
        }
        return version
    }

    func runDownload(
        job: DownloadJob,
        onProgress: @escaping (DownloadProgress) -> Void
    ) async throws {
        try await ensureYtDlpInstalled()
        try fileManager.createDirectory(atPath: job.outputDirectory, withIntermediateDirectories: true, attributes: nil)

        let process = Process()
        process.executableURL = ytdlpBinaryURL
        process.arguments = buildArguments(for: job)
        process.currentDirectoryURL = URL(fileURLWithPath: job.outputDirectory, isDirectory: true)

        let stderrPipe = Pipe()
        process.standardError = stderrPipe
        process.standardOutput = Pipe()

        try process.run()
        await MainActor.run {
            job.process = process
        }

        onProgress(
            DownloadProgress(
                percent: 0,
                speed: "",
                eta: "",
                status: .fetching,
                title: "",
                error: ""
            )
        )

        var title = ""
        var lastError = ""
        do {
            for try await line in stderrPipe.fileHandleForReading.bytes.lines {
                if Task.isCancelled {
                    terminate(process)
                    break
                }

                if let error = firstMatch(in: String(line), regex: errorRegex, group: 1), !error.isEmpty {
                    lastError = error
                }

                if title.isEmpty {
                    title = extractTitle(from: String(line))
                }

                if let pctRaw = firstMatch(in: String(line), regex: percentRegex, group: 1),
                   let pct = Double(pctRaw) {
                    let (speed, eta) = extractSpeedEta(from: String(line))
                    onProgress(
                        DownloadProgress(
                            percent: pct,
                            speed: speed,
                            eta: eta,
                            status: .downloading,
                            title: title,
                            error: ""
                        )
                    )
                    continue
                }

                if processingRegex.firstMatch(in: String(line), range: NSRange(String(line).startIndex..., in: String(line))) != nil {
                    onProgress(
                        DownloadProgress(
                            percent: 100,
                            speed: "",
                            eta: "",
                            status: .processing,
                            title: title,
                            error: ""
                        )
                    )
                }
            }
        } catch {
            // Pipe read failures are treated as process termination events.
        }

        if Task.isCancelled {
            onProgress(
                DownloadProgress(
                    percent: 0,
                    speed: "",
                    eta: "",
                    status: .cancelled,
                    title: title,
                    error: ""
                )
            )
            await MainActor.run {
                if job.process === process {
                    job.process = nil
                }
            }
            return
        }

        process.waitUntilExit()
        await MainActor.run {
            if job.process === process {
                job.process = nil
            }
        }

        if process.terminationStatus == 0 {
            onProgress(
                DownloadProgress(
                    percent: 100,
                    speed: "",
                    eta: "",
                    status: .finished,
                    title: title,
                    error: ""
                )
            )
            return
        }

        let message = lastError.isEmpty ? "yt-dlp exited with code \(process.terminationStatus)" : lastError
        onProgress(
            DownloadProgress(
                percent: 0,
                speed: "",
                eta: "",
                status: .error,
                title: title,
                error: message
            )
        )
    }

    func openFolder(path: String) {
        let url = URL(fileURLWithPath: path, isDirectory: true)
        NSWorkspace.shared.open(url)
    }

    private func ensureYtDlpInstalled(forceDownload: Bool = false) async throws {
        try fileManager.createDirectory(at: binDirectory, withIntermediateDirectories: true, attributes: nil)
        let currentVersion = await installedVersion()
        if !forceDownload && !currentVersion.isEmpty {
            return
        }

        let (tempURL, response) = try await session.download(from: ytdlpDownloadURL)
        guard let http = response as? HTTPURLResponse, (200...299).contains(http.statusCode) else {
            throw NSError(domain: "YtDlpNativeMac", code: 2, userInfo: [NSLocalizedDescriptionKey: "Failed to download yt-dlp binary."])
        }

        let targetTemp = binDirectory.appendingPathComponent("yt-dlp.tmp", isDirectory: false)
        if fileManager.fileExists(atPath: targetTemp.path) {
            try fileManager.removeItem(at: targetTemp)
        }
        try fileManager.moveItem(at: tempURL, to: targetTemp)

        if fileManager.fileExists(atPath: ytdlpBinaryURL.path) {
            try fileManager.removeItem(at: ytdlpBinaryURL)
        }
        try fileManager.moveItem(at: targetTemp, to: ytdlpBinaryURL)
        try fileManager.setAttributes([.posixPermissions: 0o755], ofItemAtPath: ytdlpBinaryURL.path)
    }

    private func latestVersion() async throws -> String {
        var request = URLRequest(url: latestReleaseApiURL)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("YtDlpGuiNative/1.0", forHTTPHeaderField: "User-Agent")

        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse, (200...299).contains(http.statusCode) else {
            throw NSError(domain: "YtDlpNativeMac", code: 3, userInfo: [NSLocalizedDescriptionKey: "Failed to query latest yt-dlp release."])
        }

        guard
            let json = try JSONSerialization.jsonObject(with: data) as? [String: Any],
            let tag = json["tag_name"] as? String
        else {
            throw NSError(domain: "YtDlpNativeMac", code: 4, userInfo: [NSLocalizedDescriptionKey: "Latest release response did not include tag_name."])
        }
        return tag.trimmingCharacters(in: .whitespacesAndNewlines).trimmingCharacters(in: CharacterSet(charactersIn: "v"))
    }

    private func version(at executableURL: URL) -> String {
        guard fileManager.fileExists(atPath: executableURL.path) else {
            return ""
        }

        let process = Process()
        process.executableURL = executableURL
        process.arguments = ["--version"]
        let stdout = Pipe()
        process.standardOutput = stdout
        process.standardError = Pipe()
        do {
            try process.run()
        } catch {
            return ""
        }

        process.waitUntilExit()
        guard process.terminationStatus == 0 else {
            return ""
        }
        let data = stdout.fileHandleForReading.readDataToEndOfFile()
        let raw = String(data: data, encoding: .utf8) ?? ""
        return raw.trimmingCharacters(in: .whitespacesAndNewlines).components(separatedBy: .newlines).first ?? ""
    }

    private func buildArguments(for job: DownloadJob) -> [String] {
        var args = [
            "--newline",
            "-o",
            URL(fileURLWithPath: job.outputDirectory, isDirectory: true)
                .appendingPathComponent("%(title)s.%(ext)s", isDirectory: false).path,
        ]

        if job.format.hasPrefix("Video") {
            let ext = parentheticalValue(in: job.format, fallback: "MP4").lowercased()
            let formatSelector: String
            switch job.quality {
            case "Best":
                formatSelector = "bestvideo+bestaudio/best"
            case "4K":
                formatSelector = "bestvideo[height<=2160]+bestaudio/best[height<=2160]/best"
            case "1440p":
                formatSelector = "bestvideo[height<=1440]+bestaudio/best[height<=1440]/best"
            case "1080p":
                formatSelector = "bestvideo[height<=1080]+bestaudio/best[height<=1080]/best"
            case "720p":
                formatSelector = "bestvideo[height<=720]+bestaudio/best[height<=720]/best"
            case "480p":
                formatSelector = "bestvideo[height<=480]+bestaudio/best[height<=480]/best"
            case "360p":
                formatSelector = "bestvideo[height<=360]+bestaudio/best[height<=360]/best"
            default:
                formatSelector = "bestvideo+bestaudio/best"
            }
            args.append(contentsOf: ["-f", formatSelector])
            if ext != "webm" {
                args.append(contentsOf: ["--merge-output-format", ext])
            }
        } else {
            let codec = parentheticalValue(in: job.format, fallback: "MP3").lowercased()
            args.append(contentsOf: ["-f", "bestaudio/best", "-x", "--audio-format", codec])
            switch job.quality {
            case "320kbps":
                args.append(contentsOf: ["--audio-quality", "320"])
            case "256kbps":
                args.append(contentsOf: ["--audio-quality", "256"])
            case "192kbps":
                args.append(contentsOf: ["--audio-quality", "192"])
            case "128kbps":
                args.append(contentsOf: ["--audio-quality", "128"])
            default:
                break
            }
        }

        if !job.playlist {
            args.append("--no-playlist")
        }
        if job.subtitles {
            args.append(contentsOf: ["--write-subs", "--write-auto-subs", "--sub-langs", "en,en.*"])
        }
        if job.sponsorBlock {
            args.append(contentsOf: ["--sponsorblock-remove", "sponsor,selfpromo,interaction"])
        }
        if fileManager.fileExists(atPath: ffmpegBinaryURL.path) {
            args.append(contentsOf: ["--ffmpeg-location", binDirectory.path])
        }

        args.append(job.url)
        return args
    }

    private func parentheticalValue(in input: String, fallback: String) -> String {
        guard let open = input.firstIndex(of: "("), let close = input.firstIndex(of: ")"), open < close else {
            return fallback
        }
        return String(input[input.index(after: open) ..< close]).trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private func firstMatch(in text: String, regex: NSRegularExpression, group: Int) -> String? {
        let range = NSRange(text.startIndex..., in: text)
        guard let match = regex.firstMatch(in: text, range: range), match.numberOfRanges > group else {
            return nil
        }
        guard let swiftRange = Range(match.range(at: group), in: text) else {
            return nil
        }
        return String(text[swiftRange]).trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private func extractTitle(from line: String) -> String {
        if let destination = firstMatch(in: line, regex: destinationRegex, group: 1), !destination.isEmpty {
            return URL(fileURLWithPath: destination).deletingPathExtension().lastPathComponent
        }
        if let already = firstMatch(in: line, regex: alreadyRegex, group: 1), !already.isEmpty {
            return URL(fileURLWithPath: already).deletingPathExtension().lastPathComponent
        }
        return ""
    }

    private func extractSpeedEta(from line: String) -> (String, String) {
        guard let speed = firstMatch(in: line, regex: detailRegex, group: 1),
              let eta = firstMatch(in: line, regex: detailRegex, group: 2) else {
            return ("", "")
        }
        let cleanSpeed = speed.contains("Unknown") ? "" : speed
        let cleanEta = eta.contains("Unknown") ? "" : eta
        return (cleanSpeed, cleanEta)
    }

    private func terminate(_ process: Process) {
        guard process.isRunning else {
            return
        }
        process.terminate()
    }

    private func isNewerVersion(current: String, latest: String) -> Bool {
        if current.isEmpty || latest.isEmpty {
            return false
        }

        func parse(_ value: String) -> [Int]? {
            let parts = value.split(separator: ".")
            return parts.compactMap { Int($0) }.count == parts.count ? parts.compactMap { Int($0) } : nil
        }

        guard let currentParts = parse(current), let latestParts = parse(latest) else {
            return current != latest
        }

        let maxCount = max(currentParts.count, latestParts.count)
        for index in 0 ..< maxCount {
            let c = index < currentParts.count ? currentParts[index] : 0
            let l = index < latestParts.count ? latestParts[index] : 0
            if l > c {
                return true
            }
            if l < c {
                return false
            }
        }
        return false
    }

    private func runBlocking<T>(_ work: @escaping () -> T) async -> T {
        await withCheckedContinuation { continuation in
            DispatchQueue.global(qos: .utility).async {
                continuation.resume(returning: work())
            }
        }
    }
}
