# Rhythmancy – Menüsystem

## Öffnen und testen

`Assets/Scenes/Menü.unity` öffnen und Play drücken. Die bisherige `Assets/Scenes/MainMenu.unity` wurde auf Wunsch entfernt. `Menü` ist die neue Startszene in den Build Settings. „Spielen“ wählt einen der drei Slots und startet die vorhandene Szene `Tutorial Level`. Ein belegter oder defekter Slot wird nur nach Bestätigung überschrieben.

Escape öffnet im Gameplay die separate Szene `PauseMenu` additiv. „Fortsetzen“ entlädt nur diese Szene; Spieler, Position und Gameplay-Szene bleiben bestehen. Das Pausenmenü besitzt keinen Spielen-Button. Escape bleibt auch bei einer umbelegten Pause-Taste verfügbar.

Editor-Werkzeuge:

- `Tools > Rhythmancy > Create Menu System`: legt die Menüs und Prefabs an. Bestehende Menühierarchien werden wiederverwendet; AudioManager und LevelManager sind direkt in Menü.unity konfiguriert; das Setup benötigt keine alte Menüszene.
- `Tools > Rhythmancy > Validate Menu System`: prüft Szenen, Panels, Buttons und Build-Einträge.
- `Tools > Rhythmancy > Run Menu Smoke Tests (Play Mode)`: in Menü.unity im Play-Modus starten. Testet echte Button-Aufrufe, Einstellungen/Rebinding, Dialoge, alle drei Slots, Szenenwechsel, Escape/Pause/Fortsetzen und Weiter. Verwendet ausschließlich eigene JSON-Dateien unter `/tmp/RhythmancyMenuTests-*`; Lautstärken/Tasten werden danach wiederhergestellt. Ergebnis: `Captures/menu-tests.txt`. Während des automatischen Tests nicht selbst klicken.

## Objekte und Dateien

- `Menü.unity`: `MainMenuCanvas`, Hintergrund, getrennte Noten/Notenlinien, Logo, Hauptmenü, NewGamePanel, SettingsPanel, SaveLoadPanel, AchievementsPanel, HelpPanel mit Scroll View, CreditsPanel, ConfirmationDialog, TransitionOverlay und StatusMessage. Dazu EventSystem, konfigurierte Kopien des vorhandenen AudioManager und LevelManager.
- `PauseMenu.unity`: `PauseMenuCanvas` mit eigenen Fortsetzen-/Speichern-/Laden-/Einstellungen-/Hauptmenü-/Beenden-Aktionen, Untermenüs und Dialog.
- `Assets/Prefabs/MainMenuCanvas.prefab`, `PauseMenuCanvas.prefab`, `MenuButtonPlaceholder.prefab`: wiederverwendbare UI-Vorlagen. Die Szenen sind unabhängig editierbar; spätere Änderungen an Szenen und Prefabs werden nicht automatisch gegenseitig überschrieben.
- `RhythmancyMenuController`: Hauptmenü, Navigation und UI-Aufbau; `AdditivePauseMenuController`: Pausenaktionen; `PauseSceneLauncher`: einmaliger globaler Escape-Listener.
- `RhythmancySettings`: persistente Audio-/Tastenbelegung. `MenuButtonEffect`, `MenuSliderValue`, `MenuSpritePlaceholder`, `MenuStatus`: kleine UI-Komponenten.
- `SaveSystem`: validierte JSON-Dateien, drei Slots, atomisches Ersetzen und Fehlerstatus. `MenuSaveService`: gemeinsamer Ablauf für Speichern/Laden aus Menüs. `GameSession`: Fortschritt zwischen Szenen.
- `AchievementManager`, `AchievementCatalog`, `Assets/Resources/RhythmancyAchievements.asset`: Definitionen, Icons und unabhängig gespeicherter globaler Fortschritt.
- `Assets/Editor/RhythmancyMenuSetup.cs` und `RhythmancyMenuSmokeTests.cs`: Einrichtung und wiederholbare Tests.

Die Button-Aktionen werden genau einmal in `Awake` verbunden. Leere persistente OnClick-Listen im Inspector sind deshalb beabsichtigt. Namen der funktionalen Buttons/Panels beim Gestalten beibehalten.

## Eigene Grafiken einsetzen

Bilder als `Sprite (2D and UI)` importieren. Im Canvas das jeweilige Objekt auswählen und beim `Image` unter `Source Image` das Sprite zuweisen. Für unverfälschte Farben `Color` auf Weiß stellen; bei skalierbaren Buttonrahmen Sprite-Borders im Sprite Editor und `Image Type = Sliced` verwenden.

- Hintergrund: `MainMenuCanvas/Background`.
- Logo: `MainMenuCanvas/MainMenuPanel/Logo`. `MenuSpritePlaceholder` aktiviert das Bild und blendet den Text automatisch aus, sobald ein Sprite zugewiesen ist.
- Buttons: das Image des jeweiligen Button-Objekts. Die untergeordnete TMP-Beschriftung bleibt unabhängig editierbar.
- Noten: `Decorations/MusicNotes/MusicNote_01` usw. Jede Note ist einzeln positionierbar und hat den automatischen Sprite/Text-Wechsel.
- Notenlinien: einzelne Images unter `Decorations/MusicLines`.
- Untermenüs: Image des jeweiligen Panels.
- Erfolgsicons: im Asset `Assets/Resources/RhythmancyAchievements.asset`; optional direkt an den Platzhalter-Images der Erfolgszeilen.

CanvasScaler nutzt 1920×1080 und `Expand`, damit die Oberfläche auch bei schmaleren Seitenverhältnissen vollständig in die Anzeige passt. Größe und Position von Dekorationen sind über RectTransform editierbar. Die Menüs und Button-Animationen laufen mit unskalierter Zeit.

## Speichern und Gameplay-Anbindung

Dateien: `Application.persistentDataPath/save_0.json` bis `save_2.json`. Im Hauptmenü wird ausschließlich geladen/gelöscht bzw. ein neuer Slot angelegt. Aktives Speichern erfolgt im Pausenmenü. Fehlgeschlagene Schreib-/Löschvorgänge erscheinen als Statusmeldung; „Speichern und verlassen“ verlässt das Spiel bei einem Schreibfehler nicht.

Gespeichert und wieder geladen werden Szene, Spielerposition/Blickrichtung/HP, Münzen, die vier vorhandenen Upgrade-Stufen, abgeschlossene Level, Kapitel-/Levelauswahl und Spielzeit. Welle, Gegnerzahl und Punkte sind ebenfalls Datenfelder. Gegner-Tode erhöhen die Gegnerzahl und sind mit den beiden Gegner-Erfolgen verbunden. Mehrfache Treffer auf einen bereits besiegten Gegner zählen nicht mehrfach.

**Grenzen der vorhandenen Spielsysteme:** Es gibt noch keinen eigenständigen Wellenmanager. `GameSession.CompleteWave(int completedWave)` ist die definierte Anbindung: aktualisiert den Slot-Fortschritt und meldet die überlebte Welle an die Erfolge. Bis ein tatsächlicher Wellenablauf diese Methode aufruft, bleiben Wellenfortschritt und die beiden Wellen-Erfolge entsprechend unverändert. Zusätzliche Erfolge im Catalog mit stabiler eindeutiger ID anlegen und über `AchievementManager.Instance.AddProgress(id, amount)` bedienen.

Ein gespeichertes Spiel ist ein Spieler-/Fortschritts-Checkpoint, kein vollständiger Welt-Snapshot: dynamische Gegner, Projektile, Kartentimer, zufällig erzeugte Umgebung und der genaue Tutorial-Schritt werden beim Laden neu aufgebaut. Für deren exakte Wiederherstellung müssen die jeweiligen Gameplay-Systeme um Serialisierung ergänzt werden. „Fortsetzen“ aus der Pause benötigt dies nicht und erhält den laufenden Zustand unverändert.

Ältere JSON-Spielstände ohne Szene werden kompatibel über SafeZone geladen. Defekte Dateien werden nicht als gültige Continue-Ziele behandelt, bleiben aber im Slot sichtbar und können nach Bestätigung gelöscht/überschrieben werden.

## Audio und Steuerung

Audio verwendet den vorhandenen AudioManager einschließlich der konfigurierten Musikclips. Es existiert kein AudioMixer im Projekt; daher regelt der bestehende Manager seine bekannte Musikquelle und registrierten SFX-Quellen direkt, ohne ein zweites Audiosystem einzuführen. Neue dynamische AudioSources wie bisher über `AudioManager.RegisterAudioSource` anmelden. Lautstärkeänderungen werden sofort angewendet und per PlayerPrefs gespeichert. Keine Grafikeinstellungen.

Bewegung, Fähigkeiten, Interaktion und Pause verwenden die vorhandenen Input-System-Tasten mit gespeicherter Umbelegung. Pfeiltasten bleiben als zusätzliche Bewegung erhalten. Die Notenspur löst Zeichen layoutabhängig auf und ist szenenabhängig konfiguriert: Tutorial zunächst U, Spieler-Prefab Z/U/I/O/H/J/K/L. Diese Rhythmustasten werden über `BeatLaneController.laneKeys` im Inspector gepflegt und sind im Menü nicht umbelegbar. Das Hilfemenü erklärt dies. Das vorhandene Tutorial enthält weiterhin einzelne feste Text-/Fortschrittsabfragen für WASD; diese wurden nicht neu geschrieben.

## Erhaltung bestehender Inhalte

Das alte Hauptmenü wurde am 10. November durch die eigenständige Szene Menü ersetzt. Bestehende Gameplay-Szenen bleiben erhalten. Gemeinsame Skripte wurden nur für Speicherung, Fortschrittsereignisse und das Unterdrücken von Angriffen/Interaktionen während der Pause angepasst. Der vorhandene ältere PauseMenuController bleibt für seine Game-over-/Musikfunktionen bestehen; das neue Escape-Menü wird vom PauseSceneLauncher geöffnet.

## Verifizierter Stand (3. November 2026)

Unity kompiliert die Skripte ohne Fehler. Der vollständige Play-Mode-Smoke-Test ist nach den Layoutkorrekturen erfolgreich: alle Untermenüs, Rebinding/Reset, Slotstatus, defekte JSON-Datei, Löschen/Abbrechen, Neuspiel/Überschreibschutz, Weiter, Laden in die gespeicherte Szene, drei Slots, Escape/Pause und Fortsetzen mit unverändertem Spielerobjekt. Hauptmenü, Einstellungen, Hilfe, Erfolge und Credits wurden zusätzlich über Game-View-Screenshots visuell geprüft. Ein simulierter Dateisystemfehler meldet den Fehler an die UI zurück; Lautstärke 37 % wurde sowohl persistent als auch an der laufenden Musikquelle bestätigt. Wiederholtes Setup erzeugt keine zusätzlichen UI-Objekte. Das alte MainMenu.unity hat keinen Diff.


## Zusammenführung am 10. November 2026

Das gesicherte Menü wurde mit Commit `4c6348d` zusammengeführt. Menü.unity ist
Startszene; PauseMenu.unity ist für additive Pause registriert. Rückkehrziele im
Spieler-Prefab und Tutorial sowie die Standardwerte in Audio-/Pause-Controller
verweisen auf Menü. MainMenu.unity samt .meta wurde auf Wunsch gelöscht.

Die neue Gegner-Todesanimation, Bodenverlangsamung, verzögerter Levelaufbau,
Umgebungskonfiguration und die verbesserte Intro-Sequenz bleiben erhalten.
Der LevelManager in Menü.unity übernimmt die aktuelle Konfiguration des Kollegen.
Ein darin noch vorhandener Verweis auf den gelöschten Stein wurde auf Stein1
aktualisiert. Additive Pause erzeugt keine Umgebung neu und setzt die
Bodenverlangsamung nicht zurück. Gegnerfortschritt wird vor der Todesanimation
nur einmal gemeldet.

Prüfung: Runtime- und Editor-Skripte mit Unitys Roslyn-Compiler und den vorhandenen
Unity-Referenzen erfolgreich kompiliert. Menü-/Pause-Szenen und Canvas-Prefabs auf
fehlende Asset-GUIDs geprüft. Kein neuer Play-Mode-/Sichttest: Ein separater
Unity-Batchlauf wurde durch die bereits geöffnete Projektinstanz blockiert.
Der PASS-Bericht unter Captures stammt weiterhin aus der früheren Version.
Zum interaktiven Prüfen Menü.unity öffnen, Play starten und
Tools > Rhythmancy > Run Menu Smoke Tests (Play Mode) ausführen.
