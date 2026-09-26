# ZasLauncher 1.0.0.25 – přenositelnost přihlašování RDP

Jarka (Codex), 26. 9. 2026. Jiří hlásil 0x0002000D na druhém MacBooku;
Royal TSX se tam ke stejnému serveru připojí.

## Změřené

- Dodaný rdp.log má 2 298 stejných záznamů state=4, ready=0. Neobsahuje
  počátek spojení ani konkrétní protokolovou příčinu. Pole error=0 se týkalo
  clipboard ACK, nikoli návratového kódu spojení.
- V 1.0.0.24 bylo 29 knihoven a všechny jejich přímé závislosti byly lokální
  nebo systémové. Chyběl však dynamicky načítaný OpenSSL legacy.dylib.
- Přibalené OpenSSL s cestou k Homebrew modul legacy i MD4 načte. S prázdnou
  cestou k modulům se modul nenačte a MD4 není dostupné. Použité WinPR není
  sestaveno s interní MD4/RC4 implementací.

To je prokázaná chyba přenositelnosti. Že způsobila právě Jiřího konkrétní
neúspěšné spojení, je nutné ověřit novým balíčkem na jeho MacBooku.

## Oprava

- bundle.py přibaluje legacy.dylib z téže instalace jako libcrypto.3.dylib.
  U pluginu bez LC_ID_DYLIB zpracuje i první závislost, přepíše ji na
  @loader_path/libcrypto.3.dylib a modul podepíše. Při chybějícím modulu build selže.
- RdpConnection před inicializací FreeRDP nastaví vyhledávání providerů přímo
  do adresáře rdp aplikace. Handle libcrypto zůstává držen po dobu procesu.
  Nezapisuje systémové nastavení ani uživatelské proměnné prostředí.
- Chybový stav se loguje pouze při změně stavu nebo kódu. Přibyl údaj
  connection-error=0x…; nedochází k opakování každých 33 ms.
- Testovací projekt načítá produkční assembly s ikonou, kterou začalo sdílené
  XAML okna používat v předchozí změně ikon.

## Ověření

- tests/portable-crypto.py: prázdná cesta k systémovým modulům jako negativní
  kontrola; s cestou do balíčku projde MD4 známého vektoru a dostupnost RC4.
  Prošlo i proti výsledné podepsané aplikaci.
- tests/run-macos.sh: všechny kontroly prošly, včetně nativních kontrol se
  sanitizery, dvou místních RDP relací, vstupů, reconnectu, schránky a UI.
- macOS arm64 Release publish a codesign --verify --deep --strict prošly.
- Windows není touto opravou cíleno; nový Windows balíček nebyl sestaven.
- Zákaznické připojení na MacBooku nebylo v této session ověřeno.

Aplikace: /Users/jiriz/ZasLauncher-builds/1.0.0.25/ZasLauncher.app
Přenést celý .app balíček a před spuštěním ukončit původní Launcher.
