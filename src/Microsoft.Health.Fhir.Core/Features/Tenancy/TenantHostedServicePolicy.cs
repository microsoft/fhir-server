// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Default <see cref="ITenantHostedServicePolicy"/>, seeded with the hosted services that ship with
    /// the FHIR server. Unknown services are rejected rather than defaulted, so that a newly added
    /// background service fails immediately and visibly instead of silently never running for any tenant.
    /// </summary>
    public sealed class TenantHostedServicePolicy : ITenantHostedServicePolicy
    {
        private readonly Dictionary<string, TenantHostedServiceDisposition> _dispositions =
            new(StringComparer.Ordinal)
            {
                ["Microsoft.Health.Fhir.Api.Features.BackgroundJobService.HostingBackgroundService"] = TenantHostedServiceDisposition.Relocated,
                ["Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.WatchdogsBackgroundService"] = TenantHostedServiceDisposition.Relocated,
                ["Microsoft.Health.Fhir.Core.Features.Search.Registry.SearchParameterCacheRefreshBackgroundService"] = TenantHostedServiceDisposition.Relocated,
                ["Microsoft.Health.Fhir.SqlServer.Features.Search.QueryPlanReuseChecker"] = TenantHostedServiceDisposition.Relocated,
                ["Microsoft.Health.Fhir.CosmosDb.Features.Storage.CosmosContainerProvider"] = TenantHostedServiceDisposition.Relocated,
                ["Microsoft.Health.Fhir.Core.Features.Definition.CompartmentDefinitionManager"] = TenantHostedServiceDisposition.Shared,
                ["Microsoft.Health.Fhir.Core.Features.Search.CodeSystemResolver"] = TenantHostedServiceDisposition.Shared,
                ["Microsoft.Health.Fhir.Web.PrometheusMetricsServer"] = TenantHostedServiceDisposition.Shared,
                ["Microsoft.Health.Fhir.Api.OpenIddict.Services.OpenIddictApplicationCreater"] = TenantHostedServiceDisposition.Shared,
                ["Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckPublisherHostedService"] = TenantHostedServiceDisposition.Shared,
                ["Microsoft.Health.Api.Features.Audit.AuditEventTypeMapping"] = TenantHostedServiceDisposition.Shared,
                ["Microsoft.Health.Fhir.Core.Features.Security.RoleLoader"] = TenantHostedServiceDisposition.PerTenantInitializer,
            };

        /// <inheritdoc />
        public TenantHostedServiceDisposition Classify(string hostedServiceTypeName)
        {
            EnsureArg.IsNotNullOrWhiteSpace(hostedServiceTypeName, nameof(hostedServiceTypeName));

            if (_dispositions.TryGetValue(hostedServiceTypeName, out TenantHostedServiceDisposition disposition))
            {
                return disposition;
            }

            throw new TenantHostedServiceNotClassifiedException(hostedServiceTypeName);
        }

        /// <inheritdoc />
        public void Set(string hostedServiceTypeName, TenantHostedServiceDisposition disposition)
        {
            EnsureArg.IsNotNullOrWhiteSpace(hostedServiceTypeName, nameof(hostedServiceTypeName));

            _dispositions[hostedServiceTypeName] = disposition;
        }
    }
}
