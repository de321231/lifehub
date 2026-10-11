# Installation und Betrieb

## 1. Voraussetzungen

- **Docker Engine** mit **Compose v2** (empfohlen), oder
- **.NET SDK 10** + **PostgreSQL 17** für den Direktbetrieb
- Für Produktion: ein **Reverse Proxy** mit TLS (nginx, Caddy, Traefik …)

---

## 2. Erstinstallation (Docker Compose)

```bash
git clone <repository> desd-lifehub
cd desd-lifehub

cp .env.example .env
```

In `.env` mindestens setzen:

| Variable | Beschreibung |
| --- | --- |
| `POSTGRES_PASSWORD` | Starkes Datenbankpasswort |
| `LICENSE_ENCRYPTION_KEY` | 32 Byte base64, z. B. `openssl rand -base64 32` |
| `LICENSE_HASH_PEPPER` | Zufälliger Pepper |
| `BOOTSTRAP_ADMIN_EMAIL` | E-Mail des ersten Systemadministrators |
| `BOOTSTRAP_ADMIN_PASSWORD` | Starkes Passwort (≥ 12 Zeichen, Gross-/Klein, Ziffer, Sonderzeichen) |
| `OPENLICENSD_APITOKEN` | Bearer-Token für Firmen-Kontingente (optional) |

Starten:

```bash
docker compose up -d --build
docker compose logs -f web
```

Aufruf: `http://<server>:8080`

- Erstanmeldung mit den `BOOTSTRAP_*`-Werten; danach können diese aus `.env`
  entfernt werden.
- Systemverwaltung: `/adminpanel`
- Firmenverwaltung: `/{FIRMEN-ID}/adminlogin`

---

## 3. Reverse Proxy und TLS

Setzen Sie `SECURITY_REQUIREHTTPS=true` (erzwingt HTTPS-Weiterleitung und
Secure-Cookies) und betreiben Sie die App ausschliesslich über einen TLS-Proxy.

Vertrauen Sie Proxy-Headern nur, wenn Sie die Proxy-Netze kennen:

```env
PROXY_TRUSTED_NETWORKS=172.16.0.0/12
```

**Caddy-Beispiel**

```
lifehub.example.de {
    reverse_proxy 127.0.0.1:8080
}
```

**nginx-Beispiel**

```nginx
server {
    listen 443 ssl;
    server_name lifehub.example.de;

    ssl_certificate     /etc/letsencrypt/live/lifehub.example.de/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/lifehub.example.de/privkey.pem;

    client_max_body_size 25m;

    location / {
        proxy_pass         http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header   Host              $host;
        proxy_set_header   X-Real-IP         $remote_addr;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_set_header   X-Forwarded-Host  $host;
    }
}
```

---

## 4. Health-Endpunkte

| Pfad | Zweck |
| --- | --- |
| `/health` | Gesamtstatus inkl. Datenbank |
| `/health/ready` | Bereitschaft (Datenbank erreichbar) |
| `/health/live` | Lebendigkeit (Prozess antwortet) |

---

## 5. Direktbetrieb ohne Docker

```bash
# Verbindung und Schlüssel setzen (User-Secrets im Development):
cd src/DESD.LifeHub.Web
dotnet user-secrets set "Database:Password" "<passwort>"
dotnet user-secrets set "LicenseKeyProtection:EncryptionKey" "$(openssl rand -base64 32)"

dotnet run --urls "http://0.0.0.0:8080"
```

In Produktion bevorzugt Umgebungsvariablen statt User-Secrets verwenden.

---

## 6. Migrationen

Bei `Database:MigrateOnStartup=true` (Standard) werden ausstehende Migrationen
beim Start automatisch nach einem **Retry** angewendet. Kontrollierter Betrieb:

```bash
Database__MigrateOnStartup=false
dotnet ef database update -p src/DESD.LifeHub.Infrastructure -s src/DESD.LifeHub.Web
```

Neue Migration erzeugen:

```bash
dotnet ef migrations add <Name> -p src/DESD.LifeHub.Infrastructure -s src/DESD.LifeHub.Web
```

---

## 7. Update

1. **Backup** erstellen (siehe [BACKUP.md](BACKUP.md)).
2. Neue Version holen: `git pull`.
3. `docker compose up -d --build`.
4. Migrationen laufen beim Start automatisch (bzw. kontrolliert nach Abschnitt 6).
5. Logs und `/health/ready` prüfen.

---

## 8. Fehlerbehebung

| Symptom | Ursache / Lösung |
| --- | --- |
| Container startet, App beendet sich sofort | Datenbank nicht erreichbar → Logs prüfen, Zugangsdaten in `.env` |
| „LicenseKeyProtection:EncryptionKey ist nicht konfiguriert“ | `LICENSE_ENCRYPTION_KEY` fehlt oder ist kein base64 mit 32 Byte |
| Kein Kontingent erzeugbar | `OPENLICENSD_APITOKEN` fehlt oder ist ungültig (401 → Token prüfen) |
| Lizenzen gelten als „nicht erreichbar“ | Ausgehende Verbindung zu `https://license.desd.uk` blockiert |
| Anmeldung schlägt fehl | Konto deaktiviert oder Passwortregeln; Bootstrap nur beim ersten Start |
| Falsche Client-IP im Rate-Limiting | `PROXY_TRUSTED_NETWORKS` setzen |

---

## 9. Nächste Schritte

- [LICENSING.md](LICENSING.md) – Lizenzierung
- [BACKUP.md](BACKUP.md) – Datensicherung
- [SECURITY.md](SECURITY.md) – Sicherheit
