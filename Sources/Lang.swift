import SwiftUI

/// App language: Bangla (default) or English. Saved between launches.
final class Lang: ObservableObject {
    static let shared = Lang()
    @Published var english: Bool = UserDefaults.standard.bool(forKey: "english") {
        didSet { UserDefaults.standard.set(english, forKey: "english") }
    }
}

/// Picks the Bangla or English text for the current language.
func L(_ bn: String, _ en: String) -> String { Lang.shared.english ? en : bn }

/// "বাং | EN" switch.
struct LangSwitch: View {
    @ObservedObject var lang = Lang.shared
    var body: some View {
        Picker("", selection: $lang.english) {
            Text("বাংলা").tag(false)
            Text("English").tag(true)
        }
        .pickerStyle(.segmented).labelsHidden().fixedSize()
        .help("ভাষা / Language")
    }
}
