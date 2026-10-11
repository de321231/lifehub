# Lizenzierung

Dieses Dokument beschreibt das Lizenzmodell von DESD LifeHub und die exakte
Anbindung an **OpenLicensd** (`https://license.desd.uk`, Produkt
`desd-lifehub`).

> Es werden ausschliesslich die nachfolgend dokumentierten Endpunkte verwendet.
> Es sind keine Endpunkte „erfunden“; die Angaben entsprechen der OpenLicensd-
> Spezifikation (`docs/api.md`, `docs/openapi.yaml` des OpenLicensd-Projekts).

---

## 1. Modell

Es gibt zwei Ebenen:

1. **Persönliche Lizenz** – genau **ein Schlüssel pro Benutzerkonto**. Der
   Schlüssel wird nach der Anmeldung aktiviert, serverseitig gespeichert
   (verschlüsselt) und regelmässig gegen OpenLicensd geprüft.
2. **Firmen-Kontingent (Pool)** – der Systemadministrator erzeugt über die
   OpenLicensd-Admin-API **N Lizenzen pro Firma**. Diese werden serverseitig
   verschlüsselt abgelegt und von der Firmenverwaltung an Mitarbeiter
   zugewiesen bzw. wieder freigegeben.

Mitarbeiter sehen **niemals** einen Rohschlüssel. Die Firmenverwaltung sieht nur
Zählwerte und ein maskiertes Präfix (z. B. `X4F9K…YZ2CD`).

---

## 2. Serverseitige Prüfung

- Validierung erfolgt **ausschliesslich im Backend** über
  `OpenLicensdClient` → `POST /api/v1/validate`.
- Im Frontend existiert **keine** Lizenzlogik.
- Jede geschützte Aktion prüft Authentifizierung **und** Berechtigung **und**
  Lizenz (`LicenseGateMiddleware` + `ILicenseAccessService`).
- Ergebnisse werden zeitlich begrenzt zwischengespeichert
  (`ValidationCacheMinutes`, `NegativeCacheMinutes`, `DecisionCacheSeconds`),
  HTTP-Aufrufe haben einen Timeout (`TimeoutSeconds`).

### Ergebnisklassen

| Interner Status | Bedeutung | Zugriff |
| --- | --- | --- |
| `Valid` | Server bestätigt die Lizenz | ja |
| `NotFound` | `not_found` – unbekannter Schlüssel | nein |
| `Expired` | `expired` | nein |
| `Revoked` | `revoked` | nein |
| `ProductMismatch` | `product_mismatch` | nein |
| `FingerprintRequired` | `fingerprint_required` | nein |
| `ActivationLimitReached` | `activation_limit` | nein |
| `Invalid` | eindeutig abgelehnt, unbekannter Grund | nein |
| `Unreachable` | Server nicht erreichbar / 5xx / 429 / Timeout | Kulanz* |
| `Malformed` | 2xx, aber nicht interpretierbar | Kulanz* |

\* **Kulanzfrist:** Nur wenn zuletzt ein **gültiges** Ergebnis vorlag, wird der
Zugriff für `GracePeriodHours` (Standard 72 h) weiter gewährt. Für eindeutig
abgelehnte oder gesperrte Lizenzen (`NotFound`, `Expired`, `Revoked`, …) gibt es
**keine** Kulanz.

---

## 3. OpenLicensd-Vertrag

Basisadresse ist die Wurzel (z. B. `https://license.desd.uk`), versioniert unter
`/api/v1`.

### 3.1 Validierung (öffentlich)

```
POST /api/v1/validate
```

Wird von `OpenLicensdClient` verwendet. Antwortfelder werden zu
`LicenseCheckResult` normalisiert (u. a. `valid`, `reason`, `expires_at`).

### 3.2 Admin-API (Bearer-Token)

Alle Admin-Aufrufe benötigen `Authorization: Bearer <token>`. Bearer-Aufrufe
unterliegen **keinem** CSRF. Das Token wird nur serverseitig verwendet.

**Produkte auflisten / finden**

```
GET /api/v1/products?search=<code>&page=&page_size=
→ { items: [ { id, name, code, description, created_at, updated_at } ], ... }
```

**Richtlinien eines Produkts**

```
GET /api/v1/policies?product_id=<id>
→ { items: [ { id, product_id, name, duration_days, expiration_basis,
               grace_period_days, max_activations } ], ... }
```

**Lizenz erzeugen**

```
POST /api/v1/licenses
{ "label": "...", "product_id": "...", "policy_id": "...",
  "expires_at": "2027-01-01T00:00:00Z", "max_activations": 1 }
→ 201 Created
{ id, label, key, key_prefix, product_id, product_code, product_name,
  policy_id, policy_name, expires_at, activated_at, revoked, created_at,
  last_validated_at, validation_count, max_activations, activation_count }
```

Der Rohschlüssel (`key`) wird **genau einmal** zurückgegeben und sofort
verschlüsselt gespeichert; er wird nie protokolliert oder ausgeliefert.

**Lizenz sperren**

```
PATCH /api/v1/licenses/{id}/revoke
→ License
```

**Fehler** kommen als `{ "error": "..." }`. `OpenLicensdAdminClient` ordnet
401/403 und sonstige Fehlerantworten als **fachlichen** Fehler ein und
unterscheidet sie strikt von einem Netzwerk-/Timeout-Fehler (`Unreachable`).

### 3.3 Auflösung von Produkt- und Richtlinien-ID

Ist `OpenLicensd:ProductId` bzw. `OpenLicensd:PolicyId` gesetzt, wird die
jeweilige Liste nicht abgefragt. Andernfalls löst der Dienst das Produkt über
den Produktcode (`OpenLicensd:ProductCode`) und danach die passende Richtlinie
auf.

---

## 4. Ablauf der Firmen-Kontingente

1. **Systemadmin** (`/adminpanel` → „Lizenzen“) wählt eine Firma und erzeugt
   *N* Lizenzen. Optional: Ablaufdatum, Aktivierungen pro Lizenz, Bezeichnung.
2. Für jeden Schlüssel ruft das Backend `POST /api/v1/licenses` auf und legt den
   Schlüssel verschlüsselt in `company_licenses` ab (Felder u. a.
   `LicenseKeyCiphertext`, `LicenseKeyHash` (unique), `LicenseKeyPrefix`).
3. **Firmenadmin** (`/{FIRMEN-ID}/adminlogin` → `/{FIRMEN-ID}/admin`) sieht Mitarbeiter samt
   Lizenzstatus, „frei/Zugewiesen/Gesperrt“ und weist freie Schlüssel zu bzw.
   gibt sie frei.
4. Die Zuweisung erfolgt über den zentralen `LicenseService` (Eindeutigkeit,
   Prüfung, Kulanz). Ein Schlüssel kann **höchstens einem** Konto zugeordnet
   werden (Datenbank-Constraint + Transaktion).
5. **Sperren** entfernt die Zuordnung und ruft zusätzlich
   `PATCH /api/v1/licenses/{id}/revoke` auf.

### Zugriffsregeln

- Nur DESD-Systemadministratoren (`Admin`) dürfen Kontingente erzeugen und
  halten das OpenLicensd-API-Token.
- Firmenadmins verwalten **ausschliesslich** ihre eigene Firma.
- Mitarbeiter sehen keine Rohschlüssel.
- Alle Lizenzoperationen werden auditiert.

### Ausnahmen von der persönlichen Lizenzschranke

Die persönliche Lizenzschranke (`LicenseGateMiddleware`) gilt **nicht** für die
Verwaltungsoberflächen:

- Systemverwaltung `/adminpanel` bzw. `/Admin/…` (`Admin`-Rolle erforderlich),
- Firmenverwaltung `/{FIRMEN-ID}/adminlogin` und `/{FIRMEN-ID}/admin`
  (Manager-/Eigentümer-Rolle der jeweiligen Firma erforderlich).

Grund: Ein Administrator muss Lizenzen (auch die eigene) vergeben und erneuern
können. Andernfalls könnte sich das System selbst aussperren. Der Zugriff bleibt
vollständig über **Anmeldung und Rollenprüfung** geschützt – die Ausnahme
betrifft nur die *Produktlizenz*, nicht die *Berechtigung*.

---

## 5. Umgebungsvariablen (Auszug)

| Variable | Zweck |
| --- | --- |
| `OpenLicensd__BaseUrl` | Basisadresse des Lizenzservers |
| `OpenLicensd__ProductCode` | Produktcode (`desd-lifehub`) |
| `OpenLicensd__ApiToken` | Bearer-Token für die Admin-API |
| `OpenLicensd__ProductId` / `__PolicyId` | Optionale feste IDs |
| `OpenLicensd__ValidationCacheMinutes` | Gültige Ergebnisse cachen |
| `OpenLicensd__NegativeCacheMinutes` | Ablehnende Ergebnisse cachen |
| `OpenLicensd__DecisionCacheSeconds` | Zugriffsentscheidung cachen (0 = aus) |
| `OpenLicensd__GracePeriodHours` | Lokale Kulanzfrist in Stunden |
| `LicenseKeyProtection__EncryptionKey` | AES-256-GCM-Schlüssel (Pflicht) |
| `LicenseKeyProtection__HashPepper` | Pepper für den Eindeutigkeits-Hash |

---

## 6. Tests

Die Lizenzlogik ist mit einem **simulierten** Lizenzserver abgedeckt
(`tests/DESD.LifeHub.Tests`), ohne echte Schlüssel zu verbrauchen:

- gültig, unbekannt (`not_found`), abgelaufen, gesperrt
- doppelte Zuweisung desselben Schlüssels
- Serverausfall: Ablehnung ohne vorherige Gültigkeit, Kulanz nach Gültigkeit,
  Ablehnung nach Ablauf der Kulanz
- Admin-API: Parsein von Snake-Case-Antworten, Bearer-Header, Unterscheidung
  fachlicher Fehler von Netzwerkfehlern
