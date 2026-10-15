# PresenceSync – Informationen für Admins von Kunden-Tenants

Für IT-Administratoren eines Tenants, in dem ein Benutzer – meist ein externer Berater mit Mitgliedskonto –
PresenceSync verwenden möchte und Ihre Zustimmung (Consent) braucht. Diese Seite erklärt, worauf Sie zustimmen, wie
Sie die Zustimmung auf diesen einen Benutzer beschränken und wie Sie sie überwachen und widerrufen.

> **Kurzfassung:** PresenceSync ist eine lokale Windows-App ohne Server. Sie liest und setzt per Microsoft Graph
> ausschließlich den **Teams-Status des angemeldeten Benutzers selbst**. Angefragt werden nur die delegierten
> Berechtigungen `User.Read`, `Presence.ReadWrite` und `offline_access`. Zwischen Tenants wird nur das Signal
> „belegt / nicht stören“ übertragen – keine Inhalte, keine Namen, keine Termine. Die Zustimmung lässt sich auf einen
> einzelnen Benutzer beschränken und jederzeit widerrufen.

## Worum es geht

Externe Mitarbeiter sind im Teams-Client oft gleichzeitig in mehreren Tenants angemeldet; der Teams-Status ist aber
pro Tenant getrennt. Telefoniert der Mitarbeiter im Tenant seines Arbeitgebers, erscheint er in Ihrem Tenant weiter
als „Verfügbar“. PresenceSync setzt ihn in diesem Fall auch bei Ihnen auf „Beschäftigt“ und danach wieder zurück.
**Für Ihre Benutzer wird der Status des externen Kollegen dadurch zuverlässiger.**

Funktionsweise im Überblick: [README](../README.md). Technische Details und Datenflüsse:
[architecture.md](architecture.md).

## Die Anwendung

| | |
| --- | --- |
| Name | PresenceSync |
| Herausgeber | wird im Consent-Dialog angezeigt; bei verifiziertem Herausgeber mit blauem Haken |
| Client-ID | `<clientId>` – vom anfragenden Benutzer bzw. dessen Organisation |
| App-Typ | öffentlicher Client (Windows-Desktop-App), Multi-Tenant |
| Secrets / Zertifikate | keine |
| Application Permissions (ohne Benutzer) | **keine** |
| Delegierte Berechtigungen | `User.Read`, `Presence.ReadWrite`, `offline_access` |
| Quellcode | öffentlich, MIT-Lizenz: <https://github.com/Lars-Level21/presencesync> |

Durch die Zustimmung entsteht in Ihrem Tenant eine **Unternehmensanwendung** „PresenceSync“. Die App-Registrierung
selbst liegt im Tenant des Herausgebers – in der Regel der Organisation des anfragenden Benutzers.

## Berechtigungen

| Berechtigung | Typ | Admin-Consent laut Microsoft | Wofür PresenceSync sie nutzt |
| --- | --- | --- | --- |
| `User.Read` | delegiert | nein | Anmeldung, eigene Objekt-ID, einmalig der Name Ihres Tenants als Beschriftung in der App |
| `Presence.ReadWrite` | delegiert | nein | eigenen Teams-Status lesen und setzen |
| `offline_access` | delegiert | nein | Token-Erneuerung im Hintergrund |

Dass Ihr Tenant dennoch nach einer Administratorgenehmigung fragt, liegt an Ihrer Consent-Richtlinie, nicht an der
Art der Berechtigungen. Die vollständige Liste der Graph-Aufrufe mit Häufigkeit steht in
[architecture.md → Graph-Aufrufe](architecture.md#graph-aufrufe).

**Was die App nicht kann und nicht tut**

- Keinen Status **anderer** Benutzer lesen oder setzen – delegierte Berechtigungen gelten nur für den angemeldeten
  Benutzer, und die App adressiert ausschließlich dessen eigene ID.
- Kein Zugriff auf Chats, Nachrichten, Mails, Dateien, Kalender, Kontakte, Teams-Inhalte oder Verzeichnisdaten anderer.
- Keine Zugriffe ohne angemeldeten Benutzer, kein Server, keine Telemetrie.

## Datenfluss in Kürze

PresenceSync liest auf dem Gerät des Benutzers dessen Status in jedem seiner Tenants. Ist er in einem Tenant belegt,
setzt die App in den anderen eine eigene Status-Session „Beschäftigt“ bzw. „Nicht stören“, die nach 5 Minuten
abläuft und laufend erneuert wird, solange der Anlass besteht.

- **Aus Ihrem Tenant hinaus** geht nur die Information „belegt / nicht belegt“ – keine Gesprächspartner, Themen,
  Teilnehmer, Uhrzeiten oder Inhalte.
- **Übertragen** wird nur über das Gerät des Benutzers, nicht über Dritte.
- **Gespeichert** wird auf dem Gerät nur Tenant-ID, Anzeigename und Anmeldename. Der Token-Cache ist per Windows-DPAPI
  an den Benutzer gebunden, das Protokoll enthält keine Token, Anmeldenamen sind maskiert.

Sequenzdiagramm und Tabelle aller Daten: [architecture.md → Datenfluss](architecture.md#datenfluss-beim-auto-sync).

## Sicherheit

- **Minimale Rechte:** drei delegierte Low-Privilege-Berechtigungen, keine Application Permissions, keine Secrets.
- **Conditional Access gilt vollständig.** Die App ist ein normaler öffentlicher Client und unterliegt allen
  Richtlinien Ihres Tenants (MFA, Gerätekonformität, Standort, Sitzungsdauer). Verlangt eine Richtlinie ein Gerät, das
  der Benutzer nicht hat, ist die App in Ihrem Tenant nicht nutzbar. Sie versucht nicht, das zu umgehen: keine fremden
  Client-IDs, keine undokumentierten Endpunkte.
- **Keine automatischen Anmeldedialoge:** Interaktive Anmeldung nur nach ausdrücklichem Klick des Benutzers.
- **Fail-safe:** Status-Sessions laufen nach 5 Minuten von selbst ab; beim Beenden der App und beim Abmelden von
  Windows werden sie aktiv gelöscht.
- **Vorrang der Benutzerentscheidung:** Ein im Teams-Client gesetzter Status wird nicht überschrieben.
- **Geringe Last:** pro Tenant etwa 3 Lesezugriffe pro Minute, Schreibzugriffe nur bei Statuswechseln; Drosselung wird
  mit `Retry-After` und Backoff beachtet.
- **Nachvollziehbar:** quelloffen, automatisierte Tests, Schwachstellenscan in der Build-Pipeline; Sicherheitsmodell
  und bekannte Einschränkungen in [SECURITY.md](../SECURITY.md).

**Worst Case:** Erlangt ein Angreifer Codeausführung auf dem Gerät des Benutzers und missbraucht diese App, kann er
den Teams-Status **dieses einen Benutzers** lesen und ändern – mehr nicht.

## Zustimmung erteilen

Ersetzen Sie `<clientId>` durch die Client-ID aus der Anfrage und `<tenant>` durch Ihre Tenant-ID oder Domain.

### Variante 1 (empfohlen): nur für den einen Benutzer

Die Berechtigungen gelten ausschließlich für das Konto des anfragenden Benutzers; für alle anderen gibt es keine
Freigabe.

```powershell
Connect-MgGraph -Scopes "Application.ReadWrite.All","DelegatedPermissionGrant.ReadWrite.All" -TenantId <tenant>

$app   = Get-MgServicePrincipal -Filter "appId eq '<clientId>'"
if (-not $app) { $app = New-MgServicePrincipal -AppId '<clientId>' }
$graph = Get-MgServicePrincipal -Filter "appId eq '00000003-0000-0000-c000-000000000000'"
$user  = Get-MgUser -UserId '<upn-des-benutzers>'

New-MgOauth2PermissionGrant -BodyParameter @{
    clientId    = $app.Id
    consentType = 'Principal'
    principalId = $user.Id
    resourceId  = $graph.Id
    scope       = 'User.Read Presence.ReadWrite offline_access'
}
```

### Variante 2: Admin-Consent mit Zuweisungspflicht

1. Zustimmung erteilen: `https://login.microsoftonline.com/<tenant>/adminconsent?client_id=<clientId>`
2. *Entra Admin Center → Unternehmensanwendungen → PresenceSync → Eigenschaften →* **Zuweisung erforderlich = Ja**
3. *Benutzer und Gruppen →* nur den anfragenden Benutzer zuweisen.

### Variante 3: über den Admin-Consent-Workflow

Hat der Benutzer bereits eine Anfrage gestellt, finden Sie sie unter *Unternehmensanwendungen →
Administratorzustimmungsanforderungen*. Nach der Genehmigung empfehlen wir zusätzlich die Schritte 2 und 3 aus
Variante 2.

## Überwachen und widerrufen

| Aufgabe | Ort |
| --- | --- |
| Anmeldungen der App sehen | *Entra ID → Anmeldeprotokolle*, Filter Anwendung = „PresenceSync“ |
| Graph-Aufrufe nachvollziehen | Microsoft-Graph-Aktivitätsprotokolle (sofern aktiviert), Filter auf die Client-ID |
| Erteilte Berechtigungen prüfen | *Unternehmensanwendungen → PresenceSync → Berechtigungen* |
| Zugriff für einen Benutzer entziehen | Zuweisung entfernen bzw. dessen `oauth2PermissionGrant` löschen |
| App vollständig sperren | *Unternehmensanwendungen → PresenceSync → Eigenschaften →* „Für Benutzer aktiviert, sich anzumelden?“ = Nein, oder die Unternehmensanwendung löschen |
| Laufende Sitzungen beenden | *Benutzer → Sitzungen widerrufen* |

Nach einem Widerruf zeigt PresenceSync den Tenant als „Gesperrt“ bzw. „Anmeldung erforderlich“; eine gesetzte
Status-Session läuft spätestens nach 5 Minuten ab.

## Häufige Fragen

**Sieht PresenceSync, mit wem der Benutzer in unserem Tenant telefoniert?**
Nein. Graph liefert für den eigenen Status nur Verfügbarkeit und Aktivität (z. B. „Im Anruf“), keine Gesprächspartner.

**Erfährt ein anderer Kunde etwas über unseren Tenant?**
Nur, dass der Benutzer gerade „belegt“ ist – dasselbe, als hätte er dort manuell „Beschäftigt“ gesetzt.

**Kann die App den Status unserer Mitarbeiter ändern?**
Nein. Delegierte Berechtigungen gelten nur für den angemeldeten Benutzer; die App adressiert ausschließlich dessen ID.

**Braucht die App Administratorrechte auf dem Gerät?**
Nein. Sie läuft im Benutzerkontext.

**Fordert die App künftig mehr Rechte an?**
Geplant ist optional ein Kalender-Abgleich, der anonyme „Blocker“-Termine im **eigenen** Kalender des Benutzers
anlegt (`Calendars.ReadWrite`). Diese Berechtigung ist nicht Teil der aktuellen Registrierung und würde erst bei
Aktivierung **gesondert** angefragt, mit eigener Zustimmung und aktualisierter Dokumentation.

## Kontakt

Fragen zur konkreten Anfrage richten Sie an den anfragenden Benutzer bzw. dessen Organisation.
Sicherheitsmeldungen zur Software: [SECURITY.md](../SECURITY.md).
