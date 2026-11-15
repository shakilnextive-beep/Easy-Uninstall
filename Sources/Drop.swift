import SwiftUI
import AppKit
import UniformTypeIdentifiers

/// Apps dragged from Finder onto the window or the Dock icon wait here until the window picks them up.
final class DropInbox: ObservableObject {
    static let shared = DropInbox()
    @Published var urls: [URL] = []

    static func load(_ providers: [NSItemProvider]) -> Bool {
        var found = false
        for p in providers where p.canLoadObject(ofClass: URL.self) {
            found = true
            _ = p.loadObject(ofClass: URL.self) { url, _ in
                guard let url, url.isFileURL else { return }
                DispatchQueue.main.async { shared.urls.append(url) }
            }
        }
        return found
    }
}

extension Store {
    /// The .app bundle a dropped item belongs to: the app itself, a file inside it, or an alias to it.
    static func appBundle(for url: URL) -> URL? {
        var u = url.resolvingSymlinksInPath()
        if let r = try? URL(resolvingAliasFileAt: u) { u = r }
        let start = u
        while u.path != "/" {
            if u.pathExtension.lowercased() == "app" { return u }
            u.deleteLastPathComponent()
        }
        // A folder that holds the app, e.g. /Applications/Adobe Photoshop 2027/Adobe Photoshop 2027.app
        let inside = ((try? FS.fm.contentsOfDirectory(at: start, includingPropertiesForKeys: nil)) ?? [])
            .filter { $0.pathExtension.lowercased() == "app" }
        let folder = start.lastPathComponent.lowercased()
        return inside.first { $0.deletingPathExtension().lastPathComponent.lowercased() == folder }
            ?? inside.first { folder.hasPrefix($0.deletingPathExtension().lastPathComponent.lowercased()) }
            ?? (inside.count == 1 ? inside[0] : nil)
    }

    func uninstallDropped(_ urls: [URL], nav: Nav) {
        var apps: [InstalledApp] = []
        var notApps: [String] = []
        var blocked: [String] = []
        for url in urls {
            guard let bundle = Store.appBundle(for: url), let app = Scanner.makeApp(bundle) else {
                notApps.append(url.lastPathComponent); continue
            }
            if app.isProtected || app.bundleID == Bundle.main.bundleIdentifier || app.url.path.hasPrefix("/System/") {
                blocked.append(app.name); continue
            }
            if !apps.contains(where: { $0.id == app.id }) { apps.append(app) }
        }

        if !blocked.isEmpty {
            Actions.info(L("এগুলো মুছা যাবে না", "These can't be removed"),
                         L("macOS এর নিজের app বা Easy Uninstall নিজে: ", "macOS's own apps or Easy Uninstall itself: ") + blocked.joined(separator: ", "))
        }
        if apps.isEmpty {
            if !notApps.isEmpty {
                Actions.info(L("এটা কোনো app না", "That's not an app"),
                             L("শুধু app (.app) টেনে এনে ছাড়ুন। Finder এর Applications folder থেকে টানুন।\n\n",
                               "Drop apps (.app) only — drag them from Finder's Applications folder.\n\n") + notApps.joined(separator: ", "))
            }
            return
        }
        for a in apps { picked.insert(a.id) }
        // A sheet is already open: keep them ticked in the list for the next Uninstall
        guard selected == nil, batch == nil else { NSSound.beep(); return }
        nav.section = .uninstall
        if apps.count == 1 { selected = apps[0] } else { batch = BatchJob(apps: apps) }
    }
}

struct DropOverlay: View {
    var body: some View {
        ZStack {
            Color.black.opacity(0.35)
            VStack(spacing: 14) {
                Image(systemName: "trash.circle.fill").font(.system(size: 64)).foregroundStyle(.white, Palette.coral)
                Text(L("এখানে ছেড়ে দিন — Uninstall হবে", "Drop here to uninstall")).font(.title2.weight(.bold))
                Text(L("মুছার আগে কী কী যাবে দেখিয়ে জিজ্ঞেস করবে", "You'll see what will be removed before anything is deleted"))
                    .font(.callout).foregroundStyle(.secondary)
            }
            .padding(36)
            .background(RoundedRectangle(cornerRadius: 22).fill(.regularMaterial))
            .overlay(RoundedRectangle(cornerRadius: 22).strokeBorder(Palette.coral, style: StrokeStyle(lineWidth: 3, dash: [10, 6])))
        }
        .ignoresSafeArea()
        .allowsHitTesting(false)
    }
}

/// Dashed drop hint shown next to the app list.
struct DropHint: View {
    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: "arrow.down.app")
            Text(L("Finder থেকে app টেনে এনে এখানে ছাড়ুন", "Drag an app here from Finder"))
        }
        .font(.caption).foregroundStyle(.secondary)
        .padding(.horizontal, 10).padding(.vertical, 5)
        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Color.secondary.opacity(0.45), style: StrokeStyle(lineWidth: 1, dash: [4, 3])))
        .help(L("Easy Uninstall এর Dock icon এর উপরে ছাড়লেও হবে", "Dropping on Easy Uninstall's Dock icon works too"))
    }
}
