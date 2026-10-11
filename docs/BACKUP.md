# Datensicherung und Wiederherstellung

Alle fachlichen Daten liegen in **PostgreSQL**. Zusätzlich sind zwei Geheimnisse
ausserhalb der Datenbank zu sichern:

| Gut | Wo | Sichern? |
| --- | --- | --- |
| Fach- und Kontodaten | PostgreSQL-Volume `desd-lifehub-db-data` | ja |
| Lizenzschlüssel-Verschlüsselung | `LICENSE_ENCRYPTION_KEY` | **ja, zwingend** |
| Hash-Pepper | `LICENSE_HASH_PEPPER` | ja |
| OpenLicensd-API-Token | `OPENLICENSD_APITOKEN` | ja |
| `.env` | Host | ja (verschlüsselt) |

> **Wichtig:** Ohne `LICENSE_ENCRYPTION_KEY` sind die gespeicherten
> Lizenzschlüssel nicht wiederherstellbar. Bewahren Sie den Schlüssel getrennt
> vom Datenbank-Backup auf (z. B. in einem Passwortmanager oder Secret Store).

---

## 1. Datenbank-Backup (Docker)

Komplettes logisches Backup als SQL-Dump:

```bash
docker compose exec -T db \
  pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc \
  > "lifehub-$(date +%Y%m%d-%H%M%S).dump"
```

Das Format `-Fc` (custom) ist komprimiert und mit `pg_restore` selektiv
einlesbar.

Nur die Lizenz- und Kontingenttabellen:

```bash
docker compose exec -T db \
  pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc \
  -t company_licenses -t user_licenses -t license_validation_logs \
  > "lifehub-licenses-$(date +%Y%m%d).dump"
```

---

## 2. Wiederherstellung (Docker)

```bash
# 1. Anwendung stoppen (keine Schreibzugriffe während des Restores)
docker compose stop web

# 2. In eine frische, leere Datenbank einspielen.
#    Empfehlung: DB neu anlegen, damit kein Altbestand vermischt wird.
docker compose exec -T db \
  psql -U "$POSTGRES_USER" -d postgres -c "DROP DATABASE IF EXISTS $POSTGRES_DB;"
docker compose exec -T db \
  psql -U "$POSTGRES_USER" -d postgres -c "CREATE DATABASE $POSTGRES_DB;"

docker compose exec -T db \
  pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists \
  < lifehub-20260101-120000.dump

# 3. Starten
docker compose start web
```

Stellen Sie sicher, dass `LICENSE_ENCRYPTION_KEY` und `LICENSE_HASH_PEPPER`
**identisch** zur gesicherten Umgebung sind, sonst sind die Lizenzschlüssel
nicht mehr entschlüsselbar bzw. die Eindeutigkeits-Hashes stimmen nicht.

---

## 3. Volume-Snapshot (Infrastruktur-Ebene)

Auf Hosts mit Volume-Snapshots (ZFS, LVM, Cloud-Volume) kann zusätzlich das
Daten-Volume `desd-lifehub-db-data` konsistent gesichert werden. Für ein
konsistentes Dateisystem-Backup die Datenbank kurz in den Backup-Modus versetzen
oder den Container stoppen:

```bash
docker compose stop db
# Snapshot des Volumes erstellen
docker compose start db
```

---

## 4. Aufbewahrung und Zeitplan

Empfehlung:

- **Täglich**: vollständiger Dump, 30 Tage Aufbewahrung.
- **Wöchentlich**: vollständiger Dump, 6 Monate Aufbewahrung.
- **Monatlich**: vollständiger Dump, 1 Jahr Aufbewahrung.
- **Vor jedem Update**: zusätzliches manuelles Backup (siehe
  [DEPLOYMENT.md](DEPLOYMENT.md), Abschnitt „Update“).

Backups verschlüsselt und ausserhalb des Servers ablegen. Regelmässig eine
**Wiederherstellung testen** (Abschnitt 2), nicht nur das Backup erzeugen.

---

## 5. Konsistenzprüfung

Nach einem Restore:

```bash
curl -fsS http://localhost:8080/health/ready
```

Erwartet: HTTP 200. Danach stichprobenartig anmelden und den Lizenzstatus unter
`/License` prüfen.
