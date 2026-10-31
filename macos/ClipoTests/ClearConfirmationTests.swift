@testable import Clipo
import Foundation
import Testing

/// The Clear button's two-click confirmation, on a clock the test moves.
@MainActor
final class ClearConfirmationTests {
    private var now = Date(timeIntervalSince1970: 1_800_000_000)

    private func confirmation() -> ClearConfirmation {
        ClearConfirmation(window: 3) { [unowned self] in now }
    }

    @Test func theFirstPressOnlyArms() {
        var clear = confirmation()

        let cleared = clear.press()
        #expect(!cleared)
        #expect(clear.isArmed)
    }

    @Test func aSecondPressInTimeClearsAndDisarms() {
        var clear = confirmation()
        _ = clear.press()
        now += 2

        let cleared = clear.press()
        #expect(cleared)
        #expect(!clear.isArmed)
        // The next press starts over.
        let clearedAgain = clear.press()
        #expect(!clearedAgain)
    }

    @Test func aLateSecondPressArmsAgain() {
        var clear = confirmation()
        _ = clear.press()
        now += 3

        #expect(!clear.isArmed)
        let cleared = clear.press()
        #expect(!cleared)
        #expect(clear.isArmed)
    }

    @Test func disarmingNeedsTwoPressesAgain() {
        var clear = confirmation()
        _ = clear.press()
        clear.disarm()

        let clearedFirst = clear.press()
        #expect(!clearedFirst)
        let clearedSecond = clear.press()
        #expect(clearedSecond)
    }
}
