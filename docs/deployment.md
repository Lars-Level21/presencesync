# PresenceSync bereitstellen

Für IT-Abteilungen, die PresenceSync ihren Mitarbeitern zur Verfügung stellen – und für Einzelnutzer, die nur
Abschnitt 1 brauchen. Was die App tut und wie sie aufgebaut ist: [README](../README.md),
[architecture.md](architecture.md).

**Ablauf**

1. [App-Registrierung](#1-app-registrierung) im Tenant der Organisation anlegen und den Herausgeber verifizieren – einmalig.
2. [Richtlinien](#2-richtlinien) setzen: Client-ID und optional Home-Tenant zentral vorgeben.
3. [App verteilen](#3-app-verteilen), z. B. als Intune-Win32-App.
4. [Kunden-Tenants anbinden](#4-kunden-tenants-anbinden): Die Mitarbeiter fügen ihre Kunden-Tenants hinzu; wo nötig,
   erteilt die Kunden-IT die Zustimmung.

## 1. App-Registrierung

PresenceSync braucht genau **eine** App-Registrierung im Tenant der Organisation. Sie ist Multi-Tenant; in jedem
Kunden-Tenant entsteht beim ersten Login bzw. beim Admin-Consent automatisch eine Unternehmensanwendung
(Service Principal). Mitarbeiter legen keine eigenen App-Registrierungen an.

### Eckdaten

| Einstellung | Wert |
| --- | --- |
| Unterstützte Kontotypen | Konten in einem beliebigen Organisationsverzeichnis (`AzureADMultipleOrgs`) |
| Plattform | Mobile- und Desktopanwendungen |
| Redirect URIs | `ms-appx-web://microsoft.aad.brokerplugin/{clientId}` (Windows-Anmeldung) und `http://localhost` (Browser-Fallback) |
| Öffentliche Clientflows zulassen | Ja |
| Secrets / Zertifikate | keine |
| API-Berechtigungen (Microsoft Graph, delegiert) | `User.Read`, `Presence.ReadWrite`, `offline_access` |

### Anlegen per Skript (Azure CLI)

```powershell
az login --tenant <tenant-id> --allow-no-subscriptions
./scripts/New-AppRegistration.ps1 -DisplayName "PresenceSync" -MpnId <PartnerGlobal-ID>
```

Das Skript legt die Registrierung an, setzt beide Redirect URIs, trägt die drei delegierten Berechtigungen ein (die
IDs liest es zur Laufzeit aus Microsoft Graph), erzeugt den Service Principal im eigenen Tenant und setzt optional
den verifizierten Herausgeber. Die ausgegebene `appId` ist die **Client-ID**.

### Anlegen im Portal

1. Entra Admin Center → *Anwendungen* → *App-Registrierungen* → *Neue Registrierung*
   - Name `PresenceSync`, Kontotyp *Konten in einem beliebigen Organisationsverzeichnis*
   - Redirect URI: Plattform *Öffentlicher Client/nativ (mobil und Desktop)*, `http://localhost`
2. *Authentifizierung* → *Plattform hinzufügen* → *Mobile- und Desktopanwendungen* →
   `ms-appx-web://microsoft.aad.brokerplugin/<clientId>` ergänzen; *Öffentliche Clientflows zulassen* = **Ja**.
3. *API-Berechtigungen* → Microsoft Graph → *Delegiert*: `User.Read`, `Presence.ReadWrite`, `offline_access`.
4. *Branding & Eigenschaften*: Herausgeberdomäne, Logo, Startseite und **Datenschutz-URL** (z. B. die veröffentlichte
   [PRIVACY.md](../PRIVACY.md)) hinterlegen – Kunden-Admins sehen diese Angaben im Consent-Dialog.
5. Herausgeber verifizieren (nächster Abschnitt).

### Herausgeber-Verifizierung

**Wozu:** Für neu registrierte Multi-Tenant-Apps **ohne** verifizierten Herausgeber blockiert Microsoft die Zustimmung
durch Benutzer weitgehend; viele Tenants erlauben sie zudem nur für verifizierte Herausgeber. Ohne Verifizierung sehen
Mitarbeiter in Kunden-Tenants „Administratorgenehmigung erforderlich“ (`AADSTS90094`). Mit Verifizierung zeigt der
Consent-Dialog den Herausgeber mit blauem Haken, und die Low-Privilege-Berechtigungen von PresenceSync dürfen Benutzer
in der Regel selbst freigeben – sofern die Kunden-IT das nicht generell abgeschaltet hat. Die Verifizierung wird
einmal gesetzt und gilt für alle Kunden-Tenants.

**Voraussetzungen**

| Was | Wo prüfen |
| --- | --- |
| Partnerkonto im Microsoft AI Cloud Partner Program (früher MPN), Verifizierung abgeschlossen („authorized“/„success“) | [Partner Center → Partner profile](https://partner.microsoft.com/pcv/accountsettings/connectedpartnerprofile) |
| **Partner-ID vom Typ `PartnerGlobal`** – die Standort-ID (*PartnerLocation*) sieht fast gleich aus und wird abgelehnt | Partner Center → Account settings → *Identifiers* → *Microsoft Cloud Partners Program* → Spalte *Type* |
| Die Domain der Primary-Contact-E-Mail im Partner Center ist im Tenant als Custom Domain DNS-verifiziert | Entra Admin Center → *Domain names* |
| Entra-Rolle *Application Administrator* oder *Cloud Application Administrator* | Entra Admin Center → *Roles & admins* |
| Partner-Center-Rolle *Global Admin*, *AI Cloud Partner Program Admin* oder *Accounts Admin* für denselben Benutzer | [Partner Center → User management](https://partner.microsoft.com/pcv/users) |
| Anmeldung **mit MFA in derselben Sitzung** | im Zweifel ab- und neu anmelden |

**Schritte**

1. [Entra Admin Center](https://entra.microsoft.com) → *App registrations* → **PresenceSync** → **Branding & properties**.
2. **Publisher domain**: steht dort `*.onmicrosoft.com`, über *Update domain* die eigene verifizierte Domain wählen.
3. **Publisher verification** → **Add MPN ID to verify publisher** → im Panel die **PartnerGlobal**-ID eintragen →
   **Verify and save**.
4. Danach zeigt *Branding & properties* die MPN-ID und den **Publisher display name** mit blauem Haken. Der Name stammt
   aus dem Partner-Center-Profil und sollte der Firmenname sein; ändern nur dort, danach *Check for updates*.

Per Skript erledigt `-MpnId <PartnerGlobal-ID>` Schritt 3; die Publisher Domain (Schritt 2) muss vorher gesetzt sein.

**Fehlersuche:** Das Portal zeigt bei vielen Prüffehlern nur *„There's a temporary problem with the service“*. Den
echten Fehlercode liefert derselbe Aufruf direkt gegen Graph (Object-ID der App, nicht die Client-ID):

```powershell
$objectId = az ad app show --id <clientId> --query id -o tsv
'{"verifiedPublisherId":"<PartnerGlobal-ID>"}' | Set-Content $env:TEMP\pv.json
az rest --method POST --uri "https://graph.microsoft.com/v1.0/applications/$objectId/setVerifiedPublisher" --headers "Content-Type=application/json" --body "@$env:TEMP\pv.json"
```

| Fehlercode | Ursache | Abhilfe |
| --- | --- | --- |
| `MPNGlobalAccountNotFound`, `MPNAccountInvalid`, `NoPublisherIdOnAssociatedMPNAccount` | falsche ID, meist die Standort-ID | ID vom Typ `PartnerGlobal` verwenden |
| `MPNAccountNotFoundOrNoAccess` | Partner-Center-Rolle fehlt oder Tenant nicht mit dem Partnerkonto verknüpft | *User management* bzw. *Tenant management* prüfen |
| `MPNAccountNotVetted` | Partner-Verifizierung nicht abgeschlossen | offene Aktionen im Partnerprofil erledigen |
| `NoPublisherDomainOnApplication`, `PublisherDomainMismatch` | Publisher Domain fehlt oder passt nicht zur Primary-Contact-Domain | Schritt 2; Domain im Tenant verifizieren |
| `NotAuthorizedToVerifyPublisher` | Entra-Rolle fehlt | *Application Administrator* zuweisen lassen |
| `InteractionRequired` | keine MFA in der Sitzung | neu anmelden mit MFA |
| `UnableToAddPublisher`, `UserUnableToAddPublisher` | Risikobewertung von Microsoft | Benutzerrisiko prüfen bzw. später erneut versuchen |

Vollständige Liste: [Troubleshoot publisher verification](https://learn.microsoft.com/entra/identity-platform/troubleshoot-publisher-verification).

## 2. Richtlinien

Mit Richtlinien gibt die Organisation die Client-ID vor; Mitarbeiter können sie in der App nicht ändern.

Registry-Schlüssel `SOFTWARE\Policies\PresenceSync` unter `HKLM` (Computer) oder `HKCU` (Benutzer). Die
Computerrichtlinie hat je Wert Vorrang vor der Benutzerrichtlinie; beide haben Vorrang vor der `config.json`.

| Wert | Typ | Wirkung |
| --- | --- | --- |
| `ClientId` | REG_SZ (GUID) | Client-ID der App-Registrierung. Wird verwendet und in den Einstellungen gesperrt („Von Ihrer Organisation vorgegeben“). |
| `AllowCustomClientId` | REG_DWORD 0/1 | 1 = `ClientId` ist nur ein Vorschlag für eine leere Einstellung. Standard 0. |
| `HomeTenantId` | REG_SZ (GUID) | Home-Tenant wird automatisch angelegt, als *Home* markiert und ist nicht entfernbar. Ist das Windows-Konto ein Mitgliedskonto dieses Tenants, meldet sich die App **ohne Benutzerinteraktion** an; sonst genügt ein Klick auf *Anmelden*. |

- Ungültige Werte (keine GUID) werden ignoriert und im Log vermerkt.
- `SOFTWARE\Policies` ist in beiden Zweigen von Windows geschützt: Standardbenutzer können die Werte weder anlegen
  noch ändern.
- Die App liest die Richtlinie beim Start und vor jedem Speichern der Einstellungen. Solange eine Richtlinie aktiv ist,
  zeigen die Einstellungen „Von Ihrer Organisation verwaltet“.
- Die automatische Anmeldung akzeptiert nur ein **Mitgliedskonto genau dieses Tenants**, keine Gast-Identität des
  Windows-Kontos in einem fremden Tenant.

### Intune mit importierter ADMX-Vorlage (empfohlen)

1. *Intune Admin Center → Geräte → Konfiguration → Importieren (ADMX)*: `deploy/admx/PresenceSync.admx` und
   `deploy/admx/en-US/PresenceSync.adml` hochladen.
2. *Richtlinie erstellen → Windows 10 und höher → Vorlagen → Importierte administrative Vorlagen*.
3. Unter *Computerkonfiguration → PresenceSync* aktivieren: **Client-ID der App-Registrierung vorgeben**, optional
   **Home-Tenant vorgeben**.
4. Der Geräte- bzw. Benutzergruppe der Mitarbeiter zuweisen.

### Intune-Plattformskript

*Geräte → Skripts und Wartungen → Plattformskripts*: [`scripts/Set-PresenceSyncPolicy.ps1`](../scripts/Set-PresenceSyncPolicy.ps1)
mit festen Parametern, *mit den Anmeldeinformationen des angemeldeten Benutzers ausführen* = **Nein** (SYSTEM),
*64-Bit-PowerShell* = **Ja**.

Manuell als Administrator:

```powershell
.\scripts\Set-PresenceSyncPolicy.ps1 -ClientId <client-id> -HomeTenantId <tenant-id>
```

Entfernen: `.\scripts\Set-PresenceSyncPolicy.ps1 -Remove`

### Gruppenrichtlinie (Active Directory)

1. `deploy/admx/PresenceSync.admx` nach `\\<domäne>\SYSVOL\<domäne>\Policies\PolicyDefinitions\` kopieren,
   `de-DE\PresenceSync.adml` und `en-US\PresenceSync.adml` in die gleichnamigen Unterordner.
2. Gruppenrichtlinienverwaltung → *Computer- (oder Benutzer-)konfiguration → Richtlinien → Administrative Vorlagen →
   PresenceSync*.

## 3. App verteilen

### Intune-Win32-App

Paketordner:

```
PresenceSync-Paket\
  PresenceSync.exe              aus dem Release, Prüfsumme kontrollieren (SECURITY.md)
  Install-PresenceSync.ps1      deploy/intune
  Uninstall-PresenceSync.ps1    deploy/intune
  Set-PresenceSyncPolicy.ps1    scripts – nur, wenn die Richtlinie mit installiert werden soll
```

Mit dem *Microsoft Win32 Content Prep Tool* zu `.intunewin` packen und in Intune anlegen:

| Einstellung | Wert |
| --- | --- |
| Installationsbefehl | `powershell.exe -ExecutionPolicy Bypass -NoProfile -File .\Install-PresenceSync.ps1` (optional `-ClientId <guid> -HomeTenantId <guid>`) |
| Deinstallationsbefehl | `powershell.exe -ExecutionPolicy Bypass -NoProfile -File .\Uninstall-PresenceSync.ps1` |
| Installationsverhalten | System |
| Erkennungsregel | Datei `%ProgramFiles%\PresenceSync\PresenceSync.exe` vorhanden |
| Anforderungen | Windows 10 1809+ oder Windows 11, x64 |

Das Installationsskript legt die App nach `%ProgramFiles%\PresenceSync` und trägt einen Autostart für alle Benutzer
ein. Die Richtlinie besser getrennt per ADMX verwalten – dann lässt sie sich ändern, ohne die App neu zu verteilen.

> Die Exe ist noch nicht Authenticode-signiert ([security-review.md](security-review.md), Befund 5). Mit
> AppLocker/WDAC muss sie bis dahin per Hash freigegeben werden. SmartScreen-Warnungen treten bei Verteilung über
> Intune in der Regel nicht auf, da die Datei nicht aus dem Internet heruntergeladen wird.

### Was Mitarbeiter danach erleben

1. PresenceSync startet nach der Windows-Anmeldung im Infobereich.
2. Mit `HomeTenantId` ist der Home-Tenant eingerichtet und meist bereits angemeldet.
3. Kunden-Tenants fügen sie selbst hinzu (nächster Abschnitt).

## 4. Kunden-Tenants anbinden

1. *Tray-Symbol → Zahnrad → Tenant hinzufügen → Konto auswählen …*, Konto des Kunden-Tenants wählen, **Speichern**.
   Gastkonten (B2B): vorher unter *Erweitert* die Tenant-ID oder Domain des Kunden eintragen.
2. Erlaubt der Kunden-Tenant die Zustimmung durch Benutzer, bestätigt der Mitarbeiter den Consent-Dialog selbst.
3. Sonst erscheint „Administratorgenehmigung erforderlich“. Dann die Kunden-IT mit der
   [Anfrage-Vorlage](consent-request-template.md) und [consent.md](consent.md) ansprechen – mit **Ihrer** Client-ID
   und Ihrem Herausgebernamen eingesetzt. Nach der Freigabe den Tenant erneut hinzufügen.

## 5. Prüfen und Fehlerbilder

- Richtlinie: `Get-ItemProperty HKLM:\SOFTWARE\Policies\PresenceSync`
- App: Einstellungen zeigen „Von Ihrer Organisation verwaltet“, die Client-ID ist ausgegraut.
- Log (`%LOCALAPPDATA%\PresenceSync\logs`, Tray-Fenster → Dokument-Symbol): `Richtlinie aktiv: …`,
  `Konfiguration aktiv: N Tenant(s)`, pro Tenant `verbunden`; bei automatischer Anmeldung
  `Automatisch mit Windows-Konto angemeldet`.

Steht bei einem Tenant „Anmeldung erforderlich“ oder „Gesperrt“, nennt die App den Grund im Klartext:

| Code | Bedeutung | Lösung |
| --- | --- | --- |
| `AADSTS65001` | Zustimmung fehlt | *Anmelden* und zustimmen |
| `AADSTS90094` | Administratorgenehmigung erforderlich | Kunden-IT, siehe Abschnitt 4 |
| `AADSTS50105` | Zuweisung erforderlich, Benutzer nicht zugewiesen | Kunden-IT weist den Benutzer der Unternehmensanwendung zu |
| `AADSTS50076` | MFA erforderlich | *Anmelden* |
| `AADSTS53000` / `53001` | Conditional Access verlangt ein konformes bzw. eingebundenes Gerät | mit diesem Gerät nicht nutzbar |
| HTTP 403 | kein Teams-/Presence-Zugriff (Lizenz) oder Zustimmung widerrufen | Lizenz bzw. Zustimmung prüfen |
