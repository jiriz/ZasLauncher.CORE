# ZasLauncher 1.0.0.26 – indikace přenosu souborů

Jarka (Codex), 27. 9. 2026.
Spodní lišta každé RDP relace zobrazuje během přenosu souborů neurčitý
animovaný ukazatel a směr přenosu. Textová schránka jej nezapíná.

- RDP → Mac: aktivní od začátku stahování do jeho dokončení a předání do
  schránky. Ve finally zhasne také při chybě nebo zrušení.
- Mac → RDP: aktivní při obsluze skutečných požadavků FILECONTENTS_RANGE.
  Běží i během blokujícího odesílání odpovědi. Po posledním požadavku má
  doběh 1 s, aby mezi bloky neblikal. Samotná nabídka souboru nebo dotaz na
  velikost jej nespouští. Protokol neoznamuje dokončení celé vzdálené operace,
  proto jde o indikaci aktivity, nikoli procenta nebo potvrzení zápisu na disk.
- Odpojení ukazatel skryje; údaje jsou oddělené pro jednotlivé relace.
- Přenosové buffery a obsah souborů se kvůli indikaci neduplikují.

Ověření: celý tests/run-macos.sh prošel. Doplněny kontroly nativního stavu
před přenosem, během čtení/odesílání a po doběhu; test chybného příjmu ověřuje
zhasnutí ukazatele a textový přenos jeho neaktivitu. Build macOS arm64,
kontrola podpisu a test přibaleného OpenSSL provideru prošly.

Build: /Users/jiriz/ZasLauncher-builds/1.0.0.26/ZasLauncher.app
Předání: iCloud Stahování/ZasLauncher-1.0.0.26-mac-arm64.zip.
Běžící uživatelská aplikace nebyla nahrazena ani restartována.
