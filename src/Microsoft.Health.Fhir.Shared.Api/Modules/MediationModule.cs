// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Api.Modules
{
    /// <summary>
    /// Installs mediation components in container
    /// </summary>
    public class MediationModule : IStartupModule
    {
        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            services.AddMediatR(KnownAssemblies.All);

            // AddMediatR does process generic classes. Manually adding these.
            services.AddTransient(
                typeof(IRequestHandler<PatchResourceRequest<JsonPatchDocument>, UpsertResourceResponse>),
                typeof(PatchResourceHandler<JsonPatchDocument>));
            services.AddTransient(
                typeof(IRequestHandler<PatchResourceRequest<Parameters>, UpsertResourceResponse>),
                typeof(PatchResourceHandler<Parameters>));
            services.AddTransient(
                typeof(IRequestHandler<ConditionalPatchResourceRequest<JsonPatchDocument>, UpsertResourceResponse>),
                typeof(ConditionalPatchResourceHandler<JsonPatchDocument>));
            services.AddTransient(
                typeof(IRequestHandler<ConditionalPatchResourceRequest<Parameters>, UpsertResourceResponse>),
                typeof(ConditionalPatchResourceHandler<Parameters>));

            // Allows handlers to provide capabilities
            var openRequestInterfaces = new[]
            {
                typeof(IRequestHandler<,>),
                typeof(INotificationHandler<>),
            };

            services.TypesInSameAssembly(KnownAssemblies.All)
                .Where(y => y.Type.IsGenericType && openRequestInterfaces.Contains(y.Type.GetGenericTypeDefinition()))
                .Transient()
                .AsImplementedInterfaces(x => x == typeof(IProvideCapability));
        }
    }
}
