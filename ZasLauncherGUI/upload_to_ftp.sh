#!/bin/bash

set -e

# FTP přihlašovací údaje
FTP_HOST="www.zasgroup.cz"
FTP_USER="ftp_zas"
FTP_PASS="qudhuf-6kycga-tazgEf"   # ⚠️ Vlož reálné heslo nebo ho načítej bezpečněji
FTP_BASE_DIR="/ftp_zas/zas_001_cz_zasgroup/files/zla"

# Cílové soubory
FILES_TO_UPLOAD=(
  "../publish/ZAS.SimpleTestGUI.Install.dmg"
  "../publish/ZAS.SimpleTestGUI.Install.pkg"
  "../publish/ZAS.SimpleTestGUI.Install.exe"
  "update.json"
)

# Načtení verze z update.json
VERSION=$(jq -r .version update.json)

echo "📦 Verze: $VERSION"

# Vytvoření FTP příkazů
lftp -u "$FTP_USER","$FTP_PASS" "$FTP_HOST" <<EOF
set ftp:ssl-allow no
cd $FTP_BASE_DIR

# 1. vytvoř nový adresář s verzí (pokud neexistuje)
mkdir -p $VERSION
cd $VERSION

# 2. nahraj soubory do verze
$(for FILE in "${FILES_TO_UPLOAD[@]}"; do echo "put -O . $FILE"; done)

# 3. nahraj soubory i do nadřazeného adresáře
cd ..
$(for FILE in "${FILES_TO_UPLOAD[@]}"; do echo "put -O . $FILE"; done)

bye
EOF

echo "✅ Upload dokončen na FTP ($FTP_HOST)"