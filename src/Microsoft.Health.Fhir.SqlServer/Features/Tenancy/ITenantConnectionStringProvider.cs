// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Tenancy;

namespace Microsoft.Health.Fhir.SqlServer.Features.Tenancy
{
    /// <summary>
    /// Supplies the SQL connection string for a tenant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hosts may register a custom implementation before calling <see cref="FhirServerBuilderSqlServerRegistrationExtensions.AddSqlServer"/>
    /// to override the default provider. The root-owned singleton is reused by every tenant container;
    /// tenant containers do not take ownership of or dispose it.
    /// </para>
    /// <para>
    /// Connection strings are deliberately kept out of <see cref="TenantDescriptor.Properties"/>, which is
    /// a secret-free surface that may be logged.
    /// </para>
    /// </remarks>
    public interface ITenantConnectionStringProvider
    {
        /// <summary>
        /// Gets the connection string for the supplied tenant.
        /// </summary>
        /// <param name="tenant">The tenant.</param>
        /// <returns>The connection string.</returns>
        string GetConnectionString(TenantDescriptor tenant);
    }
}
