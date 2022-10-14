// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.SqlServer.Configs;

namespace Microsoft.Health.Fhir.SchemaManager;

public class CommandLineOptions
{
    public Uri? Server { get; set; }

    public string? ConnectionString { get; set; }

    public SqlServerAuthenticationType? AuthenticationType { get; set; }

    public string? ManagedIdentityClientId { get; set; }
}
