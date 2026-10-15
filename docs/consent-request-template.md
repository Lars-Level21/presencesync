# Vorlage: Anfrage an die IT eines Kunden-Tenants

*Für Mitarbeiter, deren Kunden-Tenant eine Administratorgenehmigung verlangt. Text kopieren, Platzhalter in spitzen
Klammern ersetzen, als E-Mail senden. Hintergrund für die Admins: [consent.md](consent.md).*

---

**Betreff:** Zustimmung für die App „PresenceSync“ (nur Teams-Status, nur mein Konto)

Hallo,

ich arbeite mit dem Konto `<upn im kunden-tenant>` in eurem Tenant und bin gleichzeitig in weiteren Tenants in
Microsoft Teams angemeldet (`<eigene Organisation>` und andere Kunden). Damit ihr bei euch seht, dass ich gerade in
einem Call oder Meeting bin, auch wenn dieser in einem anderen Tenant stattfindet, nutze ich die App PresenceSync. Sie
gleicht ausschließlich **meinen eigenen** Teams-Status zwischen meinen Konten ab.

Eure Tenant-Richtlinie verlangt dafür eine Administratorgenehmigung:

| | |
| --- | --- |
| App-Name | PresenceSync |
| Client-ID | `<clientId>` |
| Herausgeber | `<Herausgeber, wie im Consent-Dialog angezeigt>` (verifizierter Herausgeber) |
| App-Typ | Windows-Desktop-App, kein Server, keine Secrets |
| Berechtigungen | nur delegiert: `User.Read`, `Presence.ReadWrite`, `offline_access` |

**Was die App tut:** Sie liest meinen eigenen Teams-Status und setzt ihn auf „Beschäftigt“, solange ich in einem
anderen Tenant im Call bin. Dazu legt sie eine eigene Status-Session an, die nach spätestens 5 Minuten von selbst
ausläuft.

**Was die App nicht tut:** keinen Zugriff auf Daten oder den Status anderer Benutzer, auf Chats, Mails, Dateien oder
Kalender; keine Application Permissions, kein Backend, keine Telemetrie. Aus eurem Tenant geht nur das Signal
„belegt / nicht belegt“ hinaus.

**Am einfachsten für euch:** die Zustimmung **nur für mein Konto** erteilen (`consentType = Principal`) oder den
Admin-Consent erteilen und die Unternehmensanwendung per „Zuweisung erforderlich“ auf mein Konto beschränken.
Anleitung mit PowerShell-Snippet, Berechtigungsdetails, Datenflüssen und Widerruf:

- Informationen für Admins: https://github.com/leveltwentyone/presencesync/blob/main/docs/consent.md
- Security-Review: https://github.com/leveltwentyone/presencesync/blob/main/docs/security-review.md

Admin-Consent für den ganzen Tenant (danach bitte auf mein Konto beschränken):

```
https://login.microsoftonline.com/<kunden-tenant-id>/adminconsent?client_id=<clientId>
```

Danke!
`<Name>`
