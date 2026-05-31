#!/bin/bash

set -e

# Výchozí hodnoty
DO_MAC=true
DO_VERSION=true
DO_WIN=true
DO_SIGN=true
DO_NOTARIZE=true
DO_DMG=true
DO_PKG=true
DO_GIT_PULL=true
ONLY_WIN=false

# Zpracování parametrů
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --no-version) DO_VERSION=false ;;
        --no-mac) DO_MAC=false ;;
        --no-win) DO_WIN=false ;;
        --no-sign) DO_SIGN=false ;;
        --no-notarize) DO_NOTARIZE=false ;;
        --no-dmg) DO_DMG=false ;;
        --no-pkg) DO_PKG=false ;;
        --no-git-pull) DO_GIT_PULL=false ;;
        --only-win) ONLY_WIN=true ;;
        *) echo "Neznámý parametr: $1" ;;
    esac
    shift
done

if [[ "$ONLY_WIN" == "true" ]]; then
  DO_MAC=false
  DO_WIN=true
  DO_SIGN=false
  DO_NOTARIZE=false
  DO_DMG=false
  DO_PKG=false
fi

if [[ "$DO_MAC" == "false" ]]; then
  DO_SIGN=false
  DO_NOTARIZE=false
  DO_DMG=false
  DO_PKG=false
fi

APP_NAME="ZasLauncher"
EXECUTABLE_NAME="ZasLauncherGUI"
APP_BUNDLE="${APP_NAME}.app"
PUBLISH_DIR_ALL="../publish"
PUBLISH_DIR_MAC="publish-mac"
PUBLISH_DIR_WIN="publish-win"
LAUNCHER_NAME="$EXECUTABLE_NAME"
OUTPUT_APP="$APP_NAME.app"
DMG_NAME="${APP_NAME}.Install.dmg"
PKG_NAME="${APP_NAME}.Install.pkg"
DMG_TEMP_DIR="${APP_NAME}_dmg"
UPDATE_JSON="update.json"
INNO_SCRIPT="${APP_NAME}.iss"
CSPROJ_FILE="./${APP_NAME}GUI.csproj"
SIGN_IDENTITY="Developer ID Application: Jiri Zdrazil (KF6D8644LM)"
INSTALLER_IDENTITY="Developer ID Installer: Jiri Zdrazil (KF6D8644LM)"
APPLE_ID="jiriz.bg@gmail.com"
TEAM_ID="KF6D8644LM"
APP_SPECIFIC_PASSWORD="pash-xtvo-begj-mses"
ZIP_FILE="${APP_NAME}.zip"
ICON_SRC="./zasgroup.icns"
ENTITLEMENTS="./Entitlements.entitlements"
INFO_TEMPLATE="./Info.plist.template.xml"

rm -rf "$PUBLISH_DIR_ALL"
mkdir -p "$PUBLISH_DIR_ALL"

if [[ ! -f "$CSPROJ_FILE" ]]; then
  echo "❌ Projekt nenalezen: $CSPROJ_FILE"
  exit 1
fi

# 0. Získání a zvýšení verze
OLD_VERSION=$(xmllint --xpath "string(//Project/PropertyGroup/Version)" "$CSPROJ_FILE")
if [[ -z "$OLD_VERSION" ]]; then echo "❌ Nelze načíst <Version>"; exit 1; fi
if [[ ! "$OLD_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then echo "❌ Neplatná verze: $OLD_VERSION"; exit 1; fi

if [[ "$DO_VERSION" == "true" ]]; then
  echo "🔄 Zvyšuji verzi v $CSPROJ_FILE..."
  IFS='.' read -r MAJOR MINOR PATCH BUILD <<< "$OLD_VERSION"
  NEW_VERSION="$MAJOR.$MINOR.$PATCH.$((BUILD + 1))"
  sed -i '' "s|<Version>$OLD_VERSION</Version>|<Version>$NEW_VERSION</Version>|" "$CSPROJ_FILE"
else
  NEW_VERSION="$OLD_VERSION"
fi

echo "✅ Verze: $NEW_VERSION"

# 1. update.json
echo "{ \"version\": \"$NEW_VERSION\" }" > "$UPDATE_JSON"

# 2. Inno Setup skript
if [[ -f "$INNO_SCRIPT" ]]; then
  sed -i '' "s/^#define MyAppVersion \".*\"/#define MyAppVersion \"$NEW_VERSION\"/" "$INNO_SCRIPT"
fi

# 3. Build aplikací
echo "🏗️  Build pro macOS a Windows..."

if [[ "$DO_MAC" == "true" ]]; then
  dotnet publish "$CSPROJ_FILE" \
    -c Release \
    -r osx-arm64 \
    --self-contained true \
    -p:InvariantGlobalization=true \
    -p:PublishSingleFile=false \
    -p:UseAppHost=true \
    -p:MacOSAppBundle=false \
    -o "$PUBLISH_DIR_ALL/$PUBLISH_DIR_MAC"
fi

if [[ "$DO_WIN" == "true" ]]; then
  dotnet publish "$CSPROJ_FILE" \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:InvariantGlobalization=true \
    -p:PublishSingleFile=true \
    -o "$PUBLISH_DIR_ALL/$PUBLISH_DIR_WIN"
fi

# 4. Vytvoření macOS aplikace
if [[ "$DO_MAC" == "true" ]]; then
  if [[ ! -f "$PUBLISH_DIR_ALL/$PUBLISH_DIR_MAC/$EXECUTABLE_NAME" ]]; then
      echo "❌ Spustitelný soubor '$EXECUTABLE_NAME' nebyl nalezen v $PUBLISH_DIR_ALL/$PUBLISH_DIR_MAC"
      echo "Obsah publish složky:"
      ls -la "$PUBLISH_DIR_ALL/$PUBLISH_DIR_MAC" || true
      exit 1
  fi

  if [[ ! -f "$INFO_TEMPLATE" ]]; then
      echo "❌ Info.plist template nenalezen: $INFO_TEMPLATE"
      exit 1
  fi

  if [[ ! -f "$ICON_SRC" ]]; then
      echo "❌ Ikona nenalezena: $ICON_SRC"
      exit 1
  fi

  echo "🛠️  Vytvářím strukturu $OUTPUT_APP..."
  rm -rf "$PUBLISH_DIR_ALL/$OUTPUT_APP"
  mkdir -p "$PUBLISH_DIR_ALL/$OUTPUT_APP/Contents/MacOS"
  mkdir -p "$PUBLISH_DIR_ALL/$OUTPUT_APP/Contents/Resources"

  echo "📦 Kopíruji obsah publish složky..."
  cp -R "$PUBLISH_DIR_ALL/$PUBLISH_DIR_MAC/"* "$PUBLISH_DIR_ALL/$OUTPUT_APP/Contents/MacOS/"

  echo "🎨 Přidávám ikonu..."
  cp "$ICON_SRC" "$PUBLISH_DIR_ALL/$OUTPUT_APP/Contents/Resources/"

  echo "🚀 Nastavuji spustitelný soubor '$EXECUTABLE_NAME'..."
  chmod 755 "$PUBLISH_DIR_ALL/$OUTPUT_APP/Contents/MacOS/$EXECUTABLE_NAME"

  echo "📝 Generuji Info.plist..."
  INFO_TARGET="$PUBLISH_DIR_ALL/$OUTPUT_APP/Contents/Info.plist"

  cat > "$INFO_TARGET" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>cs</string>
    <key>CFBundleExecutable</key>
    <string>$EXECUTABLE_NAME</string>
    <key>CFBundleIdentifier</key>
    <string>cz.zasgroup.zaslauncher</string>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundleDisplayName</key>
    <string>$APP_NAME</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$NEW_VERSION</string>
    <key>CFBundleVersion</key>
    <string>$NEW_VERSION</string>
    <key>CFBundleIconFile</key>
    <string>$(basename "$ICON_SRC")</string>
    <key>LSUIElement</key>
    <true/>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF

  plutil -lint "$INFO_TARGET"
  echo "🔎 LSUIElement v Info.plist:"
  /usr/libexec/PlistBuddy -c "Print :LSUIElement" "$INFO_TARGET"

  find "$PUBLISH_DIR_ALL/$OUTPUT_APP/Contents/MacOS" -name ".DS_Store" -type f -delete
  find "$PUBLISH_DIR_ALL/$OUTPUT_APP/Contents/MacOS" -name "*.icns" -type f -delete
  find "$PUBLISH_DIR_ALL/$OUTPUT_APP/Contents/MacOS" -name "*.pdb" -type f -delete
  xattr -cr "$PUBLISH_DIR_ALL/$OUTPUT_APP"
  /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister \
    -f "$PUBLISH_DIR_ALL/$OUTPUT_APP" 2>/dev/null || true

  echo "✅ Aplikace $APP_BUNDLE vytvořena: $PUBLISH_DIR_ALL/$OUTPUT_APP"
  echo "▶️ Test: open -n -a \"$(cd "$PUBLISH_DIR_ALL" && pwd)/$OUTPUT_APP\""
  echo "🔎 Přímé spuštění kvůli diagnostice: \"$PUBLISH_DIR_ALL/$OUTPUT_APP/Contents/MacOS/$EXECUTABLE_NAME\""
fi

if [[ "$DO_SIGN" == "true" ]]; then
  echo "✍️ Podepisuji $APP_BUNDLE ..."

  find "$PUBLISH_DIR_ALL/$OUTPUT_APP/Contents/MacOS" -type f | while read fname; do
      echo "  [INFO] Signing $fname"
      codesign --force --timestamp --options=runtime --entitlements "$ENTITLEMENTS" --sign "$SIGN_IDENTITY" "$fname"
  done

  echo "  [INFO] Signing app bundle"
  codesign --force --timestamp --options=runtime --entitlements "$ENTITLEMENTS" --sign "$SIGN_IDENTITY" "$PUBLISH_DIR_ALL/$OUTPUT_APP"
  codesign --verify --deep --strict --verbose=2 "$PUBLISH_DIR_ALL/$OUTPUT_APP"
  echo "✅ Aplikace $APP_BUNDLE je podepsána."
fi

if [[ "$DO_NOTARIZE" == "true" ]]; then
  echo "✍️ Notarizuji $APP_BUNDLE ..."
  ditto -c -k --keepParent "$PUBLISH_DIR_ALL/$OUTPUT_APP" "$PUBLISH_DIR_ALL/$ZIP_FILE"
  xcrun notarytool submit "$PUBLISH_DIR_ALL/$ZIP_FILE" --wait --apple-id "$APPLE_ID" --team-id "$TEAM_ID" --password "$APP_SPECIFIC_PASSWORD"
  xcrun stapler staple "$PUBLISH_DIR_ALL/$OUTPUT_APP"
  rm -f "$PUBLISH_DIR_ALL/$ZIP_FILE"
  echo "✅ Aplikace $APP_BUNDLE je notarizována."
fi

# 9. DMG
if [[ "$DO_DMG" == "true" ]]; then
  rm -rf "$PUBLISH_DIR_ALL/$DMG_TEMP_DIR"
  mkdir "$PUBLISH_DIR_ALL/$DMG_TEMP_DIR"
  cp -R "$PUBLISH_DIR_ALL/$APP_BUNDLE" "$PUBLISH_DIR_ALL/$DMG_TEMP_DIR/"
  ln -s /Applications "$PUBLISH_DIR_ALL/$DMG_TEMP_DIR/Applications"
  hdiutil create -volname "$APP_NAME" -srcfolder "$PUBLISH_DIR_ALL/$DMG_TEMP_DIR" -ov -format UDZO "$PUBLISH_DIR_ALL/$DMG_NAME"
fi

# 10. .pkg
if [[ "$DO_PKG" == "true" ]]; then
  pkgbuild --install-location /Applications --component "$PUBLISH_DIR_ALL/$APP_BUNDLE" --sign "$INSTALLER_IDENTITY" "$PUBLISH_DIR_ALL/$PKG_NAME"
  if [[ "$DO_NOTARIZE" == "true" ]]; then
    xcrun notarytool submit "$PUBLISH_DIR_ALL/$PKG_NAME" --apple-id "$APPLE_ID" --team-id "$TEAM_ID" --password "$APP_SPECIFIC_PASSWORD" --wait
    xcrun stapler staple "$PUBLISH_DIR_ALL/$PKG_NAME"
  fi
fi

# 11. Git
if [[ "$DO_GIT_PULL" == "true" ]]; then
  git add --all
  git commit -m "Zvýšena verze na $NEW_VERSION" -a
  git push
  git tag "v$NEW_VERSION"
  git push origin "v$NEW_VERSION"
fi

# 12. Úklid
rm -rf "$PUBLISH_DIR_ALL/$DMG_TEMP_DIR"

echo "✅ Hotovo! Verze $NEW_VERSION připravena."