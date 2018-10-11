#
# Module manifest for module 'FhirServerRelease'
#
@{
    RootModule = 'FhirServerRelease.psm1'
    ModuleVersion = '0.0.1'
    GUID = '4e840205-d0bd-4b83-9834-f799b4625355'
    Author = 'Microsoft Healthcare NExT'
    CompanyName = 'https://microsoft.com'
    Description = 'PowerShell Module for managing Azure Active Directory registrations and users for Microsoft FHIR Server for a Test Environment.'
    PowerShellVersion = '3.0'
    FunctionsToExport = 'Set-FhirServerApiApplicationRoles', 'Set-FhirServerApiUsers', 'Set-FhirServerClientAppRoleAssignments'
    CmdletsToExport = @()
    AliasesToExport = @()    
}
    