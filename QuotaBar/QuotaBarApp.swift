import SwiftUI
import WidgetKit

@main
struct QuotaBarApp: App {
    @StateObject private var model = AppModel()

    var body: some Scene {
        Window("QuotaBar", id: "dashboard") {
            DashboardView()
                .environmentObject(model)
                .frame(minWidth: 480, minHeight: 520)
        }
        .defaultSize(width: 560, height: 680)
        .windowResizability(.contentMinSize)

        MenuBarExtra {
            MenuBarView()
                .environmentObject(model)
        } label: {
            MenuBarLabel(model: model)
        }
        .menuBarExtraStyle(.window)

        Settings {
            SettingsView()
                .environmentObject(model)
                .frame(width: 520)
        }
    }
}

private struct MenuBarLabel: View {
    @ObservedObject var model: AppModel

    var body: some View {
        let provider = model.focusProvider
        let metric = provider.flatMap { model.primaryMetric(for: $0) }

        if let metric, metric.resolvedRemaining != nil || metric.resolvedUsed != nil {
            HStack(spacing: 4) {
                Image(systemName: provider?.kind.symbolName ?? "gauge.with.dots.needle.33percent")
                Text(QuotaFormatter.primary(metric))
            }
        } else {
            Image(systemName: "gauge.with.dots.needle.33percent")
        }
    }
}
