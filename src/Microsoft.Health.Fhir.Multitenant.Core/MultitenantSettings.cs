// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Multitenant.Core;

/// <summary>
/// Configuration settings for the multitenant FHIR server.
/// </summary>
public class MultitenantSettings
{
    /// <summary>
    /// Gets or sets the configuration section name.
    /// </summary>
    public const string SectionName = "Multitenant";

    /// <summary>
    /// Gets the list of tenant configurations.
    /// </summary>
#pragma warning disable CA2227 // Collection properties should be read only - Required for configuration binding
#pragma warning disable CA1002 // Do not expose generic lists - Required for configuration binding
    public List<TenantConfiguration> Tenants { get; set; } = [];
#pragma warning restore CA1002
#pragma warning restore CA2227

    /// <summary>
    /// Gets or sets the port number for the router/gateway.
    /// </summary>
    public int RouterPort { get; set; } = 8080;
}
