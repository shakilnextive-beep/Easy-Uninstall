import SwiftUI
import AppKit

// MARK: - Disk info

struct DiskInfo {
    var total: Int64 = 0
    var free: Int64 = 0
    var used: Int64 { total - free }

    static func read() -> DiskInfo {
        let keys: Set<URLResourceKey> = [.volumeTotalCapacityKey, .volumeAvailableCapacityForImportantUsageKey, .volumeAvailableCapacityKey]
        guard let v = try? URL(fileURLWithPath: "/").resourceValues(forKeys: keys) else { return DiskInfo() }
        let free = v.volumeAvailableCapacityForImportantUsage ?? Int64(v.volumeAvailableCapacity ?? 0)
        return DiskInfo(total: Int64(v.volumeTotalCapacity ?? 0), free: free)
    }
}

// MARK: - Suggestions

struct Suggestion: Identifiable {
    enum Kind { case deleteContents, emptyTrash, explore, trashFolder, oldVersion(InstalledApp), snapshots(Int) }
    enum Safety { case safe, review }
    let id = UUID()
    let title: String
    let detail: String
    let icon: String
    let safety: Safety
    let paths: [URL]
    var size: Int64
    let kind: Kind
}

enum SuggestionScanner {
    static func scan(apps: [InstalledApp]) -> [Suggestion] {
        let h = FS.home
        func p(_ s: String) -> URL { h.appendingPathComponent(s) }
        func existing(_ urls: [URL]) -> [URL] { urls.filter { FS.fm.fileExists(atPath: $0.path) } }
        var out: [Suggestion] = []

        func addSafe(_ title: String, _ detail: String, _ icon: String, _ urls: [URL]) {
            let ex = existing(urls)
            let size = ex.reduce(0) { $0 + FS.size(of: $1) }
            if size > 10_000_000 {
                out.append(Suggestion(title: title, detail: detail, icon: icon, safety: .safe, paths: ex, size: size, kind: .deleteContents))
            }
        }

        let adobeCommon = p("Library/Application Support/Adobe/Common")
        addSafe("Adobe Media Cache",
                L("Premiere Pro / After Effects video preview দ্রুত দেখানোর জন্য এই ফাইল বানায়। মুছলে project খোলার সময় আবার নিজে বানিয়ে নেবে। Video editor দের disk ভরার এটা সবচেয়ে বড় কারণ।", "Premiere Pro / After Effects create these files to show video previews fast. Delete them and they're rebuilt when you open a project. This is the #1 reason video editors' disks fill up."),
                "film.stack",
                ["Media Cache Files", "Media Cache", "Peak Files", "Team Projects Cache", "PTX"].map { adobeCommon.appendingPathComponent($0) })
        addSafe("App Cache",
                L("App গুলো দ্রুত চলার জন্য অস্থায়ী ফাইল জমায় (browser, Adobe, Spotify ইত্যাদি)। মুছলে app গুলো দরকার মতো আবার বানিয়ে নেবে। মুছার আগে বড় app গুলো বন্ধ রাখা ভালো।", "Apps store temporary files to run faster (browsers, Adobe, Spotify and more). Delete them and apps rebuild what they need. Best to quit big apps first."),
                "shippingbox", [p("Library/Caches")])
        addSafe(L("Log ফাইল", "Log files"),
                L("App ও system এর হিসাব-নিকাশের রেকর্ড। সাধারণ ব্যবহারকারীর কোন কাজে লাগে না।", "Records kept by apps and the system. No use for everyday users."),
                "doc.text", [p("Library/Logs")])
        addSafe("Developer / Python / Node cache",
                L("Programming tool গুলোর download cache। দরকার হলে আবার download হবে।", "Download cache from programming tools. Re-downloaded if needed."),
                "hammer", [p(".cache"), p(".npm/_cacache"), p("Library/Developer/Xcode/DerivedData"),
                           p("Library/Developer/CoreSimulator/Caches")])

        // Trash
        let trashSize = FS.size(of: p(".Trash"))
        out.append(Suggestion(title: L("Trash (ময়লার ঝুড়ি)", "Trash"),
                              detail: L("Trash এ রাখা ফাইলও disk এর জায়গা নেয়! খালি না করলে জায়গা ফেরত পাবেন না।", "Files in the Trash still take disk space! You won't get the space back until you empty it.") + (trashSize == 0 ? L(" (size দেখতে Full Disk Access লাগে)", " (needs Full Disk Access to show size)") : ""),
                              icon: "trash", safety: .safe, paths: [p(".Trash")], size: trashSize, kind: .emptyTrash))

        // Old app versions (e.g. Photoshop 2026 + 2027)
        // Same app installed twice: same bundle ID, or same name with a different year ("Premiere Pro 2026" / "2027")
        let yearRx = try! NSRegularExpression(pattern: "\\s+(19|20)\\d{2}$")
        func key(_ a: InstalledApp) -> String {
            let r = NSRange(a.name.startIndex..., in: a.name)
            if yearRx.firstMatch(in: a.name, range: r) != nil {
                return "name:" + yearRx.stringByReplacingMatches(in: a.name, range: r, withTemplate: "")
            }
            return "id:" + a.bundleID
        }
        let groups = Dictionary(grouping: apps.filter { !$0.bundleID.isEmpty && !$0.isProtected }, by: key)
        for (_, list) in groups where list.count > 1 {
            let sorted = list.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
            let newest = sorted.last!
            for old in sorted.dropLast() {
                // Adobe apps live in their own folder, e.g. /Applications/Adobe Photoshop 2026/
                let parent = old.url.deletingLastPathComponent()
                let size = FS.size(of: parent.path != "/Applications" && parent.lastPathComponent == old.name ? parent : old.url)
                out.append(Suggestion(title: L("পুরনো version: \(old.name)", "Old version: \(old.name)"),
                                      detail: L("\(newest.name) ও ইনস্টল আছে। দুটো version রাখার দরকার না থাকলে পুরনোটা uninstall করুন।", "\(newest.name) is also installed. If you don't need both versions, uninstall the old one.")
                                        + (old.bundleID.hasPrefix("com.adobe") ? L(" Adobe app হলে Creative Cloud app থেকে uninstall করা সবচেয়ে ভালো।", " For Adobe apps, uninstalling from the Creative Cloud app works best.") : ""),
                                      icon: "square.stack.3d.down.right", safety: .safe, paths: [old.url], size: size, kind: .oldVersion(old)))
            }
        }

        // Folders in /Applications left behind by apps that were already removed
        let appsDir = URL(fileURLWithPath: "/Applications")
        for d in (try? FS.fm.contentsOfDirectory(at: appsDir, includingPropertiesForKeys: [.isDirectoryKey], options: [.skipsHiddenFiles])) ?? [] {
            guard d.pathExtension != "app", d.lastPathComponent != "Utilities",
                  (try? d.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true else { continue }
            let inside = (try? FS.fm.contentsOfDirectory(atPath: d.path)) ?? []
            if inside.contains(where: { $0.hasSuffix(".app") }) { continue }
            // keep folders holding shortcuts to apps that still exist (e.g. Adobe Creative Cloud)
            let hasLiveLink = inside.contains { name in
                let full = d.appendingPathComponent(name).path
                guard let dest = try? FS.fm.destinationOfSymbolicLink(atPath: full) else { return false }
                return FS.fm.fileExists(atPath: dest)
            }
            if hasLiveLink { continue }
            let size = FS.size(of: d)
            out.append(Suggestion(title: L("অবশিষ্ট ফোল্ডার: \(d.lastPathComponent)", "Leftover folder: \(d.lastPathComponent)"),
                                  detail: L("এই app টি আগেই মুছে ফেলা হয়েছে, কিন্তু ফোল্ডারটা (plug-in, script ইত্যাদি) রয়ে গেছে। এখন আর কোন কাজে লাগে না।", "This app was already deleted, but its folder (plug-ins, scripts and so on) was left behind. It's no longer used."),
                                  icon: "folder.badge.minus", safety: .safe, paths: [d], size: size, kind: .trashFolder))
        }

        // Review folders
        for (name, detail, icon) in [
            ("Downloads", L("Download করা ফাইল — installer (.dmg, .pkg, .zip) ইনস্টল হয়ে গেলে আর লাগে না।", "Downloaded files — installers (.dmg, .pkg, .zip) aren't needed once installed."), "arrow.down.circle"),
            ("Documents/Adobe", L("Premiere / After Effects এর project, Auto-Save ও preview ফাইল। পুরনো project এর Auto-Save গুলো সাধারণত মুছা যায়।", "Premiere / After Effects projects, Auto-Saves and previews. Auto-Saves of old projects can usually go."), "folder"),
            ("Library/Application Support/MobileSync/Backup", L("iPhone / iPad এর backup। পুরনো device এর backup থাকলে মুছতে পারেন।", "iPhone / iPad backups. Backups of old devices can be deleted."), "iphone"),
            ("Movies", L("Video ফাইল।", "Video files."), "film"),
            ("Desktop", L("Desktop এর ফাইল।", "Files on your Desktop."), "menubar.dock.rectangle"),
        ] {
            let u = p(name)
            guard FS.fm.fileExists(atPath: u.path) else { continue }
            let s = FS.size(of: u)
            if s > 500_000_000 {
                out.append(Suggestion(title: name.components(separatedBy: "/").last!, detail: detail, icon: icon,
                                      safety: .review, paths: [u], size: s, kind: .explore))
            }
        }

        // Time Machine local snapshots
        let snaps = Shell.run("/usr/bin/tmutil", ["listlocalsnapshots", "/"])
            .split(separator: "\n").filter { $0.contains("com.apple.TimeMachine") }.count
        if snaps > 0 {
            out.append(Suggestion(title: L("Time Machine snapshot (\(snaps) টি)", "Time Machine snapshots (\(snaps))"),
                                  detail: L("macOS নিজে থেকে backup এর কপি রাখে — এটা \"System Data\" বড় দেখানোর কারণ। মুছলে কোন ক্ষতি নেই, তবে Mac এর password লাগবে।", "macOS keeps backup copies by itself — this is why \"System Data\" looks big. Deleting them is harmless, but needs your Mac password."),
                                  icon: "clock.arrow.circlepath", safety: .safe, paths: [], size: 0, kind: .snapshots(snaps)))
        }

        return out.sorted { a, b in
            if a.safety != b.safety { return a.safety == .safe }
            return a.size > b.size
        }
    }
}

enum Shell {
    static func run(_ cmd: String, _ args: [String]) -> String {
        let p = Process(); p.executableURL = URL(fileURLWithPath: cmd); p.arguments = args
        let pipe = Pipe(); p.standardOutput = pipe; p.standardError = Pipe()
        do { try p.run() } catch { return "" }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        return String(data: data, encoding: .utf8) ?? ""
    }
}

// MARK: - Explorer & large files

struct Entry: Identifiable, Hashable {
    var id: String { url.path }
    let url: URL
    var size: Int64
    let isFolder: Bool
}

enum Safe {
    /// Only allow trashing things that are clearly user content, never system or top-level folders.
    static func canTrash(_ url: URL) -> Bool {
        let path = url.standardizedFileURL.path
        let home = FS.home.path
        let blocked = [home, home + "/Library", home + "/Documents", home + "/Desktop", home + "/Downloads",
                       home + "/Movies", home + "/Music", home + "/Pictures", home + "/Library/Application Support",
                       home + "/Library/Containers", home + "/Library/Group Containers", home + "/Library/Preferences",
                       home + "/Library/Mobile Documents", home + "/Library/Mail", home + "/Library/Keychains"]
        if blocked.contains(path) { return false }
        if path.hasPrefix(home + "/Library/Keychains") { return false }
        if path.hasPrefix(home + "/") { return true }
        if path.hasPrefix("/Applications/") && !path.contains("/Contents/") {
            let bid = Bundle(url: url)?.bundleIdentifier ?? ""
            return !bid.hasPrefix("com.apple.")
        }
        return false
    }
}

enum CleanTab: Hashable { case smart, suggestions, explorer, bigFiles }

final class CleanupModel: ObservableObject {
    @Published var tab: CleanTab = .smart
    let smart = SmartModel()
    @Published var disk = DiskInfo.read()

    @Published var suggestions: [Suggestion] = []
    @Published var scanning = false

    @Published var stack: [URL] = []          // explorer path; empty = overview
    @Published var entries: [Entry] = []
    @Published var exploring = false
    private var sizeCache: [String: Int64] = [:]

    @Published var bigFiles: [Entry] = []
    @Published var bigScanning = false
    @Published var bigScanned = false
    @Published var query = ""
    @Published var risk: RiskFilter = .all
    @Published var bigQuery = ""
    @Published var bigKind: FileKind = .all
    @Published var bigRisk: RiskFilter = .all
    @Published var explorerQuery = ""

    func refreshDisk() { disk = DiskInfo.read() }

    func scanSuggestions(apps: [InstalledApp]) {
        scanning = true
        DispatchQueue.global(qos: .userInitiated).async {
            let s = SuggestionScanner.scan(apps: apps.isEmpty ? Scanner.loadApps() : apps)
            DispatchQueue.main.async { self.suggestions = s; self.scanning = false; self.refreshDisk() }
        }
    }

    // Explorer
    var roots: [URL] { [FS.home, URL(fileURLWithPath: "/Applications"), URL(fileURLWithPath: "/Library"),
                        URL(fileURLWithPath: "/private/var"), URL(fileURLWithPath: "/opt"), URL(fileURLWithPath: "/usr/local")] }

    func open(_ url: URL?) {
        if let u = url { stack.append(u) }
        load()
    }
    func back() { if !stack.isEmpty { stack.removeLast(); load() } }
    func jump(to i: Int) { stack = Array(stack.prefix(i)); load() }

    func load() {
        explorerQuery = ""
        let children: [URL]
        if let cur = stack.last {
            children = (try? FS.fm.contentsOfDirectory(at: cur, includingPropertiesForKeys: [.isDirectoryKey, .isPackageKey], options: [])) ?? []
        } else {
            children = roots.filter { FS.fm.fileExists(atPath: $0.path) }
        }
        entries = children.map { u in
            let v = try? u.resourceValues(forKeys: [.isDirectoryKey, .isPackageKey])
            let folder = (v?.isDirectory ?? false) && !(v?.isPackage ?? false)
            return Entry(url: u, size: sizeCache[u.path] ?? -1, isFolder: folder)
        }
        sort()
        let todo = entries.filter { $0.size < 0 }.map(\.url)
        guard !todo.isEmpty else { exploring = false; return }
        exploring = true
        let key = stack.last?.path ?? ""
        let q = OperationQueue(); q.maxConcurrentOperationCount = 4
        var remaining = todo.count
        for u in todo {
            q.addOperation {
                let s = FS.size(of: u)
                DispatchQueue.main.async {
                    self.sizeCache[u.path] = s
                    remaining -= 1
                    guard (self.stack.last?.path ?? "") == key else { return }
                    if let i = self.entries.firstIndex(where: { $0.url == u }) { self.entries[i].size = s }
                    // keep rows still while measuring so they don't jump under the mouse
                    if remaining == 0 { self.sort(); self.exploring = false }
                }
            }
        }
    }

    private func sort() { entries.sort { $0.size > $1.size } }

    func forget(_ url: URL) {
        // drop cached sizes of the item and its parents so totals refresh
        sizeCache = sizeCache.filter { !url.path.hasPrefix($0.key) && !$0.key.hasPrefix(url.path) }
    }

    func scanBigFiles(minBytes: Int64 = 500_000_000) {
        bigScanning = true
        DispatchQueue.global(qos: .userInitiated).async {
            var found: [Entry] = []
            let keys: [URLResourceKey] = [.totalFileAllocatedSizeKey, .isRegularFileKey]
            let skip = [FS.home.appendingPathComponent(".Trash").path, FS.home.appendingPathComponent("Library/Containers").path]
            if let en = FS.fm.enumerator(at: FS.home, includingPropertiesForKeys: keys, options: [], errorHandler: { _, _ in true }) {
                for case let f as URL in en {
                    if skip.contains(f.path) { en.skipDescendants(); continue }
                    guard let v = try? f.resourceValues(forKeys: Set(keys)), v.isRegularFile == true else { continue }
                    let s = Int64(v.totalFileAllocatedSize ?? 0)
                    if s >= minBytes { found.append(Entry(url: f, size: s, isFolder: false)) }
                }
            }
            found.sort { $0.size > $1.size }
            DispatchQueue.main.async { self.bigFiles = found; self.bigScanning = false; self.bigScanned = true }
        }
    }
}

// MARK: - Actions

enum Actions {
    static func confirm(_ title: String, _ text: String, ok: String) -> Bool {
        let a = NSAlert()
        a.messageText = title; a.informativeText = text
        a.addButton(withTitle: ok); a.addButton(withTitle: L("বাতিল", "Cancel"))
        a.alertStyle = .warning
        return a.runModal() == .alertFirstButtonReturn
    }

    static func info(_ title: String, _ text: String) {
        let a = NSAlert(); a.messageText = title; a.informativeText = text; a.runModal()
    }

    static func reveal(_ url: URL) { NSWorkspace.shared.activateFileViewerSelecting([url]) }

    /// Permanently deletes the contents of the given folders (cache-type data that apps rebuild).
    static func deleteContents(of folders: [URL]) {
        for f in folders {
            let items = (try? FS.fm.contentsOfDirectory(at: f, includingPropertiesForKeys: nil, options: [])) ?? []
            for i in items { try? FS.fm.removeItem(at: i) }
        }
    }

    static func emptyTrash() {
        var err: NSDictionary?
        NSAppleScript(source: "tell application \"Finder\" to empty trash")?.executeAndReturnError(&err)
    }

    static func deleteSnapshots() {
        var err: NSDictionary?
        NSAppleScript(source: "do shell script \"/usr/bin/tmutil deletelocalsnapshots / ; /usr/bin/tmutil thinlocalsnapshots / 999999999999 4\" with administrator privileges")?
            .executeAndReturnError(&err)
    }
}

// MARK: - Views

struct DiskBar: View {
    let disk: DiskInfo
    var body: some View {
        let frac = disk.total > 0 ? Double(disk.used) / Double(disk.total) : 0
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline) {
                Text("Macintosh HD").font(.headline)
                Spacer()
                Text(L("খালি আছে \(FS.pretty(disk.free))", "\(FS.pretty(disk.free)) free")).bold()
                Text(L("/ মোট \(FS.pretty(disk.total))", "of \(FS.pretty(disk.total))")).foregroundStyle(.secondary)
            }
            GeometryReader { g in
                ZStack(alignment: .leading) {
                    Capsule().fill(Color.secondary.opacity(0.2))
                    Capsule().fill(frac > 0.9 ? Color.red.gradient : frac > 0.75 ? Color.orange.gradient : Color.blue.gradient)
                        .frame(width: max(8, g.size.width * frac))
                }
            }
            .frame(height: 12)
            if frac > 0.85 {
                Text(L("⚠️ Disk প্রায় ভরা! Mac ধীর হয়ে যেতে পারে। নিচের পরামর্শগুলো দেখুন।", "⚠️ Disk almost full! Your Mac may slow down. See the suggestions below.")).font(.caption).foregroundStyle(.red)
            }
        }
        .padding(14)
        .background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 12))
    }
}

struct CleanupView: View {
    @ObservedObject var m: CleanupModel
    @ObservedObject var store: Store
    var only: CleanTab? = nil      // set by the sidebar: show just this part, no picker

    var body: some View {
        VStack(spacing: 12) {
            DiskBar(disk: m.disk)
            if only == nil {
            Picker("", selection: $m.tab) {
                Text("✨ Smart Clean").tag(CleanTab.smart)
                Text(L("💡 পরামর্শ", "💡 Suggestions")).tag(CleanTab.suggestions)
                Text(L("📂 কোথায় জায়গা গেল?", "📂 Where did my space go?")).tag(CleanTab.explorer)
                Text(L("🐘 বড় ফাইল", "🐘 Big files")).tag(CleanTab.bigFiles)
            }
            .pickerStyle(.segmented).labelsHidden()
            }

            switch only ?? m.tab {
            case .smart:
                SmartCleanView(m: m.smart) {
                    m.refreshDisk(); m.suggestions = []; m.bigScanned = false; m.bigFiles = []; m.entries = []
                }
            case .suggestions: suggestionsView
            case .explorer: explorerView
            case .bigFiles: bigFilesView
            }
        }
        .padding(14)
        .onAppear {
            m.refreshDisk()
            if m.suggestions.isEmpty && !m.scanning { m.scanSuggestions(apps: store.apps) }
            if only == .explorer && m.entries.isEmpty { m.load() }
        }
        .onChange(of: m.tab) { t in if t == .explorer && m.entries.isEmpty { m.load() } }
    }

    // Suggestions
    @ViewBuilder private var suggestionsView: some View {
        if m.scanning {
            Spacer(); ProgressView(L("আপনার Mac পরীক্ষা করা হচ্ছে…", "Checking your Mac…")); Spacer()
        } else {
            let found = m.suggestions.filter { matchesQuery(m.query, $0.title, $0.detail) }
            FilterBar(query: $m.query, risk: $m.risk,
                      safeCount: found.filter { $0.safety == .safe }.count,
                      riskyCount: found.filter { $0.safety == .review }.count)
            ScrollView {
                VStack(alignment: .leading, spacing: 10) {
                    RiskLegend()
                    let safe = m.risk.allows(safe: true) ? found.filter { $0.safety == .safe } : []
                    let review = m.risk.allows(safe: false) ? found.filter { $0.safety == .review } : []
                    if safe.isEmpty && review.isEmpty {
                        Text(L("এই filter এ কিছু পাওয়া যায়নি", "Nothing matches this filter")).foregroundStyle(.secondary).frame(maxWidth: .infinity).padding(.top, 30)
                    }
                    let safeTotal = safe.reduce(0) { $0 + $1.size }
                    if !safe.isEmpty {
                        sectionHeader(L("✅ মুছলে কোন সমস্যা নেই", "✅ Safe to delete"), safeTotal > 0 ? L("প্রায় \(FS.pretty(safeTotal)) খালি হবে", "About \(FS.pretty(safeTotal)) will be freed") : "", .green)
                        ForEach(safe) { s in suggestionCard(s) }
                    }
                    if !review.isEmpty {
                        sectionHeader(L("⚠️ সাবধান — মুছলে সমস্যা হতে পারে", "⚠️ Careful — deleting may cause problems"), L("এগুলো আপনার নিজের ফাইল। সব একসাথে মুছবেন না — ভিতরে ঢুকে দেখে শুধু অপ্রয়োজনীয়গুলো মুছুন", "These are your own files. Don't delete them all at once — open them and remove only what you don't need"), .red)
                        ForEach(review) { s in suggestionCard(s) }
                    }
                    Text(L("বাকি জায়গা macOS নিজে ব্যবহার করে (system file, virtual memory)। এগুলো হাত দেওয়া ঠিক না।", "The rest is used by macOS itself (system files, virtual memory). Best not to touch it."))
                        .font(.caption).foregroundStyle(.secondary).padding(.top, 6)
                }
            }
            HStack {
                Spacer()
                Button { m.scanSuggestions(apps: store.apps) } label: { Label(L("আবার পরীক্ষা করুন", "Check again"), systemImage: "arrow.clockwise") }
            }
        }
    }

    private func sectionHeader(_ t: String, _ sub: String, _ c: Color) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(t).font(.headline).foregroundStyle(c)
            if !sub.isEmpty { Text(sub).font(.caption).foregroundStyle(.secondary) }
        }.padding(.top, 6)
    }

    private func suggestionCard(_ s: Suggestion) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: s.icon).font(.system(size: 18)).foregroundStyle(.white)
                .frame(width: 36, height: 36)
                .background((s.safety == .safe ? Color.green : Color.red).gradient, in: RoundedRectangle(cornerRadius: 8))
            VStack(alignment: .leading, spacing: 3) {
                HStack {
                    Text(s.title).font(.system(size: 14, weight: .semibold))
                    Spacer()
                    if s.size > 0 { Text(FS.pretty(s.size)).font(.system(size: 14, weight: .bold)).monospacedDigit() }
                }
                Text(s.detail).font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                HStack {
                    Spacer()
                    if let p = s.paths.first { Button(L("Finder এ দেখুন", "Show in Finder")) { Actions.reveal(p) } }
                    actionButton(s)
                }.padding(.top, 2)
            }
        }
        .padding(12)
        .background(Color.primary.opacity(0.04), in: RoundedRectangle(cornerRadius: 10))
    }

    @ViewBuilder private func actionButton(_ s: Suggestion) -> some View {
        switch s.kind {
        case .deleteContents:
            Button(L("পরিষ্কার করুন", "Clean up")) {
                if Actions.confirm(L("\(s.title) মুছবেন?", "Delete \(s.title)?"), L("\(FS.pretty(s.size)) খালি হবে। এগুলো অস্থায়ী ফাইল, app গুলো দরকার হলে আবার বানিয়ে নেবে। (এগুলো সরাসরি মুছে যাবে, Trash এ যাবে না)", "Frees \(FS.pretty(s.size)). These are temporary files; apps rebuild them when needed. (Deleted directly, not moved to the Trash)"), ok: L("মুছে ফেলুন", "Delete")) {
                    DispatchQueue.global().async {
                        Actions.deleteContents(of: s.paths)
                        DispatchQueue.main.async { m.scanSuggestions(apps: store.apps) }
                    }
                }
            }.buttonStyle(.borderedProminent).tint(.green)
        case .emptyTrash:
            Button(L("Trash খালি করুন", "Empty Trash")) {
                if Actions.confirm(L("Trash খালি করবেন?", "Empty the Trash?"), L("Trash এর সব ফাইল স্থায়ীভাবে মুছে যাবে, আর ফেরত আনা যাবে না।", "Everything in the Trash will be permanently deleted and can't be recovered."), ok: L("খালি করুন", "Empty")) {
                    Actions.emptyTrash(); m.scanSuggestions(apps: store.apps)
                }
            }.buttonStyle(.borderedProminent).tint(.green)
        case .snapshots:
            Button(L("Snapshot মুছুন", "Delete snapshots")) {
                if Actions.confirm(L("Time Machine snapshot মুছবেন?", "Delete Time Machine snapshots?"), L("Mac এর password চাইবে। আপনার external Time Machine backup এ কোন প্রভাব পড়বে না।", "Your Mac password will be asked. Your external Time Machine backups are not affected."), ok: L("মুছুন", "Delete")) {
                    DispatchQueue.global().async {
                        Actions.deleteSnapshots()
                        DispatchQueue.main.async { m.scanSuggestions(apps: store.apps) }
                    }
                }
            }.buttonStyle(.borderedProminent).tint(.green)
        case .trashFolder:
            Button(L("Trash এ পাঠান", "Move to Trash")) {
                if Actions.confirm(L("\(s.title) মুছবেন?", "Delete \(s.title)?"), L("Trash এ যাবে — দরকার হলে ফেরত আনতে পারবেন।", "Goes to the Trash — you can get it back if needed."), ok: L("Trash এ পাঠান", "Move to Trash")) {
                    let failed = Remover.trash(s.paths)
                    if !failed.isEmpty { Actions.info(L("মুছা যায়নি", "Couldn't delete"), failed.joined(separator: "\n")) }
                    m.scanSuggestions(apps: store.apps)
                }
            }.buttonStyle(.borderedProminent).tint(.green)
        case .explore:
            Button(L("ভিতরে দেখুন", "Look inside")) {
                m.stack = []; m.tab = .explorer; m.open(s.paths.first)
            }.buttonStyle(.borderedProminent).tint(.red)
        case .oldVersion(let app):
            if app.bundleID.hasPrefix("com.adobe"),
               let cc = NSWorkspace.shared.urlForApplication(withBundleIdentifier: "com.adobe.acc.AdobeCreativeCloud") {
                Button(L("Creative Cloud খুলুন", "Open Creative Cloud")) { NSWorkspace.shared.open(cc) }
            }
            Button("Uninstall") { store.selected = app }.buttonStyle(.borderedProminent).tint(.green)
        }
    }

    // Explorer
    @ViewBuilder private var explorerView: some View {
        HStack(spacing: 4) {
            Button { m.back() } label: { Image(systemName: "chevron.left") }.disabled(m.stack.isEmpty)
            Button(L("সব", "All")) { m.jump(to: 0) }.buttonStyle(.link)
            ForEach(Array(m.stack.enumerated()), id: \.offset) { i, u in
                Image(systemName: "chevron.right").font(.caption2).foregroundStyle(.tertiary)
                Button(u.path == FS.home.path ? "Home" : u.lastPathComponent) { m.jump(to: i + 1) }
                    .buttonStyle(.link).lineLimit(1)
            }
            Spacer()
            if m.exploring { ProgressView().controlSize(.small); Text(L("মাপা হচ্ছে…", "Measuring…")).font(.caption).foregroundStyle(.secondary) }
        }
        SearchField(text: $m.explorerQuery, placeholder: L("এই ফোল্ডারে নাম দিয়ে খুঁজুন…", "Search this folder by name…"))
        let maxSize = max(1, m.entries.map(\.size).max() ?? 1)
        List(m.entries.filter { matchesQuery(m.explorerQuery, $0.url.lastPathComponent, ExplorerRow.rootName($0.url)) }) { e in
            ExplorerRow(e: e, maxSize: maxSize, showFullPath: m.stack.isEmpty,
                        onOpen: { m.open(e.url) }, onTrash: { trash(e) })
        }
        .listStyle(.inset)
        if m.stack.isEmpty && !m.exploring && !m.entries.isEmpty {
            let other = m.disk.used - m.entries.reduce(0) { $0 + max(0, $1.size) }
            if other > 0 {
                Text(L("🍎 macOS নিজে ও লুকানো system ফাইল (virtual memory, snapshot ইত্যাদি): প্রায় \(FS.pretty(other)) — এগুলো হাত দেওয়া ঠিক না।", "🍎 macOS itself and hidden system files (virtual memory, snapshots and so on): about \(FS.pretty(other)) — best not to touch."))
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        Text(L("ফোল্ডারে click করে ভিতরে ঢুকুন। সবচেয়ে বড়টা সবার উপরে থাকে।", "Click a folder to go inside. The biggest is always on top.")).font(.caption).foregroundStyle(.secondary)
    }

    private func trash(_ e: Entry) {
        if e.url.pathExtension == "app", let app = store.apps.first(where: { $0.url == e.url }) {
            store.selected = app; return
        }
        guard Actions.confirm(L("\"\(e.url.lastPathComponent)\" Trash এ পাঠাবেন?", "Move \"\(e.url.lastPathComponent)\" to the Trash?"), L("\(FS.pretty(e.size)) — ভুল হলে Trash থেকে ফেরত আনতে পারবেন। জায়গা খালি করতে পরে Trash খালি করুন।", "\(FS.pretty(e.size)) — if it's a mistake you can restore it from the Trash. Empty the Trash later to free the space."), ok: L("Trash এ পাঠান", "Move to Trash")) else { return }
        let failed = Remover.trash([e.url])
        if !failed.isEmpty { Actions.info(L("মুছা যায়নি", "Couldn't delete"), failed.joined(separator: "\n")) }
        m.forget(e.url); m.entries.removeAll { $0.url == e.url }; m.refreshDisk()
        m.suggestions = []
    }

    // Big files
    @ViewBuilder private var bigFilesView: some View {
        if m.bigScanning {
            Spacer(); ProgressView(L("500 MB এর বড় ফাইল খোঁজা হচ্ছে… (কয়েক মিনিট লাগতে পারে)", "Looking for files over 500 MB… (may take a few minutes)")); Spacer()
        } else if !m.bigScanned {
            Spacer()
            VStack(spacing: 10) {
                Text(L("আপনার Home ফোল্ডারে 500 MB এর চেয়ে বড় সব ফাইল খুঁজে বের করবে।", "Finds every file over 500 MB in your Home folder.")).foregroundStyle(.secondary)
                Button(L("খোঁজা শুরু করুন", "Start search")) { m.scanBigFiles() }.buttonStyle(.borderedProminent).controlSize(.large)
            }
            Spacer()
        } else {
            HStack {
                Text(L("\(m.bigFiles.count) টি বড় ফাইল · মোট \(FS.pretty(m.bigFiles.reduce(0) { $0 + $1.size }))", "\(m.bigFiles.count) big files · total \(FS.pretty(m.bigFiles.reduce(0) { $0 + $1.size }))")).font(.callout)
                Spacer()
                Button { m.scanBigFiles() } label: { Label(L("আবার খুঁজুন", "Search again"), systemImage: "arrow.clockwise") }
            }
            let kindFiltered = m.bigFiles.filter { (m.bigKind == .all || FileKind.of($0.url) == m.bigKind) && matchesQuery(m.bigQuery, $0.url.path) }
            let safeN = kindFiltered.filter { BigFileVerdict.of($0.url).safe }.count
            HStack(spacing: 10) {
                FilterBar(query: $m.bigQuery, risk: $m.bigRisk, safeCount: safeN, riskyCount: kindFiltered.count - safeN)
                Picker("", selection: $m.bigKind) {
                    Text(L("সব", "All")).tag(FileKind.all)
                    Text(L("🎬 ভিডিও", "🎬 Video")).tag(FileKind.video)
                    Text("📦 Installer / Zip").tag(FileKind.installer)
                    Text(L("🎵 অডিও", "🎵 Audio")).tag(FileKind.audio)
                    Text(L("অন্যান্য", "Other")).tag(FileKind.other)
                }
                .pickerStyle(.segmented).labelsHidden().fixedSize()
            }
            RiskLegend()
            List(kindFiltered.filter { m.bigRisk.allows(safe: BigFileVerdict.of($0.url).safe) }) { e in
                BigFileRow(e: e) {
                    let v = BigFileVerdict.of(e.url)
                    let msg = (v.safe ? "🟢 " : L("🔴 সাবধান: ", "🔴 Careful: ")) + v.note + L("\n\n\(FS.pretty(e.size)) — ভুল হলে Trash থেকে ফেরত আনতে পারবেন।", "\n\n\(FS.pretty(e.size)) — if it's a mistake you can restore it from the Trash.")
                    if Actions.confirm(L("\"\(e.url.lastPathComponent)\" Trash এ পাঠাবেন?", "Move \"\(e.url.lastPathComponent)\" to the Trash?"), msg, ok: v.safe ? L("Trash এ পাঠান", "Move to Trash") : L("তবুও Trash এ পাঠান", "Move to Trash anyway")) {
                        let failed = Remover.trash([e.url])
                        if failed.isEmpty { m.bigFiles.removeAll { $0.url == e.url } }
                        m.refreshDisk()
                    }
                }
            }
            .listStyle(.inset)
        }
    }
}

/// Explains one big file in plain words: green = safe to delete, red = keep unless sure.
struct BigFileVerdict {
    let safe: Bool
    let note: String

    static func of(_ url: URL) -> BigFileVerdict {
        let p = url.path.lowercased()
        let ext = url.pathExtension.lowercased()
        let kind = FileKind.of(url)
        func has(_ s: String...) -> Bool { s.contains { p.contains($0) } }

        // App internals that must stay (checked first so a cache inside them does not mislead)
        if has("vm_bundles", ".vmwarevm", ".pvm/", "/parallels/", "utm/data") || ["img", "vdi", "vmdk", "qcow2", "hdd"].contains(ext) && has("/library/") {
            let owner = appName(url) ?? L("একটি app", "an app")
            return .init(safe: false, note: L("\(owner) এর virtual machine ডিস্ক — হাতে মুছলে app নষ্ট হবে। app এর নিজের settings থেকে বন্ধ/রিসেট করুন।", "Virtual machine disk of \(owner) — deleting it by hand will break the app. Turn it off or reset it in the app's own settings."))
        }
        if has("/mobilesync/backup") {
            return .init(safe: false, note: L("iPhone/iPad এর backup — মুছলে backup চলে যাবে। iCloud এ backup থাকলে মুছতে পারেন।", "iPhone/iPad backup — deleting removes the backup. Fine to delete if it's backed up to iCloud."))
        }
        if has("/photos library.photoslibrary", "/music/music/", "/itunes/") {
            return .init(safe: false, note: L("Photos/Music library এর অংশ — হাতে মুছবেন না।", "Part of the Photos/Music library — don't delete by hand."))
        }

        // Safe: caches, updates, logs, installers
        if has("crx_cache", "googleupdater", "/softwareupdate/", "/updates/", "squirrel", "shipit") {
            return .init(safe: true, note: L("পুরোনো update ফাইল — app ইতিমধ্যে আপডেট হয়ে গেছে, মুছলে কোন সমস্যা নেই।", "Old update file — the app is already updated, safe to delete."))
        }
        if has("media cache", "peak files", "/common/media cache") || ["cfa", "pek", "mcdb", "ims"].contains(ext) {
            return .init(safe: true, note: L("Adobe Media Cache — Premiere/After Effects দরকার হলে আবার বানিয়ে নেবে।", "Adobe Media Cache — Premiere/After Effects rebuild it when needed."))
        }
        if has("/caches/", "/.cache/", "/cache/", "/deriveddata/", "/tmp/", "/temp/") {
            return .init(safe: true, note: L("Cache ফাইল — app দরকার হলে আবার বানিয়ে নেবে।", "Cache file — the app rebuilds it when needed."))
        }
        if has("/library/logs/") || ext == "log" {
            return .init(safe: true, note: L("Log ফাইল — শুধু সমস্যা খোঁজার জন্য, মুছলে কোন সমস্যা নেই।", "Log file — only for troubleshooting, safe to delete."))
        }
        if ["dmg", "pkg", "iso", "xip"].contains(ext) {
            return .init(safe: true, note: L("Installer ফাইল — software ইনস্টল হয়ে গেলে আর দরকার নেই।", "Installer — not needed once the software is installed."))
        }
        if kind == .installer && has("/downloads/") {
            return .init(safe: true, note: L("Download করা zip — ভিতরের জিনিস বের করে নিলে আর দরকার নেই।", "Downloaded zip — not needed once you've extracted it."))
        }

        // Red: user's own work and app data
        if ["prproj", "aep", "aepx", "psd", "psb", "ai", "indd", "fcpbundle", "drp", "veg"].contains(ext) {
            return .init(safe: false, note: L("আপনার project ফাইল — মুছলে কাজ হারাবেন।", "Your project file — deleting loses your work."))
        }
        if kind == .video {
            return .init(safe: false, note: L("আপনার ভিডিও — project এ লাগলে রাখুন। অন্য ড্রাইভে কপি করা থাকলে মুছতে পারেন।", "Your video — keep it if a project uses it. Fine to delete if it's copied to another drive."))
        }
        if kind == .audio {
            return .init(safe: false, note: L("আপনার অডিও ফাইল — দরকার না হলে বা backup থাকলে মুছতে পারেন।", "Your audio file — delete if not needed or backed up."))
        }
        if kind == .installer {
            return .init(safe: false, note: L("Zip/archive — ভিতরে কি আছে দেখে নিন, অন্য কোথাও কপি না থাকলে রাখুন।", "Zip/archive — check what's inside; keep it if there's no other copy."))
        }
        if has("/library/") {
            let owner = appName(url) ?? L("একটি app", "an app")
            return .init(safe: false, note: L("\(owner) এর ভিতরের ফাইল — হাতে মুছলে app ঠিকমতো চলবে না। দরকার না হলে app টাই Uninstall করুন।", "Internal file of \(owner) — deleting it by hand breaks the app. If you don't need it, uninstall the app instead."))
        }
        return .init(safe: false, note: L("কি ফাইল নিশ্চিত না — খুলে দেখে নিন, না বুঝলে রাখুন।", "Unknown file — open it to check; keep it if unsure."))
    }

    /// Folder name right after Application Support / Containers / Library, e.g. "Claude".
    static func appName(_ url: URL) -> String? {
        let c = url.pathComponents
        for marker in ["Application Support", "Group Containers", "Containers", "Caches"] {
            if let i = c.firstIndex(of: marker), i + 1 < c.count { return c[i + 1] }
        }
        if let i = c.firstIndex(of: "Library"), i + 1 < c.count - 1 { return c[i + 1] }
        return nil
    }
}

struct BigFileRow: View {
    let e: Entry
    let onTrash: () -> Void
    var body: some View {
        let v = BigFileVerdict.of(e.url)
        let tint: Color = v.safe ? .green : .red
        HStack(spacing: 10) {
            Circle().fill(tint).frame(width: 10, height: 10)
            Image(nsImage: NSWorkspace.shared.icon(forFile: e.url.path)).resizable().frame(width: 28, height: 28)
            VStack(alignment: .leading, spacing: 2) {
                Text(e.url.lastPathComponent).lineLimit(1).truncationMode(.middle)
                Text(v.note).font(.caption).foregroundStyle(tint).lineLimit(2)
                Text(FS.short(e.url.deletingLastPathComponent())).font(.caption2).foregroundStyle(.secondary).lineLimit(1).truncationMode(.middle)
            }
            Spacer()
            Text(FS.pretty(e.size)).font(.callout.bold()).monospacedDigit()
            Button { Actions.reveal(e.url) } label: { Image(systemName: "magnifyingglass") }
                .buttonStyle(.borderless).help(L("Finder এ দেখুন", "Show in Finder"))
            if Safe.canTrash(e.url) {
                Button(action: onTrash) { Image(systemName: "trash") }
                    .buttonStyle(.borderless).foregroundStyle(tint)
                    .help(v.safe ? L("Trash এ পাঠান — নিরাপদ", "Move to Trash — safe") : L("সাবধান — মুছার আগে নিশ্চিত হন", "Careful — make sure before deleting"))
            }
        }
        .padding(.vertical, 3)
        .background(tint.opacity(0.06), in: RoundedRectangle(cornerRadius: 6))
    }
}

struct ExplorerRow: View {
    static func rootName(_ u: URL) -> String {
        switch u.path {
        case FS.home.path: return L("🏠 আপনার Home ফোল্ডার (Documents, Downloads, app data…)", "🏠 Your Home folder (Documents, Downloads, app data…)")
        case "/Applications": return L("📦 Applications — ইনস্টল করা software", "📦 Applications — installed software")
        case "/Library": return L("🧩 Library — সব user এর app data ও plug-in", "🧩 Library — app data and plug-ins for all users")
        case "/private/var": return L("⚙️ System এর অস্থায়ী ফাইল", "⚙️ System temporary files")
        default: return FS.short(u)
        }
    }
    let e: Entry
    let maxSize: Int64
    let showFullPath: Bool
    let onOpen: () -> Void
    let onTrash: () -> Void

    var body: some View {
        let frac = e.size > 0 ? Double(e.size) / Double(maxSize) : 0
        HStack(spacing: 10) {
            Image(nsImage: NSWorkspace.shared.icon(forFile: e.url.path)).resizable().frame(width: 24, height: 24)
            VStack(alignment: .leading, spacing: 3) {
                Text(showFullPath ? Self.rootName(e.url) : e.url.lastPathComponent).lineLimit(1).truncationMode(.middle)
                GeometryReader { g in
                    Capsule().fill(Color.accentColor.opacity(0.7))
                        .frame(width: frac > 0 ? max(3, g.size.width * frac) : 0)
                }.frame(height: 5)
            }
            Text(e.size < 0 ? "…" : FS.pretty(e.size)).font(.callout.bold()).monospacedDigit()
                .frame(width: 80, alignment: .trailing)
            Button { Actions.reveal(e.url) } label: { Image(systemName: "magnifyingglass") }
                .buttonStyle(.borderless).help(L("Finder এ দেখুন", "Show in Finder"))
            if Safe.canTrash(e.url) {
                Button(action: onTrash) { Image(systemName: "trash") }
                    .buttonStyle(.borderless).foregroundStyle(.red).help(L("Trash এ পাঠান", "Move to Trash"))
            }
            Image(systemName: "chevron.right").foregroundStyle(e.isFolder ? Color.secondary : Color.clear)
        }
        .contentShape(Rectangle())
        .onTapGesture { if e.isFolder { onOpen() } }
        .padding(.vertical, 2)
    }
}

/// Small key shown at the top of the lists: green = safe to remove, red = may cause problems.
struct RiskLegend: View {
    var body: some View {
        HStack(spacing: 14) {
            Label { Text(L("সবুজ = মুছলে / বন্ধ করলে কোন সমস্যা নেই", "Green = safe to delete / turn off")) } icon: { Circle().fill(Color.green).frame(width: 10, height: 10) }
            Label { Text(L("লাল = সাবধান, সমস্যা হতে পারে", "Red = careful, may cause problems")) } icon: { Circle().fill(Color.red).frame(width: 10, height: 10) }
            Spacer()
        }
        .font(.caption)
    }
}


// MARK: - Filters

enum RiskFilter: Hashable { case all, safe, risky
    func allows(safe: Bool) -> Bool { self == .all || (self == .safe) == safe }
}

enum FileKind: Hashable { case all, video, installer, audio, other
    static func of(_ url: URL) -> FileKind {
        switch url.pathExtension.lowercased() {
        case "mp4", "mov", "mxf", "mkv", "avi", "m4v", "mts", "m2ts", "webm", "braw", "r3d", "prores", "cfa", "pek", "mcdb": return .video
        case "dmg", "pkg", "zip", "iso", "xip", "rar", "7z", "tar", "gz", "app": return .installer
        case "wav", "mp3", "aif", "aiff", "m4a", "flac", "caf": return .audio
        default: return .other
        }
    }
}

/// Case-insensitive search over several text fields.
func matchesQuery(_ q: String, _ fields: String...) -> Bool {
    let t = q.trimmingCharacters(in: .whitespaces)
    return t.isEmpty || fields.contains { $0.localizedCaseInsensitiveContains(t) }
}

struct SearchField: View {
    @Binding var text: String
    var placeholder = L("নাম দিয়ে খুঁজুন…", "Search by name…")
    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
            TextField(placeholder, text: $text).textFieldStyle(.plain)
            if !text.isEmpty {
                Button { text = "" } label: { Image(systemName: "xmark.circle.fill") }
                    .buttonStyle(.plain).foregroundStyle(.secondary)
            }
        }
        .padding(.horizontal, 8).padding(.vertical, 5)
        .background(Color.primary.opacity(0.06), in: RoundedRectangle(cornerRadius: 7))
    }
}

/// Search box + "all / green / red" switch with counts.
struct FilterBar: View {
    @Binding var query: String
    @Binding var risk: RiskFilter
    let safeCount: Int
    let riskyCount: Int
    var body: some View {
        HStack(spacing: 10) {
            SearchField(text: $query)
            Picker("", selection: $risk) {
                Text(L("সব (\(safeCount + riskyCount))", "All (\(safeCount + riskyCount))")).tag(RiskFilter.all)
                Text(L("🟢 নিরাপদ (\(safeCount))", "🟢 Safe (\(safeCount))")).tag(RiskFilter.safe)
                Text(L("🔴 সাবধান (\(riskyCount))", "🔴 Careful (\(riskyCount))")).tag(RiskFilter.risky)
            }
            .pickerStyle(.segmented).labelsHidden().fixedSize()
        }
    }
}
