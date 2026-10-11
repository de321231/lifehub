# Sicherheit

Dieses Dokument beschreibt das Sicherheitsmodell von DESD LifeHub und gibt
Betreiber-Checklisten an die Hand.

---

## 1. Grundsätze

- **Serverseitige Durchsetzung:** Authentifizierung, Berechtigung und
  Lizenzprüfung erfolgen ausschliesslich im Backend. Im Browser existiert keine
  Lizenz- oder Berechtigungslogik, die sich manipulieren liesse.
- **Mandantentrennung:** Zugriffe werden pro Organisation geprüft; fremde
  Mandanten sind unerreichbar.
- **Keine Geheimnisse im Browser:** Lizenzschlüssel und das OpenLicensd-Token
  verlassen den Server nicht. Schlüssel erscheinen weder in URLs noch in Logs.
- **Keine Standardzugangsdaten:** Es gibt keine fest verdrahteten Benutzer oder
  Passwörter. Der erste Administrator wird über Umgebungsvariablen angelegt.

> **Ausnahme von der Lizenzschranke:** Die Verwaltungsoberflächen
> (`/adminpanel`, `/Admin/…`, `/{FIRMEN-ID}/admin…`) sind von der *Produktlizenz*
> befreit, damit sich das System nicht selbst aussperrt. Sie bleiben über
> Anmeldung und Rollenprüfung (`Admin` bzw. Manager/Eigentümer der Firma)
> geschützt. Siehe [LICENSING.md](LICENSING.md), Abschnitt „Ausnahmen“.

---

## 2. Lizenzschlüssel

- Lizenzschlüssel werden mit **AES-256-GCM** verschlüsselt gespeichert
  (`LicenseKeyCiphertext`). Der Schlüssel stammt aus
  `LicenseKeyProtection__EncryptionKey` (32 Byte, base64) und liegt **nie** in
  der Datenbank.
- Für die Eindeutigkeit wird zusätzlich ein **gesalzener Hash**
  (`LicenseKeyHash`, unique) mit `LicenseKeyProtection__HashPepper` gespeichert.
  Dadurch ist eine doppelte Zuweisung derselben Lizenz erkennbar, ohne den
  Klartext abzulegen.
- Angezeigt wird nur ein **maskiertes Präfix** (z. B. `X4F9K…YZ2CD`).
- Das OpenLicensd-Admin-Token wird **pro Anfrage** als Bearer-Header gesetzt und
  nur aus der Serverkonfiguration gelesen. Bearer-Aufrufe unterliegen keinem CSRF.

---

## 3. Kulanzfrist

- Bei einem **Ausfall** des Lizenzservers (Timeout, 5xx, 429, nicht
  interpretierbare Antwort) erhalten Benutzer mit **zuletzt gültiger** Lizenz für
  `OpenLicensd__GracePeriodHours` weiter Zugriff.
- Eindeutig abgelehnte oder gesperrte Lizenzen (`not_found`, `expired`,
  `revoked`, …) erhalten **keine** Kulanz.
- Ohne jemals bestätigte Lizenz führt ein Ausfall zu **keinem** Zugriff.

---

## 4. Authentifizierung und Sitzungen

- ASP.NET Core Identity mit gehashten Passwörtern und Sicherheitsstempel.
- Passwortregeln: mindestens 12 Zeichen, Gross- und Kleinbuchstaben, Ziffer und
  Sonderzeichen.
- Deaktivierte Konten (`IsActive = false`) können sich nicht anmelden.
- Einladungen und Passwort-zurücksetzen-Tokens werden **einmalig** angezeigt.
- Sitzungslaufzeit über `Security__SessionTimeoutMinutes`.

---

## 5. Transport und Header

- `Security__RequireHttps=true` erzwingt HTTPS-Weiterleitung, HSTS und
  Secure-Cookies.
- Proxy-Header werden nur vertraut, wenn `Proxy__TrustedNetworks` gesetzt ist.
- Gesetzte Sicherheits-Header (`SecurityHeadersExtensions`):
  - `Content-Security-Policy` mit `script-src 'self'` (keine Inline-Skripte),
    `style-src 'self' 'unsafe-inline'` (Inline-Styles für die
    Kalenderpositionierung), `frame-ancestors 'none'`, `object-src 'none'`.
  - `X-Content-Type-Options: nosniff`
  - `X-Frame-Options: DENY`
  - `Referrer-Policy: strict-origin-when-cross-origin`
  - `Cross-Origin-Opener-Policy: same-origin`
  - `Permissions-Policy` ohne Geolocation/Mikrofon/Kamera.

---

## 6. Schutz vor Missbrauch

- **Rate Limiting** (falls `Security__EnableRateLimiting=true`): eigene Fenster
  für Anmeldung, Registrierung und Lizenzaktivierung; Antwort `429` mit
  JSON-Fehler für API-Pfade.
- **Antiforgery:** Name `desd.lifehub.csrf`, HttpOnly, `SameSite=Lax`.
- **Bounded Caching:** Lizenzprüfungen werden zeitlich begrenzt
  zwischengespeichert (`ValidationCacheMinutes`, `NegativeCacheMinutes`,
  `DecisionCacheSeconds`), HTTP-Aufrufe haben einen Timeout.
- **Keine Test-/Entwicklungs-Umgehung** der Lizenzprüfung in der
  Produktionskonfiguration.

---

## 7. Auditierung

Alle sicherheits- und lizenzrelevanten Vorgänge werden auditiert
(`AuditService`), u. a.:

- Lizenzaktivierung, erneute Prüfung, Entfernen
- Erzeugung von Firmen-Kontingenten und Zuweisungen
- Sperren von Lizenzen
- Rollen- und Kontowartung

Die Protokolle enthalten keine Klartextschlüssel.

---

## 8. Betriebs-Checkliste

Vor dem Produktivbetrieb:

- [ ] `SECURITY_REQUIREHTTPS=true` und TLS über Reverse Proxy aktiv.
- [ ] `LICENSE_ENCRYPTION_KEY` gesetzt (32 Byte base64) und **sicher gesichert**.
- [ ] `LICENSE_HASH_PEPPER` gesetzt und stabil.
- [ ] Starke `POSTGRES_PASSWORD` und starkes Bootstrap-Admin-Passwort.
- [ ] `BOOTSTRAP_*` nach der Ersteinrichtung aus `.env` entfernt.
- [ ] `.env` und Backups nicht im Repository, verschlüsselt abgelegt.
- [ ] `PROXY_TRUSTED_NETWORKS` nur mit den tatsächlichen Proxy-Netzen gesetzt.
- [ ] `OPENLICENSD_APITOKEN` nur auf dem Server, mit minimal nötigen Rechten.
- [ ] Ausgehende Verbindungen zu `https://license.desd.uk` erlaubt.
- [ ] Regelmässige Backups inkl. Wiederherstellungstest
      (siehe [BACKUP.md](BACKUP.md)).
- [ ] Logs auf Anomalien überwacht (Anmeldefehler, Lizenzfehler, 429).

---

## 9. Schwachstellen melden

Sicherheitsrelevante Hinweise bitte **nicht** öffentlich, sondern vertraulich an
den Betreiber/zuständigen Administrator melden.
