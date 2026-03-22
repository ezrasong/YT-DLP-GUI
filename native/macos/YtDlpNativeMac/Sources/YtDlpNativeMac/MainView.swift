import SwiftUI

struct MainView: View {
    @ObservedObject var viewModel: MainViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                Text("yt-dlp GUI Native")
                    .font(.system(size: 28, weight: .semibold))
                Spacer()
                Button(viewModel.checkingUpdates ? "Checking..." : "Check updates") {
                    Task {
                        await viewModel.checkUpdates()
                    }
                }
                .disabled(viewModel.checkingUpdates || viewModel.updating)

                Button(viewModel.updateActionLabel) {
                    Task {
                        await viewModel.installOrUpdate()
                    }
                }
                .disabled(!viewModel.updateActionEnabled)
            }

            HStack(spacing: 8) {
                Text(viewModel.binaryStatusText)
                    .foregroundStyle(color(for: viewModel.binaryTone))
                Text("|").foregroundStyle(.secondary)
                Text(viewModel.updateStatusText)
                    .foregroundStyle(color(for: viewModel.updateTone))
            }
            .font(.caption)

            GroupBox("Video / Playlist URL") {
                HStack(spacing: 8) {
                    TextField("Paste a URL here...", text: $viewModel.url)
                        .textFieldStyle(.roundedBorder)
                        .onSubmit {
                            viewModel.addToQueue()
                        }
                    Button("Add to Queue") {
                        viewModel.addToQueue()
                    }
                }
            }

            GroupBox("Options") {
                VStack(spacing: 10) {
                    HStack(spacing: 12) {
                        Picker("Format", selection: $viewModel.selectedFormat) {
                            ForEach(viewModel.formatOptions, id: \.self) { format in
                                Text(format).tag(format)
                            }
                        }

                        Picker("Quality", selection: $viewModel.selectedQuality) {
                            ForEach(viewModel.qualityOptions, id: \.self) { quality in
                                Text(quality).tag(quality)
                            }
                        }
                    }

                    HStack(spacing: 8) {
                        Text("Save to")
                            .foregroundStyle(.secondary)
                        TextField("Output directory", text: $viewModel.outputDirectory)
                            .textFieldStyle(.roundedBorder)
                    }

                    HStack(spacing: 16) {
                        Toggle("Subtitles", isOn: $viewModel.subtitles)
                        Toggle("SponsorBlock", isOn: $viewModel.sponsorBlock)
                        Toggle("Full Playlist", isOn: $viewModel.playlist)
                    }
                }
                .padding(.top, 6)
            }

            HStack {
                Text("Queue \(viewModel.jobs.isEmpty ? "" : "(\(viewModel.jobs.count))")")
                    .font(.title3.weight(.semibold))
                Spacer()
                Button("Download All") {
                    viewModel.startAll()
                }
                Button("Clear Done") {
                    viewModel.clearDone()
                }
            }

            if viewModel.jobs.isEmpty {
                ZStack {
                    RoundedRectangle(cornerRadius: 12)
                        .fill(Color.secondary.opacity(0.08))
                    Text("No downloads yet. Paste a URL and add it to the queue.")
                        .foregroundStyle(.secondary)
                }
            } else {
                ScrollView {
                    LazyVStack(spacing: 8) {
                        ForEach(viewModel.jobs) { job in
                            DownloadRow(
                                job: job,
                                onCancel: { viewModel.cancel(job: job) },
                                onOpen: { viewModel.openFolder(job: job) },
                                onRemove: { viewModel.remove(job: job) },
                                onStart: { viewModel.start(job: job) }
                            )
                        }
                    }
                }
            }

            Text(viewModel.footerText)
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .padding(16)
        .task {
            viewModel.onLaunch()
        }
    }

    private func color(for tone: StatusTone) -> Color {
        switch tone {
        case .neutral:
            return .secondary
        case .success:
            return .green
        case .warning:
            return .orange
        case .danger:
            return .red
        }
    }
}

private struct DownloadRow: View {
    @ObservedObject var job: DownloadJob
    let onCancel: () -> Void
    let onOpen: () -> Void
    let onRemove: () -> Void
    let onStart: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text(job.title)
                    .font(.headline)
                    .lineLimit(1)
                Spacer()
                Text(job.statusLabel)
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(statusColor)
            }

            ProgressView(value: job.progress, total: 100)
                .tint(statusColor)

            HStack(spacing: 8) {
                Text(job.detailsLabel)
                    .font(.caption)
                    .foregroundStyle(job.status == .error ? Color.red : Color.secondary)
                    .lineLimit(1)
                Spacer()
                if job.status == .queued {
                    Button("Start") {
                        onStart()
                    }
                }
                if !job.isTerminal {
                    Button("Cancel") {
                        onCancel()
                    }
                }
                if job.status == .finished {
                    Button("Open") {
                        onOpen()
                    }
                }
                if job.isTerminal {
                    Button("Remove") {
                        onRemove()
                    }
                }
            }
        }
        .padding(10)
        .background(
            RoundedRectangle(cornerRadius: 12)
                .fill(Color.secondary.opacity(0.08))
        )
    }

    private var statusColor: Color {
        switch job.status {
        case .queued:
            return .secondary
        case .fetching, .downloading:
            return .blue
        case .processing:
            return .orange
        case .finished:
            return .green
        case .error:
            return .red
        case .cancelled:
            return .orange
        }
    }
}
