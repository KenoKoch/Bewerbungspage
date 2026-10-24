-- ─────────────────────────────────────────────────────────────────────────────
--  Einmalig nach dem ersten Bicep-Deployment ausführen – angemeldet als
--  Microsoft-Entra-Administrator des SQL Servers (Azure-Portal → Datenbank →
--  Abfrage-Editor, oder: sqlcmd -S <server>.database.windows.net -d sqldb-kenokoch --authentication-method ActiveDirectoryDefault).
--
--  <web-app-name> durch die Bicep-Ausgabe "webAppName" ersetzen, z. B. app-kenokoch-a1b2c3.
-- ─────────────────────────────────────────────────────────────────────────────

-- Benutzer für die Managed Identity der Web App anlegen (passwortlos)
CREATE USER [<web-app-name>] FROM EXTERNAL PROVIDER;

-- Lesen und Schreiben der Moduldaten
ALTER ROLE db_datareader ADD MEMBER [<web-app-name>];
ALTER ROLE db_datawriter ADD MEMBER [<web-app-name>];

-- Schemas und Tabellen anlegen: Die Anwendung wendet die EF-Core-Migrationen beim Start selbst an.
-- Wer Migrationen lieber in der Pipeline ausführt, setzt "Database:MigrateOnStartup" auf false und lässt diese Zeile weg.
ALTER ROLE db_ddladmin ADD MEMBER [<web-app-name>];
