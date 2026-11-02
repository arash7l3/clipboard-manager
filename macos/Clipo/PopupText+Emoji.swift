import ClipoCore
import Foundation

/// The words and symbols of the Emoji tab.
extension PopupText {
    static var emojiTabTitle: String {
        String(localized: "Emoji", comment: "Popup tab with the emoji picker")
    }

    /// Screen-reader label of the grid of emoji.
    static var emojiGridLabel: String {
        emojiTabTitle
    }

    /// Screen-reader label of the bar of emoji sections.
    static var emojiBarLabel: String {
        String(localized: "Emoji categories", comment: "Screen-reader label of the emoji picker's section buttons")
    }

    /// The tab's title in the tab bar.
    static func title(for tab: PopupTab) -> String {
        switch tab {
        case let .history(category): tabTitle(category)
        case .emoji: emojiTabTitle
        }
    }

    /// A section's heading, tooltip and screen-reader label.
    static func emojiSectionTitle(_ kind: EmojiGridSection.Kind) -> String {
        switch kind {
        case .recent:
            String(localized: "Recent", comment: "Emoji picker section of the emoji used last")
        case .results:
            String(localized: "Search Results", comment: "Emoji picker heading of the search results")
        case let .group(group):
            emojiGroupTitle(group)
        }
    }

    static func emojiGroupTitle(_ group: EmojiGroup) -> String {
        switch group {
        case .smileysAndPeople:
            String(localized: "Smileys & People", comment: "Emoji picker section of faces and people")
        case .animalsAndNature:
            String(localized: "Animals & Nature", comment: "Emoji picker section of animals and plants")
        case .foodAndDrink:
            String(localized: "Food & Drink", comment: "Emoji picker section of food and drink")
        case .activities:
            String(localized: "Activities", comment: "Emoji picker section of sports and events")
        case .travelAndPlaces:
            String(localized: "Travel & Places", comment: "Emoji picker section of places and vehicles")
        case .objects:
            String(localized: "Objects", comment: "Emoji picker section of things")
        case .symbols:
            String(localized: "Symbols", comment: "Emoji picker section of signs and symbols")
        case .flags:
            String(localized: "Flags", comment: "Emoji picker section of flags")
        }
    }

    /// The SF Symbol on a section's button in the bar.
    static func emojiSectionSymbol(_ kind: EmojiGridSection.Kind) -> String {
        switch kind {
        case .recent, .results: "clock"
        case let .group(group):
            switch group {
            case .smileysAndPeople: "face.smiling"
            case .animalsAndNature: "pawprint"
            case .foodAndDrink: "fork.knife"
            case .activities: "sportscourt"
            case .travelAndPlaces: "car"
            case .objects: "lightbulb"
            case .symbols: "heart"
            case .flags: "flag"
            }
        }
    }
}
