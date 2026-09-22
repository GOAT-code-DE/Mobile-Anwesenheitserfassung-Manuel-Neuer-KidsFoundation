# Prüfstand vom 21.09.2026

## Durchgeführt

- .NET 10 Release-Build und Veröffentlichungspaket erfolgreich, Compilerwarnungen als Fehler behandelt.
- 27 automatisierte Fach- und HTTP-Tests erfolgreich. Enthalten: Standortrechte, Administration ohne implizite Kinderdatenrechte, CSRF, parallele/idempotente Anwesenheit, Dubletten, Bearbeitungskonflikte, Jahres- und Archivgrenzen, Geburtstage, deutsche Mitternacht/Sommerzeit, Filter und Zählregeln, eingeschränkte Statistik nach Löschung, XLSX ohne Kinderdaten.
- Vollständiger Einladungsablauf mit Passwort, Authenticator-Einrichtung, Wiederherstellungscodes und sofortiger Sitzungssperrung durch die Administration im HTTP-Test geprüft.
- Abhängigkeitsprüfung mit `dotnet list src/NeuerKids/NeuerKids.csproj package --vulnerable --include-transitive`: keine bekannten anfälligen Pakete laut NuGet-Quelle zum Prüfzeitpunkt.
- SQL-Server-Migration und idempotentes SQL-Skript erfolgreich erzeugt. Kein Unterschied zwischen Datenmodell und Migration.
- Browserabläufe mit Chromium/Chrome und Playwright-WebKit bei 390 × 844 sowie 1440 × 1000 geprüft: Demo-Anmeldung, Profil mit Notfallkontakt anlegen und heute erfassen, Namenssuche, Anwesenheit zurücknehmen/erneut erfassen, Testprofil als Fehlanlage entfernen, kombinierte Auswertungsfilter, Diagrammklick und erneuter Klick, Zurücksetzen und Excel-Download. Keine JavaScript-/Konsolenfehler oder horizontales Überlaufen der Seite in diesen Abläufen.
- Exportanforderung übernimmt Standort, Altersbereich und angeklickten Wochentag. Die inhaltliche Berechnung und das XLSX-Format werden zusätzlich in den serverseitigen Tests geprüft.
- Original-Logo, lokal eingebundene Foundation-Schriften und Farben auf Anmelde- und Auswertungsseite visuell geprüft.

## Browserprüfung wiederholen

In einem Terminal die lokale Demo starten. In einem weiteren Terminal aus dem Repository-Verzeichnis:

```sh
pnpm --dir tests/browser install --frozen-lockfile
pnpm --dir tests/browser exec playwright install chromium webkit
pnpm --dir tests/browser test
```

Node.js 24 und pnpm 11 verwenden. Unter Linux bei Bedarf `playwright install --with-deps chromium webkit` ausführen. Die Prüfung erlaubt nur localhost/127.0.0.1, erfordert den ausdrücklich gekennzeichneten Development-Demo-Zugang und erstellt ausschließlich erfundene Testprofile. Diese werden im erfolgreichen Ablauf wieder entfernt. Bei einem abgebrochenen Test kann ein Profil mit dem Vornamen „Browserprobe“ zurückbleiben; es darf in der Demo als Fehlanlage entfernt werden. Screenshots entstehen unter `artifacts/browser/` im jeweiligen Arbeitsverzeichnis und werden nicht eingecheckt.

## Noch im Pilotbetrieb zu prüfen

- Physische iPhone-/Android-Geräte: WebKit auf einem Rechner ersetzt keinen echten iPhone/Safari-Gerätetest.
- Verbindungsabbrüche auf realen Mobilgeräten, Datenmengen/Last und parallele Nutzung auf Azure SQL.
- Anwendung der Migration gegen das tatsächliche Azure SQL, HTTPS/Proxy-Konfiguration, persistent geschützte Sitzungsschlüssel, getrennte Umgebungen und Fehlerüberwachung.
- Sicherungs- und Wiederherstellungsprobe einschließlich erneut anzuwendender Löschungen.
- Datenschutz-, Aufbewahrungs-, Anonymisierungs- und Markenfreigabe durch die Foundation.

Azure wurde nicht eingerichtet; ein Produktivbetrieb mit echten Kinderdaten ist nicht freigegeben.

## Öffentliche statische Vorschau

Zusätzlich mit `tests/browser/preview.mjs` in Chromium und WebKit geprüft: Start ohne Anmeldung, Standortwechsel mit Erhalt beim Seitenwechsel, Profile anlegen/bearbeiten/entfernen, optionaler Kontakt, Tagesanwesenheit zurücknehmen und erneut erfassen, Nachtrag, Archivreaktivierung, Rollenwechsel, simulierte Einladung, kombinierte Filter, Diagrammklick und Entfernen des Filters, Zurücksetzen, Excel-Download und mobile/Desktop-Darstellung. Alle Kindersuchfelder haben geprüfte Innenabstände für Suchtext und Löschen-Schaltfläche. Auf dem Handy steht die Suche vor der höchstens 110 Pixel hohen Zusammenfassung; dekorative System- und Emoji-Symbole werden nicht angezeigt. Die mobile Suche wurde außerdem bei einer auf 430 Pixel reduzierten sichtbaren Höhe geprüft; das erste Ergebnis bleibt vollständig oberhalb der simulierten Bildschirmtastatur sichtbar. Mitarbeitende werden von einem direkten Auswertungsaufruf zur Tagesansicht geleitet und sehen keinen Auswertungsreiter; serverseitige Berichts- und Exportabfragen liefern für diese Rolle 403. Zahlen über hohen Zeit- und Wochentagsbalken bleiben innerhalb der Diagrammflächen sichtbar. Bekannte Beispielsummen: Gelsenkirchen September 204 Besuche/24 Kinder; Bottrop September, 10–14 Jahre und Dienstag: 11 Besuche. Keine JavaScript-/Konsolenfehler, keine API-Schreibaufrufe oder Browser-Speicherung. XLSX-Dateien als ZIP/XML geprüft, Filterbeschreibung und Zahlen enthalten, keine persönlichen Datenzeilen.
