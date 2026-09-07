#!/usr/bin/env bash
#
# Signs, notarizes and staples the macOS .app bundles that publish-*.ps1 produced, then repacks them.
#
# Runs only on macOS, only in CI, and only when the credentials are present - the workflow skips it
# otherwise, so a fork still gets working (unsigned) artifacts rather than a red build.
#
# Why this exists at all: the Sigstore provenance attestation proves a build came from this repository,
# and says nothing whatsoever to Gatekeeper. Without notarization, the first thing a macOS user is told
# about this app is that it cannot be opened.
#
# Required environment:
#   MACOS_CERT_P12        base64 of a Developer ID Application .p12
#   MACOS_CERT_PASSWORD   its export password
#   APPLE_TEAM_ID         the 10-character team identifier
#   APPLE_API_KEY_ID      App Store Connect key id
#   APPLE_API_ISSUER_ID   App Store Connect issuer id
#   APPLE_API_KEY_P8      the .p8 private key, verbatim
set -euo pipefail

ARTIFACTS="${ARTIFACTS_DIR:-artifacts}"
KEYCHAIN="build.keychain"
KEYCHAIN_PASSWORD="$(uuidgen)"
WORK="$(mktemp -d)"
trap 'security delete-keychain "$KEYCHAIN" 2>/dev/null || true; rm -rf "$WORK"' EXIT

if ! ls "$ARTIFACTS"/*.zip >/dev/null 2>&1; then
  echo "No .zip artifacts in $ARTIFACTS - nothing to sign."
  exit 0
fi

# ---- a throwaway keychain, unlocked for this job only -----------------------------------------
#
# Never the login keychain: it is not ours to touch, and on a hosted runner it does not exist.
echo "$MACOS_CERT_P12" | base64 --decode > "$WORK/cert.p12"

security create-keychain -p "$KEYCHAIN_PASSWORD" "$KEYCHAIN"
security set-keychain-settings -lut 3600 "$KEYCHAIN"
security unlock-keychain -p "$KEYCHAIN_PASSWORD" "$KEYCHAIN"
security import "$WORK/cert.p12" -k "$KEYCHAIN" -P "$MACOS_CERT_PASSWORD" -T /usr/bin/codesign

# Without this, codesign blocks on a GUI prompt that nobody can answer, and the job hangs until it
# times out rather than failing with something readable.
security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$KEYCHAIN_PASSWORD" "$KEYCHAIN" >/dev/null
security list-keychains -d user -s "$KEYCHAIN" "$(security list-keychains -d user | tr -d '" ' | head -1)"

IDENTITY="$(security find-identity -v -p codesigning "$KEYCHAIN" | awk -F'"' '/Developer ID Application/ {print $2; exit}')"
if [ -z "$IDENTITY" ]; then
  echo "::error::No 'Developer ID Application' identity in the imported certificate."
  exit 1
fi
echo "Signing as: $IDENTITY"

# The App Store Connect key, for notarytool.
mkdir -p "$WORK/keys"
printf '%s' "$APPLE_API_KEY_P8" > "$WORK/keys/AuthKey.p8"

for zip in "$ARTIFACTS"/*.zip; do
  case "$zip" in *osx*) ;; *) continue ;; esac

  echo "==> $zip"
  stage="$WORK/$(basename "$zip" .zip)"
  mkdir -p "$stage"
  ditto -x -k "$zip" "$stage"

  app="$(find "$stage" -maxdepth 1 -name '*.app' -print -quit)"
  if [ -z "$app" ]; then
    echo "  no .app inside; leaving it alone."
    continue
  fi

  # INSIDE-OUT. Every nested binary first, the bundle last: the hardened runtime requires each one to
  # carry its own signature, and signing the bundle first invalidates it the moment a nested file is
  # signed afterwards.
  find "$app" -type f \( -name '*.dylib' -o -name '*.so' -o -perm +111 \) -print0 |
    while IFS= read -r -d '' binary; do
      # Skip the main executable here; it is signed with the bundle below.
      [ "$binary" = "$app/Contents/MacOS/$(basename "$app" .app)" ] && continue
      codesign --force --timestamp --options runtime --sign "$IDENTITY" "$binary" 2>/dev/null || true
    done

  codesign --force --timestamp --options runtime --sign "$IDENTITY" "$app"
  codesign --verify --deep --strict --verbose=2 "$app"

  # Notarization takes a zip, and --wait blocks until Apple has answered - which is the point: a
  # release that shipped before the ticket existed would staple nothing.
  submission="$WORK/$(basename "$zip")"
  ditto -c -k --sequesterRsrc --keepParent "$app" "$submission"

  xcrun notarytool submit "$submission" \
    --key "$WORK/keys/AuthKey.p8" \
    --key-id "$APPLE_API_KEY_ID" \
    --issuer "$APPLE_API_ISSUER_ID" \
    --wait

  # Stapled so the app opens on a machine that is offline, or that cannot reach Apple.
  xcrun stapler staple "$app"
  xcrun stapler validate "$app"

  # Repack over the original, with ditto: a plain zip loses the symlinks and the executable bit that
  # make a bundle a bundle.
  rm -f "$zip"
  ditto -c -k --sequesterRsrc --keepParent "$app" "$zip"
  echo "  signed, notarized, stapled -> $zip"
done
