# Betrieb und Übergabe

## GitHub zuerst

Der Quellcode liegt in einem öffentlichen GitHub-Repository. Eine ausdrücklich gekennzeichnete, statische Vorschau mit erfundenen Daten wird über GitHub Pages bereitgestellt. Diese übernimmt die Oberfläche der ASP.NET-App und simuliert ihre Datenfunktionen im Arbeitsspeicher des Browsers. GitHub Pages führt den ASP.NET-Server nicht aus. Azure-Ressourcen und eigene Domains wurden nicht eingerichtet. Die nachfolgenden Betriebsanforderungen gelten für die spätere Serverfassung mit echten Daten.

## Voraussetzungen vor echten Daten

Die Foundation dokumentiert Verantwortlichkeit, Verarbeitungszwecke und Rechtsgrundlage, Erforderlichkeit der Pflichtfelder, Datenschutzhinweise einschließlich Notfallkontakten, Auftragsverarbeitung sowie Aufbewahrung und Löschung. Jugendhilferechtliche Anforderungen werden mit den verantwortlichen Stellen abgeglichen. Eine Datenschutz-Folgenabschätzung wird auf Erforderlichkeit geprüft. Die Anonymisierung der Monatsaggregate wird insbesondere hinsichtlich kleiner Gruppen und Differenzbildung beurteilt. Es wird keine universelle gesetzliche Löschfrist angenommen.

Ohne dokumentierte Freigabe ausschließlich synthetische Testdaten verwenden. Die Konfiguration ist ein technisches Freigabetor, kein Ersatz für diese Prüfung.

## Azure-Zielarchitektur

- Getrennte App-Service-Instanzen und Azure-SQL-Datenbanken für Staging und Produktion, bevorzugt Germany West Central.
- App Service mit .NET 10, HTTPS-only und verwalteter Identität. Azure SQL mit Verschlüsselung, Entra-Authentifizierung und eingeschränktem Netzwerkzugriff. Die Webapp benötigt nur ihre eigene Datenbank; Schemaänderungen durch einen gesonderten Deployment-Zugang.
- Anwendung unter eigener Domain, `AllowedHosts` ausdrücklich auf diese Domain setzen. Keine Wildcard-Freigabe als Standard.
- Datenbanksicherungen und App-Service-/Key-Vault-Zugriffsrechte gemäß freigegebenem Betriebskonzept. Speicherort allein ersetzt keine Auftragsverarbeitung oder Prüfung von Drittlandzugriffen.
- Sitzungsschlüssel dauerhaft und zwischen App-Instanzen gemeinsam bereitstellen. `DataProtection__KeyPath` auf einen geschützten persistenten Pfad setzen. Der Datenträger muss verschlüsselt, Zugriff auf den App-Prozess und berechtigte Administration begrenzt sein. App-Service-Standardpfade und das Container-Dateisystem sind nicht automatisch ein geeignetes Schlüsselkonzept.

## Erforderliche Einstellungen

Einstellungen außerhalb des Repositories setzen, bei Geheimnissen über Key Vault beziehungsweise App-Service-Referenzen:

| Einstellung | Bedeutung |
|---|---|
| `ASPNETCORE_ENVIRONMENT=Production` | Produktivmodus |
| `Demo__Enabled=false` | Keine Demo-Zugänge |
| `Database__Provider=SqlServer` | Azure SQL |
| `ConnectionStrings__Database` | Produktionsverbindung; bevorzugt verwaltete Identität |
| `AllowedHosts` | Tatsächlicher App-Hostname |
| `DataProtection__KeyPath` | Geschützter gemeinsamer persistenter Schlüsselspeicher |
| `Privacy__Approved=true` | Dokumentierte Freigabe liegt vor |
| `Privacy__LegalBasisReference` | Verweis auf die interne freigegebene Dokumentation |
| `Privacy__AnonymizationApproved=true` | Aggregationsverfahren einschließlich kleiner Gruppen geprüft |
| `Privacy__InactiveMonths` | Tatsächlich freigegebene Aufbewahrungsfrist; positive Monatszahl |
| `Privacy__AuditRetentionDays` | Tatsächlich freigegebene Protokollaufbewahrung; positive Tageszahl |

Eine Beispielverbindung ohne Passwort lautet `Server=tcp:<server>.database.windows.net,1433;Database=<database>;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;`. Keine Verbindung mit ausgeschalteter Zertifikatsprüfung einsetzen.

## Datenbank und erste Administration

Die SQL-Server-Migrationen liegen im Projekt. Die lokale SQLite-Demo wird separat mit `EnsureCreated` angelegt; diese Datei wird nicht nach Azure kopiert.

```sh
dotnet tool restore
dotnet ef migrations script --idempotent --project src/NeuerKids -o /tmp/neuerkids-migration.sql
# SQL-Skript im freigegebenen Deployment-Prozess gegen die Zieldatenbank ausführen.
```

Alternativ kann ein privilegierter Wartungslauf der Anwendung mit `--migrate` die vorhandenen Migrationen anwenden. Keine automatische Migration in jeder Webinstanz aktivieren.

Die erste Administration entsteht einmalig mit gesetztem `Bootstrap__Email` und allen Betriebsvariablen:

```sh
dotnet NeuerKids.dll --bootstrap-admin
```

Der Befehl verweigert die Ausführung, sobald eine Administration existiert. Er gibt einen vertraulichen, 24 Stunden gültigen Aktivierungspfad aus. Nur in einer nicht mitprotokollierten administrativen Sitzung verwenden, der konkreten Person sicher übergeben und anschließend `Bootstrap__Email` entfernen. Konten erhalten dadurch keine Standortrechte. Weitere Administrationen und Standortzuweisungen erfolgen im Verwaltungsbereich.

Bei Einladungen wird der Link an die verifizierte E-Mail-Adresse beziehungsweise über einen identitätsgeprüften Kanal weitergegeben. Vor einer Authenticator-Rücksetzung Identität außerhalb der App prüfen. Es gibt bewusst keinen E-Mail-Versand ohne konfigurierten, freigegebenen Versanddienst.

## Löschläufe und Wiederherstellung

Der Hintergrunddienst prüft beim Start und anschließend täglich die freigegebenen Fristen. Er löscht abgelaufene Profile einschließlich Notfallkontakten und Einzelanwesenheiten transaktional; unabhängige monatliche Summen bleiben erhalten. Personenbezogene Referenzen in Änderungsprotokollen werden beim Purge entfernt. Das Tages-Auswahlarchiv nach zwölf Monaten bleibt hiervon unabhängig.

Fehler im Löschlauf werden als technische Fehlermeldung ohne Kinderdaten protokolliert. Diese Meldungen in Azure Monitor alarmieren. Zusätzlich extern `/health` überwachen; dies ist eine Prozessprüfung, kein vollständiger Datenbankcheck. Auf erhöhte Fehlerraten und gescheiterte Deployments alarmieren; Anfragekörper, Cookies, Querystrings mit Aktivierungstokens und personenbezogene Daten nicht in Telemetrie übernehmen.

Backups müssen eine eigene begründete Aufbewahrungsfrist erhalten. Wiederherstellungen zunächst in isolierter Umgebung durchführen. Vor Freigabe dokumentierte Löschungen seit dem Sicherungszeitpunkt erneut anwenden und den automatischen Fristenlauf durchführen, damit Daten nicht wieder produktiv erscheinen. Manuelle Löschungen benötigen hierfür einen gesondert geschützten, befristeten betrieblichen Wiederherstellungsnachweis außerhalb der regulären App-Auswertung. Auch die Aufbewahrung dieses Nachweises ist festzulegen.

## Rollout

1. CI erfolgreich, SQL-Migrationsskript geprüft; separates Staging mit synthetischen Daten.
2. Reale iPhone/Safari- und Android/Chrome-Geräte, Netzunterbrechung, parallele Nutzung, Anmelden und Wiederherstellung prüfen.
3. Datenschutz-, Schlüssel-, Backup- und Monitoringkonfiguration freigeben; Wiederherstellung in isolierter Azure-Umgebung praktisch testen.
4. Pilot an einem Standort, dann beide Standorte.

Ein echter Azure-Deployment- oder Restore-Test ist ohne eingerichtete Azure-Umgebung nicht durchgeführt. Diese Schritte sind Teil der Einführung, nicht als bereits abgeschlossen zu behandeln.
