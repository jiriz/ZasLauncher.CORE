# RDP v záložkách — ZasLauncher 1.0.0.16

Změna: Jarka (Codex), 25. 9. 2026.

## Použití

Každé RDP připojení se otevře jako záložka v jednom okně Launcheru.
V záložce je přímo vzdálená plocha, samostatné okno ani samostatná aplikace
sdl-freerdp se již nespouští. Přepnutí záložky nechává ostatní relace připojené.
Křížek ukončí pouze příslušné připojení; zavření správce nebo ukončení Launcheru
počká na odpojení všech jeho relací.

Panel nad plochou obsahuje reconnect, odpojení, Ctrl+Alt+Del a dva směry přenosu
textové schránky. Pro vložení místního textu použijte **Schránka → RDP** a ve
vzdálené aplikaci Ctrl+V. Pro opačný směr nejdříve Ctrl+C ve vzdálené aplikaci,
potom **RDP → schránka**. Přenos je záměrně ruční, aby záložky na pozadí
nepřepisovaly místní schránku. Podporován je text do 1 MiB v UTF-16, ne soubory.

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
Přesměrování zvuku, tiskáren, disků a souborové schránky tato integrace nepřidává.

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
