# NEUER KIDS · Anwesenheit

Mobile Webapp für die Manuel Neuer Kids Foundation: Tagesanwesenheit, Kinderverwaltung und ein interaktives Dashboard für Gelsenkirchen und Bottrop.

**Entwicklungsstand mit ausschließlich synthetischen Beispieldaten. Kein freigegebener Echtbetrieb.** Der Quellcode wird zunächst in diesem privaten GitHub-Repository verwaltet. Es gibt keine automatische Veröffentlichung und kein Azure-Abonnement wird angelegt.

## Lokal ausprobieren

Voraussetzung: [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
bash scripts/run-demo.sh
```

Anschließend `http://127.0.0.1:5080` öffnen und einen der ausdrücklich markierten Demo-Zugänge wählen. Die Hausleitung kann beide Standorte nutzen, Mitarbeitende nur Gelsenkirchen. Die zentrale Administration hat zunächst keinen Zugriff auf Kinderdaten.

Windows/PowerShell:

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_URLS='http://127.0.0.1:5080'
dotnet run --project src/NeuerKids/NeuerKids.csproj
```

Die lokale SQLite-Datenbank und Sitzungsschlüssel werden unter `src/NeuerKids/App_Data/` erzeugt und **nicht** eingecheckt. Die Beispieldaten tragen bewusst Namen wie „Beispiel“, „Muster“ und „Demokind“. Der vereinfachte Demo-Zugang ist nur in `Development` aktiv. Niemals eine Demo-Instanz mit echten Daten betreiben oder öffentlich zugänglich machen.

## Enthalten

- Mobile Tagesansicht mit Namenssuche, Neuanlage und Schutz gegen doppelte Anwesenheit – auch bei Parallelzugriffen.
- Kinderprofile mit mehreren Staatsangehörigkeiten, automatisch berechnetem Alter und optionalem Notfallkontakt.
- Standortbezogene Rollen: Mitarbeitende, Hausleitung; separate zentrale Kontenverwaltung.
- Archivsuche nach mehr als zwölf Kalendermonaten ohne Besuch, Reaktivierung durch neue Anwesenheit.
- Dashboard mit freien Zeiträumen, Standort-, Alters-, Geschlechts-, Nationalitäts- und Wochentagsfiltern, klickbaren Diagrammen und Vergleichszeiträumen.
- Echter `.xlsx`-Export aus derselben Berechnung wie das Dashboard, ohne Kinderdaten.
- Persönliche Konten auf Einladung, Passwörter, TOTP/Authenticator, Wiederherstellungscodes und Sitzungen bis zur nächsten Mitternacht in Europa/Berlin.
- Administrativ erzeugte, einmalige Aktivierungs-/Rücksetzlinks mit 24 Stunden Laufzeit. Die Administration gibt sie über einen verifizierten Kanal weiter; die App versendet keine E-Mails.
- Korrektur von Anwesenheiten, getrennte Entfernung von Fehlanlagen und persönlichen Daten, protokollierte Änderungen, konfigurierbare automatische Löschung.
- ASP.NET Core 10, Razor Pages, JavaScript ohne Frontend-Build-Schritt, EF Core, Azure-SQL-Migration und Dockerfile.
- Gestaltung mit Original-Logo, Blau sowie roten, gelben und grünen Akzenten der Foundation; Schriften lokal eingebunden. Quellen und Markenhinweise: [docs/BRAND.md](docs/BRAND.md).

## Prüfungen

```sh
dotnet restore NeuerKids.slnx --locked-mode
dotnet build NeuerKids.slnx -c Release --no-restore -warnaserror
dotnet test NeuerKids.slnx -c Release --no-build
```

GitHub Actions führt diese Prüfungen bei Änderungen aus und stellt das Anwendungspaket als Artefakt bereit. Es wird **nicht** automatisch veröffentlicht. Browserprüfungen und Einschränkungen sind in [docs/VERIFICATION.md](docs/VERIFICATION.md) dokumentiert.

## Regeln und Grenzen

- Tagesanwesenheit zeigt, wer **heute da war**, nicht wer gerade noch im Haus ist.
- Alter bezieht sich in Auswertungen auf den Besuchstag. Korrekturen der Stammdaten wirken auf noch vorhandene Einzelanwesenheiten.
- Unterschiedliche Kinder werden über den ganzen Zeitraum gezählt, nie durch Summieren der Tageswerte. Profile sind je Standort getrennt; standortübergreifende Kinderzahlen sind Standortzählungen.
- Mehrfachstaatsangehörigkeiten sind im Gesamtwert einmal enthalten, können aber in mehreren Balken erscheinen.
- Die Ein-Jahres-Regel ist eine Auswahlregel, **keine gesetzliche Löschfrist**.
- Nach der endgültigen Profillöschung bleiben nur unabhängige Standort-/Monatsaggregate ohne Kennungen und demografische Merkmale. Exakte Besuche sind für vollständig eingeschlossene Archivmonate ohne Detailfilter verfügbar; unterschiedliche Kinder nur für einen einzelnen vollständigen Monat. Andere betroffene Kennzahlen werden als nicht vollständig verfügbar gekennzeichnet. Diagramme zeigen dann nur noch erhaltene Einzelanwesenheiten, Archivzahlen stehen separat und ungefiltert daneben.
- Aufbewahrungsregeln und die Angemessenheit der Anonymisierung, insbesondere kleine Gruppen und Differenzangriffe, müssen vor echten Daten organisatorisch geprüft und freigegeben werden. Das System stellt kein pauschales Datenschutz-Zertifikat aus.
- Für frei ausgewählte Zeiträume gilt derzeit eine Grenze von zehn Jahren pro Abfrage. Große Datenmengen benötigen vor dem Rollout einen Lasttest mit dem tatsächlichen Mengengerüst.
- Keine Offline-Synchronisation, keine öffentlichen Registrierungen, keine Elternkonten und kein Import alter Listen.

## Späterer Azure-Betrieb

Siehe [docs/OPERATIONS.md](docs/OPERATIONS.md). Der Produktivstart ist technisch gesperrt, solange die erforderliche Datenschutz-Konfiguration fehlt. Echte Daten, Datenbanken, Passwörter, Aktivierungslinks, TOTP-Schlüssel und Zertifikate gehören niemals ins Repository.
