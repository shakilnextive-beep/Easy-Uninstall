import SwiftUI
import AppKit
import UniformTypeIdentifiers

// MARK: - This Mac (model, picture, chip)

struct DeviceInfo {
    let name: String       // "Mac mini"
    let year: String?      // "2024"
    let chip: String       // "Apple M4"
    let memory: Int64
    let os: String         // "macOS 26.1"
    let image: NSImage?    // the system's own picture of this exact model
    let symbol: String     // fallback SF Symbol

    static let current = read()

    static func sysctl(_ key: String) -> String {
        var size = 0
        guard sysctlbyname(key, nil, &size, nil, 0) == 0, size > 0 else { return "" }
        var buf = [CChar](repeating: 0, count: size)
        guard sysctlbyname(key, &buf, &size, nil, 0) == 0 else { return "" }
        return String(cString: buf)
    }

    private static func read() -> DeviceInfo {
        let model = sysctl("hw.model")   // e.g. "Mac16,10"
        let type = UTType(tag: model, tagClass: UTTagClass(rawValue: "com.apple.device-model-code"), conformingTo: nil)
        let known = type.map { !$0.isDynamic && $0.identifier.hasPrefix("com.apple.") } ?? false
        let name = known ? (type?.localizedDescription ?? "Mac") : "Mac"
        var year: String?
        if known, let id = type?.identifier, let r = id.range(of: "(19|20)[0-9]{2}", options: .regularExpression) {
            year = String(id[r])
        }
        let image = known ? type.map { NSWorkspace.shared.icon(for: $0) } : nil
        let lower = name.lowercased()
        let symbol = lower.contains("book") ? "laptopcomputer"
            : lower.contains("mini") ? "macmini"
            : lower.contains("studio") ? "macstudio"
            : lower.contains("pro") ? "macpro.gen3"
            : "desktopcomputer"
        let v = ProcessInfo.processInfo.operatingSystemVersion
        let os = "macOS \(v.majorVersion).\(v.minorVersion)" + (v.patchVersion > 0 ? ".\(v.patchVersion)" : "")
        let chip = sysctl("machdep.cpu.brand_string")
        return DeviceInfo(name: name, year: year, chip: chip.isEmpty ? model : chip,
                          memory: Int64(ProcessInfo.processInfo.physicalMemory), os: os, image: image, symbol: symbol)
    }
}

struct DeviceBadge: View {
    private let d = DeviceInfo.current
    var body: some View {
        HStack(spacing: 14) {
            Group {
                if let img = d.image {
                    Image(nsImage: img).resizable().aspectRatio(contentMode: .fit)
                } else {
                    Image(systemName: d.symbol).resizable().aspectRatio(contentMode: .fit).foregroundStyle(Palette.lilac).padding(8)
                }
            }
            .frame(width: 150, height: 110)
            .scaleEffect(1.25)
            .shadow(color: .black.opacity(0.35), radius: 8, y: 4)
            VStack(alignment: .leading, spacing: 2) {
                Text(d.name).font(.system(size: 18, weight: .bold, design: .rounded)).foregroundStyle(.white)
                Text([d.year, d.chip].compactMap { $0 }.joined(separator: " · ")).font(.callout).foregroundStyle(Palette.lilac)
                Text("\(FS.pretty(d.memory)) RAM · \(d.os)").font(.caption).foregroundStyle(Palette.lilac.opacity(0.8))
            }
        }
    }
}

// MARK: - RAM in the header: tap to see apps, one button to clean

final class Flag: ObservableObject { @Published var on = false }

extension RamModel {
    /// Normal quit (the app asks to save). Red apps need a confirmation first.
    func askQuit(_ a: RamApp) {
        if !a.caution || Actions.confirm(L("\(a.name) বন্ধ করবেন?", "Quit \(a.name)?"), a.note, ok: L("বন্ধ করুন", "Quit")) { quit(a) }
    }

    /// "Clean RAM": quits every green (safe) app after one confirmation.
    func cleanSafe() {
        let green = apps.filter { !$0.caution }
        guard !green.isEmpty else {
            Actions.info(L("RAM পরিষ্কার আছে", "RAM is already clean"),
                         L("বন্ধ করার মতো নিরাপদ কোনো app চলছে না। লাল app গুলো কাজের app — সেগুলো নিজে save করে বন্ধ করুন।",
                           "No safe-to-quit apps are running. The red ones are work apps — save and quit them yourself."))
            return
        }
        let total = green.reduce(Int64(0)) { $0 + $1.memory }
        if Actions.confirm(L("RAM পরিষ্কার করবেন?", "Clean RAM?"),
                           L("এই \(green.count) টি app বন্ধ হবে, প্রায় \(FS.pretty(total)) RAM খালি হবে:\n\n", "These \(green.count) apps will quit, freeing about \(FS.pretty(total)) of RAM:\n\n")
                           + green.map(\.name).joined(separator: ", ")
                           + L("\n\nকাজের app (লাল) গুলো বন্ধ হবে না।", "\n\nWork apps (red) are left open."),
                           ok: L("পরিষ্কার করুন", "Clean")) {
            green.forEach { quit($0) }
        }
    }
}

struct RamChip: View {
    @ObservedObject var ram: RamModel
    var onDark = false
    let openAll: () -> Void
    @StateObject private var pop = Flag()

    var body: some View {
        let i = ram.info
        let frac = i.total > 0 ? min(1, Double(i.used) / Double(i.total)) : 0
        let color: Color = i.pressure >= 4 ? .red : i.pressure >= 2 ? Palette.amber : Palette.teal
        HStack(spacing: 4) {
            Button { pop.on.toggle() } label: {
                HStack(spacing: 10) {
                    Image(systemName: "memorychip").font(.system(size: 16, weight: .semibold)).foregroundStyle(color)
                    VStack(alignment: .leading, spacing: 4) {
                        HStack(spacing: 4) {
                            Text("RAM").fontWeight(.semibold).foregroundStyle(onDark ? Color.white : Color.primary)
                            Text("\(String(format: "%.1f", Double(i.used) / 1e9)) / \(FS.pretty(i.total))")
                                .foregroundStyle(onDark ? Palette.lilac : Color.secondary).monospacedDigit()
                        }
                        .font(.caption)
                        .fixedSize()
                        Bar(frac: frac, color: color).frame(width: 120, height: 5)
                    }
                    Image(systemName: "chevron.down").font(.caption2).foregroundStyle(onDark ? Palette.lilac : Color.secondary)
                }
                .padding(.leading, 12).padding(.trailing, 8).padding(.vertical, 7)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help(L("কোন app কত RAM নিচ্ছে দেখুন", "See which app uses how much RAM"))
            .popover(isPresented: $pop.on, arrowEdge: .bottom) {
                RamPopover(ram: ram) { pop.on = false; openAll() }
            }

            Button { ram.cleanSafe() } label: {
                Label(L("Clean", "Clean"), systemImage: "wand.and.stars")
                    .font(.caption.weight(.semibold))
                    .lineLimit(1).fixedSize()
                    .padding(.horizontal, 10).padding(.vertical, 6)
                    .foregroundStyle(.white)
                    .background(color, in: Capsule())
            }
            .buttonStyle(.plain)
            .help(L("নিরাপদ (সবুজ) app গুলো বন্ধ করে RAM খালি করুন", "Free RAM by quitting safe (green) apps"))
            .padding(.trailing, 6)
        }
        .fixedSize()
        .background(onDark ? Color.white.opacity(0.1) : Palette.tile, in: Capsule())
        .overlay(Capsule().strokeBorder((onDark ? Color.white : Color.primary).opacity(0.1)))
    }
}

struct RamPopover: View {
    @ObservedObject var ram: RamModel
    let openAll: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text(L("RAM কে কতটুকু নিচ্ছে", "What's using RAM")).font(.headline)
                Spacer()
                Text("\(FS.pretty(ram.info.used)) / \(FS.pretty(ram.info.total))").foregroundStyle(.secondary).monospacedDigit()
            }
            if ram.apps.isEmpty {
                ProgressView().controlSize(.small).frame(maxWidth: .infinity)
            }
            ForEach(ram.apps.prefix(8)) { a in
                let tint: Color = a.caution ? Palette.coral : Palette.teal
                HStack(spacing: 8) {
                    Circle().fill(tint).frame(width: 7, height: 7)
                    if let ic = a.icon { Image(nsImage: ic).resizable().frame(width: 22, height: 22) }
                    Text(a.name).lineLimit(1)
                    Spacer()
                    Text(FS.pretty(a.memory)).font(.callout.bold()).monospacedDigit()
                    Button(L("বন্ধ", "Quit")) { ram.askQuit(a) }.controlSize(.small)
                }
                .help(a.note)
            }
            Divider()
            HStack {
                Button { ram.cleanSafe() } label: { Label(L("সবুজ app বন্ধ করে Clean", "Clean: quit green apps"), systemImage: "wand.and.stars") }
                    .buttonStyle(.borderedProminent).tint(Palette.teal)
                Spacer()
                Button(L("সব দেখুন →", "See all →"), action: openAll)
            }
            Text(L("🟢 নিরাপদে বন্ধ করা যায়  ·  🔴 কাজের app, আগে save করুন", "🟢 safe to quit  ·  🔴 work app, save first"))
                .font(.caption).foregroundStyle(.secondary)
        }
        .padding(14)
        .frame(width: 380)
    }
}

// MARK: - App picker (grid or list, pick several, uninstall together)

enum IconCache {
    private static var cache: [String: NSImage] = [:]
    static func icon(_ path: String) -> NSImage {
        if let i = cache[path] { return i }
        let i = NSWorkspace.shared.icon(forFile: path)
        cache[path] = i
        return i
    }
}

struct BatchJob: Identifiable {
    let id = UUID()
    let apps: [InstalledApp]
}

struct AppsPanel: View {
    @ObservedObject var store: Store
    var compact = false
    var open: ((InstalledApp) -> Void)? = nil   // home page: a click opens the Uninstall page

    private func tap(_ app: InstalledApp) {
        if let open { open(app) } else { store.toggle(app) }
    }

    var body: some View {
        let list = store.filtered
        let picked = store.apps.filter { store.picked.contains($0.id) }
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 10) {
                TileLabel(icon: "trash", text: L("Uninstall — \(store.apps.count) টি app", "Uninstall — \(store.apps.count) apps"), color: .secondary)
                Spacer()
                SearchField(text: $store.query, placeholder: L("App খুঁজুন…", "Find an app…")).frame(maxWidth: 200)
                Picker("", selection: $store.grid) {
                    Image(systemName: "square.grid.2x2").tag(true)
                    Image(systemName: "list.bullet").tag(false)
                }
                .pickerStyle(.segmented).labelsHidden().frame(width: 76)
                .help(L("Grid / List", "Grid / List"))
            }

            if store.loading && store.apps.isEmpty {
                ProgressView(L("Apps খোঁজা হচ্ছে…", "Looking for apps…")).frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if list.isEmpty {
                Text(L("কোন app পাওয়া যায়নি", "No apps found")).foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                ScrollView {
                    if store.grid {
                        LazyVGrid(columns: [GridItem(.adaptive(minimum: compact ? 92 : 104), spacing: 8)], spacing: 8) {
                            ForEach(list) { app in AppCell(app: app, size: store.sizes[app.id], on: store.picked.contains(app.id)) { tap(app) }
                                .onAppear { store.loadSize(app) } }
                        }
                        .padding(2)
                    } else {
                        LazyVStack(spacing: 2) {
                            ForEach(list) { app in AppLine(app: app, size: store.sizes[app.id], on: store.picked.contains(app.id)) { tap(app) }
                                .onAppear { store.loadSize(app) } }
                        }
                    }
                }
            }

            HStack(spacing: 10) {
                if picked.isEmpty {
                    Text(open != nil ? L("কোনো app এ click করলে Uninstall পেজ খুলবে", "Click an app to open the Uninstall page")
                                     : L("যেগুলো মুছতে চান সেগুলোতে click করে বেছে নিন", "Click the apps you want to remove"))
                        .font(.callout).foregroundStyle(.secondary).lineLimit(1)
                    DropHint()
                } else {
                    let size = picked.reduce(Int64(0)) { $0 + max(0, store.sizes[$1.id] ?? 0) }
                    Text(L("\(picked.count) টি বাছাই · \(FS.pretty(size))", "\(picked.count) picked · \(FS.pretty(size))")).font(.callout.weight(.semibold))
                    Button(L("বাছাই মুছুন", "Clear")) { store.picked = [] }.buttonStyle(.link)
                }
                Spacer()
                Button {
                    if let open, let first = picked.first { open(first) }
                    else if picked.count == 1 { store.selected = picked[0] } else { store.batch = BatchJob(apps: picked) }
                } label: {
                    Label(picked.isEmpty ? "Uninstall" : "Uninstall (\(picked.count))", systemImage: "trash")
                }
                .buttonStyle(.borderedProminent).tint(Palette.coral)
                .disabled(picked.isEmpty)
            }
        }
    }
}

private struct PickMark: View {
    let on: Bool
    let locked: Bool
    var body: some View {
        Image(systemName: locked ? "lock.circle.fill" : on ? "checkmark.circle.fill" : "circle")
            .font(.system(size: 17, weight: .semibold))
            .foregroundStyle(locked ? Color.secondary : on ? Palette.coral : Color.secondary.opacity(0.6))
            .background(Circle().fill(Color(nsColor: .windowBackgroundColor)).padding(2))
    }
}

struct AppCell: View {
    let app: InstalledApp
    let size: Int64?
    let on: Bool
    let toggle: () -> Void

    var body: some View {
        VStack(spacing: 4) {
            ZStack(alignment: .topTrailing) {
                Image(nsImage: IconCache.icon(app.url.path)).resizable().frame(width: 52, height: 52)
                    .frame(maxWidth: .infinity)
                PickMark(on: on, locked: app.isProtected)
            }
            Text(app.name).font(.caption).lineLimit(2).multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
                .frame(height: 32, alignment: .top)
            Text(size.map { $0 >= 0 ? FS.pretty($0) : "…" } ?? " ").font(.caption2).foregroundStyle(.secondary).monospacedDigit()
        }
        .padding(8)
        .background(on ? Palette.coral.opacity(0.14) : Color.primary.opacity(0.03), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(on ? Palette.coral.opacity(0.7) : .clear, lineWidth: 1.5))
        .opacity(app.isProtected ? 0.5 : 1)
        .contentShape(Rectangle())
        .onTapGesture { if !app.isProtected { toggle() } }
        .help(app.isProtected ? L("Apple এর system app — মুছা বন্ধ", "Apple system app — can't be removed") : app.name)
    }
}

struct AppLine: View {
    let app: InstalledApp
    let size: Int64?
    let on: Bool
    let toggle: () -> Void

    var body: some View {
        HStack(spacing: 10) {
            PickMark(on: on, locked: app.isProtected)
            Image(nsImage: IconCache.icon(app.url.path)).resizable().frame(width: 28, height: 28)
            VStack(alignment: .leading, spacing: 1) {
                Text(app.name).lineLimit(1)
                if !app.version.isEmpty { Text("Version \(app.version)").font(.caption2).foregroundStyle(.secondary) }
            }
            Spacer()
            if app.isProtected {
                Text("System").font(.caption2).padding(.horizontal, 6).padding(.vertical, 2)
                    .background(Color.secondary.opacity(0.15), in: Capsule())
            }
            Text(size.map { $0 >= 0 ? FS.pretty($0) : "…" } ?? "").font(.caption).foregroundStyle(.secondary).monospacedDigit()
        }
        .padding(.horizontal, 8).padding(.vertical, 5)
        .background(on ? Palette.coral.opacity(0.14) : .clear, in: RoundedRectangle(cornerRadius: 8))
        .opacity(app.isProtected ? 0.5 : 1)
        .contentShape(Rectangle())
        .onTapGesture { if !app.isProtected { toggle() } }
    }
}

// MARK: - Uninstall several apps at once

struct OwnedLeftover: Identifiable {
    let owner: String
    var item: Leftover
    var id: String { item.id }
}

final class BatchModel: ObservableObject {
    @Published var stage: Stage = .choose
    @Published var leftovers: [OwnedLeftover] = []
}

struct BatchUninstallSheet: View {
    let apps: [InstalledApp]
    let all: [InstalledApp]
    let done: (Bool) -> Void
    @StateObject private var m = BatchModel()

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: -10) {
                ForEach(apps.prefix(8)) { a in
                    Image(nsImage: IconCache.icon(a.url.path)).resizable().frame(width: 44, height: 44)
                }
            }
            VStack(alignment: .leading, spacing: 3) {
                Text(L("\(apps.count) টি app Uninstall", "Uninstall \(apps.count) apps")).font(.title2.bold())
                Text(apps.map(\.name).joined(separator: ", ")).font(.caption).foregroundStyle(.secondary).lineLimit(3)
            }

            switch m.stage {
            case .choose:
                OptionButton(icon: "trash", color: .blue, title: L("শুধু Uninstall", "Only uninstall"),
                             subtitle: L("শুধু app গুলো Trash এ যাবে। Settings ও data থেকে যাবে।", "Only the apps go to the Trash. Settings and data stay.")) { removeSimple() }
                OptionButton(icon: "flame.fill", color: .green, title: "Full Uninstall",
                             subtitle: L("App গুলো + এদের Cache, Preferences, Application Support, Logs, Containers সহ সব ফাইল মুছবে।", "Deletes the apps plus their Cache, Preferences, Application Support, Logs, Containers and every other file.")) { scanFull() }
                HStack { Spacer(); Button(L("বাতিল", "Cancel")) { done(false) }.keyboardShortcut(.cancelAction) }
            case .scanning:
                HStack { ProgressView().controlSize(.small); Text(L("সব related ফাইল খোঁজা হচ্ছে…", "Finding all related files…")) }
            case .review:
                review
            case .working:
                HStack { ProgressView().controlSize(.small); Text(L("মুছে ফেলা হচ্ছে…", "Removing…")) }
            case .finished(let msg):
                Text(msg)
                HStack { Spacer(); Button(L("ঠিক আছে", "OK")) { done(true) }.keyboardShortcut(.defaultAction) }
            }
        }
        .padding(24)
        .frame(width: 640)
    }

    @ViewBuilder private var review: some View {
        let appsSize = apps.reduce(Int64(0)) { $0 + FS.size(of: $1.url) }
        let total = m.leftovers.filter(\.item.selected).reduce(appsSize) { $0 + $1.item.size }
        Text(m.leftovers.isEmpty
             ? L("কোন extra ফাইল পাওয়া যায়নি — শুধু app গুলো মুছবে।", "No extra files found — only the apps will be deleted.")
             : L("\(m.leftovers.count) টি related ফাইল/ফোল্ডার পাওয়া গেছে। যেগুলো রাখতে চান সেগুলোর টিক তুলে দিন:", "Found \(m.leftovers.count) related files/folders. Untick the ones you want to keep:"))
            .font(.callout)
        if m.leftovers.contains(where: { !$0.item.selected }) {
            Label(L("টিক ছাড়া ফাইলগুলো আরেকটি install করা version ও ব্যবহার করে — নিশ্চিত হলে নিজে টিক দিন।", "Unticked files are also used by another installed version — tick them yourself if you're sure."), systemImage: "exclamationmark.triangle.fill")
                .font(.caption).foregroundStyle(.orange)
        }
        if !m.leftovers.isEmpty {
            List {
                ForEach($m.leftovers) { $o in
                    Toggle(isOn: $o.item.selected) {
                        HStack {
                            Text(o.owner).font(.caption.bold()).frame(width: 120, alignment: .leading).lineLimit(1)
                            Text(FS.short(o.item.url)).font(.system(size: 12, design: .monospaced)).lineLimit(1).truncationMode(.middle)
                            Spacer()
                            Text(FS.pretty(o.item.size)).font(.caption).foregroundStyle(.secondary).monospacedDigit()
                        }
                    }
                    .toggleStyle(.checkbox)
                }
            }
            .frame(height: 260)
            .listStyle(.bordered)
        }
        HStack {
            Text(L("মোট খালি হবে: \(FS.pretty(total))", "Total to free: \(FS.pretty(total))")).font(.callout.bold())
            Spacer()
            Button(L("বাতিল", "Cancel")) { done(false) }.keyboardShortcut(.cancelAction)
            Button(L("সব মুছে ফেলুন", "Delete everything")) { removeFull() }.keyboardShortcut(.defaultAction).tint(.red)
        }
    }

    private func removeSimple() {
        m.stage = .working
        DispatchQueue.global().async {
            apps.forEach(Remover.quitIfRunning)
            let failed = Remover.trash(apps.map(\.url))
            DispatchQueue.main.async { m.stage = .finished(message(failed, full: false)) }
        }
    }

    private func scanFull() {
        m.stage = .scanning
        let ids = Set(apps.map(\.id))
        DispatchQueue.global().async {
            var seen = Set<String>()
            var out: [OwnedLeftover] = []
            for app in apps {
                // another version that stays installed shares these files: don't tick them
                let shared = !app.bundleID.isEmpty && all.contains { !ids.contains($0.id) && $0.bundleID == app.bundleID }
                for var l in Scanner.leftovers(for: app) where !seen.contains(l.id) {
                    seen.insert(l.id)
                    if shared { l.selected = false }
                    out.append(OwnedLeftover(owner: app.name, item: l))
                }
            }
            DispatchQueue.main.async { m.leftovers = out; m.stage = .review }
        }
    }

    private func removeFull() {
        m.stage = .working
        let urls = apps.map(\.url) + m.leftovers.filter(\.item.selected).map(\.item.url)
        DispatchQueue.global().async {
            apps.forEach(Remover.quitIfRunning)
            let failed = Remover.trash(urls)
            DispatchQueue.main.async { m.stage = .finished(message(failed, full: true)) }
        }
    }

    private func message(_ failed: [String], full: Bool) -> String {
        if failed.isEmpty {
            return full ? L("✅ \(apps.count) টি app এবং এদের সব ফাইল Trash এ পাঠানো হয়েছে। Trash খালি করলে স্থায়ীভাবে মুছে যাবে।", "✅ \(apps.count) apps and all their files were moved to the Trash. Empty the Trash to delete them for good.")
                        : L("✅ \(apps.count) টি app Trash এ পাঠানো হয়েছে।", "✅ \(apps.count) apps were moved to the Trash.")
        }
        return L("⚠️ কিছু ফাইল মুছা যায়নি (password দেননি বা অনুমতি নেই):\n", "⚠️ Some files couldn't be deleted (no password given or no permission):\n") + failed.joined(separator: "\n")
    }
}
