#set document(
    author: "Jakub Novák",
    title: "BitAvalanche dokumentace",
)

#set page("a4")
#set figure(supplement: "Figura")
#set text(lang: "cz")
#set image(width: 80%)
#show title: set align(center)
#show link: set text(fill: blue)
#show link: underline

#title()

#outline(title: [Obsah])

#pagebreak()

= Instalace a spuštění

+ Obdržte nejnovější verzi programu z této adresy: https://github.com/novakjak/BitAvalanche/releases/latest.
  - Pro Microsoft Windows stáhněte `BitAvalanche-win64.zip`.
+ Extrahujte staženou složku.
+ Spusťte program `BitAvalanche.exe`.

#figure(
    image("default_window.png"),
    caption: [Spuštěný program],
)

#pagebreak()

= Stažení torrentu

+ Na internetu najděte a stáhněte `.torrent` soubor.
+ Klikněte na tlačítko _Add torrent_ v BitAvalanche.
+ Najděte a vyberte stažený `.torrent` soubor.
+ Vyčkejte než se torrent stáhne.


#figure(
    image("highlight_add_torrent.png"),
    caption: [Zvýrazněné tlačítko _Add Torrent_.],
)

#figure(
    image("open_file_dialog.png"),
    caption: [Vybraný soubor `.torrent`.],
)

#figure(
    image("downloading_torrent.png"),
    caption: [Stahující se torrent.],
)

#pagebreak()

= Zobrazení dodatečných informací o torrentu

+ Vyberte torrent kliknutím myši.
+ Rozbalte nabídku na spodní straně okna.

#figure(
    image("additional_info.png"),
    caption: [Ikona pro rozbalení nabídky.],
)

#figure(
    image("additional_info_open.png"),
    caption: [Rozbalená nabídka dodatečných informací.],
)
