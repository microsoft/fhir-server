@{
    Baseline = @{
        Name = 'Baseline'
    }
    # Profile-only test classes need Category=ProfileOnly and a distinct category selected below.
    # ResourceIdBoundary is shared with the baseline Standard64 contract.
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
