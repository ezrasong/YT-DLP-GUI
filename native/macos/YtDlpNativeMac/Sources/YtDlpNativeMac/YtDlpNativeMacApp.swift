import SwiftUI

@main
struct YtDlpNativeMacApp: App {
    @StateObject private var viewModel = MainViewModel()

    var body: some Scene {
        WindowGroup {
            MainView(viewModel: viewModel)
                .frame(minWidth: 860, minHeight: 700)
        }
    }
}
