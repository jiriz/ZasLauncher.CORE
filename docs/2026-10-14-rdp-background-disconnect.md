# ZasLauncher 1.0.0.32 – RDP na pozadí a diagnostika odpojení

## Pozorování
Jiří hlásí odpojování u všech připojení, pravděpodobně při práci v jiné aplikaci. V místním rdp.log relace 341a4f40: deaktivace okna 14. 10. 2026 16:44:48.087, odpojení 16:46:52.593, state=3, connection-error=0. Uživatel znovu aktivoval okno až 16:53:37.505. To potvrzuje odpojení na pozadí, nikoli konkrétní příčinu.

Dosavadní protokolová smyčka končí také při chybě čekání, obsluhy událostí či odesílání vstupu/kanálu. Pokud FreeRDP nevyplní last_error, původní log tyto případy nerozlišuje. Lokálně načtená knihovna FreeRDP má TCP keepalive zapnuté (delay=5, interval=2, retries=3), heartbeat zapnutý, autoreconnect vypnutý. Samotné přepnutí záložky zastavuje UI timer a synchronizaci schránky, nikoli protokolové vlákno.

## Změna
- Nový MacRdpActivity získá NSProcessInfo aktivitu UserInitiatedAllowingIdleSystemSleep po dobu protokolového vlákna. Životnost je nezávislá na aktivní záložce a okně. Každá relace má vlastní token, který se v using/finally ukončí a uvolní při ukončení spojení.
- Účelem je zabránit App Nap během aktivního RDP. Uspání systému a displeje zůstává povolené. Žádné falešné pohyby myši ani klávesy, žádné obcházení serverového limitu nečinnosti.
- Log background-activity=True potvrzuje získání aktivity.
- Nový řádek protocol-end obsahuje reason, connected, FreeRDP error, server-error a wait-error. Hodnoty se pořizují před freerdp_disconnect a zapíší přímo z protokolového vlákna i u skryté záložky.
- Diagnostika neobsahuje adresy serverů, uživatelská jména, hesla ani obsah schránky. Neprovádí automatické opakování přihlášení.

## Ověření a omezení
- tests/run-macos.sh: úspěšné nativní testy se sanitizéry, místní RDP spojení, dvě nezávislé relace, vstup, resize, reconnect, cancellation/disposal, textová a souborová schránka, přepínání záložek a zavření poslední záložky.
- Nový test na macOS ověřuje získání dvou aktivit, jejich nezávislost a opakované bezpečné Dispose.
- Release publish osx-arm64 úspěšný; existující upozornění závislostí a nullable zůstávají.
- App Nap je podezření, ne prokázaná příčina zákaznických výpadků. Test zákaznického RDP při nečinnosti zatím neproveden. Pokud se problém zopakuje, vyhodnotit nový protocol-end společně s časem deaktivace okna a serverovým logem.
- Běžící instalace a relace uživatele nebyly restartovány ani nahrazeny.

## Zdroj pravidel pro macOS aktivitu
https://developer.apple.com/library/archive/documentation/Performance/Conceptual/power_efficiency_guidelines_osx/PrioritizeWorkAtTheAppLevel.html

## Distribuce
Lokální aplikace: /Users/jiriz/ZasLauncher-builds/1.0.0.32/ZasLauncher.app
ZIP předán do iCloud/Stahování/ZasLauncher-1.0.0.32/ZasLauncher-1.0.0.32.zip.

Apple notarizace Accepted (2de4925f-b2d6-475c-8d0c-cc7406c94af8), stapler úspěšný, Gatekeeper accepted / Notarized Developer ID, strict podpis ověřen. Spuštění přes systém vytvořilo proces 18458 z nového balíčku, proces zůstal běžet; čtení UI skončilo timeoutem. Testovací proces ukončen, původní proces 20088 a uživatelské relace ponechány. Živé RDP v nové verzi zatím neověřeno.
