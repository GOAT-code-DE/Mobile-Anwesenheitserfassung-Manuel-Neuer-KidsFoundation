# Teilbare Testversion

Diese Vorschau übernimmt die vorhandenen Razor-Seiten, Styles, das Original-Logo und die Bedienlogik aus `src/NeuerKids`. `build.py` erzeugt daraus eine statische Seite für GitHub Pages. Nur der Datenzugriff wird im erzeugten JavaScript durch `demo.js` ersetzt; der ASP.NET-Quellcode, seine Authentifizierung und Rechteprüfung werden nicht abgeschaltet.

Die Seite startet ohne Anmeldung über **Testen starten**. Ein Rollenwechsel zeigt Mitarbeitenden-, Hausleitungs- und Administrationsansicht. Standortwechsel, Profile inklusive mehrerer Staatsangehörigkeiten und Notfallkontakt, Dublettenhinweis, heutige Anwesenheit, Korrekturen/Nachträge, Archivsuche und Reaktivierung, Entfernung von Fehlanlagen beziehungsweise Erhalt von Monatssummen, interaktive Dashboard-Filter, Vergleiche und XLSX-Export sind in der Vorschau bedienbar.

Die 49 Profile, Namen, Kontakte und 1.673 Besuche in `fixtures.json` sind ausschließlich synthetisch. Der Demotag ist bewusst auf den 21.09.2026 festgelegt, damit Vergleiche beim Herumzeigen reproduzierbar bleiben. Angaben im Demo-Verwaltungsbereich erzeugen weder echte Zugänge noch Nachrichten. Die Berechnungen orientieren sich an den Regeln der Serverfassung; bekannte Beispielsummen werden im Browsertest abgeglichen. Diese Simulation ersetzt keine Prüfung der Serverrechte oder paralleler Zugriffe auf eine echte Datenbank.

Alle Änderungen bleiben ausschließlich im Arbeitsspeicher des aktuellen Tabs. Die Navigation erhält sie; Neuladen oder **Demo zurücksetzen** stellt die Beispiele wieder her. Kein Local Storage, Session Storage, Service Worker, Tracking oder App-API-Aufruf. Keine echten Kinderdaten eingeben.

## Bauen und prüfen

```sh
python3 preview/build.py
python3 -m http.server 5081 --bind 127.0.0.1 --directory artifacts/pages
# In einem weiteren Terminal nach Installation gemäß docs/VERIFICATION.md:
cd tests/browser
node preview.mjs
```

Mit `TEST_BASE_URL` lässt sich dieselbe Browserprüfung gegen die öffentliche statische Vorschau ausführen. Ausschließlich die bekannten Demo-Daten werden innerhalb des jeweiligen Browser-Tabs verändert. Der Test prüft Chrome/Chromium und WebKit, Standort- und Rollenwechsel, Profile, Anwesenheiten, Archiv, Dashboard, Export und kleine Bildschirme. Er prüft außerdem, dass keine Daten per POST übertragen und keine Browser-Speicher beschrieben werden.

Der Workflow `.github/workflows/preview.yml` baut und veröffentlicht ausschließlich `artifacts/pages`. Datenbankdateien, Sitzungsschlüssel, Serverkonfigurationen und echte Aktivierungslinks sind kein Bestandteil des veröffentlichten Artefakts. In GitHub unter Settings → Pages ist **GitHub Actions** als Quelle ausgewählt.
