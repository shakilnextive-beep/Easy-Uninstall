import SwiftUI
import AppKit
import Darwin

// MARK: - RAM

struct MemInfo {
    var total: Int64 = 0
    var used: Int64 = 0          // app memory + wired + compressed (like Activity Monitor)
    var swapTotal: Int64 = 0
    var swapUsed: Int64 = 0
    var pressure = 1              // 1 normal, 2 warning, 4 critical

    static func read() -> MemInfo {
        var m = MemInfo()
        m.total = Int64(ProcessInfo.processInfo.physicalMemory)

        var stats = vm_statistics64()
        var count = mach_msg_type_number_t(MemoryLayout<vm_statistics64_data_t>.size / MemoryLayout<integer_t>.size)
        let kr = withUnsafeMutablePointer(to: &stats) {
            $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) { host_statistics64(mach_host_self(), HOST_VM_INFO64, $0, &count) }
        }
        if kr == KERN_SUCCESS {
            let page = Int64(vm_kernel_page_size)
            let app = Int64(stats.internal_page_count) - Int64(stats.purgeable_count)
            m.used = (app + Int64(stats.wire_count) + Int64(stats.compressor_page_count)) * page
        }

        var swap = xsw_usage(); var size = MemoryLayout<xsw_usage>.size
        if sysctlbyname("vm.swapusage", &swap, &size, nil, 0) == 0 {
            m.swapTotal = Int64(swap.xsu_total); m.swapUsed = Int64(swap.xsu_used)
        }
        var level: Int32 = 1; var lsize = MemoryLayout<Int32>.size
        if sysctlbyname("kern.memorystatus_vm_pressure_level", &level, &lsize, nil, 0) == 0 { m.pressure = Int(level) }
        return m
    }
}

struct RamApp: Identifiable {
    var id: pid_t { pid }
    let pid: pid_t
    let name: String
    let icon: NSImage?
    let memory: Int64
    let caution: Bool
    let note: String
    let duplicate: String?       // other running version, e.g. "Adobe Premiere Pro 2026"
    let app: NSRunningApplication
}

enum RamScanner {
    static func footprint(_ pid: pid_t) -> Int64 {
        var info = rusage_info_v4()
        let r = withUnsafeMutablePointer(to: &info) {
            $0.withMemoryRebound(to: rusage_info_t?.self, capacity: 1) { proc_pid_rusage(pid, RUSAGE_INFO_V4, $0) }
        }
        return r == 0 ? Int64(info.ri_phys_footprint) : 0
    }

    /// Memory per .app bundle path, counting every helper process inside it.
    static func memoryByBundle() -> [String: Int64] {
        let n = proc_listallpids(nil, 0)
        guard n > 0 else { return [:] }
        var pids = [pid_t](repeating: 0, count: Int(n) + 64)
        let got = proc_listallpids(&pids, Int32(pids.count * MemoryLayout<pid_t>.size))
        var out: [String: Int64] = [:]
        var buf = [CChar](repeating: 0, count: 4096)
        for pid in pids.prefix(Int(max(got, 0))) where pid > 0 {
            guard proc_pidpath(pid, &buf, UInt32(buf.count)) > 0 else { continue }
            let path = String(cString: buf)
            guard let r = path.range(of: ".app/") else { continue }
            let bundle = String(path[..<r.lowerBound]) + ".app"
            out[bundle, default: 0] += footprint(pid)
        }
        return out
    }

    static let workApps = ["com.adobe.", "com.apple.finalcut", "com.blackmagic-design", "com.microsoft.word", "com.microsoft.excel",
                           "com.microsoft.powerpoint", "com.apple.iwork", "com.apple.logic", "com.apple.garageband", "com.figma",
                           "com.apple.dt.xcode", "com.microsoft.vscode", "com.apple.textedit", "com.apple.notes", "com.sketch",
                           "com.apple.compressor", "com.apple.motionapp", "com.maxon", "org.blenderfoundation"]

    static func scan() -> [RamApp] {
        let mem = memoryByBundle()
        let yearRx = try! NSRegularExpression(pattern: "\\s+(19|20)\\d{2}$")
        func base(_ s: String) -> String {
            yearRx.stringByReplacingMatches(in: s, range: NSRange(s.startIndex..., in: s), withTemplate: "")
        }
        let me = ProcessInfo.processInfo.processIdentifier
        let apps = NSWorkspace.shared.runningApplications.filter {
            $0.activationPolicy == .regular && $0.processIdentifier != me && $0.bundleIdentifier != "com.apple.finder"
        }
        // Bundle name keeps the year ("Adobe Premiere Pro 2027"); localizedName often drops it
        func title(_ a: NSRunningApplication) -> String {
            a.bundleURL?.deletingPathExtension().lastPathComponent ?? a.localizedName ?? "?"
        }
        let byBase = Dictionary(grouping: apps) { base(title($0)) }

        return apps.map { a in
            let name = title(a)
            let bid = (a.bundleIdentifier ?? "").lowercased()
            let size = a.bundleURL.flatMap { mem[$0.path] } ?? footprint(a.processIdentifier)
            let twin = byBase[base(name)]?.first { $0.processIdentifier != a.processIdentifier }.map(title)
            var caution = false
            var note = L("বন্ধ করলে কোন সমস্যা নেই — দরকার হলে আবার খুলুন।", "Safe to quit — reopen it when needed.")
            if bid.hasPrefix("com.anthropic") || name.lowercased().contains("claude") {
                caution = true; note = L("Claude বন্ধ করলে এই কথোপকথন ও চলমান কাজ থেমে যাবে।", "Quitting Claude stops this conversation and any running work.")
            } else if workApps.contains(where: { bid.hasPrefix($0) }) {
                caution = true; note = L("কাজের app — বন্ধ করার আগে project / file save করুন।", "Work app — save your project / files before quitting.")
            } else if ["chrome", "opera", "firefox", "safari", "edge", "brave", "arc"].contains(where: { name.lowercased().contains($0) }) {
                note = L("Browser — প্রতিটা tab আলাদা RAM নেয়। অপ্রয়োজনীয় tab বন্ধ করুন, আবার খুললে tab ফিরে আসে।", "Browser — every tab uses its own RAM. Close tabs you don't need; they come back when you reopen.")
            }
            if let t = twin { note = L("⚠️ \(t) ও চলছে — একই app দুইবার খোলা, একটা বন্ধ করুন। ", "⚠️ \(t) is also running — same app open twice, quit one. ") + note }
            return RamApp(pid: a.processIdentifier, name: name, icon: a.icon, memory: size,
                          caution: caution, note: note, duplicate: twin, app: a)
        }
        .sorted { $0.memory > $1.memory }
    }
}

final class RamModel: ObservableObject {
    @Published var info = MemInfo()
    @Published var apps: [RamApp] = []
    @Published var query = ""
    @Published var risk: RiskFilter = .all
    private var timer: Timer?
    private var busy = false

    func start() {
        refresh()
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { [weak self] _ in self?.refresh() }
    }
    func stop() { timer?.invalidate(); timer = nil }

    func refresh() {
        if busy { return }
        busy = true
        DispatchQueue.global(qos: .utility).async {
            let i = MemInfo.read(); let a = RamScanner.scan()
            DispatchQueue.main.async { self.info = i; self.apps = a; self.busy = false }
        }
    }

    func quit(_ a: RamApp) {
        a.app.terminate()   // normal quit: the app asks to save unsaved work
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { self.refresh() }
    }
}

struct RamView: View {
    @ObservedObject var m: RamModel

    var body: some View {
        VStack(spacing: 12) {
            summary
            let found = m.apps.filter { matchesQuery(m.query, $0.name) }
            let green = found.filter { !$0.caution }
            FilterBar(query: $m.query, risk: $m.risk, safeCount: green.count, riskyCount: found.count - green.count)
            RiskLegend()
            List(found.filter { m.risk.allows(safe: !$0.caution) }) { a in row(a) }
                .listStyle(.inset)
            HStack {
                Text(L("বন্ধ করুন চাপলে app টা স্বাভাবিকভাবে বন্ধ হয় — save না করা কাজ থাকলে app নিজেই জিজ্ঞেস করবে।", "Quit closes the app normally — if there's unsaved work, the app will ask first."))
                    .font(.caption).foregroundStyle(.secondary)
                Spacer()
                if !green.isEmpty {
                    Button {
                        if Actions.confirm(L("\(green.count) টি সবুজ app বন্ধ করবেন?", "Quit \(green.count) green apps?"), green.map { $0.name }.joined(separator: ", "), ok: L("সব বন্ধ করুন", "Quit all")) {
                            green.forEach { m.quit($0) }
                        }
                    } label: { Label(L("সব সবুজ app বন্ধ করুন", "Quit all green apps"), systemImage: "xmark.circle") }.tint(.green)
                }
            }
        }
        .padding(14)
        .onAppear { m.start() }
        .onDisappear { m.stop() }
    }

    private var summary: some View {
        let i = m.info
        let frac = i.total > 0 ? min(1, Double(i.used) / Double(i.total)) : 0
        let swapFrac = i.swapTotal > 0 ? Double(i.swapUsed) / Double(i.swapTotal) : 0
        let color: Color = i.pressure >= 4 ? .red : i.pressure >= 2 ? .orange : .green
        let state = i.pressure >= 4 ? L("🔴 RAM এর উপর অনেক চাপ — Mac ধীর হবে", "🔴 RAM under heavy pressure — your Mac will slow down") : i.pressure >= 2 ? L("🟠 RAM এর উপর চাপ বাড়ছে", "🟠 RAM pressure rising") : L("🟢 RAM ঠিক আছে", "🟢 RAM is fine")
        return VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline) {
                Text("RAM").font(.headline)
                Text(state).font(.callout).foregroundStyle(color)
                Spacer()
                Text(L("ব্যবহার \(FS.pretty(i.used))", "\(FS.pretty(i.used)) used")).bold()
                Text(L("/ মোট \(FS.pretty(i.total))", "of \(FS.pretty(i.total))")).foregroundStyle(.secondary)
            }
            GeometryReader { g in
                ZStack(alignment: .leading) {
                    Capsule().fill(Color.secondary.opacity(0.2))
                    Capsule().fill(color.gradient).frame(width: max(8, g.size.width * frac))
                }
            }.frame(height: 12)
            if i.swapTotal > 0 {
                Text(L("Swap (disk এ রাখা RAM): \(FS.pretty(i.swapUsed)) / \(FS.pretty(i.swapTotal))", "Swap (RAM kept on disk): \(FS.pretty(i.swapUsed)) / \(FS.pretty(i.swapTotal))"))
                    .font(.caption).foregroundStyle(swapFrac > 0.5 ? .red : .secondary)
                if i.swapUsed > 4_000_000_000 {
                    Text(L("💡 Swap অনেক বেশি — এটা disk এর জায়গাও খাচ্ছে। ভারী app বন্ধ করুন, আর সম্ভব হলে Mac একবার Restart দিন — তাহলে এই \(FS.pretty(i.swapUsed)) disk এ ফেরত আসবে।", "💡 Swap is very high — it's eating disk space too. Quit heavy apps, and restart your Mac if you can — that returns these \(FS.pretty(i.swapUsed)) to the disk."))
                        .font(.caption).foregroundStyle(.orange)
                }
            }
        }
        .padding(14)
        .background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 12))
    }

    private func row(_ a: RamApp) -> some View {
        let tint: Color = a.caution ? .red : .green
        return HStack(spacing: 10) {
            Circle().fill(tint).frame(width: 10, height: 10)
            if let ic = a.icon { Image(nsImage: ic).resizable().frame(width: 28, height: 28) }
            VStack(alignment: .leading, spacing: 2) {
                Text(a.name).lineLimit(1)
                Text(a.note).font(.caption).foregroundStyle(a.duplicate != nil ? Color.orange : tint).lineLimit(2)
            }
            Spacer()
            Text(FS.pretty(a.memory)).font(.callout.bold()).monospacedDigit()
            Button {
                if !a.caution || Actions.confirm(L("\(a.name) বন্ধ করবেন?", "Quit \(a.name)?"), a.note, ok: L("বন্ধ করুন", "Quit")) { m.quit(a) }
            } label: { Text(L("বন্ধ করুন", "Quit")) }
            .tint(tint)
        }
        .padding(.vertical, 3)
        .background(tint.opacity(0.06), in: RoundedRectangle(cornerRadius: 6))
    }
}
