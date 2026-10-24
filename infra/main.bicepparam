using 'main.bicep'

// Kurzname für alle Ressourcen
param appName = 'kenokoch'

// Microsoft-Entra-Administrator für Azure SQL.
// Als Name genügt ein Anzeigename – entscheidend ist die Objekt-ID deiner Anmeldung:
//   az ad signed-in-user show --query id -o tsv
param sqlAdminLogin = 'Keno Koch'
param sqlAdminObjectId = '<objekt-id-aus-microsoft-entra>'

// Kostenloses Azure-SQL-Kontingent (nur eine Datenbank pro Abonnement)
param useSqlFreeLimit = true
param sqlFreeLimitExhaustionBehavior = 'AutoPause'

// Erst setzen, wenn die Domain registriert ist und die DNS-Einträge angelegt sind (siehe README)
param customDomain = ''
param includeWww = true
