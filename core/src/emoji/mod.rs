//! The emoji picker's list: every emoji the apps offer, in picker order,
//! grouped as in the macOS picker, with English names for search and screen
//! readers.
//!
//! The list is `emoji.txt`, generated from Unicode data by `cargo xtask emoji`
//! and built into the library. It is not history: nothing here is stored,
//! except the recently used emoji (see [`Store::record_emoji`](crate::Store::record_emoji)).

use std::ops::Range;
use std::sync::OnceLock;

use crate::store::fold;

/// The built-in list. See the header of the file for its format.
const LIST: &str = include_str!("emoji.txt");

/// A picker section, in [`EmojiGroup::ALL`] order.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum EmojiGroup {
    /// Faces, gestures, people and hearts.
    SmileysAndPeople,
    /// Animals, plants and weather.
    AnimalsAndNature,
    /// Food and drink.
    FoodAndDrink,
    /// Sports, games, events and arts.
    Activities,
    /// Places, vehicles, time and the sky.
    TravelAndPlaces,
    /// Clothing, tools, devices and other things.
    Objects,
    /// Signs, arrows, shapes and keycaps.
    Symbols,
    /// Flags of countries, regions and others.
    Flags,
}

impl EmojiGroup {
    /// All groups in picker order: the order of the macOS picker's bottom bar.
    pub const ALL: [Self; 8] = [
        Self::SmileysAndPeople,
        Self::AnimalsAndNature,
        Self::FoodAndDrink,
        Self::Activities,
        Self::TravelAndPlaces,
        Self::Objects,
        Self::Symbols,
        Self::Flags,
    ];

    /// The group's key in `emoji.txt`.
    fn key(self) -> &'static str {
        match self {
            Self::SmileysAndPeople => "smileys-people",
            Self::AnimalsAndNature => "animals-nature",
            Self::FoodAndDrink => "food-drink",
            Self::Activities => "activities",
            Self::TravelAndPlaces => "travel-places",
            Self::Objects => "objects",
            Self::Symbols => "symbols",
            Self::Flags => "flags",
        }
    }

    fn index(self) -> usize {
        self as usize
    }

    /// The emoji in this group, in picker order.
    #[must_use]
    pub fn emoji(self) -> &'static [Emoji] {
        let catalog = catalog();
        &catalog.emoji[catalog.groups[self.index()].clone()]
    }
}

/// One emoji the picker offers.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct Emoji {
    /// The text to paste: one or more code points, with any variation
    /// selectors and joiners.
    pub text: &'static str,
    /// The Unicode name in English, such as "grinning face".
    pub name: &'static str,
    /// The section it is listed in.
    pub group: EmojiGroup,
}

/// Every emoji, in picker order: group by group as in [`EmojiGroup::ALL`].
#[must_use]
pub fn all() -> &'static [Emoji] {
    &catalog().emoji
}

/// The emoji whose text is exactly `text`, if the picker offers it.
#[must_use]
pub fn find(text: &str) -> Option<Emoji> {
    all().iter().find(|emoji| emoji.text == text).copied()
}

/// Emoji matching every word of `text`, in picker order. A word matches an
/// emoji if it starts one of the words of its name ("smil" finds "smiling
/// face"), ignoring case, or if it is the emoji itself. Empty `text` lists
/// every emoji.
#[must_use]
pub fn search(text: &str) -> Vec<Emoji> {
    let words: Vec<String> = text.split_whitespace().map(fold).collect();
    all()
        .iter()
        .filter(|emoji| words.iter().all(|word| matches(emoji, word)))
        .copied()
        .collect()
}

/// Whether the folded query `word` matches `emoji`.
fn matches(emoji: &Emoji, word: &str) -> bool {
    emoji.text == word
        || fold(emoji.name)
            .split(|c: char| !c.is_alphanumeric())
            .any(|name_word| name_word.starts_with(word))
}

/// The parsed list.
struct Catalog {
    emoji: Vec<Emoji>,
    /// The range of `emoji` each group covers, indexed by [`EmojiGroup::index`].
    groups: [Range<usize>; EmojiGroup::ALL.len()],
}

fn catalog() -> &'static Catalog {
    static CATALOG: OnceLock<Catalog> = OnceLock::new();
    CATALOG.get_or_init(|| parse(LIST))
}

/// Parses the list. The list is built in and checked by the tests, so a bad
/// line is a build mistake: it panics rather than drop emoji quietly.
fn parse(list: &'static str) -> Catalog {
    let mut emoji = Vec::new();
    let mut groups: [Range<usize>; EmojiGroup::ALL.len()] = Default::default();
    let mut current: Option<EmojiGroup> = None;
    for line in list.lines() {
        if line.starts_with('#') || line.is_empty() {
            continue;
        }
        if let Some(key) = line.strip_prefix('[').and_then(|l| l.strip_suffix(']')) {
            let group = EmojiGroup::ALL
                .into_iter()
                .find(|group| group.key() == key)
                .unwrap_or_else(|| panic!("unknown emoji group [{key}]"));
            assert!(
                current.map_or(0, |c| c.index() + 1) == group.index(),
                "emoji group [{key}] is out of order"
            );
            groups[group.index()] = emoji.len()..emoji.len();
            current = Some(group);
            continue;
        }
        let group = current.expect("emoji listed before the first group");
        let (text, name) = line
            .split_once(' ')
            .unwrap_or_else(|| panic!("emoji line without a name: {line:?}"));
        emoji.push(Emoji { text, name, group });
        groups[group.index()].end = emoji.len();
    }
    Catalog { emoji, groups }
}

#[cfg(test)]
mod tests {
    use std::collections::HashSet;

    use super::*;

    fn texts(list: &[Emoji]) -> Vec<&'static str> {
        list.iter().map(|emoji| emoji.text).collect()
    }

    #[test]
    fn the_list_has_every_group_in_order_and_no_duplicates() {
        let all = all();
        assert!(all.len() > 1800, "only {} emoji", all.len());
        let mut start = 0;
        for group in EmojiGroup::ALL {
            let emoji = group.emoji();
            assert!(!emoji.is_empty(), "{group:?} is empty");
            assert!(emoji.iter().all(|e| e.group == group));
            // Groups are contiguous and in ALL order.
            assert_eq!(emoji.as_ptr(), all[start..].as_ptr(), "{group:?}");
            start += emoji.len();
        }
        assert_eq!(start, all.len());
        let unique: HashSet<_> = all.iter().map(|e| e.text).collect();
        assert_eq!(unique.len(), all.len());
    }

    #[test]
    fn every_emoji_has_text_and_a_name() {
        for emoji in all() {
            assert!(!emoji.text.is_empty() && !emoji.text.contains(char::is_whitespace));
            assert!(!emoji.name.trim().is_empty(), "{:?}", emoji.text);
            assert_eq!(emoji.name, emoji.name.trim());
        }
    }

    #[test]
    fn the_list_starts_like_the_macos_picker() {
        let first = all()[0];
        assert_eq!(first.text, "\u{1F600}");
        assert_eq!(first.name, "grinning face");
        assert_eq!(texts(&EmojiGroup::Flags.emoji()[..1]), ["\u{1F3C1}"]);
        assert!(texts(EmojiGroup::Flags.emoji()).contains(&"\u{1F1F2}\u{1F1F3}"));
    }

    #[test]
    fn skin_tones_and_newer_emoji_are_left_out() {
        let tone = |c: char| ('\u{1F3FB}'..='\u{1F3FF}').contains(&c);
        assert!(!all().iter().any(|e| e.text.chars().any(tone)));
        // Emoji 15.0 is in, 15.1 (head shaking horizontally) is not.
        assert!(find("\u{1FAE8}").is_some());
        assert!(find("\u{1F642}\u{200D}\u{2194}\u{FE0F}").is_none());
    }

    #[test]
    fn find_matches_the_exact_text() {
        let heart = find("\u{2764}\u{FE0F}").unwrap();
        assert_eq!(heart.name, "red heart");
        assert_eq!(heart.group, EmojiGroup::SmileysAndPeople);
        // Without the variation selector it is not the listed emoji.
        assert!(find("\u{2764}").is_none());
        assert!(find("hello").is_none());
    }

    #[test]
    fn search_matches_word_starts_ignoring_case() {
        let found = search("GRINNING Fa");
        assert_eq!(found[0].name, "grinning face");
        assert!(found.iter().all(|e| e.name.contains("grinning")));
        // "rin" is inside "grinning" but starts no word.
        assert!(search("rin")
            .iter()
            .all(|e| !e.name.starts_with("grinning")));
        // Words after a colon or comma count too.
        assert!(texts(&search("mongolia")).contains(&"\u{1F1F2}\u{1F1F3}"));
    }

    #[test]
    fn search_needs_every_word_and_keeps_picker_order() {
        let both = search("red heart");
        assert!(both.iter().any(|e| e.text == "\u{2764}\u{FE0F}"));
        assert!(both
            .iter()
            .all(|e| e.name.contains("red") && e.name.contains("heart")));
        let positions: Vec<usize> = both
            .iter()
            .map(|e| all().iter().position(|a| a == e).unwrap())
            .collect();
        assert!(positions.windows(2).all(|w| w[0] < w[1]));
        assert_eq!(search("heart zebra"), []);
    }

    #[test]
    fn search_finds_an_emoji_by_itself_and_lists_all_for_no_words() {
        assert_eq!(texts(&search("\u{1F600}")), ["\u{1F600}"]);
        assert_eq!(search("").len(), all().len());
        assert_eq!(search("  ").len(), all().len());
    }

    #[test]
    #[should_panic(expected = "out of order")]
    fn a_group_out_of_order_is_a_build_mistake() {
        parse("[flags]\n\u{1F3C1} chequered flag\n[smileys-people]\n");
    }
}
