import Combine
import Foundation

enum DownloadStatus: String {
    case queued
    case fetching
    case downloading
    case processing
    case finished
    case error
    case cancelled
}

struct UpdateStatus {
    let installed: Bool
    let currentVersion: String
    let latestVersion: String
    let updateAvailable: Bool
    let error: String
}

struct DownloadProgress {
    let percent: Double
    let speed: String
    let eta: String
    let status: DownloadStatus
    let title: String
    let error: String
}

final class DownloadJob: ObservableObject, Identifiable {
    let id: String
    let url: String
    let format: String
    let quality: String
    let outputDirectory: String
    let subtitles: Bool
    let sponsorBlock: Bool
    let playlist: Bool

    @Published var title: String
    @Published var status: DownloadStatus = .queued
    @Published var progress: Double = 0
    @Published var speed: String = ""
    @Published var eta: String = ""
    @Published var error: String = ""

    var process: Process?
    var task: Task<Void, Never>?

    init(
        id: String,
        url: String,
        format: String,
        quality: String,
        outputDirectory: String,
        subtitles: Bool,
        sponsorBlock: Bool,
        playlist: Bool
    ) {
        self.id = id
        self.url = url
        self.format = format
        self.quality = quality
        self.outputDirectory = outputDirectory
        self.subtitles = subtitles
        self.sponsorBlock = sponsorBlock
        self.playlist = playlist
        self.title = url
    }

    var isActive: Bool {
        status == .fetching || status == .downloading || status == .processing
    }

    var isTerminal: Bool {
        status == .finished || status == .error || status == .cancelled
    }

    var statusLabel: String {
        switch status {
        case .queued: return "Queued"
        case .fetching: return "Fetching info..."
        case .downloading: return "Downloading"
        case .processing: return "Processing"
        case .finished: return "Finished"
        case .error: return "Error"
        case .cancelled: return "Cancelled"
        }
    }

    var detailsLabel: String {
        if status == .error, !error.isEmpty {
            return error
        }
        if isActive {
            let speedPart = speed.isEmpty ? "" : speed
            let etaPart = eta.isEmpty ? "" : "ETA \(eta)"
            let pieces = [speedPart, etaPart].filter { !$0.isEmpty }
            return pieces.joined(separator: " | ")
        }
        if status == .finished {
            return "Complete"
        }
        return "\(format) | \(quality)"
    }
}
