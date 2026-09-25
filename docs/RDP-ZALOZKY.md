# RDP v záložkách — ZasLauncher 1.0.0.20

Změna: Jarka (Codex), 25. 9. 2026.

## Použití

Každé RDP připojení se otevře jako záložka v jednom okně Launcheru.
V záložce je přímo vzdálená plocha, samostatné okno ani samostatná aplikace
sdl-freerdp se již nespouští. Přepnutí záložky nechává ostatní relace připojené.
Křížek ukončí pouze příslušné připojení; zavření správce nebo ukončení Launcheru
počká na odpojení všech jeho relací.

Záložky mají stejnou šířku 240 logických pixelů. Dlouhé názvy se zkracují
výpustkou; celý název je v tooltipu. Křížek je malý vektorový bez trvalého podkladu.
Pravé tlačítko nad konkrétní záložkou otevře reconnect, odpojení, Ctrl+Alt+Del
a ruční přenos schránky. Stav připojení a přenosu je dole pod plochou.

Schránka **textu, souborů a složek funguje automaticky v obou směrech**:

- Mac → RDP: zkopírovat na Macu obvyklým **Cmd+C**, ve Windows vložit **Ctrl+V**.
- RDP → Mac: ve Windows **Ctrl+C**, po dokončení přenosu vložit na Macu **Cmd+V**.
- Propojena je právě vybraná záložka. Přepnutí relace zruší rozpracované přijímání;
  zavření čeká na jeho ukončení před uvolněním nativního připojení.
- Před Ctrl+V se ověří aktuální obsah a potvrzení nabídky schránky od Windows.
  Po chybě se nevloží starý obsah. Opakované vyzkoušení lze vyvolat položkou menu
  **Schránka → RDP**, nebo novým kopírováním.
- Při přenosu z Windows se soubory nejprve stáhnou po částech. Až potom je Finder
  dostane do schránky; průběžný stav je pod vzdálenou plochou. Novější kopírování
  na Macu má přednost před rozpracovaným přenosem.

Text má limit 1 MiB v UTF-16, souborová schránka 4096 položek včetně složek.
Přenášejí se běžné soubory a složky; symbolické odkazy, neplatné názvy Windows,
kolize názvů a cesty vedoucí mimo cílovou složku se odmítají. Pro soubory se používají
64bitové velikosti a pozice; do paměti se při stahování načítá nejvýše 1 MiB dat
najednou. Převod obrázků a formátovaného textu tato změna nepřidává.
Windows server musí povolit přesměrování schránky a souborů.

Podporovány jsou klávesnice včetně rozšířených kláves, myš a kolečko, vzdálený
kurzor a změna velikosti plochy. Rozlišení se přizpůsobuje po ustálení velikosti
okna, pokud server podporuje kanál Display Control. Jinak se obraz poměrově
škáluje. Při změně záložky nebo ztrátě fokusu se uvolní stisknuté klávesy a tlačítka.
Počáteční rozložení klávesnice vychází z aktuální kultury .NET; fyzické klávesy
se předávají jako PC scancody. Systémové zkratky zachycené macOS zůstávají systému.

## Implementace

- `RdpSessionControl`: Avalonia plocha a ovládání; skrytá záložka zastaví UI časovač,
  ale nezruší relaci. UI čte pouze poslední kompletní obraz, nehromadí snímky ve frontě.
- `RdpConnection`: vlastní nativní relace a samostatné pracovní vlákno.
- `Native/zas_rdp.c`: rozhraní nad FreeRDP 3, softwarové GDI vykreslování, clipboard,
  kurzor a display control. Síťové vstupy se posílají z pracovní smyčky přes omezenou frontu;
  pohyby myši se slučují. Kopírování obrazu a schránky je chráněno mutexem.
- Odpojení přeruší také probíhající connect. Nativní kontext a buffery se uvolňují
  až po skončení workeru; bitmapy a kurzory mají explicitní Dispose. Reference na
  heslo se při zavření zahodí, skutečnou paměť managed řetězce spravuje .NET.
- Heslo se již nepředává externímu procesu jako parametr příkazové řádky.
- Chování certifikátů zůstává stejné jako před změnou (`/cert:ignore`).

Tato verze implementuje vložené RDP pro macOS. Windows sestavení stále hlásí,
že vložené RDP není na jeho platformě podporované, stejně jako před změnou.
Přesměrování zvuku, tiskáren a disků tato integrace nepřidává.

## Sestavení a balení

Build na Macu vyžaduje Xcode Command Line Tools, Python 3 a FreeRDP 3 (ověřeno
3.26.0 z Homebrew). `Native/build.sh` se volá automaticky z projektu. Přeloží adaptér,
přibalí nativní závislosti do `rdp/`, upraví jejich vazby na `@loader_path` a přidá
licenční soubory. Ověřený balík má 29 knihoven, přibližně 41 MiB. Na cílovém Macu
není kvůli těmto knihovnám potřeba Homebrew. K existujícímu publish skriptu není
potřeba přidávat ruční kopírování: `rdp/` je součástí výstupu dotnet publish.

Pro Intel je nutná odpovídající x64 instalace FreeRDP a jeho závislostí.
`FREERDP_PREFIX` umožňuje zadat jinou instalaci. Intel runtime nebyl otestován.

## Ověření

- Build macOS arm64 a publish 1.0.0.16 prošly; Windows x64 build prošel.
  Existující varování z připojeného projektu ZASutility.Standard přetrvávají.
- Místní FreeRDP sample server: dvě současná spojení s vykresleným obrazem,
  skutečná odezva obrazu na myš, klávesa G a následná změna rozlišení od serveru,
  zavření A bez odpojení B, reconnect, zrušení před zahájením connect a explicitní uvolnění.
- Avalonia Headless se Skia: vykreslené záložky, přepínání, vstupy, zavření,
  reconnect a opakované volání CloseAsync. Kontrolní snímek byl vizuálně zkontrolován.
- Nativní test s AddressSanitizer a UndefinedBehaviorSanitizer: stride snímku,
  změna rozměrů během kopírování, fronta vstupů, UTF-16 clipboard protokol a jeho limity.
- Kontrola všech přibalených dylib: odkazy vedou do balíku nebo do systémových knihoven.
- Reprodukovatelný běh: `bash tests/run-macos.sh`. Test používá jen lokální Unix socket
  a proxy na 127.0.0.1; nevstupuje do firemních relací ani do uživatelovy schránky.

Sample server neověřuje přihlášení do domény ani skutečný clipboard/display-control
kanál Windows serveru. Tyto části jsou implementované; jejich ověření proti firemnímu
Windows serveru zůstává pro uživatelské vyzkoušení. Pro clipboard jsou otestované
přímo klientské protokolové callbacky, pro rozlišení změna zaslaná sample serverem.

Lokální testovací aplikace (arm64):
`/Users/jiriz/ZasLauncher-builds/1.0.0.16/ZasLauncher.app`

Aplikace má lokální ad-hoc podpis, není notarizovaný distribuční release.
Nenasazuje se do /Applications a původní instalaci nenahrazuje.

## Úprava ovládání 1.0.0.17

Záložky mají šířku 240 logických pixelů, dlouhé názvy výpustku a celý název
v tooltipu. Křížek je vektorový bez trvalého šedého podkladu. Ovládání relace
je v kontextovém menu příslušné hlavičky, stav dole pod plochou.
UI test ověřil stejné šířky a odpojení neaktivní A přes její menu bez dopadu na B.
Build, integrace a sanitizery prošly. Commit `8d61528`.
Testovací app: `/Users/jiriz/ZasLauncher-builds/1.0.0.17/ZasLauncher.app`.

## Automatická schránka 1.0.0.18

Jarka (Codex), 25. 9. 2026:

- Nativní cliprdr: FileGroupDescriptorW a FileContents SIZE/RANGE, 64bitové pozice,
  kontrola generace schránky a ID přenosu, omezení velikostí odpovědí, zrušení přenosu.
  Potvrzení FormatList se čeká před odesláním Ctrl+V.
- `RdpClipboardSync`: automatické propojení vybrané relace, sledování changeCount
  macOS pasteboardu, potlačení zpětné smyčky, přednost nové místní kopie a ruční menu.
- `ClipboardFiles`: převod seznamu položek, kontrola cest, přenos po částech,
  ověření volného místa a odstranění nedokončených kopií.
- Dokončené soubory zůstávají v `LocalApplicationData/ZasLauncher/clipboard/`,
  na macOS typicky `~/Library/Application Support/ZasLauncher/clipboard/`.
  Neodstraňují se při zavření relace, aby pozdější vložení ve Finderu dál fungovalo.
  Při prvním načtení místní schránky v novém procesu se odstraní kopie starší 7 dnů,
  kromě kopií právě odkazovaných místní schránkou.

Ověření 1.0.0.18:

- macOS arm64 build a samostatný publish prošly, nativní část s `-Werror`.
- Native contract s ASan/UBSan: nabídka/potvrzení, UTF-16, descriptor,
  velikost/část souboru, čtení za hranicí 4 GiB na řídkém testovacím souboru,
  odmítnutí neplatného indexu, zrušená/starší odpověď.
- Managed test: strom složek, české názvy, prázdný soubor, soubor přes 2 MiB,
  přesná shoda bajtů po přenosu, omezené části, odmítnutí cest mimo složku a zrušení.
- Headless schránka: oba směry víceřádkového Unicode textu, neaktivní záložka,
  novější místní kopie, odmítnutí přenosu bez následného vložení starých dat.
- Znovu prošly dvě současné relace, vstupy, změna rozlišení, reconnect,
  UI záložek a explicitní ukončení. Testy nemění systémovou schránku uživatele.

Skutečné kopírování přes Průzkumník Windows a Finder proti firemnímu serveru
zatím nebylo ověřeno. Testovací sample server clipboard kanál nenabízí;
protokol je ověřen samostatnými callback testy a falešným protějškem pro soubory.
Zůstávají existující build varování v připojené ZASutility.Standard a původním kódu.

Testovací aplikace pro Apple Silicon:
`/Users/jiriz/ZasLauncher-builds/1.0.0.18/ZasLauncher.app`
Má lokální ad-hoc podpis; nenahrazuje instalaci v /Applications a není notarizovaná.

## Oprava skutečného připojení schránky 1.0.0.19

Jarka (Codex), 25. 9. 2026. Jiří ověřil, že ve verzi 1.0.0.18 nefunguje
text ani soubory. Ve spuštěném Launcheru byla ověřena hláška o nepotvrzeném
přenosu schránky.

Příčina: adaptér volal `freerdp_client_load_addins` v `PreConnect`. FreeRDP 3.26.0
potom v `utils_reload_channels` původní kanály zruší a vyžádá si nové přes
`instance->LoadChannels`. Tento callback chyběl. Připojení plochy fungovalo,
ale server vůbec neměl připojený kanál `cliprdr`; týkalo se to i ostatních kanálů.
Nešlo o důkaz zákazu schránky na serveru.

Oprava: `PreConnect` pouze registruje události, načítání addinů je v samostatném
`LoadChannels` callbacku. Oprava je v nativní `rdp/libzasrdp.dylib`, proto je potřeba
celá nová aplikace, ne pouze managed DLL. Stávající RDP relace musí být znovu otevřena
v nové aplikaci; běžící verze 1.0.0.18 si knihovnu sama nevymění.

Ověření:

- Před opravou místní server hlásil `clipboard joined=0`; inicializace clipboard
  serveru selhala. Po opravě `clipboard joined=1` a potvrzení přenosu prošlo.
- Nový wire test používá skutečné vyjednané RDP spojení a `CliprdrServerContext`
  nad oficiálním FreeRDP sample serverem. Ověří oba směry textu i bajtů souboru,
  včetně nabídky formátů, ACK, požadavků na data a přenosu FileContents.
  Server nabídne vlastní data až po ověření dat přijatých od klienta.
- Test pracuje pouze přes lokální Unix socket a proxy 127.0.0.1, se syntetickými
  daty. Nemění macOS schránku a nepřipojuje se k zákaznické relaci.
- Opakování: po běžném buildu spustit
  `FREERDP_SOURCE=/cesta/k/FreeRDP-3.26.0 bash tests/run-clipboard-wire.sh`.
  Zdroj musí být checkout oficiálního FreeRDP tagu 3.26.0; skript ho nestahuje.
- Běžné testy `tests/run-macos.sh` prošly. Wire test prošel také přímo s knihovnou
  z finální aplikace 1.0.0.19. macOS arm64 publish a kontrola podpisu prošly.

Lokální testovací aplikace (Apple Silicon, ad-hoc podpis):
`/Users/jiriz/ZasLauncher-builds/1.0.0.19/ZasLauncher.app`.
Skutečné vložení v zákaznickém Průzkumníku a Finderu zatím zůstává pro ověření Jiřím.

## Okno, vstupy a schránka 1.0.0.20

Jarka (Codex), 25. 9. 2026. Jiří hlásil posunuté klikání po návratu do okna
nebo jiné záložky a nadále nefunkční schránku při správných Ctrl+C/V ve Windows.
Běžící `/Applications/ZasLauncher.app` byla ověřena jako 1.0.0.19; SHA-256 nativní
knihovny odpovídal sestavení 1.0.0.19. Nešlo o neaktualizovanou knihovnu.

- Zavření poslední záložky zavře celý RDP Manager, včetně odpojení a uložení geometrie.
- Název v titulku a hlavičce záložky se ořezává o krajní mezery. Přihlašovací údaje
  zůstávají pro spojení původní.
- Souřadnice se převádějí vůči skutečnému prvku `Image` a jeho aktuální velikosti,
  nikoli odhadem ze středu okolního panelu. To zahrnuje posunutí při uspořádání/resize.
- Aktivace/deaktivace okna i opuštění záložky uvolní zachycení myši, tlačítka a
  modifikátory. Návrat obnoví fokus plochy. Pohyb myši napraví chybějící release
  tlačítka, pokud už fyzicky stisknuté není.
- Schránka Mac → RDP se nabídne při Ctrl+V (nebo ručním příkazu menu).
  Periodická synchronizace už nepřepisuje právě zkopírovaný obsah Windows starší
  kopií z Macu/Parallels. Pro paste z kontextového menu Windows lze předem použít
  „Schránka → RDP“ v menu záložky.
- Nová nabídka Windows vzniklá během čekání na ACK se nezahodí. Návrat do záložky
  také nevynucuje opětovné odeslání nezměněné místní schránky.
- Výsledek přípravy Ctrl+V se vrací přímo z asynchronní operace; nesdílí se s
  periodickým čtením, které jej dříve mohlo přepsat.
- RDP handshake odešle úvodní prázdnou nabídku schránky i bez předchozího místního
  kopírování. Data se pak vyžádají standardním clipboard kanálem.

Diagnostika: `LocalApplicationData/ZasLauncher/logs/rdp.log` (macOS typicky
`~/Library/Application Support/ZasLauncher/logs/rdp.log`). Uchovává maximálně
aktuální soubor kolem 1 MiB a jeden předchozí. Zapisuje verzi, anonymní ID relace,
fáze protokolu, generace/ACK, typ chyby, fokus a rozměry/souřadnice kliknutí.
Nezapisuje obsah schránky, názvy přenášených souborů, hosty, účty ani hesla.

Ověření:

- Headless UI: oříznutý titulek, zavření Manageru posledním křížkem, převod bodu
  obrazu po změně rozměrů a přepnutí záložek včetně nesymetrického odsazení obrazu.
- Schránka: nová vzdálená kopie během ACK a kopie Windows po změně Mac/Parallels
  schránky; obě jsou přijaty. Odmítnutý místní přenos nevloží stará data.
- Nový společný test `RdpClipboardSync` + `RdpConnection` + nativní adaptér + skutečný
  RDP server ověřuje text oběma směry. Používá izolovanou Headless schránku.
  Wire test nadále ověřuje bajty souborů v obou směrech.
- Spuštění společného testu po buildu:
  `FREERDP_SOURCE=/cesta/k/FreeRDP-3.26.0 RDP_CLIPBOARD_UI="$PWD/tests/RdpUi/bin/Debug/net10.0/RdpUi.dll" bash tests/run-clipboard-wire.sh`.

Lokální aplikace: `/Users/jiriz/ZasLauncher-builds/1.0.0.20/ZasLauncher.app`.
Pro načtení je nutné ukončit starý Launcher a spustit novou aplikaci. Konkrétní
chování zákaznické relace a schránky Parallels zůstává k ověření v reálném používání;
přiložené testy nejsou ověřením zákaznického Windows ani systémové schránky macOS.

Build/publish macOS arm64 1.0.0.20 a ověření podpisu prošly. Wire test prošel
také proti nativní knihovně přímo z výsledné aplikace.
