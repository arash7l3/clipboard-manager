import Foundation
import Testing

/// The string catalog compiles into English and Farsi tables in the app.
struct LocalizationTests {
    /// The app bundle: hosted tests run inside it.
    private let app = Bundle.main

    @Test func appIsLocalizedInFarsi() {
        #expect(app.localizations.contains("fa"))
    }

    @Test func farsiTableTranslatesMenuItems() throws {
        let path = try #require(app.path(forResource: "fa", ofType: "lproj"))
        let farsi = try #require(Bundle(path: path))
        #expect(farsi.localizedString(forKey: "Quit Clipo", value: nil, table: nil) == "خروج از Clipo")
        #expect(farsi.localizedString(forKey: "About Clipo", value: nil, table: nil) == "درباره Clipo")
    }

    @Test func farsiTableTranslatesThePopup() throws {
        let path = try #require(app.path(forResource: "fa", ofType: "lproj"))
        let farsi = try #require(Bundle(path: path))
        #expect(farsi.localizedString(forKey: "All", value: nil, table: nil) == "همه")
        #expect(farsi.localizedString(forKey: "Search", value: nil, table: nil) == "جستجو")
        let format = farsi.localizedString(forKey: "%@ and %lld more", value: nil, table: nil)
        #expect(String(format: format, "a.pdf", 3) == "a.pdf و 3 مورد دیگر")
    }

    @Test func itemCountUsesPluralRules() throws {
        let path = try #require(app.path(forResource: "en", ofType: "lproj"))
        let english = try #require(Bundle(path: path))
        let format = english.localizedString(forKey: "%lld items in history", value: nil, table: nil)
        let locale = Locale(identifier: "en")
        #expect(String(format: format, locale: locale, 1) == "1 item in history")
        #expect(String(format: format, locale: locale, 5) == "5 items in history")
    }
}
