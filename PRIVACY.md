# Datenschutzhinweise PresenceSync

*Stand: 15.10.2026. Diese Hinweise beschreiben die Datenverarbeitung durch die Software. Verantwortlich im Sinne der
DSGVO für den Einsatz ist jeweils die Person bzw. Organisation, die PresenceSync auf ihrem Gerät betreibt.*

## Herausgeber

`<Firma, Anschrift, Kontakt>`

## Grundsatz

PresenceSync läuft ausschließlich lokal auf dem Windows-Gerät des Benutzers. Der Herausgeber betreibt **keinen
Server**, erhält **keine** Daten und erhebt **keine Telemetrie** oder Nutzungsstatistik.

## Welche Daten verarbeitet die App?

| Daten | Zweck | Speicherort | Speicherdauer |
| --- | --- | --- | --- |
| Teams-Status (Verfügbarkeit, Aktivität) des Benutzers in seinen Tenants | Abgleich des Status zwischen den Tenants des Benutzers | nur Arbeitsspeicher | bis zum nächsten Abruf |
| Tenant-ID, Anzeigename, Anmeldename (UPN) je Tenant | Zuordnung der Anmeldungen | `%APPDATA%\PresenceSync\config.json` | bis der Benutzer den Tenant entfernt oder die App deinstalliert |
| Anmelde-Metadaten (Konto, Token-Cache) | Anmeldung ohne ständige Neueingabe | `%LOCALAPPDATA%\PresenceSync\msal.cache` (verschlüsselt mit Windows-DPAPI) und Windows-Token-Broker | bis Abmeldung/Ablauf/Deinstallation |
| Betriebsprotokoll (Zeitpunkte, Tenant-Anzeigenamen, Statuswerte, Fehlermeldungen; UPNs maskiert, keine Token) | Fehlersuche | `%LOCALAPPDATA%\PresenceSync\logs` | 14 Tage, danach automatisch gelöscht |

Personenbezogene Daten **anderer** Personen (Kollegen, Gesprächspartner, Besprechungsteilnehmer) werden nicht
verarbeitet.

## Empfänger

Die App kommuniziert ausschließlich mit Microsoft (Microsoft Entra ID zur Anmeldung, Microsoft Graph zum Lesen und
Setzen des eigenen Status) innerhalb der Tenants, in denen der Benutzer ein Konto hat. Es gelten die Vereinbarungen
des jeweiligen Tenants mit Microsoft. Zwischen den Tenants wird nur das abgeleitete Signal „belegt/nicht belegt“
übertragen.

## Rechte und Löschung

Alle Daten liegen beim Benutzer. Löschen: Tenant in den Einstellungen entfernen bzw. die Ordner
`%APPDATA%\PresenceSync` und `%LOCALAPPDATA%\PresenceSync` löschen. Die Berechtigung der App kann jederzeit unter
<https://myapps.microsoft.com> (eigene Apps) bzw. vom Tenant-Administrator widerrufen werden.

## Kontakt

`<Datenschutzkontakt>`
