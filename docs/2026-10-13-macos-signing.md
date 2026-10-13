# Oprava spuštění macOS balíčku — 1.0.0.31

Verze 1.0.0.30 byla podepsána původním Entitlements.entitlements obsahujícím mimo jiné LSUIElement a nepoužívaná sandboxová oprávnění. Systém AMFI odmítl spuštění s `No matching profile found` (-413). Kopie aplikace po přenosu přes iCloud navíc obsahovala FinderInfo, které neprošlo strict kontrolou podpisu. Kontrola codesign na původním lokálním balíčku sama o sobě spuštění nepotvrdila.

Entitlements nyní obsahuje pouze JIT pro .NET a oprávnění automatizace Apple Events, které Launcher používá. LSUIElement zůstává správně v Info.plist. Nová verze je 1.0.0.31; funkční změna gateway z 1.0.0.30 zůstává zachována.

Ověřeno: Release publish, podpis Developer ID, strict kontrola podpisu, spuštění přes systém z lokálního .app (proces zůstává běžet bez nového odmítnutí AMFI). Nástroj pro čtení UI vracel timeout; nabídka nebyla vizuálně ověřena. Živé RDP není součástí tohoto testu.

Pro předání přes iCloud používat ZIP, rozbalovat mimo iCloud. Nepovažovat samotný codesign --verify za důkaz spuštění. Kontrolovat také Gatekeeper po notarizaci a spouštění přes LaunchServices.

## Distribuční ověření
Apple notarizace: Accepted, submission `ce6f721f-a900-4e47-805e-ee36b693442c`. Stapler uspěl. Gatekeeper: `accepted`, `source=Notarized Developer ID`. Stejnou kontrolou prošla i nainstalovaná kopie `/Applications/ZasLauncher.app`.

Předchozí kopie je v `/Users/jiriz/ZasLauncher-builds/1.0.0.31/previous-installed-1.0.0.30.app`. Distribuční ZIP se staplovaným balíčkem je v iCloud/Stahování/ZasLauncher-1.0.0.31/ZasLauncher-1.0.0.31.zip.
