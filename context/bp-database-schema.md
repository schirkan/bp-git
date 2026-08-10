# Blue Prism DB-Schema (Tabellenstruktur)

> **Quelle:** Live-Extraktion aus Blue Prism Community Edition v7.5.1.18099 auf `OpenClawPC`, 2026-08-10 15:13.

> **Verbindung:** `(localdb)\BluePrismLocalDB` / DB `BluePrism` / Windows Integrated Auth (kein SQL-Passwort involviert).

> **Rohdaten:** `temp/bp-schema-blueprism-raw.json` (rund 535 KB, vollstÃ¤ndig programmatisch auswertbar).

> **Workboard:** Karte `Recherche A: DB-Schema` (`46d5183d-â€¦`) â€” Status `complete`.

## Eckdaten

| Metrik | Wert |
|---|---|
| BP-Version | 7.5.1.18099 |
| SQL-Server | 17.00.4025 (LocalDB) |
| BPA*-Tabellen | 181 |
| Spalten (BPA*) | 1018 |
| Primary Keys | 210 |
| Foreign Keys | 193 |
| Identity-Spalten | 72 |

## Tabellen-Kategorisierung (fÃ¼r den BP-Git-Adapter)

Welche BPA*-Tabellen fÃ¼r welche DomÃ¤ne zustÃ¤ndig sind:

### Process / Process-Dependencies
- `BPAInternalAuth`
- `BPAProcess`
- `BPAProcessActionDependency`
- `BPAProcessAlert`
- `BPAProcessAttribute`
- `BPAProcessBackup`
- `BPAProcessCalendarDependency`
- `BPAProcessCredentialsDependency`
- `BPAProcessElementDependency`
- `BPAProcessEnvironmentVarDependency`
- `BPAProcessEnvVar`
- `BPAProcessFontDependency`
- `BPAProcessIDDependency`
- `BPAProcessLock`
- `BPAProcessMITemplate`
- `BPAProcessNameDependency`
- `BPAProcessParentDependency`
- `BPAProcessQueueDependency`
- `BPAProcessSkillDependency`
- `BPAProcessWebApiDependency`
- `BPAProcessWebServiceDependency`

### Business Objects / Package
- `BPAPackage`
- `BPAPackage`
- `BPAPackageCalendar`
- `BPAPackageCredential`
- `BPAPackageDashboard`
- `BPAPackageEnvironmentVar`
- `BPAPackageFont`
- `BPAPackageProcess`
- `BPAPackageSchedule`
- `BPAPackageScheduleList`
- `BPAPackageTile`
- `BPAPackageWebApi`
- `BPAPackageWebService`
- `BPAPackageWorkQueue`

### Release
- `BPARelease`
- `BPAReleaseEntry`

### Schedule / Trigger
- `BPASchedule`
- `BPAScheduleAlert`
- `BPAScheduleList`
- `BPAScheduleListSchedule`
- `BPAScheduleLog`
- `BPAScheduleLogEntry`
- `BPAScheduleTrigger`

### Session / Runtime
- `BPASession`
- `BPASessionSource`
- `BPASessionLog_Unicode`
- `BPASessionLog_NonUnicode`
- `BPASessionLog_Unicode_pre65`
- `BPASessionLog_NonUnicode_pre65`
- `BPATask`
- `BPATaskSession`
- `BPAAliveResources`
- `BPAAliveAutomateC`
- `BPAStatistics`
- `BPAStatus`

### Resource / Resource-PC
- `BPAResource`
- `BPAResourceAttribute`
- `BPAResourceConfig`
- `BPAHybridResource`

### Environment / Credentials
- `BPAEnvironment`
- `BPAEnvironmentType`
- `BPAEnvironmentVar`
- `BPAEnvLock`
- `BPACredentials`
- `BPACredentialsProcesses`
- `BPACredentialsProperties`
- `BPACredentialsResources`
- `BPACredentialRole`
- `BPAPassword`
- `BPAPasswordRules`
- `BPAKeyStore`

### Work Queue
- `BPAWorkQueue`
- `BPAWorkQueueFilter`
- `BPAWorkQueueItem`
- `BPAWorkQueueItemAggregate`
- `BPAWorkQueueItemTag`
- `BPAWorkQueueLog`

### User / Group / Permission
- `BPAUser`
- `BPAUserExternalIdentity`
- `BPAUserExternalReloginToken`
- `BPAUserRole`
- `BPAUserRoleAssignment`
- `BPAUserRolePerm`
- `BPAGroup`
- `BPAGroupGroup`
- `BPAGroupProcess`
- `BPAGroupQueue`
- `BPAGroupResource`
- `BPAGroupTile`
- `BPAGroupUser`
- `BPAGroupUserPref`
- `BPAGroupUserRolePerm`
- `BPAPerm`
- `BPAPermGroup`
- `BPAPermGroupMember`
- `BPASSOGroupRoleMapping`
- `BPAUnmappedDomainName`
- `BPAUnmappedSSOGroups`
- `BPAPassword`
- `BPAPasswordRules`

### License / MI
- `BPALicense`
- `BPALicenseActivationRequest`
- `BPAMIControl`

### Sync (Devices / Folders)
- `BPASyncActive`
- `BPASyncCheckpoint`
- `BPASyncFolder`
- `BPASyncMetricsConditions`
- `BPASyncMetricsConditionsTags`
- `BPASyncProcess`
- `BPASyncResource`
- `BPASyncSetting`

### WebAPI / WebService / Webhook
- `BPAWebApiAction`
- `BPAWebApiService`
- `BPAWebApiHeader`
- `BPAWebApiParameter`
- `BPAWebApiCustomOutputParameter`
- `BPAWebService`
- `BPAWebServiceAsset`
- `BPAWebhooks`
- `BPAWebhooksSettings`
- `BPAWebhookSubscriptions`
- `BPAWebSkillVersion`

### Data Pipeline
- `BPADataPipelineInput`
- `BPADataPipelineProcess`
- `BPADataPipelineProcessConfig`
- `BPADataPipelineOutputConfig`
- `BPADataPipelineSettings`

### Validation Catalog
- `BPAValAction`
- `BPAValActionMap`
- `BPAValCategory`
- `BPAValCheck`
- `BPAValType`

### Dashboard / Reporting
- `BPADashboard`
- `BPADashboardTile`
- `BPATile`
- `BPATileDataSources`
- `BPAReport`

### Calendar / Holiday
- `BPACalendar`
- `BPANonWorkingDay`
- `BPAPublicHoliday`
- `BPAPublicHolidayGroup`
- `BPAPublicHolidayGroupMember`
- `BPAPublicHolidayShiftDayTypes`
- `BPAPublicHolidayWorkingDay`

### Audit / Cache / Diagnostics
- `BPAAuditEvents`
- `BPACache`
- `BPACacheETags`
- `BPACaseLock`
- `BPADBVersion`
- `BPADBMaintenanceControl`
- `BPADBMaintenanceScriptParameters`
- `BPADBMaintenanceScripts`
- `BPADataTracker`
- `BPAExceptionType`
- `BPAScreenshot`
- `BPASnapshotConfiguration`

### Skill / Spy-Mode / External
- `BPASkill`
- `BPASkillVersion`
- `BPAExternalProvider`
- `BPAExternalProviderType`
- `BPADefaultAttributeSet`
- `BPADefaultAttributeSetAttribute`
- `BPADefaultAttributeSettings`
- `BPADefaultAttributeSetUserSpyMode`

### Active Directory
- `BPAActiveDirectoryDomains`

### Tree
- `BPATree`
- `BPATreeDefaultGroup`
- `BPATreePerm`
- `BPAToolPosition`

### Tag / Storage
- `BPATag`
- `BPAFont`
- `BPAFontOCRPlusPlus`

### Package-Storage
- `BPAPackageCalendar`
- `BPAPackageCredential`
- `BPAPackageDashboard`
- `BPAPackageEnvironmentVar`
- `BPAPackageFont`
- `BPAPackageProcess`
- `BPAPackageSchedule`
- `BPAPackageScheduleList`
- `BPAPackageTile`
- `BPAPackageWebApi`
- `BPAPackageWebService`
- `BPAPackageWorkQueue`

### Preferences / System-Config
- `BPAIntegerPref`
- `BPAPref`
- `BPAStringPref`
- `BPASysConfig`
- `BPASysWebConnectionSettings`
- `BPASysWebUrlSettings`

### Alert / Notification
- `BPAAlertEvent`
- `BPAAlertsMachines`
- `BPAProcessAlert`

### License-Activation / Status
- `BPAMIControl`

## Alle 181 BPA*-Tabellen (sortiert nach Row-Count, descending)

| # | Tabelle | Row Count |
|---:|---|---:|
| 1 | `BPADBVersion` | 547 |
| 2 | `BPAUserRolePerm` | 229 |
| 3 | `BPAValCheck` | 131 |
| 4 | `BPAPublicHolidayGroupMember` | 111 |
| 5 | `BPAPermGroupMember` | 109 |
| 6 | `BPAPerm` | 108 |
| 7 | `BPAPublicHoliday` | 79 |
| 8 | `BPATreePerm` | 29 |
| 9 | `BPAPref` | 24 |
| 10 | `BPAIntegerPref` | 24 |
| 11 | `BPATileDataSources` | 17 |
| 12 | `BPATile` | 17 |
| 13 | `BPAPublicHolidayGroup` | 12 |
| 14 | `BPAValActionMap` | 12 |
| 15 | `BPAUserRole` | 11 |
| 16 | `BPAResourceAttribute` | 11 |
| 17 | `BPAPermGroup` | 10 |
| 18 | `BPAStatus` | 9 |
| 19 | `BPATree` | 6 |
| 20 | `BPAEnvironmentType` | 5 |
| 21 | `BPAValCategory` | 4 |
| 22 | `BPADashboardTile` | 4 |
| 23 | `BPAValAction` | 4 |
| 24 | `BPAProcessAttribute` | 4 |
| 25 | `BPAPublicHolidayShiftDayTypes` | 4 |
| 26 | `BPAUser` | 3 |
| 27 | `BPATreeDefaultGroup` | 3 |
| 28 | `BPAGroup` | 3 |
| 29 | `BPACacheETags` | 3 |
| 30 | `BPASessionSource` | 3 |
| 31 | `BPADataTracker` | 3 |
| 32 | `BPAValType` | 3 |
| 33 | `BPAScheduleList` | 2 |
| 34 | `BPAUserRoleAssignment` | 2 |
| 35 | `BPAAuditEvents` | 2 |
| 36 | `BPAPasswordRules` | 1 |
| 37 | `BPAResource` | 1 |
| 38 | `BPASchedule` | 1 |
| 39 | `BPAKeyStore` | 1 |
| 40 | `BPATask` | 1 |
| 41 | `BPALicense` | 1 |
| 42 | `BPAMIControl` | 1 |
| 43 | `BPAPassword` | 1 |
| 44 | `BPASysConfig` | 1 |
| 45 | `BPASysWebConnectionSettings` | 1 |
| 46 | `BPACalendar` | 1 |
| 47 | `BPAEnvironment` | 1 |
| 48 | `BPADashboard` | 1 |
| 49 | `BPAAliveResources` | 1 |
| 50 | `BPADBMaintenanceControl` | 1 |
| 51 | `BPADataPipelineSettings` | 1 |
| 52 | `BPAGroupResource` | 1 |
| 53 | `BPAWebhooksSettings` | 1 |
| 54 | `BPAScheduleTrigger` | 0 |
| 55 | `BPASession` | 0 |
| 56 | `BPASessionLog_NonUnicode` | 0 |
| 57 | `BPAScreenshot` | 0 |
| 58 | `BPASessionLog_NonUnicode_pre65` | 0 |
| 59 | `BPASkill` | 0 |
| 60 | `BPASkillVersion` | 0 |
| 61 | `BPAWebServiceAsset` | 0 |
| 62 | `BPASessionLog_Unicode` | 0 |
| 63 | `BPASessionLog_Unicode_pre65` | 0 |
| 64 | `BPAWebSkillVersion` | 0 |
| 65 | `BPAScheduleLogEntry` | 0 |
| 66 | `BPAScenario` | 0 |
| 67 | `BPAScenarioDetail` | 0 |
| 68 | `BPAScenarioLink` | 0 |
| 69 | `BPAWorkQueueLog` | 0 |
| 70 | `BPAWorkQueueItemTag` | 0 |
| 71 | `BPAResourceConfig` | 0 |
| 72 | `BPAWorkQueueItemAggregate` | 0 |
| 73 | `BPAWorkQueue` | 0 |
| 74 | `BPAScheduleListSchedule` | 0 |
| 75 | `BPAScheduleLog` | 0 |
| 76 | `BPAWorkQueueItem` | 0 |
| 77 | `BPAWorkQueueFilter` | 0 |
| 78 | `BPAScheduleAlert` | 0 |
| 79 | `BPASnapshotConfiguration` | 0 |
| 80 | `BPASysWebUrlSettings` | 0 |
| 81 | `BPATag` | 0 |
| 82 | `BPAWebApiCustomOutputParameter` | 0 |
| 83 | `BPASyncResource` | 0 |
| 84 | `BPASyncSetting` | 0 |
| 85 | `BPAWebApiHeader` | 0 |
| 86 | `BPAWebApiAction` | 0 |
| 87 | `BPAUnmappedSSOGroups` | 0 |
| 88 | `BPAToolPosition` | 0 |
| 89 | `BPAUnmappedDomainName` | 0 |
| 90 | `BPATaskSession` | 0 |
| 91 | `BPAUserExternalReloginToken` | 0 |
| 92 | `BPAUserExternalIdentity` | 0 |
| 93 | `BPASyncProcess` | 0 |
| 94 | `BPAWebhookSubscriptions` | 0 |
| 95 | `BPAWebhooks` | 0 |
| 96 | `BPAWebApiService` | 0 |
| 97 | `BPASSOGroupRoleMapping` | 0 |
| 98 | `BPAStatistics` | 0 |
| 99 | `BPAWebService` | 0 |
| 100 | `BPAWebApiParameter` | 0 |
| 101 | `BPASyncFolder` | 0 |
| 102 | `BPASyncMetricsConditions` | 0 |
| 103 | `BPASyncMetricsConditionsTags` | 0 |
| 104 | `BPAStringPref` | 0 |
| 105 | `BPASyncActive` | 0 |
| 106 | `BPASyncCheckpoint` | 0 |
| 107 | `BPAExternalProviderType` | 0 |
| 108 | `BPAExternalProvider` | 0 |
| 109 | `BPAFontOCRPlusPlus` | 0 |
| 110 | `BPAFont` | 0 |
| 111 | `BPAExceptionType` | 0 |
| 112 | `BPADefaultAttributeSetUserSpyMode` | 0 |
| 113 | `BPADefaultAttributeSettings` | 0 |
| 114 | `BPAEnvLock` | 0 |
| 115 | `BPAEnvironmentVar` | 0 |
| 116 | `BPAGroupUserRolePerm` | 0 |
| 117 | `BPAGroupUserPref` | 0 |
| 118 | `BPAInternalAuth` | 0 |
| 119 | `BPAHybridResource` | 0 |
| 120 | `BPAGroupUser` | 0 |
| 121 | `BPAGroupProcess` | 0 |
| 122 | `BPAGroupGroup` | 0 |
| 123 | `BPAGroupTile` | 0 |
| 124 | `BPAGroupQueue` | 0 |
| 125 | `BPADefaultAttributeSetAttribute` | 0 |
| 126 | `BPACredentialRole` | 0 |
| 127 | `BPACaseLock` | 0 |
| 128 | `BPACredentialsProcesses` | 0 |
| 129 | `BPACredentials` | 0 |
| 130 | `BPACache` | 0 |
| 131 | `BPAAlertEvent` | 0 |
| 132 | `BPAActiveDirectoryDomains` | 0 |
| 133 | `BPAAliveAutomateC` | 0 |
| 134 | `BPAAlertsMachines` | 0 |
| 135 | `BPADBMaintenanceScriptParameters` | 0 |
| 136 | `BPADataPipelineProcessConfig` | 0 |
| 137 | `BPADefaultAttributeSet` | 0 |
| 138 | `BPADBMaintenanceScripts` | 0 |
| 139 | `BPADataPipelineProcess` | 0 |
| 140 | `BPACredentialsResources` | 0 |
| 141 | `BPACredentialsProperties` | 0 |
| 142 | `BPADataPipelineOutputConfig` | 0 |
| 143 | `BPADataPipelineInput` | 0 |
| 144 | `BPALicenseActivationRequest` | 0 |
| 145 | `BPAProcessLock` | 0 |
| 146 | `BPAProcessIDDependency` | 0 |
| 147 | `BPAProcessNameDependency` | 0 |
| 148 | `BPAProcessMITemplate` | 0 |
| 149 | `BPAProcessFontDependency` | 0 |
| 150 | `BPAProcessElementDependency` | 0 |
| 151 | `BPAProcessCredentialsDependency` | 0 |
| 152 | `BPAProcessEnvVar` | 0 |
| 153 | `BPAProcessEnvironmentVarDependency` | 0 |
| 154 | `BPARelease` | 0 |
| 155 | `BPAPublicHolidayWorkingDay` | 0 |
| 156 | `BPAReport` | 0 |
| 157 | `BPAReleaseEntry` | 0 |
| 158 | `BPAProcessWebServiceDependency` | 0 |
| 159 | `BPAProcessQueueDependency` | 0 |
| 160 | `BPAProcessParentDependency` | 0 |
| 161 | `BPAProcessWebApiDependency` | 0 |
| 162 | `BPAProcessSkillDependency` | 0 |
| 163 | `BPAProcessCalendarDependency` | 0 |
| 164 | `BPAPackageFont` | 0 |
| 165 | `BPAPackageEnvironmentVar` | 0 |
| 166 | `BPAPackageSchedule` | 0 |
| 167 | `BPAPackageProcess` | 0 |
| 168 | `BPAPackageDashboard` | 0 |
| 169 | `BPAPackage` | 0 |
| 170 | `BPANonWorkingDay` | 0 |
| 171 | `BPAPackageCredential` | 0 |
| 172 | `BPAPackageCalendar` | 0 |
| 173 | `BPAProcessActionDependency` | 0 |
| 174 | `BPAProcess` | 0 |
| 175 | `BPAProcessBackup` | 0 |
| 176 | `BPAProcessAlert` | 0 |
| 177 | `BPAPackageWorkQueue` | 0 |
| 178 | `BPAPackageTile` | 0 |
| 179 | `BPAPackageScheduleList` | 0 |
| 180 | `BPAPackageWebService` | 0 |
| 181 | `BPAPackageWebApi` | 0 |

## Identity-Spalten (auto-increment, 72)

Diese Spalten sind die natÃ¼rlichen SchlÃ¼ssel fÃ¼r Snapshot-/Diff-Vergleiche.

| Tabelle | Spalte | Datentyp |
|---|---|---|
| `BPAActiveDirectoryDomains` | `ID` | int |
| `BPAAlertEvent` | `AlertEventID` | int |
| `BPAAuditEvents` | `eventid` | int |
| `BPACalendar` | `id` | int |
| `BPADataPipelineInput` | `id` | bigint |
| `BPADataPipelineOutputConfig` | `id` | int |
| `BPADataPipelineProcess` | `id` | int |
| `BPADataPipelineProcessConfig` | `id` | int |
| `BPADBMaintenanceScriptParameters` | `id` | int |
| `BPADBMaintenanceScripts` | `id` | int |
| `BPADefaultAttributeSet` | `DefaultAttributeSetId` | int |
| `BPADefaultAttributeSetAttribute` | `Id` | int |
| `BPADefaultAttributeSettings` | `id` | int |
| `BPADefaultAttributeSetUserSpyMode` | `Id` | int |
| `BPAEnvironment` | `Id` | int |
| `BPAExternalProvider` | `id` | int |
| `BPAExternalProviderType` | `id` | int |
| `BPAInternalAuth` | `ID` | bigint |
| `BPAKeyStore` | `id` | int |
| `BPALicense` | `id` | int |
| `BPALicenseActivationRequest` | `RequestId` | int |
| `BPAPackage` | `id` | int |
| `BPAPassword` | `id` | int |
| `BPAPerm` | `id` | int |
| `BPAPermGroup` | `id` | int |
| `BPAPref` | `id` | int |
| `BPAProcessActionDependency` | `id` | int |
| `BPAProcessCalendarDependency` | `id` | int |
| `BPAProcessCredentialsDependency` | `id` | int |
| `BPAProcessElementDependency` | `id` | int |
| `BPAProcessEnvironmentVarDependency` | `id` | int |
| `BPAProcessFontDependency` | `id` | int |
| `BPAProcessIDDependency` | `id` | int |
| `BPAProcessNameDependency` | `id` | int |
| `BPAProcessParentDependency` | `id` | int |
| `BPAProcessQueueDependency` | `id` | int |
| `BPAProcessSkillDependency` | `id` | int |
| `BPAProcessWebApiDependency` | `id` | int |
| `BPAProcessWebServiceDependency` | `id` | int |
| `BPAPublicHolidayGroup` | `id` | int |
| `BPARelease` | `id` | int |
| `BPAReleaseEntry` | `id` | int |
| `BPASchedule` | `id` | int |
| `BPAScheduleList` | `id` | int |
| `BPAScheduleLog` | `id` | int |
| `BPAScheduleLogEntry` | `id` | bigint |
| `BPAScheduleTrigger` | `id` | int |
| `BPAScreenshot` | `id` | int |
| `BPASession` | `sessionnumber` | int |
| `BPASessionLog_NonUnicode` | `logid` | bigint |
| `BPASessionLog_Unicode` | `logid` | bigint |
| `BPASnapshotConfiguration` | `id` | int |
| `BPASSOGroupRoleMapping` | `ID` | int |
| `BPASyncMetricsConditions` | `id` | int |
| `BPATag` | `id` | int |
| `BPATask` | `id` | int |
| `BPATaskSession` | `id` | int |
| `BPATreeDefaultGroup` | `id` | int |
| `BPATreePerm` | `id` | int |
| `BPAUnmappedDomainName` | `ID` | int |
| `BPAUnmappedSSOGroups` | `ID` | int |
| `BPAUserExternalReloginToken` | `id` | bigint |
| `BPAUserRole` | `id` | int |
| `BPAWebApiAction` | `actionid` | int |
| `BPAWebApiCustomOutputParameter` | `id` | int |
| `BPAWebApiHeader` | `headerid` | int |
| `BPAWebApiParameter` | `parameterid` | int |
| `BPAWebhooks` | `ident` | bigint |
| `BPAWebhookSubscriptions` | `ident` | bigint |
| `BPAWorkQueue` | `ident` | int |
| `BPAWorkQueueItem` | `ident` | bigint |
| `BPAWorkQueueLog` | `logid` | bigint |

## Foreign-Key-Beziehungen (193)

| Tabelle | Spalte | Ref-Tabelle | Ref-Spalte |
|---|---|---|---|
| `BPAActiveDirectoryDomains` | `encryptid` | `BPAKeyStore` | `id` |
| `BPAAlertEvent` | `SessionID` | `BPASession` | `sessionid` |
| `BPAAlertEvent` | `ProcessID` | `BPAProcess` | `processid` |
| `BPAAlertEvent` | `ResourceID` | `BPAResource` | `resourceid` |
| `BPAAlertEvent` | `scheduleid` | `BPASchedule` | `id` |
| `BPAAlertEvent` | `taskid` | `BPATask` | `id` |
| `BPACalendar` | `publicholidaygroupid` | `BPAPublicHolidayGroup` | `id` |
| `BPACaseLock` | `sessionid` | `BPASession` | `sessionid` |
| `BPACaseLock` | `id` | `BPAWorkQueueItem` | `ident` |
| `BPACredentialRole` | `credentialid` | `BPACredentials` | `id` |
| `BPACredentialRole` | `userroleid` | `BPAUserRole` | `id` |
| `BPACredentials` | `encryptid` | `BPAKeyStore` | `id` |
| `BPACredentialsProcesses` | `credentialid` | `BPACredentials` | `id` |
| `BPACredentialsProcesses` | `processid` | `BPAProcess` | `processid` |
| `BPACredentialsProperties` | `credentialid` | `BPACredentials` | `id` |
| `BPACredentialsResources` | `credentialid` | `BPACredentials` | `id` |
| `BPACredentialsResources` | `resourceid` | `BPAResource` | `resourceid` |
| `BPADashboard` | `userid` | `BPAUser` | `userid` |
| `BPADashboardTile` | `dashid` | `BPADashboard` | `id` |
| `BPADashboardTile` | `tileid` | `BPATile` | `id` |
| `BPADataPipelineProcess` | `config` | `BPADataPipelineProcessConfig` | `id` |
| `BPADataPipelineProcessConfig` | `encryptid` | `BPAKeyStore` | `id` |
| `BPADBMaintenanceScriptParameters` | `sprocid` | `BPADBMaintenanceScripts` | `id` |
| `BPADefaultAttributeSetAttribute` | `DefaultAttributeSetId` | `BPADefaultAttributeSet` | `DefaultAttributeSetId` |
| `BPADefaultAttributeSettings` | `UserID` | `BPAUser` | `userid` |
| `BPADefaultAttributeSetUserSpyMode` | `DefaultAttributeSetId` | `BPADefaultAttributeSet` | `DefaultAttributeSetId` |
| `BPADefaultAttributeSetUserSpyMode` | `UserId` | `BPAUser` | `userid` |
| `BPAEnvironment` | `ApplicationServerId` | `BPAEnvironment` | `Id` |
| `BPAEnvironment` | `EnvironmentTypeId` | `BPAEnvironmentType` | `Id` |
| `BPAEnvLock` | `sessionid` | `BPASession` | `sessionid` |
| `BPAExternalProvider` | `externalprovidertypeid` | `BPAExternalProviderType` | `id` |
| `BPAGroup` | `treeid` | `BPATree` | `id` |
| `BPAGroupGroup` | `groupid` | `BPAGroup` | `id` |
| `BPAGroupGroup` | `memberid` | `BPAGroup` | `id` |
| `BPAGroupProcess` | `groupid` | `BPAGroup` | `id` |
| `BPAGroupProcess` | `processid` | `BPAProcess` | `processid` |
| `BPAGroupQueue` | `groupid` | `BPAGroup` | `id` |
| `BPAGroupQueue` | `memberid` | `BPAWorkQueue` | `ident` |
| `BPAGroupResource` | `groupid` | `BPAGroup` | `id` |
| `BPAGroupResource` | `memberid` | `BPAResource` | `resourceid` |
| `BPAGroupTile` | `groupid` | `BPAGroup` | `id` |
| `BPAGroupTile` | `tileid` | `BPATile` | `id` |
| `BPAGroupUser` | `groupid` | `BPAGroup` | `id` |
| `BPAGroupUser` | `memberid` | `BPAUser` | `userid` |
| `BPAGroupUserPref` | `GroupId` | `BPAGroup` | `id` |
| `BPAGroupUserRolePerm` | `groupid` | `BPAGroup` | `id` |
| `BPAGroupUserRolePerm` | `permid` | `BPAPerm` | `id` |
| `BPAGroupUserRolePerm` | `userroleid` | `BPAUserRole` | `id` |
| `BPAGroupUserRolePerm` | `userroleid` | `BPAUserRolePerm` | `userroleid` |
| `BPAGroupUserRolePerm` | `permid` | `BPAUserRolePerm` | `permid` |
| `BPAIntegerPref` | `prefid` | `BPAPref` | `id` |
| `BPAInternalAuth` | `ProcessId` | `BPAProcess` | `processid` |
| `BPALicense` | `installedby` | `BPAUser` | `userid` |
| `BPALicenseActivationRequest` | `LicenseId` | `BPALicense` | `id` |
| `BPALicenseActivationRequest` | `UserId` | `BPAUser` | `userid` |
| `BPANonWorkingDay` | `calendarid` | `BPACalendar` | `id` |
| `BPAPackageCalendar` | `calendarid` | `BPACalendar` | `id` |
| `BPAPackageCalendar` | `packageid` | `BPAPackage` | `id` |
| `BPAPackageCredential` | `credentialid` | `BPACredentials` | `id` |
| `BPAPackageCredential` | `packageid` | `BPAPackage` | `id` |
| `BPAPackageDashboard` | `dashid` | `BPADashboard` | `id` |
| `BPAPackageDashboard` | `packageid` | `BPAPackage` | `id` |
| `BPAPackageEnvironmentVar` | `name` | `BPAEnvironmentVar` | `name` |
| `BPAPackageEnvironmentVar` | `packageid` | `BPAPackage` | `id` |
| `BPAPackageFont` | `name` | `BPAFont` | `name` |
| `BPAPackageFont` | `packageid` | `BPAPackage` | `id` |
| `BPAPackageProcess` | `packageid` | `BPAPackage` | `id` |
| `BPAPackageProcess` | `processid` | `BPAProcess` | `processid` |
| `BPAPackageSchedule` | `packageid` | `BPAPackage` | `id` |
| `BPAPackageSchedule` | `scheduleid` | `BPASchedule` | `id` |
| `BPAPackageScheduleList` | `packageid` | `BPAPackage` | `id` |
| `BPAPackageScheduleList` | `schedulelistid` | `BPAScheduleList` | `id` |
| `BPAPackageTile` | `packageid` | `BPAPackage` | `id` |
| `BPAPackageTile` | `tileid` | `BPATile` | `id` |
| `BPAPackageWebApi` | `packageid` | `BPAPackage` | `id` |
| `BPAPackageWebApi` | `webapiid` | `BPAWebApiService` | `serviceid` |
| `BPAPackageWebService` | `packageid` | `BPAPackage` | `id` |
| `BPAPackageWebService` | `webserviceid` | `BPAWebService` | `serviceid` |
| `BPAPackageWorkQueue` | `packageid` | `BPAPackage` | `id` |
| `BPAPackageWorkQueue` | `queueident` | `BPAWorkQueue` | `ident` |
| `BPAPassword` | `userid` | `BPAUser` | `userid` |
| `BPAPermGroupMember` | `permid` | `BPAPerm` | `id` |
| `BPAPermGroupMember` | `permgroupid` | `BPAPermGroup` | `id` |
| `BPAPref` | `userid` | `BPAUser` | `userid` |
| `BPAProcess` | `createdby` | `BPAUser` | `userid` |
| `BPAProcess` | `lastmodifiedby` | `BPAUser` | `userid` |
| `BPAProcessActionDependency` | `processID` | `BPAProcess` | `processid` |
| `BPAProcessAlert` | `ProcessID` | `BPAProcess` | `processid` |
| `BPAProcessAlert` | `UserID` | `BPAUser` | `userid` |
| `BPAProcessBackup` | `processid` | `BPAProcess` | `processid` |
| `BPAProcessCalendarDependency` | `processID` | `BPAProcess` | `processid` |
| `BPAProcessCredentialsDependency` | `processID` | `BPAProcess` | `processid` |
| `BPAProcessElementDependency` | `processID` | `BPAProcess` | `processid` |
| `BPAProcessEnvironmentVarDependency` | `processID` | `BPAProcess` | `processid` |
| `BPAProcessFontDependency` | `processID` | `BPAProcess` | `processid` |
| `BPAProcessIDDependency` | `processID` | `BPAProcess` | `processid` |
| `BPAProcessLock` | `processid` | `BPAProcess` | `processid` |
| `BPAProcessLock` | `userid` | `BPAUser` | `userid` |
| `BPAProcessNameDependency` | `processID` | `BPAProcess` | `processid` |
| `BPAProcessParentDependency` | `processID` | `BPAProcess` | `processid` |
| `BPAProcessQueueDependency` | `processID` | `BPAProcess` | `processid` |
| `BPAProcessSkillDependency` | `processID` | `BPAProcess` | `processid` |
| `BPAProcessWebApiDependency` | `processID` | `BPAProcess` | `processid` |
| `BPAProcessWebServiceDependency` | `processID` | `BPAProcess` | `processid` |
| `BPAPublicHoliday` | `relativetoholiday` | `BPAPublicHoliday` | `id` |
| `BPAPublicHoliday` | `shiftdaytypeid` | `BPAPublicHolidayShiftDayTypes` | `id` |
| `BPAPublicHolidayGroupMember` | `publicholidayid` | `BPAPublicHoliday` | `id` |
| `BPAPublicHolidayGroupMember` | `publicholidaygroupid` | `BPAPublicHolidayGroup` | `id` |
| `BPAPublicHolidayWorkingDay` | `calendarid` | `BPACalendar` | `id` |
| `BPAPublicHolidayWorkingDay` | `publicholidayid` | `BPAPublicHoliday` | `id` |
| `BPARelease` | `packageid` | `BPAPackage` | `id` |
| `BPARelease` | `userid` | `BPAUser` | `userid` |
| `BPAReleaseEntry` | `releaseid` | `BPARelease` | `id` |
| `BPAScenarioDetail` | `scenarioid` | `BPAScenario` | `scenarioid` |
| `BPAScenarioDetail` | `testnum` | `BPAScenario` | `testnum` |
| `BPAScenarioLink` | `processid` | `BPAProcess` | `processid` |
| `BPAScheduleAlert` | `scheduleid` | `BPASchedule` | `id` |
| `BPAScheduleAlert` | `userid` | `BPAUser` | `userid` |
| `BPAScheduleListSchedule` | `scheduleid` | `BPASchedule` | `id` |
| `BPAScheduleListSchedule` | `schedulelistid` | `BPAScheduleList` | `id` |
| `BPAScheduleLog` | `scheduleid` | `BPASchedule` | `id` |
| `BPAScheduleLogEntry` | `schedulelogid` | `BPAScheduleLog` | `id` |
| `BPAScheduleLogEntry` | `logsessionnumber` | `BPASession` | `sessionnumber` |
| `BPAScheduleLogEntry` | `taskid` | `BPATask` | `id` |
| `BPAScheduleTrigger` | `calendarid` | `BPACalendar` | `id` |
| `BPAScheduleTrigger` | `scheduleid` | `BPASchedule` | `id` |
| `BPAScreenshot` | `encryptid` | `BPAKeyStore` | `id` |
| `BPASession` | `processid` | `BPAProcess` | `processid` |
| `BPASession` | `starterresourceid` | `BPAResource` | `resourceid` |
| `BPASession` | `runningresourceid` | `BPAResource` | `resourceid` |
| `BPASession` | `statusid` | `BPAStatus` | `statusid` |
| `BPASession` | `starteruserid` | `BPAUser` | `userid` |
| `BPASession` | `queueid` | `BPAWorkQueue` | `ident` |
| `BPASessionLog_NonUnicode` | `sessionnumber` | `BPASession` | `sessionnumber` |
| `BPASessionLog_NonUnicode_pre65` | `sessionnumber` | `BPASession` | `sessionnumber` |
| `BPASessionLog_Unicode` | `sessionnumber` | `BPASession` | `sessionnumber` |
| `BPASessionLog_Unicode_pre65` | `sessionnumber` | `BPASession` | `sessionnumber` |
| `BPASkillVersion` | `skillid` | `BPASkill` | `id` |
| `BPASkillVersion` | `importedby` | `BPAUser` | `userid` |
| `BPASSOGroupRoleMapping` | `RoleID` | `BPAUserRole` | `id` |
| `BPAStringPref` | `prefid` | `BPAPref` | `id` |
| `BPASyncMetricsConditions` | `queueId` | `BPAWorkQueue` | `ident` |
| `BPASyncMetricsConditionsTags` | `metricsConditionId` | `BPASyncMetricsConditions` | `id` |
| `BPASyncMetricsConditionsTags` | `tagId` | `BPATag` | `id` |
| `BPASysConfig` | `defaultencryptid` | `BPAKeyStore` | `id` |
| `BPASysConfig` | `ArchivingResource` | `BPAResource` | `resourceid` |
| `BPATask` | `scheduleid` | `BPASchedule` | `id` |
| `BPATask` | `onfailure` | `BPATask` | `id` |
| `BPATask` | `onsuccess` | `BPATask` | `id` |
| `BPATaskSession` | `processid` | `BPAProcess` | `processid` |
| `BPATaskSession` | `taskid` | `BPATask` | `id` |
| `BPATreeDefaultGroup` | `groupid` | `BPAGroup` | `id` |
| `BPATreeDefaultGroup` | `treeid` | `BPATree` | `id` |
| `BPATreePerm` | `permid` | `BPAPerm` | `id` |
| `BPATreePerm` | `treeid` | `BPATree` | `id` |
| `BPAUserExternalIdentity` | `externalproviderid` | `BPAExternalProvider` | `id` |
| `BPAUserExternalIdentity` | `bpuserid` | `BPAUser` | `userid` |
| `BPAUserExternalReloginToken` | `bpuserid` | `BPAUser` | `userid` |
| `BPAUserRoleAssignment` | `userid` | `BPAUser` | `userid` |
| `BPAUserRoleAssignment` | `userroleid` | `BPAUserRole` | `id` |
| `BPAUserRolePerm` | `permid` | `BPAPerm` | `id` |
| `BPAUserRolePerm` | `userroleid` | `BPAUserRole` | `id` |
| `BPAValActionMap` | `actionid` | `BPAValAction` | `actionid` |
| `BPAValActionMap` | `catid` | `BPAValCategory` | `catid` |
| `BPAValActionMap` | `typeid` | `BPAValType` | `typeid` |
| `BPAValCheck` | `catid` | `BPAValCategory` | `catid` |
| `BPAValCheck` | `typeid` | `BPAValType` | `typeid` |
| `BPAWebApiAction` | `serviceid` | `BPAWebApiService` | `serviceid` |
| `BPAWebApiCustomOutputParameter` | `actionid` | `BPAWebApiAction` | `actionid` |
| `BPAWebApiHeader` | `actionid` | `BPAWebApiAction` | `actionid` |
| `BPAWebApiHeader` | `serviceid` | `BPAWebApiService` | `serviceid` |
| `BPAWebApiParameter` | `actionid` | `BPAWebApiAction` | `actionid` |
| `BPAWebApiParameter` | `serviceid` | `BPAWebApiService` | `serviceid` |
| `BPAWebhooks` | `encryptid` | `BPAKeyStore` | `id` |
| `BPAWebhookSubscriptions` | `webhookid` | `BPAWebhooks` | `ident` |
| `BPAWebhookSubscriptions` | `userid` | `BPAUser` | `userid` |
| `BPAWebServiceAsset` | `serviceid` | `BPAWebService` | `serviceid` |
| `BPAWebSkillVersion` | `versionid` | `BPASkillVersion` | `id` |
| `BPAWebSkillVersion` | `webserviceid` | `BPAWebApiService` | `serviceid` |
| `BPAWorkQueue` | `resourcegroupid` | `BPAGroup` | `id` |
| `BPAWorkQueue` | `encryptid` | `BPAKeyStore` | `id` |
| `BPAWorkQueue` | `processid` | `BPAProcess` | `processid` |
| `BPAWorkQueue` | `snapshotconfigurationid` | `BPASnapshotConfiguration` | `id` |
| `BPAWorkQueue` | `DefaultFilterID` | `BPAWorkQueueFilter` | `FilterID` |
| `BPAWorkQueueItem` | `encryptid` | `BPAKeyStore` | `id` |
| `BPAWorkQueueItem` | `queueident` | `BPAWorkQueue` | `ident` |
| `BPAWorkQueueItemAggregate` | `queueIdent` | `BPAWorkQueue` | `ident` |
| `BPAWorkQueueItemTag` | `tagid` | `BPATag` | `id` |
| `BPAWorkQueueItemTag` | `queueitemident` | `BPAWorkQueueItem` | `ident` |
| `BPMIConfiguredSnapshot` | `queueident` | `BPAWorkQueue` | `ident` |
| `BPMIQueueInterimSnapshot` | `queueident` | `BPAWorkQueue` | `ident` |
| `BPMIQueueSnapshot` | `queueident` | `BPAWorkQueue` | `ident` |
| `BPMIQueueTrend` | `queueident` | `BPAWorkQueue` | `ident` |

## Anmerkungen fÃ¼r den Adapter-Entwurf

**Frische Installation.** Die Inhalts-Tabellen fÃ¼r Prozesse/Objekte/Releases/Queues sind **alle leer** (Row Count 0). Bevor Round-Trip-Tests greifbares Material haben, mÃ¼ssen im BP-Studio 1â€“2 Demo-Prozesse + 1 Demo-Object angelegt werden.

**Identities als Anker.** Identity-Spalten (alle `int`/`bigint`) in den relevanten Tabellen (`BPAProcess` fehlt keine â€” PK ist `processid` aber explizit kein Identity, s. `BPAUserRolePerm`, `BPAPerm`, `BPAPublicHolidayGroupMember`) sind die SchlÃ¼sselkandidaten fÃ¼r die Diff-Pipeline.

**Package-Release-Trennung.** `BPAPackage` bÃ¼ndelt Prozesse + Business Objects, `BPARelease` ist das verÃ¶ffentlichte Release, beide via `BPAPackageProcess` / `BPAReleaseEntry` miteinander verknÃ¼pft. FÃ¼r â€žXML raus aus BP" muss die Adapter-Logik beide Schichten berÃ¼cksichtigen.

**Environment + Credentials sind nicht-trivial.** `BPAEnvironment` enthÃ¤lt Verweise auf `BPACredentials`, die wiederum via `BPAKeyStore` (Encryption) geschÃ¼tzt sind (`encryptid` als FK). Wer Prozesse aus BP heraus serialisiert, muss diese Credentials entweder ignorieren oder mit der Lizenz/SchlÃ¼ssel-Tresor-Policy des Kunden synchronisieren â€” **sicherheitsrelevant**.

**Session-Logs sind die grÃ¶ÃŸten Wachstumstreiber.** `BPASessionLog_*` (Unicode/NonUnicode Ã— aktuelle/pre65) ist leer heute, skaliert aber mit jeder Session-Iteration. FÃ¼r einen produktiven Git-Workflow muss man diese Tabellen ausschlieÃŸen oder in eine separate `bp-git-ignore`-Policy verschieben.

**System-Seed ist dicht befÃ¼llt.** `BPAUserRolePerm` (229 Rows), `BPAPerm` (108), `BPAPermGroupMember` (109), `BPAPublicHolidayGroupMember` (111), `BPADBVersion` (547) sind Standard-BP-Inhalte, **nicht** benutzerdefiniert. Sie gehÃ¶ren nicht in den Git-Adapter-Snapshot.

**Webhooks/Schedules** sind optional â€” viele BP-Installationen kommen ohne diese Tabellen. Wenn 0 Rows, existieren sie im Server-Konfig, sind aber nicht initialisiert.

## Empfehlung fÃ¼r den Adapter

Kern-Tabellen, die der Adapter zwingend serialisieren muss (`*.xml`/JSON-Export aus BP-DB):


- **Prozesse:** `BPAProcess` (ohne `processxml` Spaltenname â€” prÃ¼fen, in welcher Spalte der XML-Body steckt; vermutlich in einer separaten Subtabelle verlinkt).
- **Business Objects:** vermutlich in `BPAPackage*` referenziert; genaue Speicherung in den Sample-Daten prÃ¼fen.
- **Releases:** `BPARelease`, `BPAReleaseEntry`, `BPAPackageProcess` fÃ¼r die BÃ¼ndelung.
- **Schedules:** `BPASchedule`, `BPAScheduleListSchedule`, `BPAScheduleTrigger`.
- **Work Queues:** `BPAWorkQueue`, `BPAWorkQueueFilter` (Schema), `BPAWorkQueueItem` (Items).
- **Environments:** `BPAEnvironment`, `BPAEnvironmentVar` (Variablen, **nicht** Credentials!).

Tabellen, die fÃ¼r `git ignore` vorgeschlagen werden:


- `BPASessionLog_*` (Wachstumstreiber, transient)
- `BPASession`, `BPATaskSession` (Runtime-State)
- `BPAAliveResources`, `BPAAliveAutomateC` (Heartbeat)
- `BPAStatistics`, `BPAStatus` (Runtime)
- `BPACache`, `BPACacheETags` (intern)
- `BPAAuditEvents`, `BPAScreenshot` (Audit, ggf. ja nach Policy)
- `BPAPassword`, `BPAPasswordRules` (Credentials â€” **immer ignorieren**)
- `BPASyncMetrics*` (Metriken)
- `BPAPerm`, `BPAUserRolePerm`, `BPAGroup*`, `BPAPermGroup*` (System-Seed)
- `BPAPublicHoliday*`, `BPAIntegerPref`, `BPAStringPref`, `BPAPref`, `BPASysConfig` (BP-System-Setup)
- `BPADBVersion` (DB-Migration-Tracking)

