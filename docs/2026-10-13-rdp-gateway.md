# RDP gateway — ZasLauncher 1.0.0.30

## Příčina a změna
Vestavěné macOS RDP dosud ignorovalo `RDP_GW` a `RDP_GW_METHOD`, které vrací služba pro `RDP_ID`. Nově je předává přes okno a relaci do FreeRDP před připojením. Přihlašovací údaje gateway jsou stejné jako údaje cílového RDP; služba nevrací samostatné přihlášení gateway.

Metody odpovídají RD Gateway: 0 a 4 bez gateway, 1 vždy gateway, 2 automatická detekce, 3 výchozí režim knihovny. Chybějící metoda zachovává přímé připojení. Neplatná metoda či chybějící adresa při aktivní gateway vrací srozumitelnou chybu. Adresa podporuje host a volitelný port, výchozí port je 443.

Použit je RPC přes HTTPS; HTTP transport proti testované gateway vracel HTTP 500. Hesla ani obsah parametrů se nepřidávají do logu. Log obsahuje pouze číslo režimu gateway. Původní nativní ABI `zr_create` zůstalo zachováno; přidán setter `zr_gateway`.

## Ověření
- Živá služba pro RDP_ID 755 vrací gateway a metodu 1.
- Nativní test ověřuje přímé připojení bez gateway, zapnutí gateway, transport, rozdělení domény a předání syntetických přihlašovacích údajů; sanitizéry bez chyby.
- Živý test došel přes RPC/HTTPS ke gateway, která vrátila `E_PROXY_RAP_ACCESSDENIED`. Přístup k cílové ploše nebyl potvrzen. Je potřeba porovnat cílovou adresu a oprávnění s funkční konfigurací Royal TSX. Žádná nastavení KARATu ani gateway se neměnila.
- Testovací připojení bylo ukončeno; běžící relace uživatele zůstaly beze změny.

## Sestavení a regrese
- `tests/run-macos.sh` prošel: nativní sanitizéry, dvě nezávislé relace, vstup, reconnect, rušení, uvolnění, přenos souborů a Unicode schránky, přepínání záložek i zavření poslední relace.
- macOS arm64 Release publish 1.0.0.30 dokončen.
- Aplikace: `/Users/jiriz/ZasLauncher-builds/1.0.0.30/ZasLauncher.app`.
- Distribuční kopie bude v iCloud/Stahování/ZasLauncher-1.0.0.30. Běžící aplikace nebyla nahrazena ani restartována.

