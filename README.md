# KenoKoch.de – Bewerbungsseite als echtes .NET-Projekt

[![CI/CD](https://github.com/KenoKoch/Bewerbungspage/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/KenoKoch/Bewerbungspage/actions/workflows/ci-cd.yml)

Die persönliche Website von Keno Koch, Softwareentwickler für C# und .NET.
Sie stellt nicht nur den Lebenslauf vor, sondern **ist selbst das Arbeitsbeispiel**: ein modularer Monolith mit
.NET 10, Blazor, EF Core und Azure SQL, betrieben auf Azure App Service – mit acht Live-Demos, die zeigen,
wie die Seite gebaut ist.

| | |
|---|---|
| **Stack** | .NET 10 · C# 14 · Blazor (Static SSR, Server, WebAssembly) · ASP.NET Core Minimal APIs · EF Core 10 · SQL Server / Azure SQL |
| **Azure** | App Service (Linux) · Azure SQL Serverless (Free Offer) · Managed Identity · Application Insights · Bicep |
| **Qualität** | xUnit (Unit, Architektur, Integration) · Warnungen = Fehler · Central Package Management · CSP mit Nonce · Rate Limiting · Health Checks |
| **CI/CD** | GitHub Actions mit OIDC-Anmeldung, Smoke-Test nach dem Deployment |

---

## Inhalt

1. [Was die Seite zeigt](#was-die-seite-zeigt)
2. [Architektur](#architektur)
3. [Lokal starten](#lokal-starten)
4. [Inhalte anpassen](#inhalte-anpassen)
5. [Tests](#tests)
6. [Deployment auf Azure](#deployment-auf-azure)
7. [Domain kenokoch.de verbinden](#domain-kenokochcom-verbinden)
8. [Konfiguration](#konfiguration)
9. [Kosten und Betrieb](#kosten-und-betrieb)
10. [Lizenzen und Credits](#lizenzen-und-credits)

---

## Was die Seite zeigt

**Seiten:** Startseite (Hero, Über mich, Werdegang, Skills, Demo-Übersicht, Architektur, Kontakt),
`/demos`, `/architektur`, `/impressum`, `/datenschutz`.

**Live-Demos auf `/demos`:**

| # | Demo | Was dahintersteckt |
|---|------|--------------------|
| 01 | Render-Modi | Static SSR mit Enhanced Forms, InteractiveServer und InteractiveWebAssembly nebeneinander, Zustandsübergabe per `[PersistentState]` |
| 02 | **SPS-Simulator mit HMI** | Soft-SPS pro Besucher: zyklischer Scan (100 ms), Prozessabbild, FB `TankControl` Zeile für Zeile aus SCL übertragen, Verriegelungen, NOT-HALT, Meldesystem nach WinCC-Vorbild, Trend |
| 03 | Echtzeit | Live-Besucherzähler über Blazor-Circuits (`CircuitHandler`), Emoji-Reaktionen an alle Besucher, Aktivitäts-Feed aus Integration Events |
| 04 | EF Core & SQL | Gästebuch mit Validierung, Honeypot und Rate Limiting; SQL-Inspector zeigt das erzeugte SQL; Besucherstatistik ohne Cookies |
| 05 | Modularer Monolith | Module, Schemas, Migrationen, Events und Endpunkte – zur Laufzeit per Reflection ausgelesen |
| 06 | WebAssembly | Mandelbrot-Menge, berechnet von .NET im Browser, gezeichnet per JS-Interop |
| 07 | Minimal APIs | Live-Aufrufe aus WebAssembly, OpenAPI-Dokument (`/openapi/v1.json`) und Scalar-Referenz (`/scalar`) |
| 08 | Azure & Laufzeit | Region, Instanz, .NET-Version, Speicher, GC und Datenbank-Latenz – live vom Server |

---

## Architektur

```
src/
├─ KenoKoch.Web/                 Host: Komposition, Seiten, Layout, Plattform (OpenAPI, Health, Security, Telemetrie)
├─ KenoKoch.Web.Client/          WebAssembly-Komponenten (Render-Modus-Karte, Fraktal, API-Playground)
├─ KenoKoch.BuildingBlocks/      IModule, In-Memory-Event-Bus, Persistenz pro Modul-Schema, Health Checks
├─ KenoKoch.UI/                  Gemeinsame UI-Bausteine (Icons, Code-Blöcke, Formatierung) – Server + WASM
└─ Modules/
   ├─ Profile/                   Lebenslauf, Skills, /api/profile
   ├─ Guestbook/                 Gästebuch, Schema [guestbook]   + Guestbook.Contracts (Events)
   ├─ Analytics/                 Besucher & Echtzeit, Schema [analytics]
   └─ Plc/                       SPS-Simulator und HMI          + Plc.Contracts (Events)
tests/KenoKoch.Tests/            Unit-, Architektur- und Integrationstests
infra/                           Bicep (App Service, Azure SQL, App Insights, eigene Domain) + SQL-Skript
.github/workflows/               CI/CD und Infrastruktur-Deployment
```

**Regeln des modularen Monolithen** (durch `tests/KenoKoch.Tests/Architecture` im CI erzwungen):

- Module referenzieren keine anderen Module – nur deren `*.Contracts`-Projekte mit Integration Events.
- Jedes Modul mit Daten besitzt ein eigenes SQL-Schema und eine eigene Migrationshistorie.
- `BuildingBlocks` und `UI` kennen kein Modul.
- Module melden sich ausschließlich über `IModule` beim Host an (`builder.AddModules(...)` in `Program.cs`).

**Wichtige Designentscheidungen**

- **Static SSR + interaktive Inseln:** Seiten kommen als HTML, nur der Live-Zähler und die Demos bauen eine SignalR-Verbindung auf. WebAssembly wird nur auf `/demos` geladen.
- **Robust gegenüber Azure SQL Serverless:** Migrationen laufen im Hintergrund, kein Seitenaufbau wartet auf die Datenbank, die Statistik wird gepuffert und gebündelt geschrieben. Ohne Datenbank funktioniert die Seite weiter (siehe Integrationstests).
- **Datensparsam:** Keine Tracking-Cookies, keine IP-Speicherung, keine externen Skripte oder Schriften.

---

## Lokal starten

**Voraussetzungen:** .NET 10 SDK, SQL Server LocalDB (kommt mit Visual Studio) oder ein anderer SQL Server.

```shell
dotnet tool restore            # stellt dotnet-ef bereit
dotnet run --project src/KenoKoch.Web
```

Danach läuft die Seite unter <https://localhost:7194> bzw. <http://localhost:5194>.
Die Datenbank `KenoKoch` wird beim ersten Start in LocalDB angelegt und migriert (Verbindung in `appsettings.Development.json`).

Ohne SQL Server einfach `ConnectionStrings:Sql` leeren – die Seite läuft dann ohne Gästebuch und Statistik.

**Neue Migration für ein Modul anlegen** (Beispiel Gästebuch):

```shell
dotnet dotnet-ef migrations add <Name> --context GuestbookDbContext --project src/Modules/Guestbook/KenoKoch.Modules.Guestbook --startup-project src/KenoKoch.Web --output-dir Persistence/Migrations
```

---

## Inhalte anpassen

Alle Texte über dich stehen in **einer** Datei:
`src/Modules/Profile/KenoKoch.Modules.Profile/Data/profile.json`

Platzhalter stehen in `[eckigen Klammern]`. Im Development-Modus zeigt die Seite unten links an, wie viele noch offen sind.

**Vor dem Go-live bitte prüfen und ergänzen:**

- [ ] `profile.json`: Unternehmen, Zeiträume, Orte, Ausbildung, Englisch-Niveau und LinkedIn-Link (Platzhalter-Links werden automatisch ausgeblendet). GitHub zeigt auf `KenoKoch/Bewerbungspage` – das Repository muss öffentlich sein, damit die Quelltext-Links funktionieren.
- [ ] `profile.json`: Inhalte der Stationen und Skills – sie sind aus deiner Beschreibung abgeleitet, bitte auf Richtigkeit prüfen.
- [ ] `profile.json`: Die E-Mail-Adresse `kontakt@kenokoch.de` ist ein Vorschlag – das Postfach muss existieren.
- [ ] Optional `cvUrl` setzen (z. B. `/files/lebenslauf.pdf` und die Datei unter `src/KenoKoch.Web/wwwroot/files/` ablegen).
- [ ] `Impressum.razor` und `Datenschutz.razor` unter `src/KenoKoch.Web/Components/Pages/`: Anschrift eintragen. Die Texte sind Vorlagen und ersetzen keine Rechtsberatung.
- [ ] `infra/main.bicepparam`: Entra-Administrator für Azure SQL eintragen.

---

## Tests

```shell
dotnet test KenoKoch.slnx -c Release
```

| Bereich | Beispiele |
|---------|-----------|
| Architektur | Modulgrenzen, Contracts nur mit Events, eindeutige Schemas |
| SPS | Skalierung NORM_X/SCALE_X, Zweipunktregelung mit Hysterese, NOT-HALT mit Quittierung, Überfüllsicherung, Meldesystem, kompletter Scan-Zyklus |
| Domäne | Gästebuch-Invarianten, Link-Erkennung, Rate Limiting |
| Plattform | Event-Bus (fehlerhafter Handler blockiert andere nicht), Syntax-Highlighter, Formatierung, Seitenaufruf-Zählung |
| Integration | Alle Seiten rendern **ohne Datenbank**, eigene 404, CSP-Nonce im HTML, APIs und OpenAPI-Dokument |

---

## Deployment auf Azure

Alle Befehle sind für PowerShell im Repository-Ordner geschrieben.

### 0. Voraussetzungen

- Azure-Konto mit Abonnement (<https://azure.microsoft.com/free>), Anmeldung mit dem eigenen Microsoft-Konto.
- Azure CLI: `winget install --exact --id Microsoft.AzureCLI`, danach ein neues Terminal öffnen.
- GitHub-Repository **öffentlich** (Settings → General → Change visibility). Dann funktionieren die Quelltext-Links der Seite und die Deployment-Umgebung `production` auch im kostenlosen GitHub-Plan.

### 1. Infrastruktur bereitstellen

```powershell
az login
az group create --name rg-kenokoch --location westeurope

# Eigene Objekt-ID als SQL-Administrator ermitteln und in infra/main.bicepparam bei sqlAdminObjectId eintragen
az ad signed-in-user show --query id -o tsv

az deployment group create --resource-group rg-kenokoch --template-file infra/main.bicep --parameters infra/main.bicepparam --query properties.outputs
```

Die Ausgaben enthalten u. a. `webAppName`, `webAppHostName` und `customDomainVerificationId`.

### 2. Datenbankzugriff für die Managed Identity einrichten

Einmalig das Skript `infra/sql/grant-webapp-access.sql` ausführen und dabei `<web-app-name>` durch `webAppName` ersetzen:
Azure-Portal → SQL-Datenbank `sqldb-kenokoch` → Abfrage-Editor → Anmeldung mit Microsoft Entra.
Meldet der Editor, dass deine IP-Adresse nicht zugelassen ist, auf den angebotenen Link zum Zulassen klicken.
Damit verbindet sich die App ohne Passwort mit der Datenbank.

### 3. GitHub-Deployment per OIDC einrichten

```powershell
$appId    = az ad app create --display-name gh-kenokoch-deploy --query appId -o tsv
az ad sp create --id $appId
$subId    = az account show --query id -o tsv
$tenantId = az account show --query tenantId -o tsv

# Rechte nur auf die Ressourcengruppe (bei "principal does not exist" eine Minute warten und wiederholen)
az role assignment create --assignee $appId --role Contributor --scope "/subscriptions/$subId/resourceGroups/rg-kenokoch"

# Passwortlose Anmeldung für GitHub Actions in der Umgebung "production"
az ad app federated-credential create --id $appId --parameters infra/github-federated-credential.json

# Werte für die GitHub-Secrets anzeigen
"AZURE_CLIENT_ID       = $appId"
"AZURE_TENANT_ID       = $tenantId"
"AZURE_SUBSCRIPTION_ID = $subId"
```

Im GitHub-Repository unter *Settings → Secrets and variables → Actions* anlegen:

| Typ | Name | Wert |
|-----|------|------|
| Secret | `AZURE_CLIENT_ID` | App-ID der Registrierung |
| Secret | `AZURE_TENANT_ID` | `az account show --query tenantId -o tsv` |
| Secret | `AZURE_SUBSCRIPTION_ID` | `az account show --query id -o tsv` |
| Variable | `AZURE_WEBAPP_NAME` | Ausgabe `webAppName` |
| Variable | `AZURE_RESOURCE_GROUP` | `rg-kenokoch` (für den Infrastruktur-Workflow) |

Außerdem unter *Settings → Environments* die Umgebung `production` anlegen.

### 4. Ausliefern

Ein Push auf `main` baut, testet und deployt automatisch. Nach dem Deployment prüft der Workflow `/health`.
Die Infrastruktur lässt sich später über den Workflow **Infrastruktur** (zuerst mit *what-if*) aktualisieren.

---

## Domain kenokoch.de verbinden

0. Die Domain registrieren, falls noch nicht geschehen: bei einem Registrar mit DNS-Verwaltung (z. B. INWX, IONOS, Strato)
   oder direkt in Azure als *App Service-Domäne* (die DNS-Zone liegt dann in Azure DNS).
1. Beim DNS-Anbieter der Domain eintragen:

   | Typ | Name | Wert |
   |-----|------|------|
   | TXT | `asuid` | Ausgabe `customDomainVerificationId` |
   | TXT | `asuid.www` | Ausgabe `customDomainVerificationId` |
   | A | `@` | IP-Adresse der Web App: `Resolve-DnsName <webAppHostName>` oder Azure-Portal → Web App → *Benutzerdefinierte Domänen* |
   | CNAME | `www` | Ausgabe `webAppHostName` (z. B. `app-kenokoch-a1b2c3.azurewebsites.net`) |

2. Warten, bis die Einträge sichtbar sind (`Resolve-DnsName -Type TXT asuid.kenokoch.de`).
3. In `infra/main.bicepparam` `customDomain = 'kenokoch.de'` setzen und das Bicep-Deployment erneut ausführen.
   Azure bindet beide Hostnamen und stellt kostenlose, automatisch verlängerte TLS-Zertifikate aus.
   Die App leitet `www.kenokoch.de` dauerhaft auf `kenokoch.de` um.

---

## Konfiguration

| Schlüssel | Standard | Bedeutung |
|-----------|----------|-----------|
| `ConnectionStrings:Sql` | leer (Dev: LocalDB) | Datenbank. In Azure per App-Service-Verbindungszeichenfolge mit `Authentication=Active Directory Managed Identity` |
| `Database:MigrateOnStartup` | `true` | Migrationen aller Module beim Start im Hintergrund anwenden |
| `Site:BaseUrl` | `https://kenokoch.de` | Kanonische URL für Open Graph, Canonical-Links, JSON-LD |
| `Site:CanonicalHost` | leer | z. B. `kenokoch.de` – leitet `www.` darauf um |
| `Guestbook:RequireApproval` | `true` (Dev: `false`) | Neue Einträge erst nach Freigabe anzeigen |
| `Guestbook:MaxEntriesPerVisitor` / `MaxEntriesPerHour` | `3` / `30` | Spam-Schutz |
| `Plc:MaxConcurrentSessions` / `IdleTimeoutMinutes` | `50` / `10` | Schutz der Server-Ressourcen durch den SPS-Simulator |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | – | Aktiviert Telemetrie per OpenTelemetry (wird von Bicep gesetzt) |

**Gästebucheinträge freigeben** (im Abfrage-Editor):

```sql
SELECT Id, Name, Company, Message, CreatedAt FROM guestbook.Entries WHERE IsApproved = 0 ORDER BY CreatedAt DESC;
UPDATE guestbook.Entries SET IsApproved = 1 WHERE Id = <Id>;
```

---

## Kosten und Betrieb

- **App Service B1 (Linux):** kleinster Tarif mit eigener Domain, verwaltetem Zertifikat und Always On – der einzige Posten mit festen monatlichen Kosten.
- **Azure SQL Free Offer:** monatliches Freikontingent für eine Serverless-Datenbank. Mit `AutoPause` entstehen keine Kosten; ist das Kontingent aufgebraucht, pausiert die Datenbank bis zum Monatswechsel und die Seite läuft ohne Gästebuch und Statistik weiter. Alternativ `BillOverUsage`.
- **Application Insights / Log Analytics:** Tagesobergrenze von 1 GB als Kostenbremse.
- Der App-Service-Health-Check nutzt `/health` (ohne Datenbank), damit die Serverless-Datenbank pausieren kann.

---

## Lizenzen und Credits

- Schriften: [Inter](https://rsms.me/inter/) und [JetBrains Mono](https://www.jetbrains.com/lp/mono/), SIL Open Font License 1.1, lokal ausgeliefert.
- Icons: im Stil von [Lucide](https://lucide.dev) (ISC), Markenlogos von [Simple Icons](https://simpleicons.org) (CC0).
- Die Inhalte (Texte über Keno Koch) sind nicht zur Weiterverwendung freigegeben.
