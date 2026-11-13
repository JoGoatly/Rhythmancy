# Gezeichnete Spielansicht

Die Originalbilder aus `~/Downloads/2D-Assets` liegen im Projekt unter `Assets/Art/Handdrawn`. Unity benötigt den Downloads-Ordner nach dem Import nicht mehr. Die transparenten PNGs der Map werden unverändert verwendet. `Drawing.mat` zeichnet die transparenten Konturen mit Tiefentest, damit Figuren vor und hinter Bäumen korrekt verdeckt werden.

`Tools > Rhythmancy > Build Handdrawn Gameplay` importiert die Bilder und speichert das Player-Prefab sowie Tutorial, Tutorial Level und SafeZone. Vor dem erneuten Aufbau offene Szenen speichern. Der Aufbau ersetzt die erzeugte Map und HUD-Anordnung; individuell darin vorgenommene Layoutänderungen werden überschrieben. Szeneninstanzen des Players werden dafür entpackt, sodass die Anordnung direkt in jeder Szene bearbeitbar bleibt.

- Referenzauflösung des HUD: 1920 × 1080, proportional skalierend.
- Zufallspool: H, I, Z, U, J, K, L, O. Vier sichtbare Kacheln laufen von rechts nach links. Wiederholungen sind erlaubt. `Visible Note Count` und BPM bleiben im BeatLaneController einstellbar.
- Notenblätter: 1/2/3 oder Mausklick (bzw. konfigurierte Tasten). Die aktive Karte kommt nach vorn, steigt auf und vergrößert sich. Die Seitenkarten behalten ihren Fächerwinkel gemäß Bildvorlage. Die Kartenflächen bleiben ohne Beschreibungstext. Spezial zeigt einen kurzen Auswahlpuls; die bisherige Mana- und Fähigkeitenlogik bleibt maßgeblich.
- Oben links: vorhandener Münzzähler. Oben mittig: aktuelle Levelnummer und verstrichene Szenenzeit (pausiert mit dem Spiel). Das vorhandene Spiel hat keinen separaten Wellenzähler.
- Unten mittig: Instrument, Lebens- und Manabalken.
- Lebens- und Manabalken verwenden jeweils 15 separate Originalbilder aus `Musikobjekte/Musikobjekte` (leer bis voll). `IllustratedResourceBar` wählt die passende Stufe aus dem echten Slider-Wert; 100 % nutzt Rechts-03-Gruen bzw. Rechts-04-Blau. Niedrige HP verwenden die roten Varianten. Die Sprite-Umrisse werden nicht abgeschnitten.
- Map: 72 × 54 Einheiten, 60 Bodenkacheln, zweireihiger Waldsaum, Kollisionsgrenzen, zentrale Lichtung und zwei breite Laufachsen. Alle sieben transparenten Map-Motive werden genutzt. Aufbau mit festem Seed ist reproduzierbar. `PaperGround.shader` überblendet Kachelränder mit versetzten Abtastungen derselben Zeichnung; zusätzliche kollisionsfreie Busch-/Stein-/Blumengruppen lockern die Übergänge auf.
- Der LevelManager nutzt weiterhin die Bodenabmessungen zum Spawnen von Gegnern; bei vorhandener `Handdrawn Forest Map` erzeugt er keine zweite Vegetationsschicht.

`Tools > Rhythmancy > Validate Handdrawn Gameplay` prüft die gespeicherten Szenen und die Zuordnung der acht Buchstaben.

`HanddrawnGameplaySetup.CapturePreview` erstellt zusätzlich `Captures/gameplay-hud.png` und die drei Auswahlansichten `gameplay-def.png`, `gameplay-atk.png`, `gameplay-spc.png`. Die Editor-Prüfung kontrolliert Vordergrund, Vergrößerung, Fächerwinkel und alle 15 Balkenfüllstufen einschließlich 50 % und 100 %. SPC wird mit 99 % Mana (Rückmeldung, kein Verbrauch) und mit 100 % (Schockwelle und vollständiger Manaverbrauch) aufgerufen. Sie ersetzt keinen vollständigen Play-Mode-Test.

SPC benötigt weiterhin volles Mana. Bei zu wenig Mana erscheint der aktuelle Prozentwert mit dem Hinweis, ATK/DEF zum Regenerieren abzuwählen. Erfolgreiche Auslösung meldet „Schockwelle!“.
