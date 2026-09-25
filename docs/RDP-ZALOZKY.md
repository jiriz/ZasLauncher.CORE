# RDP v záložkách — ZasLauncher 1.0.0.18

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
