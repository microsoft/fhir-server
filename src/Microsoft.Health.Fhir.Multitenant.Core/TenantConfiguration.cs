// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Multitenant.Core;

/// <summary>
/// Configuration settings for a single tenant instance.
/// </summary>
public class TenantConfiguration
{
    /// <summary>
    /// Gets or sets the unique identifier for this tenant.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the port number this tenant's FHIR server will listen on.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Gets or sets the database connection string for this tenant.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets additional configuration settings for this tenant.
    /// </summary>
#pragma warning disable CA2227 // Collection properties should be read only - Required for configuration binding
    public Dictionary<string, string?> Settings { get; set; } = [];
#pragma warning restore CA2227
}
