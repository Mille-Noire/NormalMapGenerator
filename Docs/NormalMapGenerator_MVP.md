# MVP: NormalMapGenerator (C# / WPF)

## Ziel

Ein lokales Desktop-Tool zur Erstellung von Normal Maps aus Heightmaps.

Der erste MVP soll bewusst klein, stabil und nachvollziehbar bleiben:

**Heightmap laden → Normal Map berechnen → Ergebnis anzeigen → PNG exportieren**

Das Tool ist nicht webbasiert, sondern läuft als klassische Windows-Desktop-Anwendung mit C# und WPF.

---

## Technologiestack

- Sprache: C#
- Framework: .NET 8 oder neuer
- UI: WPF
- Zielplattform MVP: Windows
- Architektur: einfache Trennung zwischen UI und Bildverarbeitungslogik
- Verarbeitung: zunächst CPU-basiert
- Exportformat MVP: PNG

---

## Kernentscheidung

Der MVP konzentriert sich ausschließlich auf die Erstellung einer Normal Map aus einer vorhandenen Heightmap.

Weitere Maps wie Ambient Occlusion, Displacement, Roughness oder Specular werden bewusst nicht im ersten Schritt umgesetzt.

Der technische Kern soll zuerst sauber funktionieren, bevor zusätzliche Map-Typen, Presets oder eine 3D-Vorschau ergänzt werden.

---

## Zielgruppe

Das Tool richtet sich zunächst an den Eigenbedarf im Projekt.

Es soll dabei helfen, aus selbst erstellten oder bearbeiteten Heightmaps schnell brauchbare Normal Maps für Unity-Materialien zu erzeugen.

---

## Projektziel

Der NormalMapGenerator soll im MVP folgende Aufgabe zuverlässig erfüllen:

Ein Nutzer lädt eine Bilddatei, das Tool interpretiert diese Datei als Heightmap, erzeugt daraus eine Normal Map, zeigt das Ergebnis an und erlaubt den Export als PNG-Datei.

---

## MVP-Funktionsumfang

### [P0] Heightmap laden

Der Nutzer kann eine lokale Bilddatei als Ausgangsbasis laden.

Anforderungen:

- Laden über einen Button.
- Unterstützte Formate:
  - PNG
  - JPG
  - JPEG
- Das geladene Bild wird als Source Preview angezeigt.
- Farbige Bilder werden intern als Graustufen-Heightmap interpretiert.
- Die Originaldatei wird nicht verändert.
- Bei ungültigen oder beschädigten Dateien wird eine einfache Fehlermeldung angezeigt.

Akzeptanzkriterien:

- Eine PNG-Datei kann geladen werden.
- Eine JPG-Datei kann geladen werden.
- Das geladene Bild wird sichtbar in der UI angezeigt.
- Nach dem Laden wird automatisch eine Normal Map erzeugt.

---

### [P0] Heightmap als Graustufenwerte interpretieren

Das geladene Bild wird intern in Höhenwerte umgewandelt.

Anforderungen:

- Helle Pixel werden als hohe Bereiche interpretiert.
- Dunkle Pixel werden als niedrige Bereiche interpretiert.
- Farbige Eingaben werden in Graustufenwerte umgerechnet.
- Die Umrechnung muss reproduzierbar und stabil sein.
- Alphakanäle werden im MVP ignoriert.

Akzeptanzkriterien:

- Schwarz erzeugt niedrige Höhenwerte.
- Weiß erzeugt hohe Höhenwerte.
- Farbige Bilder führen zu einer plausiblen Normal Map.
- Transparente Bereiche verursachen keine Fehler.

---

### [P0] Normal Map generieren

Aus der Heightmap wird eine Tangent-Space Normal Map erzeugt.

Anforderungen:

- Die Berechnung erfolgt lokal und CPU-basiert.
- Für jeden Pixel wird aus den benachbarten Höhenwerten eine Normalenrichtung berechnet.
- Die Normalenrichtung wird in RGB-Farbwerte umgewandelt.
- Das Ergebnis ist eine klassische blau-violette Normal Map.
- Die Berechnung funktioniert mindestens zuverlässig für 512x512 und 1024x1024 Texturen.
- Die Berechnung darf im MVP bei größeren Texturen kurz ruckeln, soll aber nicht abstürzen.

Akzeptanzkriterien:

- Nach dem Laden einer Heightmap wird eine sichtbare Normal Map erzeugt.
- Kanten und Höhenunterschiede werden in der Normal Map erkennbar.
- Flache Bereiche erscheinen überwiegend neutral-blau.
- Die Ausgabe kann direkt als Textur weiterverwendet werden.

---

### [P0] Strength einstellen

Der Nutzer kann die Stärke der erzeugten Normal Map verändern.

Anforderungen:

- Die UI enthält einen Strength-Regler.
- Der Strength-Wert beeinflusst sichtbar die Intensität der Normal Map.
- Niedrige Werte erzeugen eine flachere Normal Map.
- Hohe Werte erzeugen stärkere Normalenunterschiede.
- Bei Änderung des Reglers wird die Normal Map neu berechnet.
- Der aktuelle Wert wird in der UI angezeigt.

Empfohlene MVP-Werte:

- Minimum: 0
- Maximum: 20
- Standardwert: 5

Akzeptanzkriterien:

- Der Strength-Regler verändert das Ergebnis sichtbar.
- Der Standardwert erzeugt ein brauchbares Ausgangsergebnis.
- Extremwerte führen nicht zu Abstürzen.
- Der aktuell eingestellte Wert ist für den Nutzer sichtbar.

---

### [P0] Invert X / Red Channel

Der Nutzer kann die X-Richtung der Normal Map invertieren.

Anforderungen:

- Die UI enthält eine Option für `Invert X`.
- Die Option invertiert den Red Channel der Normal Map.
- Die Änderung wird sofort in der Vorschau sichtbar.
- Die Einstellung wird beim Export berücksichtigt.

Akzeptanzkriterien:

- Aktivieren von `Invert X` verändert die Normal Map sichtbar.
- Deaktivieren stellt die vorherige X-Richtung wieder her.
- Der exportierte PNG-Output entspricht der aktuellen Einstellung.

---

### [P0] Invert Y / Green Channel

Der Nutzer kann die Y-Richtung der Normal Map invertieren.

Anforderungen:

- Die UI enthält eine Option für `Invert Y`.
- Die Option invertiert den Green Channel der Normal Map.
- Die Änderung wird sofort in der Vorschau sichtbar.
- Die Einstellung wird beim Export berücksichtigt.
- Diese Option ist wichtig für unterschiedliche Engine-Konventionen, insbesondere Unity-Workflows.

Empfohlene MVP-Entscheidung:

- `Invert Y` ist standardmäßig aktiviert oder gut sichtbar verfügbar.
- Die konkrete Standardbelegung kann später anhand von Unity-Tests angepasst werden.

Akzeptanzkriterien:

- Aktivieren von `Invert Y` verändert die Normal Map sichtbar.
- Deaktivieren stellt die vorherige Y-Richtung wieder her.
- Der exportierte PNG-Output entspricht der aktuellen Einstellung.

---

### [P0] Edge Handling

Ränder der Textur müssen stabil verarbeitet werden.

MVP-Entscheidung:

- Im ersten MVP wird Clamp-Handling verwendet.

Verhalten:

- Bei Pixeln am Bildrand werden fehlende Nachbarpixel durch den nächstgültigen Randpixel ersetzt.
- Dadurch entstehen keine Zugriffe außerhalb des Bildbereichs.
- Die Berechnung bleibt stabil.

Nicht Teil des MVP:

- Wrap Mode für nahtlose Texturen.
- Mirror Mode.
- Tile Preview.

Akzeptanzkriterien:

- Texturränder verursachen keine Fehler.
- Die Normal Map wird auch an den Bildrändern vollständig erzeugt.
- Es gibt keine leeren oder transparenten Randbereiche im Ergebnis.

---

### [P0] Source Preview anzeigen

Das geladene Ausgangsbild wird in der Anwendung angezeigt.

Anforderungen:

- Die UI enthält einen Bereich für die Source Preview.
- Das Bild wird proportional skaliert angezeigt.
- Die Darstellung dient nur der Kontrolle.
- Das Bild selbst wird im MVP nicht direkt bearbeitet.

Akzeptanzkriterien:

- Das geladene Bild ist sichtbar.
- Hoch- und Querformate werden korrekt skaliert.
- Die Vorschau verzerrt das Bild nicht.

---

### [P0] Normal Map Preview anzeigen

Die generierte Normal Map wird in der Anwendung angezeigt.

Anforderungen:

- Die UI enthält einen Bereich für die Normal Map Preview.
- Die Preview aktualisiert sich nach jeder relevanten Einstellungsänderung.
- Die Darstellung wird proportional skaliert.
- Die Preview zeigt genau den Stand, der beim Export gespeichert wird.

Akzeptanzkriterien:

- Die Normal Map ist nach dem Laden sichtbar.
- Änderungen an Strength, Invert X und Invert Y aktualisieren die Preview.
- Das angezeigte Ergebnis entspricht dem exportierten Bild.

---

### [P0] Normal Map als PNG exportieren

Der Nutzer kann die aktuell erzeugte Normal Map als PNG speichern.

Anforderungen:

- Die UI enthält einen Export-Button.
- Der Export-Button ist deaktiviert, solange keine Normal Map erzeugt wurde.
- Beim Export öffnet sich ein Speicher-Dialog.
- Das Exportformat ist PNG.
- Der vorgeschlagene Dateiname orientiert sich am Originalnamen.
- Die exportierte Datei enthält die aktuell angezeigte Normal Map.

Empfohlener Dateiname:

`OriginalName_normal.png`

Akzeptanzkriterien:

- Die Normal Map kann als PNG gespeichert werden.
- Die Datei lässt sich danach normal öffnen.
- Die Datei enthält die aktuellen Einstellungen.
- Der Export überschreibt keine Datei ohne normale Systemabfrage.

---

## UI-Aufbau MVP

Die Oberfläche bleibt schlicht und funktional.

### Oberer Bereich

Enthält die wichtigsten Bedienelemente:

- Button: `Load Heightmap`
- Strength-Regler
- Anzeige des Strength-Werts
- Checkbox: `Invert X`
- Checkbox: `Invert Y`

### Mittlerer Bereich

Enthält zwei Vorschaufenster nebeneinander:

- Links: Source Heightmap
- Rechts: Generated Normal Map

### Unterer Bereich

Enthält den Export:

- Button: `Export Normal Map`

---

## Verhalten der Anwendung

### Beim Start

- Es ist noch keine Datei geladen.
- Source Preview ist leer.
- Normal Map Preview ist leer.
- Export ist deaktiviert.
- Strength steht auf dem Standardwert.
- Invert-Optionen stehen auf ihren Standardwerten.

### Beim Laden einer Datei

- Die Datei wird validiert.
- Das Bild wird angezeigt.
- Die Normal Map wird automatisch erzeugt.
- Die Normal Map Preview wird aktualisiert.
- Export wird aktiviert.

### Bei Änderung von Einstellungen

- Die Normal Map wird neu berechnet.
- Die Preview wird aktualisiert.
- Der Export verwendet immer den aktuell sichtbaren Stand.

### Beim Export

- Der Nutzer wählt Speicherort und Dateiname.
- Die aktuelle Normal Map wird als PNG gespeichert.
- Die Source-Datei bleibt unverändert.

---

## Fehlerbehandlung MVP

Die Fehlerbehandlung soll einfach bleiben, aber die Anwendung darf nicht kommentarlos abstürzen.

Abzufangende Fälle:

- Datei kann nicht gelesen werden.
- Dateiformat wird nicht unterstützt.
- Bilddaten sind beschädigt.
- Exportpfad ist ungültig.
- Datei kann nicht geschrieben werden.

Verhalten:

- Es wird eine einfache Fehlermeldung angezeigt.
- Die Anwendung bleibt geöffnet.
- Bereits geladene Daten bleiben möglichst erhalten.

---

## Performance-Anforderungen MVP

Der MVP muss keine hochoptimierte Produktionslösung sein.

Mindestanforderungen:

- 512x512 Texturen laufen flüssig.
- 1024x1024 Texturen bleiben gut benutzbar.
- 2048x2048 Texturen dürfen kurz spürbar rechnen.
- Die Anwendung soll bei großen Texturen nicht abstürzen.

Nicht erforderlich im MVP:

- GPU-Berechnung.
- Async Processing.
- Fortschrittsanzeige.
- Abbrechen laufender Berechnungen.
- Caching mehrerer Zwischenschritte.

---

## Akzeptanzkriterien Gesamt-MVP

Der MVP gilt als abgeschlossen, wenn folgende Punkte erfüllt sind:

- Eine PNG-Heightmap kann geladen werden.
- Eine JPG/JPEG-Heightmap kann geladen werden.
- Das geladene Bild wird als Source Preview angezeigt.
- Aus dem Bild wird automatisch eine Normal Map erzeugt.
- Die Normal Map wird als Preview angezeigt.
- Der Strength-Wert verändert das Ergebnis sichtbar.
- Invert X verändert die X-Richtung der Normal Map.
- Invert Y verändert die Y-Richtung der Normal Map.
- Die aktuelle Normal Map kann als PNG exportiert werden.
- Die Anwendung stürzt bei normalen Eingaben nicht ab.
- Die Source-Datei wird nie verändert.

---

## Nicht Teil des MVP

Folgende Funktionen werden bewusst später behandelt:

- 3D Preview
- Cube Preview
- Sphere Preview
- Plane Preview
- GPU Acceleration
- Batch Mode
- Drag & Drop
- Ambient Occlusion Map
- Displacement Map
- Roughness Map
- Specular Map
- Metallic Map
- Heightmap-Editor
- Histogramm
- Auto-Leveling
- Contrast-Regler
- Blur/Sharpness-Regler
- Tiling/Wrap Mode
- Seamless Texture Preview
- Presets für Unity, Unreal oder Godot
- Projektdateien/Speichern von Einstellungen
- Undo/Redo
- Mehrsprachigkeit
- Installer
- Dark/Light Theme Umschaltung

---

## Nächste sinnvolle Erweiterungen nach dem MVP

### [P1] Drag & Drop

Bilder sollen direkt in das Fenster gezogen werden können.

### [P1] Unity Export Preset

Ein Preset für Unity-kompatible Normal Maps soll die richtige Kanalorientierung festlegen.

### [P1] OpenGL / DirectX Presets

Der Nutzer soll zwischen gängigen Normal-Map-Konventionen wählen können.

### [P1] Wrap Mode für tilebare Texturen

Für nahtlose Texturen sollen Ränder über die gegenüberliegende Bildseite gelesen werden können.

### [P1] Contrast-Regler

Die Heightmap soll vor der Normal-Map-Berechnung stärker oder schwächer kontrastiert werden können.

### [P1] Blur-Regler

Die Heightmap soll vor der Berechnung geglättet werden können, um harte Artefakte zu reduzieren.

### [P2] 3D Preview

Die generierte Normal Map soll auf einfachen Formen getestet werden können.

Mögliche Preview-Modelle:

- Plane
- Cube
- Sphere

### [P2] Weitere Map-Typen

Zusätzliche Maps können aus der Heightmap abgeleitet werden.

Mögliche Map-Typen:

- Displacement
- Ambient Occlusion
- Roughness
- Specular

---

## Entwicklungsreihenfolge

### Schritt 1: Projektgrundlage

- WPF-Projekt erstellen.
- Core-Bereich für Bildverarbeitung getrennt halten.
- Grundlayout der Oberfläche anlegen.

### Schritt 2: Bild laden

- Datei öffnen.
- Bild anzeigen.
- Fehlerbehandlung für ungültige Dateien ergänzen.

### Schritt 3: Normal Map erzeugen

- Heightmap-Werte aus Bilddaten ableiten.
- Normal Map berechnen.
- Ergebnis anzeigen.

### Schritt 4: Einstellungen anbinden

- Strength-Regler einbauen.
- Invert X einbauen.
- Invert Y einbauen.
- Preview bei Änderungen aktualisieren.

### Schritt 5: Export

- PNG-Export einbauen.
- Standard-Dateiname ableiten.
- Export-Button korrekt aktivieren/deaktivieren.

### Schritt 6: MVP testen

- Test mit flacher Heightmap.
- Test mit starkem Schwarz-Weiß-Kontrast.
- Test mit weichen Graustufenverläufen.
- Test mit farbigem Eingabebild.
- Test mit PNG.
- Test mit JPG.
- Test mit 512x512.
- Test mit 1024x1024.
- Test mit 2048x2048.

---

## Definition of Done

Der MVP ist fertig, wenn das Tool lokal gestartet werden kann, eine Heightmap lädt, daraus eine sichtbare Normal Map erzeugt, die wichtigsten Parameter direkt in der UI anpassbar sind und das Ergebnis als PNG exportiert werden kann.

Der MVP muss noch nicht schön, vollständig oder besonders performant sein.

Er muss zuverlässig genug sein, um damit erste eigene Normal Maps für Unity-Materialien zu erzeugen.