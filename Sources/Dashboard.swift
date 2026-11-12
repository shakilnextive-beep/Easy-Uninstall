import SwiftUI
import AppKit

// MARK: - Navigation

enum AppSection: String, Hashable, CaseIterable {
    case home, smart, suggestions, explorer, bigFiles, uninstall, unused, ram, about

    var title: String {
        switch self {
        case .home: return L("হোম", "Home")
        case .smart: return "Smart Clean"
        case .suggestions: return L("পরামর্শ", "Suggestions")
        case .explorer: return L("জায়গা কোথায় গেল", "Where's my space")
        case .bigFiles: return L("বড় ফাইল", "Big files")
        case .uninstall: return "Uninstall"
        case .unused: return L("অপ্রয়োজনীয় Software", "Unused software")
        case .ram: return "RAM"
        case .about: return L("পরিচিতি ও Update", "About and updates")
        }
    }
    var icon: String {
        switch self {
        case .home: return "square.grid.2x2"
        case .smart: return "sparkles"
        case .suggestions: return "lightbulb"
        case .explorer: return "folder.badge.questionmark"
        case .bigFiles: return "doc.on.doc"
        case .uninstall: return "trash"
        case .unused: return "shippingbox"
        case .ram: return "memorychip"
        case .about: return "info.circle"
        }
    }
    var tint: Color {
        switch self {
        case .home: return Palette.purple
        case .smart, .suggestions: return Palette.teal
        case .explorer, .bigFiles: return Palette.coral
        case .uninstall, .unused: return Palette.purple
        case .ram: return Palette.amber
        case .about: return Palette.stone
        }
    }
}

final class Nav: ObservableObject {
    @Published var section: AppSection = .home
}

enum Palette {
    static let ink = Color(red: 0x26/255, green: 0x21/255, blue: 0x5C/255)       // deep purple
    static let purple = Color(red: 0x7F/255, green: 0x77/255, blue: 0xDD/255)
    static let lilac = Color(red: 0xCE/255, green: 0xCB/255, blue: 0xF6/255)
    static let teal = Color(red: 0x1D/255, green: 0x9E/255, blue: 0x75/255)
    static let mint = Color(red: 0x5D/255, green: 0xCA/255, blue: 0xA5/255)
    static let coral = Color(red: 0xD8/255, green: 0x5A/255, blue: 0x30/255)
    static let peach = Color(red: 0xF0/255, green: 0x99/255, blue: 0x7B/255)
    static let amber = Color(red: 0xEF/255, green: 0x9F/255, blue: 0x27/255)
    static let stone = Color(red: 0xB4/255, green: 0xB2/255, blue: 0xA9/255)
    static let tile = Color.primary.opacity(0.05)
}

struct Sidebar: View {
    @ObservedObject var nav: Nav
    @ObservedObject private var lang = Lang.shared

    var body: some View {
        List(selection: Binding(get: { nav.section }, set: { if let s = $0 { nav.section = s } })) {
            HStack(spacing: 10) {
                Image(nsImage: Bundle.main.image(forResource: "AppIcon") ?? NSApp.applicationIconImage).resizable().frame(width: 30, height: 30)
                Text("Easy Uninstall").font(.headline)
            }
            .padding(.vertical, 6)

            item(.home)
            SwiftUI.Section(L("পরিষ্কার", "Clean")) {
                item(.smart); item(.suggestions); item(.explorer); item(.bigFiles)
            }
            SwiftUI.Section("Apps") {
                item(.uninstall); item(.unused)
            }
            SwiftUI.Section(L("সিস্টেম", "System")) {
                item(.ram)
            }
            SwiftUI.Section(L("অন্যান্য", "More")) {
                item(.about)
            }
        }
        .listStyle(.sidebar)
        .safeAreaInset(edge: .bottom) {
            VStack(spacing: 10) {
                UpdateBanner()
                Button { nav.section = .about } label: {
                    VStack(spacing: 1) {
                        Text("Creator: \(AppInfo.creator)").font(.caption.weight(.medium))
                        Text(AppInfo.email).font(.caption2).foregroundStyle(.secondary)
                    }
                }
                .buttonStyle(.plain)
                LangSwitch()
            }
            .padding(12)
        }
    }

    private func item(_ s: AppSection) -> some View {
        Label {
            Text(s.title)
        } icon: {
            Image(systemName: s.icon)
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(.white)
                .frame(width: 22, height: 22)
                .background(s.tint, in: RoundedRectangle(cornerRadius: 6, style: .continuous))
        }
        .tag(s)
    }
}

// MARK: - Disk breakdown for the big tile

final class Breakdown: ObservableObject {
    struct Part: Identifiable { let id = UUID(); let name: String; let size: Int64; let color: Color }
    @Published var parts: [Part] = []
    @Published var loading = false

    func load(used: Int64) {
        if loading || !parts.isEmpty { return }
        loading = true
        DispatchQueue.global(qos: .utility).async {
            let h = FS.home
            let apps = FS.size(of: URL(fileURLWithPath: "/Applications"))
            let files = ["Documents", "Desktop", "Downloads", "Movies", "Music", "Pictures"]
                .reduce(Int64(0)) { $0 + FS.size(of: h.appendingPathComponent($1)) }
            let library = FS.size(of: h.appendingPathComponent("Library"))
            let other = max(0, used - apps - files - library)
            DispatchQueue.main.async {
                self.parts = [
                    Part(name: "Apps", size: apps, color: Palette.purple),
                    Part(name: L("আপনার ফাইল", "Your files"), size: files, color: Palette.mint),
                    Part(name: L("App data ও cache", "App data and cache"), size: library, color: Palette.peach),
                    Part(name: "macOS", size: other, color: Palette.stone),
                ]
                self.loading = false
            }
        }
    }
}

// MARK: - Dashboard (bento)

struct DashboardView: View {
    @ObservedObject var nav: Nav
    @ObservedObject var store: Store
    @ObservedObject var cleanup: CleanupModel
    @ObservedObject var smart: SmartModel
    @ObservedObject var bloat: BloatModel
    @ObservedObject var ram: RamModel
    @StateObject private var breakdown = Breakdown()

    private let gap: CGFloat = 12

    var body: some View {
        GeometryReader { g in
            let col = (g.size.width - 48 - gap * 2) / 3
            ScrollView {
                VStack(alignment: .leading, spacing: gap) {
                    header
                    HStack(alignment: .top, spacing: gap) {
                        diskTile.frame(width: col * 2 + gap, height: 250)
                        VStack(spacing: gap) {
                            smartTile.frame(height: 119)
                            cleanTile.frame(height: 119)
                        }.frame(width: col)
                    }
                    HStack(alignment: .top, spacing: gap) {
                        ramTile.frame(width: col, height: 170)
                        topTile.frame(width: col * 2 + gap, height: 170)
                    }
                    HStack(spacing: gap) {
                        unusedTile.frame(width: col, height: 120)
                        appsTile.frame(width: col, height: 120)
                        bigTile.frame(width: col, height: 120)
                    }
                }
                .padding(24)
            }
        }
        .onAppear {
            cleanup.refreshDisk()
            breakdown.load(used: cleanup.disk.used)
            if cleanup.suggestions.isEmpty && !cleanup.scanning { cleanup.scanSuggestions(apps: store.apps) }
            if bloat.usage.isEmpty && !bloat.loadingUsage { bloat.loadUsage(store.apps) }
            ram.start()
        }
        .onDisappear { ram.stop() }
    }

    // Header

    private var header: some View {
        HStack(alignment: .firstTextBaseline) {
            VStack(alignment: .leading, spacing: 2) {
                Text(L("আপনার Mac", "Your Mac")).font(.system(size: 26, weight: .bold, design: .rounded))
                Text(statusLine).foregroundStyle(.secondary)
            }
            Spacer()
        }
    }

    private var statusLine: String {
        let frac = cleanup.disk.total > 0 ? Double(cleanup.disk.used) / Double(cleanup.disk.total) : 0
        if frac > 0.85 { return L("Disk প্রায় ভরা — নিচে দেখুন কোথায় জায়গা খালি করা যায়", "Disk is almost full — see below where to free space") }
        return L("সব ঠিকঠাক চলছে", "Everything looks good")
    }

    // Tiles

    private var diskTile: some View {
        let d = cleanup.disk
        let pct = d.total > 0 ? Int(Double(d.used) / Double(d.total) * 100) : 0
        return Tile(bg: Palette.ink) {
            VStack(alignment: .leading, spacing: 0) {
                TileLabel(icon: "internaldrive", text: "Macintosh HD", color: Palette.lilac)
                Spacer()
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(FS.pretty(d.free)).font(.system(size: 46, weight: .bold, design: .rounded))
                    Text(L("খালি", "free")).font(.title3).foregroundStyle(Palette.lilac)
                }
                .foregroundStyle(.white)
                Text(L("\(FS.pretty(d.total)) এর \(pct)% ভরা", "\(pct)% of \(FS.pretty(d.total)) used"))
                    .font(.callout).foregroundStyle(Palette.lilac).padding(.bottom, 14)
                StackBar(parts: breakdown.parts, total: d.total, loading: breakdown.loading)
                    .frame(height: 12)
                HStack(spacing: 14) {
                    if breakdown.parts.isEmpty {
                        Text(L("কিসে কত জায়গা — মাপা হচ্ছে…", "Measuring what uses space…")).foregroundStyle(Palette.lilac)
                    }
                    ForEach(breakdown.parts) { p in
                        HStack(spacing: 5) {
                            Circle().fill(p.color).frame(width: 8, height: 8)
                            Text("\(p.name) \(FS.pretty(p.size))")
                        }
                    }
                }
                .font(.caption).foregroundStyle(Palette.lilac).padding(.top, 10)
            }
        }
        .onTapGesture { nav.section = .explorer }
    }

    private var smartTile: some View {
        let ready = smart.stage == .ready || smart.stage == .done
        let value = smart.stage == .ready ? FS.pretty(smart.selectedSize) : smart.stage == .done ? "✓" : "—"
        return Tile(bg: Palette.teal.opacity(0.16)) {
            VStack(alignment: .leading) {
                TileLabel(icon: "sparkles", text: "Smart Clean", color: Palette.teal)
                Spacer()
                Text(value).font(.system(size: 26, weight: .bold, design: .rounded)).foregroundStyle(Palette.teal)
                Text(ready ? L("নিরাপদে মুছা যাবে", "safe to delete") : L("এখনো পরীক্ষা হয়নি", "not scanned yet"))
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .onTapGesture { nav.section = .smart }
    }

    private var cleanTile: some View {
        Tile(bg: Palette.teal) {
            VStack(spacing: 6) {
                Image(systemName: smart.stage == .scanning ? "hourglass" : "wand.and.stars").font(.system(size: 26, weight: .semibold))
                Text(L("এক click এ Clean", "One-click clean")).font(.headline)
            }
            .foregroundStyle(.white)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .onTapGesture {
            if smart.stage != .ready { smart.scan() }
            nav.section = .smart
        }
    }

    private var ramTile: some View {
        let i = ram.info
        let frac = i.total > 0 ? min(1, Double(i.used) / Double(i.total)) : 0
        let c: Color = i.pressure >= 4 ? .red : i.pressure >= 2 ? Palette.amber : Palette.teal
        return Tile(bg: Palette.tile) {
            VStack(alignment: .leading, spacing: 6) {
                TileLabel(icon: "memorychip", text: "RAM", color: .secondary)
                Spacer()
                HStack(alignment: .firstTextBaseline, spacing: 4) {
                    Text(String(format: "%.1f", Double(i.used) / 1e9)).font(.system(size: 30, weight: .bold, design: .rounded))
                    Text("/ \(FS.pretty(i.total))").foregroundStyle(.secondary)
                }
                Bar(frac: frac, color: c).frame(height: 8)
                if i.swapUsed > 4_000_000_000 {
                    Text(L("Swap \(FS.pretty(i.swapUsed)) — Restart দিলে ভালো", "Swap \(FS.pretty(i.swapUsed)) — a restart would help"))
                        .font(.caption).foregroundStyle(Palette.amber).lineLimit(1)
                }
            }
        }
        .onTapGesture { nav.section = .ram }
    }

    private var topTile: some View {
        let top = cleanup.suggestions.filter { $0.size > 0 }.sorted { $0.size > $1.size }.prefix(3)
        return Tile(bg: Palette.tile) {
            VStack(alignment: .leading, spacing: 8) {
                TileLabel(icon: "flame", text: L("সবচেয়ে বেশি জায়গা নিচ্ছে", "Taking the most space"), color: .secondary)
                if cleanup.scanning {
                    Spacer(); ProgressView().controlSize(.small).frame(maxWidth: .infinity); Spacer()
                } else {
                    ForEach(Array(top)) { s in
                        HStack(spacing: 10) {
                            Image(systemName: s.icon)
                                .font(.system(size: 12, weight: .semibold))
                                .foregroundStyle(s.safety == .safe ? Palette.teal : Palette.coral)
                                .frame(width: 26, height: 26)
                                .background((s.safety == .safe ? Palette.teal : Palette.coral).opacity(0.15), in: RoundedRectangle(cornerRadius: 7))
                            Text(s.title).lineLimit(1)
                            Spacer()
                            Pill(safe: s.safety == .safe)
                            Text(FS.pretty(s.size)).font(.callout.bold()).monospacedDigit().frame(width: 72, alignment: .trailing)
                        }
                    }
                    Spacer(minLength: 0)
                }
            }
        }
        .onTapGesture { nav.section = .suggestions }
    }

    private var unusedTile: some View {
        let n = bloat.usage.filter { ($0.days ?? 9999) >= 90 }.count
        return smallTile(icon: "shippingbox", label: L("অপ্রয়োজনীয়", "Unused"),
                         value: bloat.loadingUsage ? "…" : "\(n)", sub: L("৩ মাস খোলা হয়নি", "not opened in 3 months"), go: .unused)
    }

    private var appsTile: some View {
        smallTile(icon: "app.badge", label: "Apps", value: store.loading ? "…" : "\(store.apps.count)",
                  sub: L("Uninstall করতে click করুন", "click to uninstall"), go: .uninstall)
    }

    private var bigTile: some View {
        smallTile(icon: "doc.on.doc", label: L("বড় ফাইল", "Big files"),
                  value: cleanup.bigScanned ? "\(cleanup.bigFiles.count)" : "—",
                  sub: L("500 MB এর বেশি", "over 500 MB"), go: .bigFiles)
    }

    private func smallTile(icon: String, label: String, value: String, sub: String, go: AppSection) -> some View {
        Tile(bg: Palette.tile) {
            VStack(alignment: .leading, spacing: 2) {
                TileLabel(icon: icon, text: label, color: .secondary)
                Spacer()
                Text(value).font(.system(size: 28, weight: .bold, design: .rounded))
                Text(sub).font(.caption).foregroundStyle(.secondary).lineLimit(1)
            }
        }
        .onTapGesture { nav.section = go }
    }
}

// MARK: - Building blocks

struct Tile<Content: View>: View {
    let bg: Color
    @ViewBuilder let content: Content
    @StateObject private var hover = HoverState()

    var body: some View {
        content
            .padding(16)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .background(bg, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 18, style: .continuous).strokeBorder(Color.primary.opacity(hover.on ? 0.25 : 0.06)))
            .scaleEffect(hover.on ? 1.01 : 1)
            .animation(.easeOut(duration: 0.15), value: hover.on)
            .contentShape(Rectangle())
            .onHover { hover.on = $0; if $0 { NSCursor.pointingHand.push() } else { NSCursor.pop() } }
    }
}

final class HoverState: ObservableObject { @Published var on = false }

struct TileLabel: View {
    let icon: String
    let text: String
    let color: Color
    var body: some View {
        Label(text, systemImage: icon).font(.subheadline.weight(.medium)).foregroundStyle(color)
    }
}

struct Pill: View {
    let safe: Bool
    var body: some View {
        Text(safe ? L("নিরাপদ", "Safe") : L("সাবধান", "Careful"))
            .font(.caption2.weight(.semibold))
            .padding(.horizontal, 8).padding(.vertical, 3)
            .foregroundStyle(safe ? Palette.teal : Palette.coral)
            .background((safe ? Palette.teal : Palette.coral).opacity(0.15), in: Capsule())
    }
}

struct Bar: View {
    let frac: Double
    let color: Color
    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                Capsule().fill(Color.primary.opacity(0.1))
                Capsule().fill(color).frame(width: max(6, g.size.width * frac))
            }
        }
    }
}

struct StackBar: View {
    let parts: [Breakdown.Part]
    let total: Int64
    let loading: Bool
    var body: some View {
        GeometryReader { g in
            HStack(spacing: 3) {
                if parts.isEmpty {
                    Capsule().fill(Color.white.opacity(loading ? 0.25 : 0.15))
                } else {
                    ForEach(parts) { p in
                        Capsule().fill(p.color)
                            .frame(width: max(4, g.size.width * CGFloat(p.size) / CGFloat(max(total, 1))))
                    }
                    Capsule().fill(Color.white.opacity(0.15))
                }
            }
        }
        .animation(.easeOut(duration: 0.6), value: parts.count)
    }
}
