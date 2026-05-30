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
APP_BUNDLE="${APP_NAME}.app"
PUBLISH_DIR_ALL="../publish/"
PUBLISH_DIR_MAC="publish-mac"
PUBLISH_DIR_WIN="publish-win"
LAUNCHER_NAME="AppLauncher"
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

rm -rf "$PUBLISH_DIR_ALL"

# 0. Získání a zvýšení verze
echo "🔄 Zvyšuji verzi v $CSPROJ_FILE..."
OLD_VERSION=$(xmllint --xpath "string(//Project/PropertyGroup/Version)" "$CSPROJ_FILE")
if [[ -z "$OLD_VERSION" ]]; then echo "❌ Nelze načíst <Version>"; exit 1; fi
if [[ ! "$OLD_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then echo "❌ Neplatná verze: $OLD_VERSION"; exit 1; fi
IFS='.' read -r MAJOR MINOR PATCH BUILD <<< "$OLD_VERSION"
NEW_VERSION="$MAJOR.$MINOR.$PATCH.$((BUILD + 1))"
if [[ "$DO_VERSION" == "false" ]]; then
  NEW_VERSION=$OLD_VERSION
fi
sed -i '' "s|<Version>$OLD_VERSION</Version>|<Version>$NEW_VERSION</Version>|" "$CSPROJ_FILE"
echo "✅ Nová verze: $NEW_VERSION"

# 1. update.json
echo "{ \"version\": \"$NEW_VERSION\" }" > "$UPDATE_JSON"

# 2. Inno Setup skript
sed -i '' "s/^#define MyAppVersion \".*\"/#define MyAppVersion \"$NEW_VERSION\"/" "$INNO_SCRIPT"

# 3. Build aplikací
echo "🏗️  Build pro macOS a Windows..."
if [[ "$DO_MAC" == "true" ]]; then
  dotnet publish -c Release -r osx-arm64 --self-contained true -p:InvariantGlobalization=true -p:PublishSingleFile=true -p:UseAppHost=true -o $PUBLISH_DIR_ALL$PUBLISH_DIR_MAC
fi
if [[ "$DO_WIN" == "true" ]]; then
  dotnet publish -c Release -r win-x64 --self-contained true -p:InvariantGlobalization=true -p:PublishSingleFile=true -o $PUBLISH_DIR_ALL$PUBLISH_DIR_WIN
fi

# 4. vytvoreni aplikace
if [[ "$DO_MAC" == "true" ]]; then
# === Kontrola existence výstupního souboru ===
if [ ! -f "$PUBLISH_DIR_ALL$PUBLISH_DIR_MAC/$APP_NAME" ]; then
    echo "❌ Spustitelný soubor '$APP_NAME' nebyl nalezen v $PUBLISH_DIR_ALL$PUBLISH_DIR_MAC"
    echo "Nezapomeň použít:"
    echo "dotnet publish -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=false -o $PUBLISH_DIR_ALL$PUBLISH_DIR_MAC"
    exit 1
fi

# === Vytvoření struktury .app balíčku ===
echo "🛠️ Vytvářím strukturu $OUTPUT_APP..."
rm -rf "$PUBLISH_DIR_ALL$OUTPUT_APP"
mkdir -p "$PUBLISH_DIR_ALL$OUTPUT_APP/Contents/MacOS"
mkdir -p "$PUBLISH_DIR_ALL$OUTPUT_APP/Contents/Resources"

# === Kopírování celé publish složky ===
echo "📦 Kopíruji obsah publish složky..."
cp -R "$PUBLISH_DIR_ALL$PUBLISH_DIR_MAC"/* "$PUBLISH_DIR_ALL$OUTPUT_APP/Contents/MacOS/"
echo "🎨 Přidávám ikonu..."
cp "$ICON_SRC" "$PUBLISH_DIR_ALL$OUTPUT_APP/Contents/Resources/"

# === Vytvoření launcher skriptu ===
echo "🚀 Vytvářím launcher '$LAUNCHER_NAME'..."
cat > "$PUBLISH_DIR_ALL$OUTPUT_APP/Contents/MacOS/$LAUNCHER_NAME" <<EOF
#!/bin/bash
DIR="\$(cd "\$(dirname "\$0")" && pwd)"
"\$DIR/$APP_NAME"
EOF
chmod +x "$PUBLISH_DIR_ALL$OUTPUT_APP/Contents/MacOS/$LAUNCHER_NAME"

# === Vytvoření Info.plist ===
echo "📝 Generuji Info.plist..."
INFO_TEMPLATE="./Info.plist.template.xml"
INFO_TARGET="$PUBLISH_DIR_ALL$OUTPUT_APP/Contents/Info.plist"
# Vytvoř Info.plist s nahrazenými hodnotami
sed \
  -e "s/{{APP_NAME}}/$APP_NAME/g" \
  -e "s/{{LAUNCHER_NAME}}/$LAUNCHER_NAME/g" \
  -e "s/{{NEW_VERSION}}/$NEW_VERSION/g" \
  -e "s/{{ICON_SRC}}/$(basename "$ICON_SRC")/g" \
  "$INFO_TEMPLATE" > "$INFO_TARGET"

find "$PUBLISH_DIR_ALL$OUTPUT_APP/Contents/MacOS" -name ".DS_Store" -type f -delete
find "$PUBLISH_DIR_ALL$OUTPUT_APP/Contents/MacOS" -name "*.icns" -type f -delete
find "$PUBLISH_DIR_ALL$OUTPUT_APP/Contents/MacOS" -name "*.pdb" -type f -delete

echo "✅ Hotovo! Aplikace $APP_BUNDLE vytvořena."
fi

if [[ "$DO_SIGN" == "true" ]]; then
echo "✍️ Podepisuji $APP_BUNDLE ..."
find "$PUBLISH_DIR_ALL$OUTPUT_APP/Contents/MacOS/"|while read fname; do
    if [[ -f $fname ]]; then
        echo "  [INFO] Signing $fname"
        codesign --force --timestamp --options=runtime --entitlements "$ENTITLEMENTS" --sign "$SIGN_IDENTITY" "$fname"
    fi
done
echo "  [INFO] Signing app file"
codesign --force --timestamp --options=runtime --entitlements "$ENTITLEMENTS" --sign "$SIGN_IDENTITY" "$PUBLISH_DIR_ALL$OUTPUT_APP"
codesign --verify --verbose "$PUBLISH_DIR_ALL$OUTPUT_APP"
echo "✅ Hotovo! Aplikace $APP_BUNDLE je podepsána."
fi 

if [[ "$DO_NOTARIZE" == "true" ]]; then
  echo "✍️ Notarizuji $APP_BUNDLE ..."
  echo "  [INFO] Vytvářím ZIP: $PUBLISH_DIR_ALL$ZIP_FILE"
  ditto -c -k --keepParent "$PUBLISH_DIR_ALL$OUTPUT_APP" "$PUBLISH_DIR_ALL$ZIP_FILE"
  echo "  [INFO] Odesílám k notarizaci ..."  
  xcrun notarytool submit "$PUBLISH_DIR_ALL$ZIP_FILE" --wait --apple-id "$APPLE_ID" --team-id "$TEAM_ID" --password "$APP_SPECIFIC_PASSWORD"
  xcrun stapler staple "$PUBLISH_DIR_ALL$OUTPUT_APP"
  rm -f "$PUBLISH_DIR_ALL$ZIP_FILE"
  echo "✅ Hotovo! Aplikace $APP_BUNDLE je notarizována."
fi

# 9. DMG
if [[ "$DO_DMG" == "true" ]]; then
rm -rf "$PUBLISH_DIR_ALL$DMG_TEMP_DIR"
mkdir "$PUBLISH_DIR_ALL$DMG_TEMP_DIR"
cp -R "$PUBLISH_DIR_ALL$APP_BUNDLE" "$PUBLISH_DIR_ALL$DMG_TEMP_DIR/"
ln -s /Applications "$PUBLISH_DIR_ALL$DMG_TEMP_DIR/Applications"
hdiutil create -volname "$APP_NAME" -srcfolder "$PUBLISH_DIR_ALL$DMG_TEMP_DIR" -ov -format UDZO "$PUBLISH_DIR_ALL$DMG_NAME"
fi

# 10. .pkg
if [[ "$DO_PKG" == "true" ]]; then
pkgbuild --install-location /Applications --component "$PUBLISH_DIR_ALL$APP_BUNDLE" --sign "$INSTALLER_IDENTITY" "$PUBLISH_DIR_ALL$PKG_NAME"
if [[ "$DO_NOTARIZE" == "true" ]]; then
xcrun notarytool submit "$PUBLISH_DIR_ALL$PKG_NAME" --apple-id "$APPLE_ID" --team-id "$TEAM_ID" --password "$APP_SPECIFIC_PASSWORD" --wait
xcrun stapler staple "$PUBLISH_DIR_ALL$PKG_NAME"
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
rm -rf "$PUBLISH_DIR_ALL$DMG_TEMP_DIR"

echo "✅ Hotovo! Verze $NEW_VERSION připravena."