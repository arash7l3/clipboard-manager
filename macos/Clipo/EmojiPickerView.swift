import AppKit
import ClipoCore

/// The Emoji tab: a grid of emoji in sections under headings, with a bar of
/// section buttons under it. The keyboard stays in the popup's search field;
/// the popup moves the selection through ``EmojiModel``.
@MainActor
final class EmojiPickerView: NSView {
    static let itemSize: CGFloat = 38
    static let spacing: CGFloat = 2
    static let sideInset: CGFloat = 10
    static let headerHeight: CGFloat = 26
    static let barHeight: CGFloat = 34

    /// Called with the emoji the user clicked.
    var onChoose: (@MainActor (Emoji) -> Void)?

    let model: EmojiModel
    let collection = EmojiCollectionView()
    let scroll = NSScrollView()
    let emptyLabel = NSTextField(labelWithString: "")
    let bar = NSStackView()
    /// The bar's buttons, in order; the recent button first.
    private(set) var barEntries: [BarEntry] = []

    struct BarEntry {
        let kind: EmojiGridSection.Kind
        let button: NSButton
    }

    init(model: EmojiModel) {
        self.model = model
        super.init(frame: .zero)
        configureGrid()
        configureBar()
        layOut()
        setAccessibilityLabel(PopupText.emojiGridLabel)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("EmojiPickerView is built in code")
    }

    // MARK: Model to view

    /// Lists the model's sections again, from the top.
    func reload() {
        collection.reloadData()
        scroll.contentView.scroll(to: .zero)
        scroll.reflectScrolledClipView(scroll.contentView)
        emptyLabel.stringValue = PopupText.emptyList(failed: false, searching: !model.searchText.isEmpty)
        emptyLabel.isHidden = !model.sections.isEmpty
        showBar()
    }

    /// Marks the selected emoji and brings it into view.
    func selectionChanged() {
        for item in collection.visibleItems() {
            guard let cell = (item as? EmojiItem)?.cell, let path = collection.indexPath(for: item) else {
                continue
            }
            cell.setCurrent(model.selection?.indexPath == path)
        }
        scrollSelectionToView()
        showBar()
    }

    /// Selects the first emoji of the section of `kind` and puts its heading
    /// at the top.
    func jump(to kind: EmojiGridSection.Kind) {
        guard let section = model.sectionIndex(of: kind) else { return }
        model.select(EmojiPosition(section: section, item: 0))
        guard let layout = collection.collectionViewLayout,
              let header = layout.layoutAttributesForSupplementaryView(
                  ofKind: NSCollectionView.elementKindSectionHeader,
                  at: IndexPath(item: 0, section: section)
              )
        else { return }
        scroll.contentView.scroll(to: NSPoint(x: 0, y: header.frame.minY))
        scroll.reflectScrolledClipView(scroll.contentView)
    }

    private func scrollSelectionToView() {
        guard let position = model.selection,
              let layout = collection.collectionViewLayout,
              var frame = layout.layoutAttributesForItem(at: position.indexPath)?.frame
        else { return }
        // In a section's first row the heading comes into view too.
        if position.item < model.columns,
           let header = layout.layoutAttributesForSupplementaryView(
               ofKind: NSCollectionView.elementKindSectionHeader,
               at: IndexPath(item: 0, section: position.section)
           ) {
            frame = frame.union(header.frame)
        }
        _ = collection.scrollToVisible(frame)
    }

    // MARK: Layout

    /// Columns and page size follow the width and height the grid has.
    override func layout() {
        super.layout()
        let size = scroll.contentSize
        let usable = size.width - 2 * Self.sideInset
        model.columns = Int((usable + Self.spacing) / (Self.itemSize + Self.spacing))
        model.pageRows = Int(size.height / (Self.itemSize + Self.spacing)) - 1
    }

    private func configureGrid() {
        let flow = NSCollectionViewFlowLayout()
        flow.itemSize = NSSize(width: Self.itemSize, height: Self.itemSize)
        flow.minimumInteritemSpacing = Self.spacing
        flow.minimumLineSpacing = Self.spacing
        flow.sectionInset = NSEdgeInsets(top: 0, left: Self.sideInset, bottom: 8, right: Self.sideInset)
        collection.collectionViewLayout = flow
        collection.register(EmojiItem.self, forItemWithIdentifier: EmojiItem.identifier)
        collection.register(
            EmojiHeaderView.self,
            forSupplementaryViewOfKind: NSCollectionView.elementKindSectionHeader,
            withIdentifier: EmojiHeaderView.identifier
        )
        collection.dataSource = self
        collection.delegate = self
        collection.isSelectable = false
        collection.backgroundColors = [.clear]
        collection.autoresizingMask = [.width]
        // Arrow keys move through a grid that reads left to right.
        collection.userInterfaceLayoutDirection = .leftToRight

        scroll.documentView = collection
        scroll.hasVerticalScroller = true
        scroll.scrollerStyle = .overlay
        scroll.autohidesScrollers = true
        scroll.drawsBackground = false

        emptyLabel.textColor = .secondaryLabelColor
        emptyLabel.alignment = .center
        emptyLabel.isHidden = true
    }

    private func configureBar() {
        bar.orientation = .horizontal
        bar.distribution = .fillEqually
        bar.spacing = 0
        bar.setAccessibilityLabel(PopupText.emojiBarLabel)
        let kinds = [EmojiGridSection.Kind.recent] + model.groups.map { EmojiGridSection.Kind.group($0) }
        for (index, kind) in kinds.enumerated() {
            let title = PopupText.emojiSectionTitle(kind)
            let button = NSButton(
                image: NSImage(systemSymbolName: PopupText.emojiSectionSymbol(kind), accessibilityDescription: title)
                    ?? NSImage(),
                target: self,
                action: #selector(barClicked(_:))
            )
            button.isBordered = false
            button.imagePosition = .imageOnly
            button.refusesFirstResponder = true
            button.toolTip = title
            button.setAccessibilityLabel(title)
            button.tag = index
            bar.addArrangedSubview(button)
            barEntries.append(BarEntry(kind: kind, button: button))
        }
    }

    private func layOut() {
        for subview in [scroll, bar, emptyLabel] {
            subview.translatesAutoresizingMaskIntoConstraints = false
            addSubview(subview)
        }
        NSLayoutConstraint.activate([
            scroll.topAnchor.constraint(equalTo: topAnchor),
            scroll.leadingAnchor.constraint(equalTo: leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: trailingAnchor),
            scroll.bottomAnchor.constraint(equalTo: bar.topAnchor),
            bar.leadingAnchor.constraint(equalTo: leadingAnchor, constant: Self.sideInset),
            bar.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -Self.sideInset),
            bar.bottomAnchor.constraint(equalTo: bottomAnchor),
            bar.heightAnchor.constraint(equalToConstant: Self.barHeight),
            emptyLabel.centerXAnchor.constraint(equalTo: scroll.centerXAnchor),
            emptyLabel.centerYAnchor.constraint(equalTo: scroll.centerYAnchor)
        ])
    }

    /// Hides the bar while searching, hides the recent button while there
    /// are none, and tints the button of the selected emoji's section.
    func showBar() {
        bar.isHidden = !model.searchText.isEmpty
        let current = model.selection.map { model.sections[$0.section].kind }
        for entry in barEntries {
            entry.button.isHidden = model.sectionIndex(of: entry.kind) == nil
            entry.button.contentTintColor = entry.kind == current ? .controlAccentColor : .secondaryLabelColor
        }
    }

    @objc private func barClicked(_ sender: NSButton) {
        guard barEntries.indices.contains(sender.tag) else { return }
        jump(to: barEntries[sender.tag].kind)
    }
}

extension EmojiPickerView: NSCollectionViewDataSource, NSCollectionViewDelegateFlowLayout {
    func numberOfSections(in collectionView: NSCollectionView) -> Int {
        model.sections.count
    }

    func collectionView(_ collectionView: NSCollectionView, numberOfItemsInSection section: Int) -> Int {
        model.sections[section].emoji.count
    }

    func collectionView(
        _ collectionView: NSCollectionView,
        itemForRepresentedObjectAt indexPath: IndexPath
    ) -> NSCollectionViewItem {
        let made = collectionView.makeItem(withIdentifier: EmojiItem.identifier, for: indexPath)
        let position = EmojiPosition(section: indexPath.section, item: indexPath.item)
        guard let item = made as? EmojiItem, let emoji = model.emoji(at: position) else { return made }
        item.cell.show(emoji, current: model.selection == position)
        item.cell.onPick = { [weak self] in self?.onChoose?(emoji) }
        return item
    }

    func collectionView(
        _ collectionView: NSCollectionView,
        viewForSupplementaryElementOfKind kind: NSCollectionView.SupplementaryElementKind,
        at indexPath: IndexPath
    ) -> NSView {
        let view = collectionView.makeSupplementaryView(
            ofKind: kind,
            withIdentifier: EmojiHeaderView.identifier,
            for: indexPath
        )
        if let header = view as? EmojiHeaderView, model.sections.indices.contains(indexPath.section) {
            header.title = PopupText.emojiSectionTitle(model.sections[indexPath.section].kind)
        }
        return view
    }

    /// Search results have no heading.
    func collectionView(
        _ collectionView: NSCollectionView,
        layout collectionViewLayout: NSCollectionViewLayout,
        referenceSizeForHeaderInSection section: Int
    ) -> NSSize {
        guard model.sections.indices.contains(section), model.sections[section].kind != .results else {
            return .zero
        }
        return NSSize(width: 0, height: Self.headerHeight)
    }
}
