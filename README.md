# DESD LifeHub

Selbst gehostete, deutschsprachige Plattform für **Aufgaben, Kalender, Notizen,
Erinnerungen und Organisationen** – mit zentraler Lizenzverwaltung über
[OpenLicensd](https://license.desd.uk).

> **Status:** funktionales MVP. Der Funktionsumfang steht im Vordergrund, das
> Feinschliff-Design folgt später. Die Oberfläche ist deutschsprachig (Ansprache
> mit „Sie“) und unterstützt helles und dunkles Erscheinungsbild.

---

## 1. Funktionen

| Bereich | Funktion |
| --- | --- |
| Aufgaben | Liste, Board, Details, Fälligkeiten, Prioritäten, Labels |
| Kalender | Tag/Woche/Monat, ganztägige Termine, Verknüpfung mit Aufgaben |
| Notizen | Freitext, Anheften, Organisation |
| Erinnerungen | Hintergrunddienst, In-App-Benachrichtigungen |
| Organisationen | Persönlicher Bereich + Firmen mit Rollen (Eigentümer/Manager/Mitglied) |
| Lizenzen | Persönliche Lizenz pro Konto, serverseitige Prüfung, Kulanzfrist |
| Firmen-Kontingente | Systemadmin erzeugt N Lizenzen pro Firma, Firmenadmin verteilt sie |
| Verwaltung | `/adminpanel` (System) und `/{FIRMEN-ID}/adminlogin` (Firma) |

**Mandantentrennung:** Jede Organisation ist ein eigener Mandant. Zugriffe werden
serverseitig auf Authentifizierung, Rolle **und** Lizenz geprüft – niemals im
Browser.

---

## 2. Schnellstart (Docker)

Voraussetzungen: **Docker** mit Compose.

```bash
cp .env.example .env
# .env öffnen und alle CHANGE_ME-Werte ersetzen.
# Insbesondere: LICENSE_ENCRYPTION_KEY (openssl rand -base64 32)

docker compose up -d --build
```

Danach erreichbar unter `http://localhost:8080`.

- Systemverwaltung: `/adminpanel` (erfordert die Rolle `Admin`)
- Firmenverwaltung: `/{FIRMEN-ID}/adminlogin` (z. B. `/acme/adminlogin`)
- Erste Anmeldung mit `BOOTSTRAP_ADMIN_EMAIL` / `BOOTSTRAP_ADMIN_PASSWORD` aus der `.env`

Ausführlich: [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)

---

## 3. Lokale Entwicklung

Voraussetzungen: **.NET SDK 10**, **PostgreSQL 17**.

```bash
# Datenbank bereitstellen, z. B. per Docker:
docker run --name lifehub-db -e POSTGRES_PASSWORD=dev -e POSTGRES_USER=desd_lifehub \
           -e POSTGRES_DB=desd_lifehub -p 5432:5432 -d postgres:17

# Verbindung und Schlüssel per User-Secrets (nicht in Dateien):
cd src/DESD.LifeHub.Web
dotnet user-secrets set "Database:Password" "dev"
dotnet user-secrets set "LicenseKeyProtection:EncryptionKey" "$(openssl rand -base64 32)"

# Starten
dotnet run
```

Build und Tests:

```bash
dotnet build "DESD.LifeHub.slnx"
dotnet test  "tests/DESD.LifeHub.Tests/DESD.LifeHub.Tests.csproj"
```

Migrationen:

```bash
dotnet ef migrations add <Name> \
  -p src/DESD.LifeHub.Infrastructure \
  -s src/DESD.LifeHub.Web
dotnet ef database update \
  -p src/DESD.LifeHub.Infrastructure \
  -s src/DESD.LifeHub.Web
```

---

## 4. Konfiguration

Alle Einstellungen sind über **Umgebungsvariablen** überschreibbar. Die
Anwendung liest zusätzlich Präfix-Varianten (`DESD_…`) und doppelte
Unterstriche für Verschachtelung.

| Zweck | Beispielvariable |
| --- | --- |
| Datenbank | `Database__Host`, `Database__Password`, `ConnectionStrings__Postgres` |
| Lizenzschlüssel-Verschlüsselung | `LicenseKeyProtection__EncryptionKey` (Pflicht) |
| OpenLicensd-Prüfung | `OpenLicensd__BaseUrl`, `OpenLicensd__ProductCode` |
| OpenLicensd-Admin | `OpenLicensd__ApiToken` (nur serverseitig) |
| Sicherheit | `Security__RequireHttps`, `Proxy__TrustedNetworks` |
| Erst-Administrator | `Bootstrap__AdminEmail`, `Bootstrap__AdminPassword` |

Geheimnisse gehören **niemals** ins Repository oder Image. Siehe
[docs/SECURITY.md](docs/SECURITY.md).

---

## 5. Lizenzierung

- Jedes Benutzerkonto erhält **eine persönliche Lizenz** (Schlüssel wird nach der
  Anmeldung aktiviert; serverseitig gegen OpenLicensd geprüft).
- Firmen besitzen einen **Lizenzpool**: Der Systemadmin erzeugt Kontingente über
  die OpenLicensd-Admin-API, der Firmenadmin weist sie Mitarbeitern zu.
- Mitarbeiter sehen **niemals** Rohschlüssel, nur den Status.
- Bei einem Ausfall des Lizenzservers greift eine **Kulanzfrist** nur für zuvor
  bestätigte Lizenzen; eindeutig abgelehnte/gesperrte Lizenzen bleiben gesperrt.

Details und der exakte OpenLicensd-Vertrag: [docs/LICENSING.md](docs/LICENSING.md)

---

## 6. Dokumentation

- [docs/LICENSING.md](docs/LICENSING.md) – Lizenzmodell und OpenLicensd-Integration
- [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) – Installation, Update, Reverse-Proxy
- [docs/BACKUP.md](docs/BACKUP.md) – Datensicherung und Wiederherstellung
- [docs/SECURITY.md](docs/SECURITY.md) – Sicherheitsmodell und Checklisten

---

## 7. Projektstruktur

```
src/
  DESD.LifeHub.Core/            Domänenmodell, Abstraktionen, Enums
  DESD.LifeHub.Infrastructure/  EF Core, Identity, Fachservices, Lizenzierung
  DESD.LifeHub.Web/             Razor Pages, Middleware, APIs
tests/
  DESD.LifeHub.Tests/           Unit-/Integrationstests (SQLite, simulierte APIs)
Dockerfile, docker-compose.yml, .env.example
```
