import SwiftUI
import AppKit

// MARK: - Models

struct InstalledApp: Identifiable, Hashable {
    let id: String          // path
    let url: URL
    let name: String
    let bundleID: String
    let version: String
    var isProtected: Bool { bundleID.hasPrefix("com.apple.") && removableAppleApps[bundleID] == nil }
}

struct Leftover: Identifiable, Hashable {
    var id: String { url.path }
    let url: URL
    var size: Int64
    var selected: Bool = true
}

// MARK: - Helpers

enum FS {
    static let fm = FileManager.default
    static let home = fm.homeDirectoryForCurrentUser

    static func size(of url: URL) -> Int64 {
        var isDir: ObjCBool = false
        guard fm.fileExists(atPath: url.path, isDirectory: &isDir) else { return 0 }
        if !isDir.boolValue {
            let v = try? url.resourceValues(forKeys: [.totalFileAllocatedSizeKey, .fileSizeKey])
            return Int64(v?.totalFileAllocatedSize ?? v?.fileSize ?? 0)
        }
        var total: Int64 = 0
        let keys: [URLResourceKey] = [.totalFileAllocatedSizeKey, .fileSizeKey]
        if let en = fm.enumerator(at: url, includingPropertiesForKeys: keys, options: [], errorHandler: { _, _ in true }) {
            for case let f as URL in en {
                let v = try? f.resourceValues(forKeys: Set(keys))
                total += Int64(v?.totalFileAllocatedSize ?? v?.fileSize ?? 0)
            }
        }
        return total
    }

    static func pretty(_ bytes: Int64) -> String {
        ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
    }

    /// Display path with ~ for home.
    static func short(_ url: URL) -> String {
        url.path.replacingOccurrences(of: home.path, with: "~")
    }
}

// MARK: - Scanner

enum Scanner {
    static func appFolders() -> [URL] {
        [URL(fileURLWithPath: "/Applications"),
         URL(fileURLWithPath: "/Applications/Utilities"),
         FS.home.appendingPathComponent("Applications")]
    }

    static func loadApps() -> [InstalledApp] {
        var result: [String: InstalledApp] = [:]
        for folder in appFolders() {
            collect(in: folder, depth: 0, into: &result)
        }
        return result.values.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
    }

    private static func collect(in folder: URL, depth: Int, into result: inout [String: InstalledApp]) {
        guard let items = try? FS.fm.contentsOfDirectory(at: folder, includingPropertiesForKeys: [.isDirectoryKey], options: [.skipsHiddenFiles]) else { return }
        for item in items {
            if item.pathExtension == "app" {
                if let app = makeApp(item) { result[app.id] = app }
            } else if depth < 1, (try? item.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true,
                      item.lastPathComponent != "Utilities" {
                // apps inside sub-folders, e.g. /Applications/Adobe Photoshop/Photoshop.app
                collect(in: item, depth: depth + 1, into: &result)
            }
        }
    }

    private static func makeApp(_ url: URL) -> InstalledApp? {
        let bundle = Bundle(url: url)
        let info = bundle?.infoDictionary ?? [:]
        let name = (info["CFBundleDisplayName"] as? String)
            ?? (info["CFBundleName"] as? String)
            ?? url.deletingPathExtension().lastPathComponent
        var fileName = url.deletingPathExtension().lastPathComponent
        // /Applications/Adobe Illustrator 2026/Adobe Illustrator.app -> "Adobe Illustrator 2026"
        let parent = url.deletingLastPathComponent()
        if !appFolders().contains(parent), parent.lastPathComponent.hasPrefix(fileName) {
            fileName = parent.lastPathComponent
        }
        return InstalledApp(
            id: url.path, url: url,
            name: fileName.isEmpty ? name : fileName,
            bundleID: bundle?.bundleIdentifier ?? "",
            version: (info["CFBundleShortVersionString"] as? String) ?? "")
    }

    /// Finds leftover files belonging to an app inside the Library folders.
    static func leftovers(for app: InstalledApp) -> [Leftover] {
        let bid = app.bundleID.lowercased()
        let appName = app.name.lowercased()
        let execName = (Bundle(url: app.url)?.infoDictionary?["CFBundleExecutable"] as? String)?.lowercased()
        var names: Set<String> = [appName]
        if let e = execName, e.count >= 4 { names.insert(e) }

        let userLib = FS.home.appendingPathComponent("Library")
        let sysLib = URL(fileURLWithPath: "/Library")
        let sub = ["Application Support", "Caches", "Preferences", "Preferences/ByHost",
                   "Saved Application State", "Containers", "Group Containers", "Logs",
                   "HTTPStorages", "WebKit", "Cookies", "LaunchAgents", "Application Scripts",
                   "Logs/DiagnosticReports", "Internet Plug-Ins", "Services"]
        var dirs: [URL] = sub.map { userLib.appendingPathComponent($0) }
        dirs += ["Application Support", "Caches", "Preferences", "LaunchAgents",
                 "LaunchDaemons", "PrivilegedHelperTools", "Logs", "Internet Plug-Ins"]
            .map { sysLib.appendingPathComponent($0) }
        dirs.append(URL(fileURLWithPath: "/private/var/db/receipts"))

        var found: [String: Leftover] = [:]
        for dir in dirs {
            guard let items = try? FS.fm.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil, options: []) else { continue }
            for item in items {
                let entry = item.lastPathComponent.lowercased()
                let base = item.deletingPathExtension().lastPathComponent.lowercased()
                var match = false
                if !bid.isEmpty && bid.count >= 5 && entry.contains(bid) { match = true }
                if names.contains(entry) || names.contains(base) { match = true }
                if match, found[item.path] == nil {
                    found[item.path] = Leftover(url: item, size: 0)
                }
            }
        }
        // extra content for removable Apple apps (e.g. GarageBand sound library)
        for extra in removableAppleApps[app.bundleID] ?? [] {
            let u = URL(fileURLWithPath: (extra as NSString).expandingTildeInPath)
            if FS.fm.fileExists(atPath: u.path) { found[u.path] = Leftover(url: u, size: 0) }
        }
        var list = Array(found.values)
        for i in list.indices { list[i].size = FS.size(of: list[i].url) }
        return list.sorted { $0.url.path < $1.url.path }
    }
}

// MARK: - Removal

enum Remover {
    /// Moves items to the Trash. Items that need admin rights are moved with one password prompt.
    /// Returns the paths that could not be removed.
    static func trash(_ urls: [URL]) -> [String] {
        var needAdmin: [URL] = []
        for u in urls {
            do { try FS.fm.trashItem(at: u, resultingItemURL: nil) }
            catch { needAdmin.append(u) }
        }
        guard !needAdmin.isEmpty else { return [] }

        let trash = FS.home.appendingPathComponent(".Trash").path
        let stamp = Int(Date().timeIntervalSince1970)
        var cmds: [String] = []
        for (i, u) in needAdmin.enumerated() {
            let dest = "\(trash)/\(u.lastPathComponent) \(stamp)-\(i)"
            cmds.append("/bin/mv -f \(q(u.path)) \(q(dest))")
        }
        let shell = cmds.joined(separator: " ; ")
        let script = "do shell script \"\(escapeAS(shell))\" with administrator privileges"
        var err: NSDictionary?
        NSAppleScript(source: script)?.executeAndReturnError(&err)
        return needAdmin.filter { FS.fm.fileExists(atPath: $0.path) }.map { $0.path }
    }

    private static func q(_ s: String) -> String { "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'" }
    private static func escapeAS(_ s: String) -> String {
        s.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"")
    }

    /// Quits the app if it is running.
    static func quitIfRunning(_ app: InstalledApp) {
        let running = NSWorkspace.shared.runningApplications.filter {
            $0.bundleIdentifier == app.bundleID || $0.bundleURL == app.url
        }
        for r in running { r.terminate() }
        if !running.isEmpty { Thread.sleep(forTimeInterval: 1.5) }
        for r in running where !r.isTerminated { r.forceTerminate() }
    }
}

// MARK: - View model

@MainActor
final class Store: ObservableObject {
    @Published var apps: [InstalledApp] = []
    @Published var query = ""
    @Published var loading = false
    @Published var sizes: [String: Int64] = [:]
    @Published var selected: InstalledApp?

    var filtered: [InstalledApp] {
        let q = query.trimmingCharacters(in: .whitespaces)
        guard !q.isEmpty else { return apps }
        return apps.filter { $0.name.localizedCaseInsensitiveContains(q) || $0.bundleID.localizedCaseInsensitiveContains(q) }
    }

    func reload() {
        loading = true
        DispatchQueue.global(qos: .userInitiated).async {
            let list = Scanner.loadApps()
            DispatchQueue.main.async {
                self.apps = list
                self.loading = false
            }
        }
    }

    func loadSize(_ app: InstalledApp) {
        guard sizes[app.id] == nil else { return }
        sizes[app.id] = -1
        DispatchQueue.global(qos: .utility).async {
            let s = FS.size(of: app.url)
            DispatchQueue.main.async { self.sizes[app.id] = s }
        }
    }
}

// MARK: - Views

struct RootView: View {
    @StateObject private var store = Store()
    @StateObject private var cleanup = CleanupModel()
    @StateObject private var bloat = BloatModel()
    @StateObject private var ram = RamModel()
    @ObservedObject private var lang = Lang.shared
    @StateObject private var nav = Nav()

    var body: some View {
        NavigationSplitView {
            Sidebar(nav: nav)
                .navigationSplitViewColumnWidth(min: 190, ideal: 210, max: 260)
        } detail: {
            detail
                .id("\(nav.section)-\(lang.english)")
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .frame(minWidth: 900, minHeight: 640)
        .onAppear { store.reload(); Updater.shared.check(silent: true) }
        .onChange(of: lang.english) { _ in
            // notes are written in the old language: scan again
            cleanup.suggestions = []
            if cleanup.smart.stage == .ready { cleanup.smart.scan() }
            if !bloat.items.isEmpty { bloat.loadItems() }
        }
        .sheet(item: $store.selected) { app in
            UninstallSheet(app: app, shared: store.apps.contains { $0.id != app.id && !app.bundleID.isEmpty && $0.bundleID == app.bundleID }) { removed in
                store.selected = nil
                if removed { store.reload(); cleanup.refreshDisk(); cleanup.suggestions = []; bloat.usage = []; bloat.loadItems() }
            }
        }
    }

    @ViewBuilder private var detail: some View {
        switch nav.section {
        case .home: DashboardView(nav: nav, store: store, cleanup: cleanup, smart: cleanup.smart, bloat: bloat, ram: ram)
        case .smart: CleanupView(m: cleanup, store: store, only: .smart)
        case .suggestions: CleanupView(m: cleanup, store: store, only: .suggestions)
        case .explorer: CleanupView(m: cleanup, store: store, only: .explorer)
        case .bigFiles: CleanupView(m: cleanup, store: store, only: .bigFiles)
        case .uninstall: ContentView(store: store)
        case .unused: BloatView(m: bloat, store: store)
        case .ram: RamView(m: ram)
        case .about: AboutView()
        }
    }
}

struct ContentView: View {
    @ObservedObject var store: Store

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
                TextField(L("Software এর নাম লিখুন…", "Type a software name…"), text: $store.query)
                    .textFieldStyle(.plain)
                    .font(.system(size: 18))
                if !store.query.isEmpty {
                    Button { store.query = "" } label: { Image(systemName: "xmark.circle.fill") }
                        .buttonStyle(.plain).foregroundStyle(.secondary)
                }
                Button { store.reload() } label: { Image(systemName: "arrow.clockwise") }
                    .help(L("আবার খুঁজুন", "Search again"))
            }
            .padding(14)
            .background(.bar)

            Divider()

            if store.loading && store.apps.isEmpty {
                Spacer(); ProgressView(L("Apps খোঁজা হচ্ছে…", "Looking for apps…")); Spacer()
            } else if store.filtered.isEmpty {
                Spacer()
                Text(L("কোন software পাওয়া যায়নি", "No software found")).foregroundStyle(.secondary)
                Spacer()
            } else {
                List(store.filtered) { app in
                    AppRow(app: app, size: store.sizes[app.id])
                        .contentShape(Rectangle())
                        .onTapGesture { store.selected = app }
                        .onAppear { store.loadSize(app) }
                }
                .listStyle(.inset)
            }

            HStack {
                Text(L("\(store.filtered.count) টি software", "\(store.filtered.count) apps"))
                Spacer()
                Text(L("মুছে ফেলা সব কিছু Trash এ যায় — দরকার হলে ফেরত আনতে পারবেন", "Everything removed goes to the Trash — you can get it back if needed"))
            }
            .font(.caption).foregroundStyle(.secondary)
            .padding(.horizontal, 14).padding(.vertical, 8)
        }
    }
}

struct AppRow: View {
    let app: InstalledApp
    let size: Int64?

    var body: some View {
        HStack(spacing: 12) {
            Image(nsImage: NSWorkspace.shared.icon(forFile: app.url.path))
                .resizable().frame(width: 36, height: 36)
            VStack(alignment: .leading, spacing: 2) {
                Text(app.name).font(.system(size: 14, weight: .medium))
                Text(app.bundleID.isEmpty ? FS.short(app.url) : app.bundleID)
                    .font(.caption).foregroundStyle(.secondary).lineLimit(1)
            }
            Spacer()
            if app.isProtected {
                Text("System").font(.caption2).padding(.horizontal, 6).padding(.vertical, 2)
                    .background(Color.secondary.opacity(0.15), in: Capsule())
            }
            if let s = size, s >= 0 {
                Text(FS.pretty(s)).font(.caption).foregroundStyle(.secondary).monospacedDigit()
            }
            Image(systemName: "chevron.right").foregroundStyle(.tertiary)
        }
        .padding(.vertical, 4)
    }
}

enum Stage { case choose, scanning, review, working, finished(String) }

final class SheetModel: ObservableObject {
    @Published var stage: Stage = .choose
    @Published var leftovers: [Leftover] = []
}

struct UninstallSheet: View {
    let app: InstalledApp
    let shared: Bool   // another installed app uses the same bundle ID (e.g. Photoshop 2026 & 2027)
    let done: (Bool) -> Void

    @StateObject private var m = SheetModel()
    private var stage: Stage { m.stage }
    private var leftovers: [Leftover] { m.leftovers }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: 14) {
                Image(nsImage: NSWorkspace.shared.icon(forFile: app.url.path))
                    .resizable().frame(width: 56, height: 56)
                VStack(alignment: .leading, spacing: 3) {
                    Text(app.name).font(.title2.bold())
                    Text([app.version.isEmpty ? nil : "Version \(app.version)", FS.short(app.url)]
                        .compactMap { $0 }.joined(separator: "  ·  "))
                        .font(.caption).foregroundStyle(.secondary)
                }
            }

            switch stage {
            case .choose: chooseView
            case .scanning: HStack { ProgressView().controlSize(.small); Text(L("সব related ফাইল খোঁজা হচ্ছে…", "Finding all related files…")) }
            case .review: reviewView
            case .working: HStack { ProgressView().controlSize(.small); Text(L("মুছে ফেলা হচ্ছে…", "Removing…")) }
            case .finished(let msg):
                Text(msg)
                HStack { Spacer(); Button(L("ঠিক আছে", "OK")) { done(true) }.keyboardShortcut(.defaultAction) }
            }
        }
        .padding(24)
        .frame(width: 600)
    }

    @ViewBuilder private var chooseView: some View {
        if app.isProtected {
            Label(L("এটি Apple এর system app — নিরাপত্তার জন্য মুছা বন্ধ রাখা হয়েছে।", "This is an Apple system app — deleting is blocked for safety."), systemImage: "lock.fill")
                .foregroundStyle(.orange)
            HStack { Spacer(); Button(L("বন্ধ করুন", "Turn off")) { done(false) }.keyboardShortcut(.cancelAction) }
        } else {
            OptionButton(icon: "trash", color: .blue,
                         title: L("শুধু Uninstall", "Only uninstall"),
                         subtitle: L("শুধু app টি Trash এ যাবে। Settings ও data থেকে যাবে।", "Only the app goes to the Trash. Settings and data stay.")) {
                removeSimple()
            }
            OptionButton(icon: "flame.fill", color: .green,
                         title: "Full Uninstall",
                         subtitle: L("App + Cache, Preferences, Application Support, Logs, Containers সহ সব ফাইল মুছে দিবে।", "Deletes the app plus Cache, Preferences, Application Support, Logs, Containers and every other file.")) {
                scanFull()
            }
            HStack { Spacer(); Button(L("বাতিল", "Cancel")) { done(false) }.keyboardShortcut(.cancelAction) }
        }
    }

    @ViewBuilder private var reviewView: some View {
        let total = leftovers.filter(\.selected).reduce(0) { $0 + $1.size } + FS.size(of: app.url)
        Text(leftovers.isEmpty
             ? L("কোন extra ফাইল পাওয়া যায়নি — শুধু app টি মুছবে।", "No extra files found — only the app will be deleted.")
             : L("এই \(leftovers.count) টি related ফাইল/ফোল্ডার পাওয়া গেছে। যেগুলো রাখতে চান সেগুলোর টিক তুলে দিন:", "Found \(leftovers.count) related files/folders. Untick the ones you want to keep:"))
            .font(.callout)
        if shared && !leftovers.isEmpty {
            Label(L("সতর্কতা: এই app এর আরেকটি version ইনস্টল আছে যেটা একই ফাইল ব্যবহার করে। তাই ফাইলগুলোতে টিক দেওয়া হয়নি — নিশ্চিত হলে নিজে টিক দিন।", "Warning: another version of this app is installed and uses the same files. So they're not ticked — tick them yourself if you're sure."), systemImage: "exclamationmark.triangle.fill")
                .font(.caption).foregroundStyle(.orange)
        }
        if !leftovers.isEmpty {
            List {
                ForEach($m.leftovers) { $item in
                    Toggle(isOn: $item.selected) {
                        HStack {
                            Text(FS.short(item.url)).font(.system(size: 12, design: .monospaced))
                                .lineLimit(1).truncationMode(.middle)
                            Spacer()
                            Text(FS.pretty(item.size)).font(.caption).foregroundStyle(.secondary).monospacedDigit()
                        }
                    }
                    .toggleStyle(.checkbox)
                }
            }
            .frame(height: 240)
            .listStyle(.bordered)
        }
        HStack {
            Text(L("মোট খালি হবে: \(FS.pretty(total))", "Total to free: \(FS.pretty(total))")).font(.callout.bold())
            Spacer()
            Button(L("বাতিল", "Cancel")) { done(false) }.keyboardShortcut(.cancelAction)
            Button(L("সব মুছে ফেলুন", "Delete everything")) { removeFull() }
                .keyboardShortcut(.defaultAction)
                .tint(.red)
        }
    }

    private func removeSimple() {
        m.stage = .working
        DispatchQueue.global().async {
            Remover.quitIfRunning(app)
            let failed = Remover.trash([app.url])
            DispatchQueue.main.async { m.stage = .finished(message(failed, full: false)) }
        }
    }

    private func scanFull() {
        m.stage = .scanning
        DispatchQueue.global().async {
            let found = Scanner.leftovers(for: app)
            let list = shared ? found.map { var l = $0; l.selected = false; return l } : found
            DispatchQueue.main.async { m.leftovers = list; m.stage = .review }
        }
    }

    private func removeFull() {
        m.stage = .working
        let urls = [app.url] + leftovers.filter(\.selected).map(\.url)
        DispatchQueue.global().async {
            Remover.quitIfRunning(app)
            let failed = Remover.trash(urls)
            DispatchQueue.main.async { m.stage = .finished(message(failed, full: true)) }
        }
    }

    private func message(_ failed: [String], full: Bool) -> String {
        if failed.isEmpty {
            return full ? L("✅ \(app.name) এবং এর সব ফাইল Trash এ পাঠানো হয়েছে। Trash খালি করলে স্থায়ীভাবে মুছে যাবে।", "✅ \(app.name) and all its files were moved to the Trash. Empty the Trash to delete them for good.")
                        : L("✅ \(app.name) Trash এ পাঠানো হয়েছে।", "✅ \(app.name) was moved to the Trash.")
        }
        return L("⚠️ কিছু ফাইল মুছা যায়নি (password দেননি বা অনুমতি নেই):\n", "⚠️ Some files couldn't be deleted (no password given or no permission):\n") + failed.joined(separator: "\n")
    }
}

struct OptionButton: View {
    let icon: String, color: Color, title: String, subtitle: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 14) {
                Image(systemName: icon).font(.system(size: 22)).foregroundStyle(.white)
                    .frame(width: 44, height: 44).background(color.gradient, in: RoundedRectangle(cornerRadius: 10))
                VStack(alignment: .leading, spacing: 3) {
                    Text(title).font(.headline)
                    Text(subtitle).font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }
                Spacer()
            }
            .padding(12)
            .background(Color.primary.opacity(0.06), in: RoundedRectangle(cornerRadius: 12))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

// MARK: - App

struct EasyUninstallApp: App {
    init() { NSApplication.shared.setActivationPolicy(.regular) }
    var body: some Scene {
        WindowGroup("Easy Uninstall") { RootView() }
            .defaultSize(width: 1120, height: 800)
            .windowResizability(.contentMinSize)
    }
}

// Dry run from Terminal: EasyUninstall --scan "App Name"  (nothing is deleted)
let args = CommandLine.arguments
if args.count >= 3, args[1] == "--scan" {
    let q = args[2].lowercased()
    for app in Scanner.loadApps() where app.name.lowercased().contains(q) {
        print("== \(app.name) [\(app.bundleID)] \(FS.short(app.url)) \(FS.pretty(FS.size(of: app.url)))")
        for l in Scanner.leftovers(for: app) { print("   \(FS.short(l.url))  \(FS.pretty(l.size))") }
    }
    exit(0)
}
if args.count >= 2, args[1] == "--smart" {   // dry run: list what Smart Clean would delete
    for i in SmartScanner.scan() { print("\(i.skipReason == nil ? "🟢" : "⏸") \(FS.pretty(i.size))  \(i.title)  \(i.skipReason ?? "")") }
    exit(0)
}
if args.count >= 2, args[1] == "--ram" {   // dry run: RAM summary
    let i = MemInfo.read()
    print("used \(FS.pretty(i.used)) / \(FS.pretty(i.total))  swap \(FS.pretty(i.swapUsed))/\(FS.pretty(i.swapTotal))  pressure \(i.pressure)")
    for a in RamScanner.scan().prefix(12) { print("\(a.caution ? "🔴" : "🟢") \(FS.pretty(a.memory))  \(a.name)  \(a.note)") }
    exit(0)
}
if args.count >= 2, args[1] == "--big" {   // dry run: classify big files
    let m = CleanupModel(); m.scanBigFiles()
    while !m.bigScanned { RunLoop.main.run(until: Date().addingTimeInterval(0.5)) }
    for e in m.bigFiles { let v = BigFileVerdict.of(e.url); print("\(v.safe ? "🟢" : "🔴") \(FS.pretty(e.size))  \(FS.short(e.url))\n     \(v.note)") }
    exit(0)
}
if args.count >= 2, args[1] == "--bloat" {   // dry run: print usage + background items
    for u in UsageScanner.scan(Scanner.loadApps()).prefix(15) {
        print("\(u.days.map { "\($0)d" } ?? "never")\(u.estimated ? "~" : "")  \(FS.pretty(u.size))  \(u.app.name)")
    }
    print("---")
    for i in BackgroundScanner.scan() {
        print("\(i.orphan ? "ORPHAN" : i.disabled ? "off" : "on")\(i.running ? "*" : "")  \(i.owner)  [\(i.label)] \(i.caution ? "(caution)" : "")")
    }
    exit(0)
}
if args.count >= 2, args[1] == "--suggest" {   // dry run: print suggestions only
    for s in SuggestionScanner.scan(apps: Scanner.loadApps()) {
        print("[\(s.safety == .safe ? "SAFE" : "REVIEW")] \(s.title)  \(FS.pretty(s.size))")
    }
    exit(0)
}
EasyUninstallApp.main()
