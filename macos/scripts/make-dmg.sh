#!/bin/bash
# Builds Clipo.app (Release, universal), signs it and wraps it in a dmg.
#
# Usage: macos/scripts/make-dmg.sh [output-folder]    (default: macos/build/dist)
#
# Package the core first: (cd core && cargo xtask package macos).
#
# Signing comes from the environment (decision 0023):
#   CLIPO_SIGN_IDENTITY   A "Developer ID Application: ..." identity in the keychain.
#                         Unset: an ad-hoc signature. The dmg runs, but Gatekeeper
#                         blocks it on other Macs until it is opened from Settings.
#   CLIPO_NOTARY_PROFILE  A notarytool keychain profile (xcrun notarytool
#                         store-credentials). Set: the app and the dmg are notarized
#                         and stapled. Needs CLIPO_SIGN_IDENTITY.
#   CLIPO_DERIVED_DATA    Xcode's DerivedData folder (default: macos/build/DerivedData).
#   CLIPO_EXPECT_VERSION  Fail unless the app's version is exactly this (CI sets it
#                         from a v* tag).
set -euo pipefail

fail() {
    echo "error: $*" >&2
    exit 1
}

macos_dir=$(cd "$(dirname "$0")/.." && pwd)
repo_dir=$(dirname "$macos_dir")
out_dir=${1:-$macos_dir/build/dist}
derived=${CLIPO_DERIVED_DATA:-$macos_dir/build/DerivedData}
identity=${CLIPO_SIGN_IDENTITY:-}
notary_profile=${CLIPO_NOTARY_PROFILE:-}

[[ $(uname) == Darwin ]] || fail "needs macOS with Xcode"
[[ -f $repo_dir/core/dist/apple/ClipoCore/Package.swift ]] ||
    fail "the core is not packaged; run (cd core && cargo xtask package macos)"
if [[ -n $notary_profile && -z $identity ]]; then
    fail "notarizing needs a Developer ID: set CLIPO_SIGN_IDENTITY too"
fi
if [[ -n $identity ]]; then
    # Captured first: with pipefail, grep -q closing the pipe early would fail it.
    identities=$(security find-identity -v -p codesigning)
    grep -qF -- "$identity" <<<"$identities" ||
        fail "no valid signing identity named \"$identity\" in the keychain"
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

echo "==> Building Clipo (Release, arm64 + x86_64)"
xcodebuild build -quiet \
    -project "$macos_dir/Clipo.xcodeproj" \
    -scheme Clipo \
    -configuration Release \
    -destination 'generic/platform=macOS' \
    -derivedDataPath "$derived" \
    -clonedSourcePackagesDirPath "$derived/SourcePackages" \
    CODE_SIGNING_ALLOWED=NO

staging=$work/dmg
app=$staging/Clipo.app
mkdir -p "$staging"
ditto "$derived/Build/Products/Release/Clipo.app" "$app"

version=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$app/Contents/Info.plist")
if [[ -n ${CLIPO_EXPECT_VERSION:-} && $version != "$CLIPO_EXPECT_VERSION" ]]; then
    fail "the app's version is $version, expected $CLIPO_EXPECT_VERSION (MARKETING_VERSION)"
fi
icon=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIconFile' "$app/Contents/Info.plist" 2>/dev/null || true)
[[ -n $icon && -f $app/Contents/Resources/${icon%.icns}.icns ]] ||
    fail "the app has no icon file (CFBundleIconFile: ${icon:-unset})"
archs=$(lipo -archs "$app/Contents/MacOS/Clipo")
[[ $archs == *arm64* && $archs == *x86_64* ]] || fail "the app is not universal: $archs"

# Hardened runtime, no entitlements. A secure timestamp is required for
# notarization; ad-hoc signatures can't have one.
sign() {
    if [[ -n $identity ]]; then
        codesign --force --options runtime --timestamp --sign "$identity" "$1"
    else
        codesign --force --options runtime --timestamp=none --sign - "$1"
    fi
}

# The core is linked statically, so nothing should be nested; sign anything
# that is, innermost first, before the app itself.
echo "==> Signing with ${identity:-an ad-hoc signature}"
while IFS= read -r -d '' nested; do
    sign "$nested"
done < <(find "$app/Contents" -depth \( -name '*.framework' -o -name '*.dylib' -o -name '*.xpc' \) -print0)
sign "$app"
codesign --verify --strict --deep --verbose=2 "$app"
details=$(codesign --display --verbose=2 "$app" 2>&1)
grep -q '^CodeDirectory .*flags=.*runtime' <<<"$details" ||
    fail "the app was signed without the hardened runtime"

# Submits a file and waits; prints the notary log and fails unless accepted.
notarize() {
    local result=$work/notary.json status id
    echo "==> Notarizing $(basename "$1")"
    xcrun notarytool submit "$1" --keychain-profile "$notary_profile" \
        --wait --output-format json >"$result" || true
    status=$(plutil -extract status raw -o - "$result" 2>/dev/null || echo unknown)
    if [[ $status != Accepted ]]; then
        cat "$result" >&2
        if id=$(plutil -extract id raw -o - "$result" 2>/dev/null); then
            xcrun notarytool log "$id" --keychain-profile "$notary_profile" >&2 || true
        fi
        fail "notarization status: $status"
    fi
}

# The app gets its own ticket too, so it opens offline after leaving the dmg.
if [[ -n $notary_profile ]]; then
    ditto -c -k --keepParent "$app" "$work/Clipo.zip"
    notarize "$work/Clipo.zip"
    xcrun stapler staple "$app"
    spctl --assess --type execute --verbose=2 "$app"
fi

ln -s /Applications "$staging/Applications"

suffix=
[[ -n $identity ]] || suffix=-unsigned
mkdir -p "$out_dir"
dmg=$(cd "$out_dir" && pwd)/Clipo-$version$suffix.dmg
rm -f "$dmg" "$dmg.sha256"

# hdiutil sometimes fails with "Resource busy" right after a build; retry.
echo "==> Creating $(basename "$dmg")"
for attempt in 1 2 3; do
    if hdiutil create -volname Clipo -srcfolder "$staging" -fs HFS+ -format UDZO -ov "$dmg"; then
        break
    fi
    [[ $attempt -lt 3 ]] || fail "hdiutil could not create the dmg"
    sleep 5
done

if [[ -n $identity ]]; then
    codesign --force --timestamp --sign "$identity" "$dmg"
    codesign --verify --verbose=2 "$dmg"
fi
if [[ -n $notary_profile ]]; then
    notarize "$dmg"
    xcrun stapler staple "$dmg"
    xcrun stapler validate "$dmg"
    spctl --assess --type open --context context:primary-signature --verbose=2 "$dmg"
fi

(cd "$(dirname "$dmg")" && shasum -a 256 "$(basename "$dmg")" >"$(basename "$dmg").sha256")
echo "==> $dmg"
