// -------------------------------------------------------------------------------------------------
// <copyright file="CommandLineOptions.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SchemaManager;

using Microsoft.Health.SqlServer.Configs;

public class CommandLineOptions
{
    public Uri? Server { get; set; }

    public string? ConnectionString { get; set; }

    public SqlServerAuthenticationType? AuthenticationType { get; set; }

    public string? ManagedIdentityClientId { get; set; }
}
