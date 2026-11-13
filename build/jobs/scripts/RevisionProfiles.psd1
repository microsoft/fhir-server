@{
    Baseline = @{
        Name = 'Standard64'
        ResourceIdProfile = 'Standard64'
    }
    Profiles = @(
        @{
            Name = 'Long128'
            Slug = 'long128'
            CategoryFilter = 'Category=ResourceIdBoundary'
            ResourceIdProfile = 'Long128'
            Environment = @{
                FhirServer__CoreFeatures__UseLongResourceIds = 'true'
                TaskHosting__Enabled = 'false'
                SqlServer__AllowDatabaseCreation = 'false'
                SqlServer__SchemaOptions__AutomaticUpdatesEnabled = 'false'
                SqlServer__Initialize = 'true'
                SqlServer__DeleteAllDataOnStartup = 'false'
            }
        }
    )
}
