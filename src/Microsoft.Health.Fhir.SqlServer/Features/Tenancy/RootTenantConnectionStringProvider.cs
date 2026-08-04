// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.SqlServer.Configs;

namespace Microsoft.Health.Fhir.SqlServer.Features.Tenancy
{
    /// <summary>
    /// The default provider, which keeps every tenant on the process-wide SQL connection string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the open-source default implementation. It is a root-owned singleton that
    /// deliberately returns the root connection string for all tenants, enabling single-database
    /// multi-tenancy. Hosts may register a custom <see cref="ITenantConnectionStringProvider"/>
    /// before calling <see cref="FhirServerBuilderSqlServerRegistrationExtensions.AddSqlServer"/>
    /// to override this behavior with multi-database tenancy or other strategies.
    /// </para>
    /// <para>
    /// The root-owned instance is reused by all tenant containers, and tenant disposal does not affect it.
    /// </para>
    /// </remarks>
    public sealed class RootTenantConnectionStringProvider : ITenantConnectionStringProvider
    {
        private readonly SqlServerDataStoreConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="RootTenantConnectionStringProvider"/> class.
        /// </summary>
        /// <param name="configuration">The root SQL configuration.</param>
        public RootTenantConnectionStringProvider(IOptions<SqlServerDataStoreConfiguration> configuration)
        {
            EnsureArg.IsNotNull(configuration?.Value, nameof(configuration));

            _configuration = configuration.Value;
        }

        /// <inheritdoc />
        public string GetConnectionString(TenantDescriptor tenant) => _configuration.ConnectionString;
    }
}
