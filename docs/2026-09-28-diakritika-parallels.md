# ZasLauncher 1.0.0.28 – diakritika výsledku uspořádání

Výstup Windows PowerShell přes Parallels poškodil české znaky v souhrnu uspořádání oken.
Výsledek se nyní přenáší jako Base64 bajtů UTF-8 a na Macu se dekóduje před zobrazením.
Tím nezávisí na kódové stránce konzole Windows. Algoritmus umístění oken je beze změny.

Ověřeno skutečným dry-run přes Parallels: 14 oken, 0 chyb, přesně zachované řetězce
„bez změn“ a „široký monitor“. Okna při ověření nebyla přesouvána.
Mac arm64 publish a ověření podpisu prošly. ZIP je v iCloud/Stahování.
