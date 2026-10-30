import ClipoCore
import Foundation
import Security

/// A key store the app can also clear, to start a new history.
protocol ClearableKeyStore: KeyStore {
    /// Deletes the stored key. Succeeds if there is none.
    func delete() throws
}

/// Keeps the history key as a generic password in the login keychain.
///
/// The data protection keychain (this-device-only, after first unlock) needs
/// a signed app with a keychain entitlement; it replaces this once Clipo is
/// signed (decision 0009). Unsigned builds change identity on every rebuild,
/// so macOS asks again before handing them the key.
final class KeychainKeyStore: ClearableKeyStore {
    let service: String
    let account: String

    init(service: String, account: String = "history-key") {
        self.service = service
        self.account = account
    }

    func load() throws -> Data? {
        var query = itemQuery
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        switch status {
        case errSecSuccess:
            guard let key = result as? Data else {
                throw KeyStoreError.Failed(message: "keychain item has no data")
            }
            return key
        case errSecItemNotFound:
            return nil
        default:
            // A locked keychain or a denied prompt: not the same as "no key",
            // which would lead to offering a new history.
            throw Self.failure(status)
        }
    }

    func save(key: Data) throws {
        let updated = SecItemUpdate(
            itemQuery as CFDictionary,
            [kSecValueData as String: key] as CFDictionary
        )
        if updated == errSecSuccess { return }
        guard updated == errSecItemNotFound else { throw Self.failure(updated) }

        var item = itemQuery
        item[kSecValueData as String] = key
        item[kSecAttrLabel as String] = String(
            localized: "Clipo history key",
            comment: "Name of the keychain item that holds the history's encryption key"
        )
        let added = SecItemAdd(item as CFDictionary, nil)
        guard added == errSecSuccess else { throw Self.failure(added) }
    }

    func delete() throws {
        let status = SecItemDelete(itemQuery as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else {
            throw Self.failure(status)
        }
    }

    private var itemQuery: [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account
        ]
    }

    private static func failure(_ status: OSStatus) -> KeyStoreError {
        .Failed(message: "OSStatus \(status)")
    }
}
