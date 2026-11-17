import SwiftUI
import AppKit

/// Which apps the Uninstall list shows.
enum AppFilter: String { case all, duplicates, recent }

extension Scanner {
    /// When the app landed in its folder (≈ install date).
    static func addedDate(_ url: URL) -> Date? {
        // Spotlight's "Date Added" is what Finder and Storage settings show
        if let d = NSMetadataItem(url: url)?.value(forAttribute: "kMDItemDateAdded") as? Date { return d }
        let v = try? url.resourceValues(forKeys: [.addedToDirectoryDateKey, .creationDateKey])
        return v?.addedToDirectoryDate ?? v?.creationDate
    }

    /// "My TV 2" / "My TV copy" -> "my tv"
    static func baseName(_ s: String) -> String {
        var n = s.lowercased().trimmingCharacters(in: .whitespaces)
        for suffix in [" copy", " কপি"] where n.hasSuffix(suffix) { n.removeLast(suffix.count) }
        if let r = n.range(of: #"\s+\d+$"#, options: .regularExpression) { n.removeSubrange(r) }
        return n
    }

    /// Extra copies of installed apps kept elsewhere: Downloads, Documents, backups, build folders, other drives.
    /// Uses the Spotlight index, the same list macOS's Storage settings shows.
    static func findCopies(of installed: [InstalledApp]) -> [InstalledApp] {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/mdfind")
        p.arguments = ["kMDItemContentType == 'com.apple.application-bundle'"]
        let pipe = Pipe()
        p.standardOutput = pipe; p.standardError = FileHandle.nullDevice
        do { try p.run() } catch { return [] }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()

        let ids = Set(installed.map(\.bundleID).filter { !$0.isEmpty })
        let names = Set(installed.map { baseName($0.url.deletingPathExtension().lastPathComponent) })
        let known = Set(installed.map(\.id))
        let home = FS.home.path
        let skip = ["/System/", "/Library/", "/usr/", "/opt/", "/private/", "/Applications/",
                    home + "/Applications/", home + "/Library/", home + "/.Trash/"]

        var out: [String: InstalledApp] = [:]
        for path in String(decoding: data, as: UTF8.self).split(separator: "\n").map(String.init) {
            if known.contains(path) || skip.contains(where: { path.hasPrefix($0) }) { continue }
            let parent = (path as NSString).deletingLastPathComponent
            // helpers inside another app, Electron inside node_modules, things in the Trash
            if parent.contains(".app/") || parent.hasSuffix(".app") || path.contains("/node_modules/") || path.contains("/.Trashes/") { continue }
            let url = URL(fileURLWithPath: path)
            guard FS.fm.fileExists(atPath: path) else { continue }
            // a mounted installer disk image can't be cleaned
            if (try? url.resourceValues(forKeys: [.volumeIsReadOnlyKey]))?.volumeIsReadOnly == true { continue }
            guard let app = makeApp(url) else { continue }
            let sameID = !app.bundleID.isEmpty && ids.contains(app.bundleID)
            if sameID || names.contains(baseName(url.deletingPathExtension().lastPathComponent)) { out[path] = app }
        }
        return Array(out.values)
    }
}

extension InstalledApp {
    /// A copy that looks like the user's own build output or backup.
    var looksLikeBackup: Bool {
        let p = url.path.lowercased()
        return p.contains("/dist/") || p.contains("/build/") || p.contains("backup") || p.contains("/release/")
    }
}

extension Store {
    private func groupKey(_ a: InstalledApp) -> String {
        a.bundleID.isEmpty ? Scanner.baseName(a.name) : a.bundleID
    }

    /// Every app that exists more than once: the installed one first, then its other copies.
    var duplicateList: [InstalledApp] {
        let all = apps + copies
        let groups = Dictionary(grouping: all, by: groupKey)
        let installed = Set(apps.map(\.id))
        return all.filter { (groups[groupKey($0)]?.count ?? 0) > 1 }.sorted { a, b in
            let ka = groupKey(a), kb = groupKey(b)
            if ka != kb {
                let na = groups[ka]?.first(where: { installed.contains($0.id) })?.name ?? a.name
                let nb = groups[kb]?.first(where: { installed.contains($0.id) })?.name ?? b.name
                return na.localizedCaseInsensitiveCompare(nb) == .orderedAscending
            }
            let ia = installed.contains(a.id), ib = installed.contains(b.id)
            if ia != ib { return ia }
            return a.version.compare(b.version, options: .numeric) == .orderedDescending
        }
    }

    static let recentDays = 30

    /// Apps installed in the last 30 days, newest first.
    var recentList: [InstalledApp] {
        let since = Date().addingTimeInterval(-Double(Store.recentDays) * 86400)
        return apps.filter { (added[$0.id] ?? .distantPast) > since }
            .sorted { (added[$0.id] ?? .distantPast) > (added[$1.id] ?? .distantPast) }
    }

    var everyApp: [InstalledApp] { apps + copies }

    /// An installed app with a newer installed version of the same app (e.g. Photoshop 2026 next to 2027).
    func isOlderInstalled(_ app: InstalledApp) -> Bool {
        apps.contains { $0.id != app.id && groupKey($0) == groupKey(app)
            && $0.version.compare(app.version, options: .numeric) == .orderedDescending }
    }

    /// The second line under an app's name, depending on the filter.
    func detail(for app: InstalledApp) -> String? {
        switch filter {
        case .all: return nil
        case .recent:
            guard let d = added[app.id] else { return nil }
            let f = DateFormatter(); f.dateStyle = .medium; f.timeStyle = .short
            return L("Install: ", "Installed ") + f.string(from: d)
        case .duplicates:
            if apps.contains(where: { $0.id == app.id }) { return L("Install করা · Applications", "Installed · Applications") }
            return FS.short(app.url.deletingLastPathComponent())
        }
    }

    func findCopies() {
        copiesLoading = true
        let installed = apps
        DispatchQueue.global(qos: .utility).async {
            let found = Scanner.findCopies(of: installed)
            DispatchQueue.main.async {
                self.copies = found
                let ids = Set((self.apps + found).map(\.id))
                self.picked = self.picked.filter { ids.contains($0) }
                self.copiesLoading = false
            }
        }
    }
}

/// Small coloured tag next to a name: "Installed", "Copy", "Build/Backup".
struct AppTag: View {
    let text: String
    let color: Color
    var body: some View {
        Text(text).font(.caption2.weight(.semibold)).foregroundStyle(color)
            .padding(.horizontal, 6).padding(.vertical, 2)
            .background(color.opacity(0.15), in: Capsule())
            .fixedSize()
    }
}
