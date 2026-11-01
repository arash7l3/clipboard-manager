#!/bin/bash
# Makes sure this Mac has Clipo's self-signed code-signing identity and prints
# "<SHA-1 hash><tab><keychain>" for codesign (decision 0029).
#
# Without a Developer ID, make-dmg.sh signs with this identity instead of an
# ad-hoc signature. It stays the same from build to build, so macOS keeps the
# Accessibility permission (auto-paste) and the keychain access it gave Clipo
# across updates; an ad-hoc signature is new in every build and loses both.
#
# The identity lives in its own keychain, ~/Library/Keychains/clipo-self-signing.keychain-db,
# with a random password in ~/Library/Application Support/Clipo Build/keychain-password.
# Both are made on first use and kept. Deleting them makes a new identity, after
# which Clipo has to be given Accessibility once more.
set -euo pipefail

fail() {
    echo "error: $*" >&2
    exit 1
}

name="Clipo Self-Signed"
keychain=$HOME/Library/Keychains/clipo-self-signing.keychain-db
secrets="$HOME/Library/Application Support/Clipo Build"
password_file=$secrets/keychain-password

[[ $(uname) == Darwin ]] || fail "needs macOS"

if [[ ! -f $keychain ]]; then
    echo "==> Creating the code-signing identity \"$name\" in $keychain" >&2
    work=$(mktemp -d)
    trap 'rm -rf "$work"' EXIT
    mkdir -p "$secrets"
    chmod 700 "$secrets"
    password=$(/usr/bin/openssl rand -hex 24)
    cat >"$work/openssl.cnf" <<EOF
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
CN = $name
[ext]
basicConstraints = critical, CA:false
keyUsage = critical, digitalSignature
extendedKeyUsage = critical, codeSigning
subjectKeyIdentifier = hash
EOF
    # The system's LibreSSL: its PKCS#12 files are ones the keychain can import.
    /usr/bin/openssl req -x509 -newkey rsa:2048 -nodes -days 7300 -config "$work/openssl.cnf" \
        -keyout "$work/key.pem" -out "$work/cert.pem" 2>/dev/null
    /usr/bin/openssl pkcs12 -export -inkey "$work/key.pem" -in "$work/cert.pem" -name "$name" \
        -out "$work/identity.p12" -passout "pass:$password"
    (umask 077 && printf '%s' "$password" >"$password_file")
    security create-keychain -p "$password" "$keychain"
    # No automatic locking: builds run unattended.
    security set-keychain-settings "$keychain"
    security unlock-keychain -p "$password" "$keychain"
    security import "$work/identity.p12" -k "$keychain" -P "$password" -T /usr/bin/codesign >&2
    # Lets codesign use the key without a password dialog.
    security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$password" "$keychain" >/dev/null
fi

[[ -f $password_file ]] || fail "$password_file is missing; delete $keychain to make a new identity"
security unlock-keychain -p "$(<"$password_file")" "$keychain"

# codesign finds identities in the keychain search list; add ours if it is not there.
listed=()
while IFS= read -r line; do
    line=${line#*\"}
    line=${line%\"*}
    [[ -z $line ]] || listed+=("$line")
done < <(security list-keychains -d user)
found=false
for entry in ${listed[@]+"${listed[@]}"}; do
    [[ $entry != "$keychain" ]] || found=true
done
$found || security list-keychains -d user -s ${listed[@]+"${listed[@]}"} "$keychain"

hash=$(security find-certificate -c "$name" -Z "$keychain" | awk '/^SHA-1 hash:/ { print $3 }')
[[ -n $hash ]] || fail "no certificate named \"$name\" in $keychain"
printf '%s\t%s\n' "$hash" "$keychain"
