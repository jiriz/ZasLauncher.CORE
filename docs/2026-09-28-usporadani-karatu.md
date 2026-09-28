# Uspořádání oken KARATu – ZasLauncher 1.0.0.27

Nová nabídka **Uspořádat okna IS Karat** je přímo nad **Ukončit všechny IS Karat**.
Vyhledá viditelná hlavní okna procesů `ISKarat.Loader.Win`. Dialogy ponechá beze změn.
Každé okno zůstane na současném monitoru. Běžné monitory maximalizují okna.
Na ultraširokém monitoru šířky přesně 5120 fyzických pixelů (poměr stran větší než 2:1)
se okno obnoví a nastaví do oblasti od x=1500 po x=5120 vůči levému okraji tohoto monitoru.
Výška využije pracovní plochu; hlavní panel Windows zůstane přístupný. Běžný 5K monitor 5120×2880 se maximalizuje.

Na Windows se upravuje aktuální desktop. Na macOS se přes Parallels provede stejná operace
pod přihlášeným uživatelem ve všech běžících Windows VM. Nespravuje okna v samostatných vzdálených RDP relacích.
Výsledek vypíše počty nalezených oken, předaných požadavků a chyb pro každou VM.
Počty předaných požadavků neznamenají následné měření skutečné polohy.

## Ověření

- Šest automatických testů souřadnic: běžný monitor, 5K 16:9, ultrawide, záporný počátek,
  druhý monitor a pracovní plocha s hlavním panelem, jiná šířka ultrawide.
- Skutečné spuštění C# modulu ve Windows PowerShell 5.1 přes Parallels, pouze dry-run:
  14 hlavních oken, všechna na širokém monitoru, 0 chyb. Okna se nepřesouvala.
- Původní dlouhý příkaz překročil limit transportu Parallels; předává se proto komprimovaný zdroj
  v krátkém PowerShell bootstrapu. Ověřeno skutečným dry-run spuštěním.
- Release publish macOS arm64 a Windows x64 prošel; macOS podpis ověřen.
- Přenosný OpenSSL provider: negativní kontrola, MD4 a RC4 prošly.
- Skutečné přesunutí oken z nabídky zbývá ověřit uživatelem; vývojový test nezasahoval do otevřených oken.

## Distribuce

`ZasLauncher-1.0.0.27-mac-arm64.zip` a `ZasLauncher-1.0.0.27-win-x64.zip` jsou v iCloud/Stahování.
Mac aplikace: `/Users/jiriz/ZasLauncher-builds/1.0.0.27/ZasLauncher.app`.
