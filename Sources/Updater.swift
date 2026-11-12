import SwiftUI
import AppKit

// MARK: - App info and credits

enum AppInfo {
    static let repo = "shakilnextive-beep/Easy-Uninstall"
    static let creator = "Md. Shakil"
    static let email = "shakil.nextive@gmail.com"
    static var version: String { Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0" }
    static let repoURL = URL(string: "https://github.com/\(repo)")!
    static let feedbackURL = URL(string: "https://github.com/\(repo)/discussions")!
    static let issuesURL = URL(string: "https://github.com/\(repo)/issues/new/choose")!
    static let releasesURL = URL(string: "https://github.com/\(repo)/releases/latest")!
}

// MARK: - Updates from GitHub Releases

final class Updater: ObservableObject {
    static let shared = Updater()

    enum State: Equatable { case idle, checking, upToDate, available, downloading, failed(String) }
    @Published var state: State = .idle
    @Published var latest = ""
    @Published var notes = ""
    private var zipURL: URL?

    /// "1.2.0" > "1.1.9"
    static func isNewer(_ a: String, than b: String) -> Bool {
        let x = a.split(separator: ".").map { Int($0) ?? 0 }, y = b.split(separator: ".").map { Int($0) ?? 0 }
        for i in 0..<max(x.count, y.count) {
            let p = i < x.count ? x[i] : 0, q = i < y.count ? y[i] : 0
            if p != q { return p > q }
        }
        return false
    }

    func check(silent: Bool = false) {
        if state == .checking || state == .downloading { return }
        state = .checking
        var req = URLRequest(url: URL(string: "https://api.github.com/repos/\(AppInfo.repo)/releases/latest")!)
        req.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        req.setValue("EasyUninstall", forHTTPHeaderField: "User-Agent")
        URLSession.shared.dataTask(with: req) { data, _, err in
            var next: State = .failed(L("Internet বা GitHub এ সমস্যা", "Couldn't reach GitHub"))
            var tag = "", body = "", zip: URL?
            if let data, let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
               let t = json["tag_name"] as? String {
                tag = t.trimmingCharacters(in: CharacterSet(charactersIn: "vV"))
                body = json["body"] as? String ?? ""
                let assets = json["assets"] as? [[String: Any]] ?? []
                zip = assets.first { ($0["name"] as? String ?? "").lowercased().contains("mac") }
                    .flatMap { $0["browser_download_url"] as? String }.flatMap(URL.init(string:))
                next = (Updater.isNewer(tag, than: AppInfo.version) && zip != nil) ? .available : .upToDate
            }
            DispatchQueue.main.async {
                self.latest = tag; self.notes = body; self.zipURL = zip
                if silent, case .failed = next { self.state = .idle } else { self.state = next }
            }
        }.resume()
    }

    /// Downloads the new version, swaps it in place of this app and relaunches.
    func install() {
        guard let zipURL else { return }
        state = .downloading
        URLSession.shared.downloadTask(with: zipURL) { file, _, err in
            guard let file else { return self.fail(err?.localizedDescription ?? "download") }
            let work = FileManager.default.temporaryDirectory.appendingPathComponent("EasyUninstallUpdate-\(UUID().uuidString)")
            try? FileManager.default.createDirectory(at: work, withIntermediateDirectories: true)
            let zip = work.appendingPathComponent("update.zip")
            do { try FileManager.default.moveItem(at: file, to: zip) } catch { return self.fail(error.localizedDescription) }
            _ = Shell.run("/usr/bin/ditto", ["-x", "-k", zip.path, work.path])
            guard let newApp = (try? FileManager.default.contentsOfDirectory(at: work, includingPropertiesForKeys: nil))?
                    .first(where: { $0.pathExtension == "app" }) else { return self.fail("zip") }

            let dest = Bundle.main.bundlePath
            let pid = ProcessInfo.processInfo.processIdentifier
            func q(_ s: String) -> String { "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'" }
            let swap = "/bin/rm -rf \(q(dest)) && /usr/bin/ditto \(q(newApp.path)) \(q(dest)) && /usr/bin/xattr -dr com.apple.quarantine \(q(dest))"
            let canWrite = FileManager.default.isWritableFile(atPath: (dest as NSString).deletingLastPathComponent)
            var script = "while /bin/kill -0 \(pid) 2>/dev/null; do /bin/sleep 0.3; done; "
            if canWrite {
                script += swap + "; "
            } else {
                let osa = "do shell script \"\(swap.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\""))\" with administrator privileges"
                script += "/usr/bin/osascript -e \(q(osa)); "
            }
            script += "/usr/bin/open \(q(dest)); /bin/rm -rf \(q(work.path))"
            let p = Process()
            p.executableURL = URL(fileURLWithPath: "/bin/sh")
            p.arguments = ["-c", "nohup /bin/sh -c \(q(script)) >/dev/null 2>&1 &"]
            do { try p.run(); p.waitUntilExit() } catch { return self.fail(error.localizedDescription) }
            DispatchQueue.main.async { NSApp.terminate(nil) }
        }.resume()
    }

    private func fail(_ why: String) {
        DispatchQueue.main.async { self.state = .failed(L("Update হয়নি: ", "Update failed: ") + why) }
    }
}

/// Small banner shown in the sidebar when a new version exists.
struct UpdateBanner: View {
    @ObservedObject var u = Updater.shared
    var body: some View {
        if u.state == .available || u.state == .downloading {
            VStack(alignment: .leading, spacing: 6) {
                Label(L("নতুন version \(u.latest)", "New version \(u.latest)"), systemImage: "arrow.down.circle.fill")
                    .font(.callout.weight(.semibold)).foregroundStyle(Palette.teal)
                Button {
                    u.install()
                } label: {
                    Text(u.state == .downloading ? L("Download হচ্ছে…", "Downloading…") : L("এক click এ Update", "Update in one click"))
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent).tint(Palette.teal)
                .disabled(u.state == .downloading)
            }
            .padding(10)
            .background(Palette.teal.opacity(0.12), in: RoundedRectangle(cornerRadius: 10))
        }
    }
}

// MARK: - About page

struct AboutView: View {
    @ObservedObject var u = Updater.shared

    var body: some View {
        ScrollView {
            VStack(spacing: 18) {
                Image(nsImage: Bundle.main.image(forResource: "AppIcon") ?? NSApp.applicationIconImage).resizable().frame(width: 110, height: 110)
                VStack(spacing: 4) {
                    Text("Easy Uninstall").font(.system(size: 28, weight: .bold, design: .rounded))
                    Text("Version \(AppInfo.version)").foregroundStyle(.secondary)
                }

                card {
                    row(icon: "person.crop.circle", title: "Creator", value: AppInfo.creator)
                    Divider()
                    HStack {
                        row(icon: "envelope", title: "Email", value: AppInfo.email)
                        Button(L("Email করুন", "Send email")) {
                            NSWorkspace.shared.open(URL(string: "mailto:\(AppInfo.email)?subject=Easy%20Uninstall")!)
                        }
                    }
                    Divider()
                    HStack {
                        row(icon: "chevron.left.forwardslash.chevron.right", title: "GitHub", value: AppInfo.repo)
                        Button(L("খুলুন", "Open")) { NSWorkspace.shared.open(AppInfo.repoURL) }
                    }
                }

                card {
                    HStack(spacing: 12) {
                        Image(systemName: "arrow.triangle.2.circlepath").font(.title2).foregroundStyle(Palette.purple)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(L("Update", "Updates")).font(.headline)
                            Text(statusText).font(.callout).foregroundStyle(.secondary)
                        }
                        Spacer()
                        if u.state == .available {
                            Button(L("এক click এ Update", "Update in one click")) { u.install() }
                                .buttonStyle(.borderedProminent).tint(Palette.teal)
                        } else {
                            Button(L("Update খুঁজুন", "Check for updates")) { u.check() }
                                .disabled(u.state == .checking || u.state == .downloading)
                        }
                    }
                    if u.state == .available && !u.notes.isEmpty {
                        Text(u.notes).font(.caption).foregroundStyle(.secondary).frame(maxWidth: .infinity, alignment: .leading)
                    }
                }

                card {
                    HStack(spacing: 12) {
                        Image(systemName: "bubble.left.and.bubble.right").font(.title2).foregroundStyle(Palette.teal)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(L("মতামত ও পরামর্শ", "Review and suggestions")).font(.headline)
                            Text(L("আপনার মতামত, সমস্যা বা নতুন idea আমাকে জানান।", "Tell me what you think, report a problem or share an idea."))
                                .font(.callout).foregroundStyle(.secondary)
                        }
                        Spacer()
                        Button(L("মতামত দিন", "Give feedback")) { NSWorkspace.shared.open(AppInfo.feedbackURL) }
                        Button(L("সমস্যা জানান", "Report a bug")) { NSWorkspace.shared.open(AppInfo.issuesURL) }
                    }
                }

                Text("Made with ♥ by \(AppInfo.creator)").font(.caption).foregroundStyle(.secondary)
            }
            .padding(32)
            .frame(maxWidth: 620)
            .frame(maxWidth: .infinity)
        }
    }

    private var statusText: String {
        switch u.state {
        case .idle: return L("এখন version \(AppInfo.version)", "You have version \(AppInfo.version)")
        case .checking: return L("খোঁজা হচ্ছে…", "Checking…")
        case .upToDate: return L("আপনার কাছে সবচেয়ে নতুন version আছে ✓", "You're up to date ✓")
        case .available: return L("নতুন version \(u.latest) পাওয়া গেছে!", "Version \(u.latest) is available!")
        case .downloading: return L("Download হচ্ছে… শেষ হলে app নিজে আবার খুলবে", "Downloading… the app will reopen by itself")
        case .failed(let why): return why
        }
    }

    private func card<C: View>(@ViewBuilder _ c: () -> C) -> some View {
        VStack(alignment: .leading, spacing: 10) { c() }
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Palette.tile, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
    }

    private func row(icon: String, title: String, value: String) -> some View {
        HStack(spacing: 10) {
            Image(systemName: icon).frame(width: 22).foregroundStyle(.secondary)
            Text(title).foregroundStyle(.secondary).frame(width: 64, alignment: .leading)
            Text(value).textSelection(.enabled)
            Spacer()
        }
    }
}
