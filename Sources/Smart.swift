import SwiftUI
import AppKit

// MARK: - Smart Cache Cleaner
// Finds only things that are safe to delete (caches, logs, old installers, Trash),
// shows them once, then deletes everything selected in one click — Trash included.

struct SmartItem: Identifiable {
    enum Mode { case contents, items, trash }   // empty these folders / delete these files / empty Trash
    let id = UUID()
    let title: String
    let note: String
    let icon: String
    let urls: [URL]
    let mode: Mode
    var size: Int64
    var on = true
    var skipReason: String? = nil
}

enum SmartScanner {
    /// Running app that owns a cache folder (cleaning a running app's cache can upset it).
    static func runningOwner(_ folder: String, _ running: [NSRunningApplication]) -> String? {
        let f = folder.lowercased()
        if f.contains("claude") || f.contains("anthropic") { return "Claude" }
        for app in running {
            let bid = (app.bundleIdentifier ?? "").lowercased()
            let name = (app.localizedName ?? "").lowercased()
            if !bid.isEmpty && (f == bid || bid.hasPrefix(f + ".") || f.hasPrefix(bid + ".")) { return app.localizedName ?? bid }
            if name.count >= 4 && f.count >= 4 && (f.contains(name) || name.contains(f)) { return app.localizedName }
        }
        return nil
    }

    static func displayName(_ folder: String) -> String {
        if folder.contains("."), let u = NSWorkspace.shared.urlForApplication(withBundleIdentifier: folder) {
            return u.deletingPathExtension().lastPathComponent
        }
        return folder
    }

    static func scan() -> [SmartItem] {
        let h = FS.home
        func p(_ s: String) -> URL { h.appendingPathComponent(s) }
        let running = NSWorkspace.shared.runningApplications
        var out: [SmartItem] = []

        // 1. App caches, one row per app
        let caches = p("Library/Caches")
        var small: [URL] = []; var smallSize: Int64 = 0
        let skipNames: Set<String> = ["CloudKit", "FamilyCircle", "GeoServices", "Metadata", "SentryCrash"]
        for d in (try? FS.fm.contentsOfDirectory(at: caches, includingPropertiesForKeys: nil, options: [])) ?? [] {
            let n = d.lastPathComponent
            if n.hasPrefix("com.apple.") || n.hasPrefix(".") || skipNames.contains(n) { continue }
            let s = FS.size(of: d)
            if s < 1_000_000 { continue }
            if let owner = runningOwner(n, running) {
                if s >= 20_000_000 {
                    out.append(SmartItem(title: "\(displayName(n)) cache", note: L("App ফিরে আসলে আবার বানিয়ে নেবে।", "Rebuilt when the app comes back."), icon: "shippingbox",
                                         urls: [d], mode: .contents, size: s, on: false,
                                         skipReason: L("\(owner) এখন চালু আছে — বন্ধ করে আবার পরীক্ষা করলে এটাও মুছা যাবে।", "\(owner) is running now — quit it and check again to clean this too.")))
                }
                continue
            }
            if s < 20_000_000 { small.append(d); smallSize += s; continue }
            out.append(SmartItem(title: "\(displayName(n)) cache", note: L("অস্থায়ী ফাইল — app দরকার হলে আবার বানিয়ে নেবে।", "Temporary files — the app rebuilds them when needed."),
                                 icon: "shippingbox", urls: [d], mode: .contents, size: s))
        }
        if !small.isEmpty {
            out.append(SmartItem(title: L("ছোট ছোট cache (\(small.count) টি app)", "Small caches (\(small.count) apps)"), note: L("অনেক app এর অল্প অল্প অস্থায়ী ফাইল একসাথে।", "Little bits of temporary files from many apps."),
                                 icon: "square.grid.3x3", urls: small, mode: .contents, size: smallSize))
        }

        // 2. Adobe Media Cache — only when Premiere / After Effects are closed
        let common = p("Library/Application Support/Adobe/Common")
        let media = ["Media Cache Files", "Media Cache", "Peak Files", "PTX"].map { common.appendingPathComponent($0) }
            .filter { FS.fm.fileExists(atPath: $0.path) }
        let mediaSize = media.reduce(0) { $0 + FS.size(of: $1) }
        if mediaSize > 1_000_000 {
            let busy = running.first { app in
                let b = (app.bundleIdentifier ?? "").lowercased()
                return ["com.adobe.premierepro", "com.adobe.aftereffects", "com.adobe.ame", "com.adobe.audition", "com.adobe.premiererush"].contains { b.hasPrefix($0) }
            }
            out.append(SmartItem(title: "Adobe Media Cache", note: L("Premiere / After Effects এর preview ফাইল — project খুললে আবার বানিয়ে নেবে।", "Premiere / After Effects preview files — rebuilt when you open a project."),
                                 icon: "film.stack", urls: media, mode: .contents, size: mediaSize, on: busy == nil,
                                 skipReason: busy.map { L("\($0.localizedName ?? "Adobe app") চালু আছে — বন্ধ করে আবার পরীক্ষা করলে এটাও মুছা যাবে।", "\($0.localizedName ?? "Adobe app") is running — quit it and check again to clean this too.") }))
        }

        // 3. Logs, update leftovers, developer caches
        func add(_ title: String, _ note: String, _ icon: String, _ urls: [URL]) {
            let ex = urls.filter { FS.fm.fileExists(atPath: $0.path) }
            let s = ex.reduce(0) { $0 + FS.size(of: $1) }
            if s > 1_000_000 { out.append(SmartItem(title: title, note: note, icon: icon, urls: ex, mode: .contents, size: s)) }
        }
        add(L("Log ফাইল", "Log files"), L("App ও system এর রেকর্ড — সাধারণ কাজে লাগে না।", "Records from apps and the system — not needed day to day."), "doc.text", [p("Library/Logs")])
        add(L("পুরোনো update ফাইল", "Old update files"), L("Chrome / Google এর আগের update প্যাকেজ — আপডেট হয়ে গেছে, আর লাগবে না।", "Previous Chrome / Google update packages — already updated, no longer needed."), "arrow.triangle.2.circlepath",
            [p("Library/Application Support/Google/GoogleUpdater/crx_cache")])
        add("Developer cache", L("Programming tool এর download cache — দরকার হলে আবার download হবে।", "Programming tool download cache — re-downloaded if needed."), "hammer",
            [p(".npm/_cacache"), p("Library/Developer/Xcode/DerivedData"), p("Library/Developer/CoreSimulator/Caches")])

        // 4. Installers sitting in Downloads
        let dl = p("Downloads")
        let installers = ((try? FS.fm.contentsOfDirectory(at: dl, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles])) ?? [])
            .filter { ["dmg", "pkg", "iso", "xip", "mpkg"].contains($0.pathExtension.lowercased()) }
        let instSize = installers.reduce(0) { $0 + FS.size(of: $1) }
        if instSize > 1_000_000 {
            out.append(SmartItem(title: L("Downloads এর installer (\(installers.count) টি)", "Installers in Downloads (\(installers.count))"),
                                 note: installers.prefix(4).map { $0.lastPathComponent }.joined(separator: ", ") + (installers.count > 4 ? " …" : "")
                                    + L(" — software ইনস্টল হয়ে গেছে, আর লাগবে না।", " — software already installed, no longer needed."),
                                 icon: "arrow.down.circle", urls: installers, mode: .items, size: instSize))
        }

        out.sort { $0.size > $1.size }

        // 5. Trash — always last
        let trash = p(".Trash")
        out.append(SmartItem(title: L("Trash (ময়লার ঝুড়ি)", "Trash"), note: L("Trash এর ফাইল খালি না করলে জায়গা ফেরত আসে না।", "Files in the Trash don't free space until it's emptied."),
                             icon: "trash", urls: [trash], mode: .trash, size: FS.size(of: trash)))
        return out
    }

    static func clean(_ items: [SmartItem]) {
        for it in items where it.on && it.skipReason == nil {
            switch it.mode {
            case .contents:
                for f in it.urls {
                    for c in (try? FS.fm.contentsOfDirectory(at: f, includingPropertiesForKeys: nil, options: [])) ?? [] {
                        try? FS.fm.removeItem(at: c)
                    }
                }
            case .items:
                for u in it.urls { try? FS.fm.removeItem(at: u) }
            case .trash:
                var err: NSDictionary?
                NSAppleScript(source: "tell application \"Finder\" to empty trash")?.executeAndReturnError(&err)
                if err != nil {   // fall back to deleting directly
                    for c in (try? FS.fm.contentsOfDirectory(at: it.urls[0], includingPropertiesForKeys: nil, options: [])) ?? [] {
                        try? FS.fm.removeItem(at: c)
                    }
                }
            }
        }
    }
}

final class SmartModel: ObservableObject {
    enum Stage { case idle, scanning, ready, cleaning, done }
    @Published var stage: Stage = .idle
    @Published var items: [SmartItem] = []
    @Published var freed: Int64 = 0

    var selectedSize: Int64 { items.filter { $0.on && $0.skipReason == nil }.reduce(0) { $0 + $1.size } }

    func scan() {
        stage = .scanning
        DispatchQueue.global(qos: .userInitiated).async {
            let found = SmartScanner.scan()
            DispatchQueue.main.async { self.items = found; self.stage = .ready }
        }
    }

    func clean(onDone: @escaping () -> Void) {
        let chosen = items
        let estimate = selectedSize
        let before = DiskInfo.read().free
        stage = .cleaning
        DispatchQueue.global(qos: .userInitiated).async {
            SmartScanner.clean(chosen)
            Thread.sleep(forTimeInterval: 1)
            let after = DiskInfo.read().free
            DispatchQueue.main.async {
                self.freed = max(after - before, estimate)
                self.stage = .done
                onDone()
            }
        }
    }
}

struct SmartCleanView: View {
    @ObservedObject var m: SmartModel
    let onCleaned: () -> Void

    var body: some View {
        switch m.stage {
        case .idle:
            Spacer()
            VStack(spacing: 12) {
                Image(systemName: "sparkles").font(.system(size: 44)).foregroundStyle(.green)
                Text("Smart Cache Cleaner").font(.title2.bold())
                Text(L("শুধু সেই ফাইলগুলো খুঁজবে যেগুলো মুছলে কোন সমস্যা হয় না — cache, log, পুরোনো update, installer আর Trash।\nআগে আপনাকে তালিকা দেখাবে, তারপর এক click এ সব মুছে জায়গা খালি করবে।", "Finds only files that are safe to delete — caches, logs, old updates, installers and the Trash.\nShows you the list first, then deletes it all in one click."))
                    .multilineTextAlignment(.center).foregroundStyle(.secondary)
                Button { m.scan() } label: { Label(L("পরীক্ষা করুন", "Scan"), systemImage: "magnifyingglass") }
                    .buttonStyle(.borderedProminent).controlSize(.large).tint(.green)
            }
            Spacer()
        case .scanning:
            Spacer(); ProgressView(L("নিরাপদে মুছা যায় এমন ফাইল খোঁজা হচ্ছে…", "Looking for files that are safe to delete…")); Spacer()
        case .cleaning:
            Spacer(); ProgressView(L("পরিষ্কার করা হচ্ছে… (Trash খালি করার অনুমতি চাইলে OK দিন)", "Cleaning… (click OK if asked for permission to empty the Trash)")); Spacer()
        case .done:
            Spacer()
            VStack(spacing: 12) {
                Image(systemName: "checkmark.seal.fill").font(.system(size: 50)).foregroundStyle(.green)
                Text(L("প্রায় \(FS.pretty(m.freed)) জায়গা খালি হলো!", "About \(FS.pretty(m.freed)) freed!")).font(.title2.bold())
                Text(L("App গুলো প্রথমবার খুলতে একটু বেশি সময় নিতে পারে — ওরা নতুন করে cache বানাচ্ছে, এটা স্বাভাবিক।", "Apps may take a bit longer to open the first time — they're rebuilding their cache. That's normal."))
                    .font(.callout).foregroundStyle(.secondary).multilineTextAlignment(.center)
                Button { m.scan() } label: { Label(L("আবার পরীক্ষা করুন", "Check again"), systemImage: "arrow.clockwise") }
            }
            Spacer()
        case .ready:
            readyView
        }
    }

    @ViewBuilder private var readyView: some View {
        let skipped = m.items.filter { $0.skipReason != nil }
        HStack {
            VStack(alignment: .leading, spacing: 2) {
                Text(L("🟢 এগুলো মুছলে কোন সমস্যা নেই", "🟢 Safe to delete")).font(.headline).foregroundStyle(.green)
                Text(L("যেটা রাখতে চান তার টিক তুলে দিন। বাকি সব এক click এ মুছে যাবে।", "Untick anything you want to keep. Everything else is deleted in one click.")).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            Button { m.scan() } label: { Image(systemName: "arrow.clockwise") }.help(L("আবার পরীক্ষা করুন", "Check again"))
        }
        List {
            ForEach($m.items) { $it in
                if it.skipReason == nil { row($it) }
            }
            if !skipped.isEmpty {
                Section(L("⏸ এখন বাদ রাখা হলো", "⏸ Skipped for now")) {
                    ForEach(skipped) { it in
                        HStack(spacing: 10) {
                            Image(systemName: it.icon).frame(width: 24).foregroundStyle(.secondary)
                            VStack(alignment: .leading, spacing: 2) {
                                Text(it.title)
                                Text(it.skipReason ?? "").font(.caption).foregroundStyle(.secondary)
                            }
                            Spacer()
                            Text(FS.pretty(it.size)).foregroundStyle(.secondary).monospacedDigit()
                        }
                    }
                }
            }
        }
        .listStyle(.inset)
        HStack {
            Text(L("মোট খালি হবে: \(FS.pretty(m.selectedSize))", "Total to free: \(FS.pretty(m.selectedSize))")).font(.headline).foregroundStyle(.green)
            Spacer()
            Button {
                let n = m.items.filter { $0.on && $0.skipReason == nil }.count
                if Actions.confirm(L("\(n) টি জিনিস মুছে \(FS.pretty(m.selectedSize)) খালি করবেন?", "Delete \(n) items and free \(FS.pretty(m.selectedSize))?"),
                                   L("এগুলো সরাসরি মুছে যাবে, Trash ও খালি হবে — ফেরত আনা যাবে না। তবে সব cache / অস্থায়ী ফাইল, তাই কোন সমস্যা হবে না।", "These are deleted directly and the Trash is emptied — they can't be recovered. They're all caches / temporary files, so nothing will break."),
                                   ok: L("Clean করুন", "Clean")) {
                    m.clean(onDone: onCleaned)
                }
            } label: { Label(L("Clean করুন", "Clean"), systemImage: "sparkles").padding(.horizontal, 8) }
            .buttonStyle(.borderedProminent).controlSize(.large).tint(.green)
            .disabled(m.selectedSize == 0 && !m.items.contains { $0.mode == .trash && $0.on })
        }
    }

    private func row(_ it: Binding<SmartItem>) -> some View {
        HStack(spacing: 10) {
            Toggle("", isOn: it.on).labelsHidden().toggleStyle(.checkbox)
            Image(systemName: it.wrappedValue.icon).foregroundStyle(.white).frame(width: 28, height: 28)
                .background(Color.green.gradient, in: RoundedRectangle(cornerRadius: 6))
            VStack(alignment: .leading, spacing: 2) {
                Text(it.wrappedValue.title).lineLimit(1)
                Text(it.wrappedValue.note).font(.caption).foregroundStyle(.secondary).lineLimit(2)
            }
            Spacer()
            Text(it.wrappedValue.size > 0 ? FS.pretty(it.wrappedValue.size) : "—").font(.callout.bold()).monospacedDigit()
        }
        .opacity(it.wrappedValue.on ? 1 : 0.5)
        .padding(.vertical, 2)
    }
}
