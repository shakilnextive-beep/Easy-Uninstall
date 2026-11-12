import SwiftUI
import AppKit
import CoreServices

// MARK: - App usage

struct AppUsage: Identifiable {
    var id: String { app.id }
    let app: InstalledApp
    let lastUsed: Date?
    let estimated: Bool       // true when guessed from the app's settings file
    let size: Int64
    var days: Int? { lastUsed.map { Int(Date().timeIntervalSince($0) / 86400) } }
}

/// Apple apps that live in /Applications and can be removed safely (App Store apps).
let removableAppleApps: [String: [String]] = [
    "com.apple.garageband10": ["/Library/Application Support/GarageBand", "/Library/Audio/Apple Loops",
                               "/Library/Audio/Apple Loops Index", "~/Library/Application Support/GarageBand"],
    "com.apple.iMovieApp": ["~/Movies/iMovie Library.imovielibrary", "~/Movies/iMovie Theater.theater"],
    "com.apple.iWork.Keynote": [], "com.apple.iWork.Pages": [], "com.apple.iWork.Numbers": [],
    "com.apple.dt.Xcode": ["~/Library/Developer/Xcode", "~/Library/Developer/CoreSimulator"],
]

enum UsageScanner {
    static func lastUsed(_ app: InstalledApp) -> (Date?, Bool) {
        // running right now?
        if NSWorkspace.shared.runningApplications.contains(where: { $0.bundleURL == app.url }) { return (Date(), false) }
        var dates: [Date] = []
        if let item = MDItemCreateWithURL(nil, app.url as CFURL),
           let d = MDItemCopyAttribute(item, kMDItemLastUsedDate) as? Date { dates.append(d) }
        // last time the app's program file was started (also catches apps launched by other apps,
        // e.g. Media Encoder started by Premiere for export)
        if let exe = Bundle(url: app.url)?.executableURL {
            var st = stat()
            if stat(exe.path, &st) == 0 {
                dates.append(Date(timeIntervalSince1970: TimeInterval(st.st_atimespec.tv_sec)))
            }
        }
        if let d = dates.max() { return (d, false) }
        // fallback: when did the app last write its settings / data?
        guard !app.bundleID.isEmpty else { return (nil, false) }
        let lib = FS.home.appendingPathComponent("Library")
        let candidates = ["Preferences/\(app.bundleID).plist", "Containers/\(app.bundleID)", "Caches/\(app.bundleID)",
                          "Application Support/\(app.bundleID)", "Application Support/\(app.name)",
                          "Saved Application State/\(app.bundleID).savedState", "HTTPStorages/\(app.bundleID)"]
        let fallback = candidates.compactMap {
            try? lib.appendingPathComponent($0).resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate
        }
        if let d = fallback.max() { return (d, true) }
        return (nil, false)
    }

    static func scan(_ apps: [InstalledApp]) -> [AppUsage] {
        let me = Bundle.main.bundleIdentifier
        let list = apps.filter { $0.bundleID != me && !$0.isProtected && !$0.url.path.hasPrefix("/Applications/Utilities/") }
        var out = [AppUsage?](repeating: nil, count: list.count)
        DispatchQueue.concurrentPerform(iterations: list.count) { i in
            let a = list[i]
            let (d, est) = lastUsed(a)
            let parent = a.url.deletingLastPathComponent()
            let size = FS.size(of: parent.path != "/Applications" && parent.lastPathComponent == a.name ? parent : a.url)
            out[i] = AppUsage(app: a, lastUsed: d, estimated: est, size: size)
        }
        return out.compactMap { $0 }.sorted {
            switch ($0.lastUsed, $1.lastUsed) {
            case (nil, nil): return $0.size > $1.size
            case (nil, _): return true
            case (_, nil): return false
            case let (a?, b?): return a < b
            }
        }
    }
}

// MARK: - Background items (LaunchAgents / LaunchDaemons)

struct BackgroundItem: Identifiable {
    enum Domain { case user, allUsers, system }
    var id: String { plist.path }
    let label: String
    let plist: URL
    let domain: Domain
    let program: String?
    let owner: String          // friendly app name
    let orphan: Bool           // the program it starts no longer exists
    var disabled: Bool
    var running: Bool
    let note: String
    let caution: Bool
}

enum BackgroundScanner {
    static let uid = getuid()

    static func scan() -> [BackgroundItem] {
        let userDisabled = parseDisabled(Shell.run("/bin/launchctl", ["print-disabled", "gui/\(uid)"]))
        let sysDisabled = parseDisabled(Shell.run("/bin/launchctl", ["print-disabled", "system"]))
        var runningLabels = Set<String>()
        for line in Shell.run("/bin/launchctl", ["list"]).split(separator: "\n") {
            let parts = line.split(separator: "\t")
            if parts.count == 3, parts[0] != "-", parts[0] != "PID" { runningLabels.insert(String(parts[2])) }
        }

        let dirs: [(URL, BackgroundItem.Domain)] = [
            (FS.home.appendingPathComponent("Library/LaunchAgents"), .user),
            (URL(fileURLWithPath: "/Library/LaunchAgents"), .allUsers),
            (URL(fileURLWithPath: "/Library/LaunchDaemons"), .system),
        ]
        // vendor prefixes of installed apps, e.g. "com.google"
        let vendors = Set(Scanner.loadApps().map { $0.bundleID.split(separator: ".").prefix(2).joined(separator: ".").lowercased() })
        var out: [BackgroundItem] = []
        for (dir, domain) in dirs {
            for f in (try? FS.fm.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil)) ?? [] where f.pathExtension == "plist" {
                let dict = NSDictionary(contentsOf: f) as? [String: Any] ?? [:]
                let label = dict["Label"] as? String ?? f.deletingPathExtension().lastPathComponent
                if label.hasPrefix("com.apple.") { continue }
                let program = dict["Program"] as? String ?? (dict["ProgramArguments"] as? [String])?.first
                let vendor = label.split(separator: ".").prefix(2).joined(separator: ".").lowercased()
                // An empty plist runs nothing: vendors (e.g. Google) leave these on purpose as placeholders.
                // Only show it when the vendor's apps are all gone.
                if dict.isEmpty && vendors.contains(vendor) { continue }
                let orphan = dict.isEmpty || (program.map { !FS.fm.fileExists(atPath: $0) } ?? false)
                let disabledMap = domain == .system ? sysDisabled : userDisabled
                let (note, caution) = describe(label)
                out.append(BackgroundItem(
                    label: label, plist: f, domain: domain, program: program,
                    owner: ownerName(label: label, program: program),
                    orphan: orphan, disabled: disabledMap[label] ?? false,
                    running: runningLabels.contains(label), note: note, caution: caution))
            }
        }
        return out.sorted { ($0.orphan ? 0 : 1, $0.owner) < ($1.orphan ? 0 : 1, $1.owner) }
    }

    private static func parseDisabled(_ text: String) -> [String: Bool] {
        var map: [String: Bool] = [:]
        for line in text.split(separator: "\n") {
            let t = line.trimmingCharacters(in: .whitespaces)
            guard t.hasPrefix("\""), let end = t.dropFirst().firstIndex(of: "\"") else { continue }
            let label = String(t[t.index(after: t.startIndex)..<end])
            map[label] = t.hasSuffix("disabled") || t.hasSuffix("true")
        }
        return map
    }

    private static func ownerName(label: String, program: String?) -> String {
        if let p = program, let r = p.range(of: ".app/") {
            let appPath = String(p[..<r.lowerBound])
            return (appPath as NSString).lastPathComponent
        }
        let known: [(String, String)] = [("com.adobe", "Adobe"), ("com.google", "Google Chrome"), ("com.coccoc", "Cốc Cốc Browser"),
                                         ("org.freedownloadmanager", "Free Download Manager"), ("com.microsoft", "Microsoft"),
                                         ("com.premierecast", "PremiereCast"), ("com.profx", "Pro FX Studio"),
                                         ("us.zoom", "Zoom"), ("com.dropbox", "Dropbox"), ("com.spotify", "Spotify")]
        return known.first { label.hasPrefix($0.0) }?.1 ?? label
    }

    /// Plain-language explanation and whether disabling may break something.
    private static func describe(_ label: String) -> (String, Bool) {
        let l = label.lowercased()
        if l.contains("keystone") || l.contains("googleupdater") {
            return (L("Browser auto-update করার জন্য সারাক্ষণ চলে। বন্ধ করলে browser ঠিকই চলবে, শুধু নিজে থেকে update হবে না।", "Runs all the time to auto-update the browser. Turning it off is fine — the browser still works, it just won't update by itself."), false) }
        if l.contains("ccxprocess") { return (L("Creative Cloud এর tutorial / stock / extra feature। বন্ধ করলে কোন সমস্যা নেই।", "Creative Cloud tutorials / stock / extra features. Safe to turn off."), false) }
        if l.contains("adobecreativecloud") { return (L("Mac চালু হলেই Creative Cloud app চালু করে। বন্ধ করলে দরকার হলে নিজে খুলে নেবেন।", "Starts the Creative Cloud app when the Mac starts. Turn it off and open it yourself when needed."), false) }
        if l.contains("gc.invoker") || l.contains("agsservice") || l.contains("gc.") {
            return (L("Adobe Genuine Service — license যাচাই করে। বন্ধ করা যায়, তবে Adobe পরে আবার চালু করে দিতে পারে।", "Adobe Genuine Service — checks your license. You can turn it off, but Adobe may turn it back on later."), true) }
        if l.contains("adobedesktopservice") || l.contains("acc.installer") {
            return (L("Creative Cloud এর sign-in, install ও update এর জন্য দরকার। বন্ধ করলে Adobe app update/sign-in এ সমস্যা হতে পারে।", "Needed for Creative Cloud sign-in, installs and updates. Turning it off may break Adobe app updates or sign-in."), true) }
        if l.contains("armdc") || l.contains("arm") { return (L("Adobe Acrobat / Reader এর auto-updater। বন্ধ করলে Acrobat নিজে update হবে না।", "Adobe Acrobat / Reader auto-updater. Turn it off and Acrobat won't update by itself."), false) }
        if l.contains("fdm") { return (L("Mac চালু হলেই Free Download Manager চালু করে।", "Starts Free Download Manager when the Mac starts."), false) }
        return (L("এই app এর background helper। বন্ধ করলে app টি খুললে ঠিকই কাজ করবে, শুধু Mac চালু হলে নিজে থেকে চলবে না।", "Background helper for this app. Turn it off and the app still works when you open it — it just won't run at startup."), false)
    }

    // Actions
    private static func target(_ i: BackgroundItem) -> String {
        i.domain == .system ? "system/\(i.label)" : "gui/\(uid)/\(i.label)"
    }

    /// Turns the item off (persistently, reversible) and stops it now.
    static func setEnabled(_ i: BackgroundItem, _ on: Bool) -> Bool {
        let t = target(i)
        let domainTarget = i.domain == .system ? "system" : "gui/\(uid)"
        let cmds = on
            ? ["/bin/launchctl enable \(t)", "/bin/launchctl bootstrap \(domainTarget) \(q(i.plist.path))"]
            : ["/bin/launchctl disable \(t)", "/bin/launchctl bootout \(t)"]
        if i.domain == .system { return admin(cmds.joined(separator: " ; ")) }
        if on {
            _ = Shell.run("/bin/launchctl", ["enable", t])
            _ = Shell.run("/bin/launchctl", ["bootstrap", domainTarget, i.plist.path])
        } else {
            _ = Shell.run("/bin/launchctl", ["disable", t])
            _ = Shell.run("/bin/launchctl", ["bootout", t])
        }
        return true
    }

    /// Removes a leftover item completely (stops it and moves its file to the Trash).
    static func remove(_ i: BackgroundItem) -> Bool {
        if i.domain == .system { _ = admin("/bin/launchctl bootout \(target(i))") }
        else { _ = Shell.run("/bin/launchctl", ["bootout", target(i)]) }
        return Remover.trash([i.plist]).isEmpty
    }

    private static func q(_ s: String) -> String { "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'" }
    private static func admin(_ shell: String) -> Bool {
        let esc = shell.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"")
        var err: NSDictionary?
        NSAppleScript(source: "do shell script \"\(esc)\" with administrator privileges")?.executeAndReturnError(&err)
        return err == nil
    }
}

// MARK: - Model

enum BloatTab: Hashable { case unused, background, builtIn }

final class BloatModel: ObservableObject {
    @Published var tab: BloatTab = .unused
    @Published var usage: [AppUsage] = []
    @Published var items: [BackgroundItem] = []
    @Published var loadingUsage = false
    @Published var loadingItems = false
    @Published var minDays = 90
    @Published var query = ""
    @Published var risk: RiskFilter = .all

    func loadUsage(_ apps: [InstalledApp]) {
        loadingUsage = true
        DispatchQueue.global(qos: .userInitiated).async {
            let u = UsageScanner.scan(apps.isEmpty ? Scanner.loadApps() : apps)
            DispatchQueue.main.async { self.usage = u; self.loadingUsage = false }
        }
    }

    func loadItems() {
        loadingItems = true
        DispatchQueue.global(qos: .userInitiated).async {
            let i = BackgroundScanner.scan()
            DispatchQueue.main.async { self.items = i; self.loadingItems = false }
        }
    }

    var unused: [AppUsage] { usage.filter { ($0.days ?? Int.max) >= minDays } }
    var builtInApps: [URL] {
        ((try? FS.fm.contentsOfDirectory(at: URL(fileURLWithPath: "/System/Applications"), includingPropertiesForKeys: nil)) ?? [])
            .filter { $0.pathExtension == "app" }
            .sorted { $0.lastPathComponent < $1.lastPathComponent }
    }
}

// MARK: - Views

struct BloatView: View {
    @ObservedObject var m: BloatModel
    @ObservedObject var store: Store

    var body: some View {
        VStack(spacing: 12) {
            Picker("", selection: $m.tab) {
                Text(L("💤 ব্যবহার হয় না এমন app", "💤 Unused apps")).tag(BloatTab.unused)
                Text(L("⚙️ Background এ চলে", "⚙️ Runs in background")).tag(BloatTab.background)
                Text("🍎 Apple built-in").tag(BloatTab.builtIn)
            }
            .pickerStyle(.segmented).labelsHidden()

            switch m.tab {
            case .unused: unusedView
            case .background: backgroundView
            case .builtIn: builtInView
            }
        }
        .padding(14)
        .onAppear {
            if m.usage.isEmpty && !m.loadingUsage { m.loadUsage(store.apps) }
            if m.items.isEmpty && !m.loadingItems { m.loadItems() }
        }
    }

    // Unused apps
    @ViewBuilder private var unusedView: some View {
        if m.loadingUsage {
            Spacer(); ProgressView(L("কোন app কবে ব্যবহার করেছেন দেখা হচ্ছে…", "Checking when you last used each app…")); Spacer()
        } else {
            let base = m.unused.filter { matchesQuery(m.query, $0.app.name, $0.app.bundleID) }
            let isSafe: (AppUsage) -> Bool = { !$0.app.bundleID.hasPrefix("com.adobe") }
            let list = base.filter { m.risk.allows(safe: isSafe($0)) }
            FilterBar(query: $m.query, risk: $m.risk,
                      safeCount: base.filter(isSafe).count, riskyCount: base.filter { !isSafe($0) }.count)
            RiskLegend()
            HStack {
                Text(L("যেগুলো", "Apps not opened in")).foregroundStyle(.secondary)
                Picker("", selection: $m.minDays) {
                    Text(L("১ মাস", "1 month")).tag(30); Text(L("৩ মাস", "3 months")).tag(90); Text(L("৬ মাস", "6 months")).tag(180); Text(L("১ বছর", "1 year")).tag(365)
                }
                .labelsHidden().frame(width: 100)
                Text(L("বা তার বেশি সময় খোলেননি: \(list.count) টি · মোট \(FS.pretty(list.reduce(0) { $0 + $1.size }))", "or more: \(list.count) · total \(FS.pretty(list.reduce(0) { $0 + $1.size }))"))
                    .font(.callout.bold())
                Spacer()
                Button { m.loadUsage(store.apps) } label: { Image(systemName: "arrow.clockwise") }
            }
            if list.isEmpty {
                Spacer(); Text(m.unused.isEmpty ? L("🎉 এমন কোন app নেই — সব app ই ব্যবহার করছেন।", "🎉 Nothing here — you use all your apps.") : L("এই filter এ কিছু পাওয়া যায়নি", "Nothing matches this filter")).foregroundStyle(.secondary); Spacer()
            } else {
                List(list) { u in UsageRow(u: u) { store.selected = u.app } }
                    .listStyle(.inset)
            }
            Text(L("\"রেকর্ড পাওয়া যায়নি\" মানে macOS এর কাছে খোলার কোন হিসাব নেই — মুছার আগে নিজে একবার ভেবে নিন। Uninstall চাপলে আগের মতো \"শুধু\" বা \"Full\" বেছে নিতে পারবেন।", "\"No record\" means macOS has no history of it being opened — think it over before deleting. Uninstall still lets you choose \"Only\" or \"Full\"."))
                .font(.caption).foregroundStyle(.secondary)
        }
    }

    // Background items
    @ViewBuilder private var backgroundView: some View {
        if m.loadingItems {
            Spacer(); ProgressView(); Spacer()
        } else {
            HStack(alignment: .top) {
                Text(L("এগুলো Mac চালু হলেই নিজে থেকে background এ চলে — RAM, battery ও internet খরচ করে। বন্ধ করলে app গুলো ঠিকই চলবে। যেকোন সময় আবার চালু করতে পারবেন।", "These start on their own in the background whenever the Mac starts — using RAM, battery and internet. The apps still work if you turn these off, and you can turn them back on any time."))
                    .font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button(L("Login Items খুলুন", "Open Login Items")) {
                    NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.LoginItems-Settings.extension")!)
                }
                Button { m.loadItems() } label: { Image(systemName: "arrow.clockwise") }
            }
            let base = m.items.filter { matchesQuery(m.query, $0.owner, $0.label, $0.note) }
            FilterBar(query: $m.query, risk: $m.risk,
                      safeCount: base.filter { !$0.caution }.count, riskyCount: base.filter(\.caution).count)
            RiskLegend()
            List(base.filter { m.risk.allows(safe: !$0.caution) }) { i in
                BackgroundRow(i: i,
                              toggle: { on in
                                  if !on && i.caution && !Actions.confirm(L("\(i.owner) এর এই অংশ বন্ধ করবেন?", "Turn off this part of \(i.owner)?"), i.note, ok: L("বন্ধ করুন", "Turn off")) { return }
                                  _ = BackgroundScanner.setEnabled(i, on); m.loadItems()
                              },
                              remove: {
                                  if Actions.confirm(L("অবশিষ্ট helper মুছবেন?", "Delete leftover helper?"), L("\(i.label)\n\nএটা যে app চালাত সেটা আর Mac এ নেই, তাই এটা কোন কাজে লাগে না। Trash এ যাবে।", "\(i.label)\n\nThe app it belonged to is no longer on this Mac, so it does nothing. It will go to the Trash."), ok: L("মুছে ফেলুন", "Delete")) {
                                      _ = BackgroundScanner.remove(i); m.loadItems()
                                  }
                              })
            }
            .listStyle(.inset)
        }
    }

    // Built-in Apple apps
    @ViewBuilder private var builtInView: some View {
        VStack(alignment: .leading, spacing: 8) {
            Label(L("এই app গুলো মুছা যায় না — আর মুছলেও আপনার জায়গা খালি হবে না", "These apps can't be deleted — and deleting them wouldn't free any space"), systemImage: "lock.shield")
                .font(.headline)
            Text(L("Chess, News, Stocks এর মতো app গুলো macOS এর lock করা \"System\" অংশে থাকে (Sealed System Volume)। আপনার ফাইলগুলো থাকে আলাদা অংশে। তাই এগুলো মুছলে আপনার ব্যবহারযোগ্য জায়গা ০ বাইটও বাড়বে না, উল্টো Mac এর নিরাপত্তা (SIP) বন্ধ করতে হবে ও পরের macOS update এ আবার ফিরে আসবে।\n\nভালো খবর: এগুলো না খুললে কোন RAM বা battery খরচ করে না। না চাইলে শুধু Dock থেকে সরিয়ে দিন (Dock এ icon এ right-click → Options → Remove from Dock)।", "Apps like Chess, News and Stocks live on macOS's locked \"System\" volume (Sealed System Volume). Your files live on a separate volume, so deleting these frees 0 bytes of usable space — and you'd have to turn off Mac security (SIP), and they'd come back with the next macOS update.\n\nGood news: unopened, they use no RAM or battery. If you don't want them, just remove them from the Dock (right-click the icon → Options → Remove from Dock)."))
                .font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
        .padding(12)
        .background(Color.blue.opacity(0.08), in: RoundedRectangle(cornerRadius: 10))
        ScrollView {
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 96))], spacing: 12) {
                ForEach(m.builtInApps, id: \.self) { u in
                    VStack(spacing: 4) {
                        Image(nsImage: NSWorkspace.shared.icon(forFile: u.path)).resizable().frame(width: 40, height: 40)
                        Text(u.deletingPathExtension().lastPathComponent).font(.caption2).lineLimit(1)
                    }
                }
            }
        }
        Text(L("Apple এর যে app গুলো মুছা যায় (GarageBand, iMovie, Keynote, Pages, Numbers, Xcode) সেগুলো ইনস্টল থাকলে \"ব্যবহার হয় না এমন app\" তালিকায় দেখাবে।", "Apple apps that can be deleted (GarageBand, iMovie, Keynote, Pages, Numbers, Xcode) show in the \"Unused apps\" list if installed."))
            .font(.caption).foregroundStyle(.secondary)
    }
}

struct UsageRow: View {
    let u: AppUsage
    let uninstall: () -> Void
    var body: some View {
        HStack(spacing: 12) {
            Image(nsImage: NSWorkspace.shared.icon(forFile: u.app.url.path)).resizable().frame(width: 34, height: 34)
            VStack(alignment: .leading, spacing: 2) {
                Text(u.app.name).font(.system(size: 14, weight: .medium))
                Text(usedText).font(.caption).foregroundStyle(color)
            }
            Spacer()
            Text(FS.pretty(u.size)).font(.callout.bold()).monospacedDigit()
            Button("Uninstall", action: uninstall).buttonStyle(.borderedProminent).tint(color)
        }
        .padding(.vertical, 3)
    }
    private var usedText: String {
        guard let d = u.days else { return L("খোলার কোন রেকর্ড পাওয়া যায়নি", "No record of being opened") + adobeNote }
        let t = d >= 365 ? L("\(d / 365) বছর", "\(d / 365) yr") : d >= 30 ? L("\(d / 30) মাস", "\(d / 30) mo") : L("\(d) দিন", "\(d) days")
        return L("শেষ ব্যবহার: \(t) আগে", "Last used: \(t) ago") + (u.estimated ? L(" (আনুমানিক)", " (estimate)") : "") + adobeNote
    }
    private var adobeNote: String {
        u.app.bundleID.hasPrefix("com.adobe") ? L(" · অন্য Adobe app এটা ভিতরে ব্যবহার করতে পারে (যেমন Premiere export এ Media Encoder)", " · Other Adobe apps may use it internally (e.g. Media Encoder for Premiere exports)") : ""
    }
    private var color: Color { u.app.bundleID.hasPrefix("com.adobe") ? .red : .green }
}

struct BackgroundRow: View {
    let i: BackgroundItem
    let toggle: (Bool) -> Void
    let remove: () -> Void

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: i.orphan ? "trash.fill" : i.caution ? "exclamationmark.triangle.fill" : "gearshape.2.fill")
                .font(.system(size: 16)).foregroundStyle(.white)
                .frame(width: 32, height: 32)
                .background((i.disabled && !i.orphan ? Color.gray : i.caution ? Color.red : Color.green).gradient, in: RoundedRectangle(cornerRadius: 8))
            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 6) {
                    Text(i.owner).font(.system(size: 14, weight: .semibold))
                    if i.orphan { badge(L("অবশিষ্ট — app টি আর নেই", "Leftover — app is gone"), .green) }
                    else if i.disabled { badge(L("বন্ধ", "Off"), .gray) }
                    else if i.running { badge(L("এখন চলছে", "Running"), .blue) }
                    if i.domain == .system { badge(L("password লাগবে", "Needs password"), .secondary) }
                }
                Text(i.orphan ? L("এই helper যে app চালাত সেটা মুছে ফেলা হয়েছে। এটা এখন শুধু শুধু পড়ে আছে — মুছে ফেলা নিরাপদ।", "The app this helper belonged to was deleted. It's just sitting there now — safe to delete.") : i.note)
                    .font(.caption).foregroundStyle(i.caution ? Color.red : Color.secondary).fixedSize(horizontal: false, vertical: true)
                Text(i.label).font(.system(size: 10, design: .monospaced)).foregroundStyle(.tertiary)
            }
            Spacer()
            if i.orphan {
                Button(L("মুছে ফেলুন", "Delete"), action: remove).buttonStyle(.borderedProminent).tint(.green)
            } else if i.disabled {
                Button(L("চালু করুন", "Turn on")) { toggle(true) }
            } else {
                Button(L("বন্ধ করুন", "Turn off")) { toggle(false) }.buttonStyle(.borderedProminent).tint(i.caution ? .red : .green)
            }
        }
        .padding(.vertical, 4)
    }

    private func badge(_ t: String, _ c: Color) -> some View {
        Text(t).font(.caption2).padding(.horizontal, 6).padding(.vertical, 1)
            .background(c.opacity(0.18), in: Capsule()).foregroundStyle(c)
    }
}
