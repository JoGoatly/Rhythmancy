# Maincharacter und Notenblätter

## Charakterwerte bearbeiten

Auf dem Player gibt es die Komponente `MainCharacter`. Unter `Base Stats` werden HP, Bewegungstempo, Aura-Schaden/-Radius, Mana, Mana-Regeneration, Projektil-Freischaltung/-Schaden/-Anzahl und Münzmagnet eingestellt. Die Einrichtung übernimmt die bisherigen Werte jeder Szene und des Player-Prefabs, ohne sie zu vereinheitlichen.

`MainCharacter.Stats` berechnet Grundwerte plus die dauerhaften Verbesserungen des aktiven Spielstands. `RefreshStats()` wendet sie erneut an, ohne Boni mehrfach zu addieren. Mehr maximale HP/Mana füllen den aktuellen Vorrat nicht automatisch auf; weniger Maximum begrenzt den Vorrat entsprechend. `PlayerController` führt weiterhin Bewegung, Schaden, Heilung, Münzen und Effekte aus. `CardAbilityController` verwaltet Mana, Kartenauswahl und zeitlich begrenzte Blatteffekte.

Die alten serialisierten Felder in PlayerController/CardAbilityController bleiben als Migrationsdaten und Fallback für noch nicht umgestellte Spieler erhalten. Bei einem eingerichteten Spieler sind `MainCharacter.Base Stats` und die Notenblatt-Assets maßgeblich. Alte Projektil-/Magnet-Upgrades wirken weiterhin zusätzlich; Projektile bleiben ohne Freischaltung deaktiviert.

## Notenblätter bearbeiten und ergänzen

Die drei Startblätter liegen unter `Assets/Resources/StarterATK.asset`, `StarterDEF.asset`, `StarterSPC.asset`. Ihre Effekte entsprechen dem bisherigen Spiel. Musik ist zunächst nicht gesetzt, damit die vorhandene Playlist unverändert spielt.

Über `Create > Rhythmancy > Notenblatt` ein neues Asset erstellen und anschließend in das Array `Sheets` von `Assets/Resources/MusicSheetCatalog.asset` aufnehmen. Dieser Katalog ist am MainCharacter zugewiesen.

Pro Blatt:

- `Id`: eindeutige, dauerhaft stabile Kennung für Spielstände.
- `Display Name`, `Description`, `Artwork`: Name, Beschreibung und Kartenbild.
- `Sheet Class`: ATK, DEF oder SPC. Es gibt einen ausgerüsteten Slot pro Klasse.
- `Gold Price`: Kaufpreis in Gold. `Available From Start` ist für die kostenlosen Startblätter aktiv.
- `Selected Music`: optionaler AudioClip während der Aktivierung. Leeres Feld lässt die normale Musik laufen. Ablegen, Wechsel oder Ende des SPC-Pulses stellt die vorherige Musik samt Abspielposition wieder her. Lautstärke und Musikpause verwenden den bestehenden AudioManager.
- ATK/DEF: Mana-Verbrauch pro Sekunde, Aura-Schaden/-Radius, Schadensreduktion, Heilmenge/-intervall. Auch Mischwirkungen sind möglich, etwa ein ATK-Blatt mit geringer Heilung.
- SPC: Radius, Rückstoß, Betäubungsdauer, Farbe, Effektdauer und Auswahlpuls. SPC bleibt eine einmalige Fähigkeit bei vollem Mana und verbraucht alles; die ATK/DEF-Dauereffekte gelten nicht für SPC.

Blätter besitzen heißt noch nicht, dass sie aktiv sind: Kaufen → ausrüsten → im Spiel die entsprechende Karte wählen. ATK und DEF sind weiterhin gegenseitig exklusiv. Die Startblätter sind auch in alten Spielständen verfügbar. Unbekannte, gelöschte oder nicht besessene ausgerüstete IDs fallen auf das Startblatt der Klasse zurück.

## Shop-Grundlage – noch keine neue Shop-Oberfläche

Die bisherigen Münzen heißen im neuen API `GameSession.Gold`; das gespeicherte Feld `coinCount` bleibt aus Kompatibilitätsgründen bestehen. `MusicMedallions` ist eine separate Währung und startet bei null. Es gibt noch keine neu erfundene Quelle für Medaillons und keine festgelegten Stat-Upgrade-Preise.

- Notenblatt kaufen: `GameSession.TryBuyMusicSheet(sheet)` bezahlt ausschließlich mit Gold. Fehlendes Gold, doppelter Besitz und ungültige Preise werden abgelehnt.
- Ausrüsten: `MainCharacter.TryEquipSheet(id)` prüft Katalog und Besitz und aktualisiert die Karten. Dabei wird die bisher aktive Karte abgelegt.
- Stat verbessern: `MainCharacter.TryImproveStat(stat, amount, medallionCost)` bezahlt ausschließlich mit Musik-Medaillons und wendet die Verbesserung sofort an. Der spätere Shop liefert die konfigurierten Preise und Beträge; ganzzahlige Stats akzeptieren keine Bruchteile.
- Belohnungen: bestehendes `GameSession.AddCoins(amount)` und neues `AddMusicMedallions(amount)`.

Käufe/Änderungen gelten zunächst für die laufende GameSession. Der Shop muss danach den bestehenden Speichervorgang aufrufen, wenn ein Kauf sofort auf Festplatte gesichert werden soll. Das vorhandene Pause-Speichern nimmt die neuen Daten bereits mit. Der alte UpgradeController und seine bisherigen Angebote bleiben bis zur Shop-Überarbeitung bestehen.

## Speicherung und Prüfung

Pro Slot gespeichert: Gold, Medaillons, dauerhafte Stat-Boni, gekaufte Notenblatt-IDs und drei ausgerüstete IDs. Alte JSON-Spielstände ohne diese Felder bleiben lesbar. Bei einem neuen Spiel werden die neuen Fortschrittsdaten zurückgesetzt. Aktuelles Mana und momentan aktivierte Karten werden wie bisher beim Szenenstart neu initialisiert.

`Tools > Rhythmancy > Configure Main Character` erstellt fehlende Start-Assets und ergänzt die Komponente im Player-Prefab sowie Tutorial, Tutorial Level und SafeZone. Vorhandene neue Grundwerte und Assets werden beim erneuten Aufruf beibehalten. Der Lauf prüft getrennte Währungen, doppelte Käufe, Stat-Käufe, Ausrüsten, JSON-Roundtrip, Slotwechsel, alte Spielstände und wiederholte Stat-Aktualisierung mit temporärem Sessionzustand, ohne echte Save-Dateien zu überschreiben.
