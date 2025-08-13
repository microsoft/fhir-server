// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Guidance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Guidance
{
    public class DuplicateClinicalReferenceBehavior : IPipelineBehavior<CreateResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<UpsertResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<DeleteResourceRequest, DeleteResourceResponse>
    {
        private readonly CoreFeatureConfiguration _coreFeatureConfiguration;
        private readonly IClinicalReferenceDuplicator _clinicalReferenceDuplicator;

        public DuplicateClinicalReferenceBehavior(
            IOptions<CoreFeatureConfiguration> coreFeatureConfiguration,
            IClinicalReferenceDuplicator clinicalReferenceDuplicator)
        {
            EnsureArg.IsNotNull(coreFeatureConfiguration?.Value, nameof(coreFeatureConfiguration));
            EnsureArg.IsNotNull(clinicalReferenceDuplicator, nameof(clinicalReferenceDuplicator));

            _coreFeatureConfiguration = coreFeatureConfiguration.Value;
            _clinicalReferenceDuplicator = clinicalReferenceDuplicator;
        }

        public async Task<UpsertResourceResponse> Handle(
            CreateResourceRequest request,
            RequestHandlerDelegate<UpsertResourceResponse> next,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            EnsureArg.IsNotNull(next, nameof(next));

            var response = await next(cancellationToken);
            if (_coreFeatureConfiguration.EnableClinicalReferenceDuplication
                && response?.Outcome?.RawResourceElement?.InstanceType != null
                && response?.Outcome?.RawResourceElement?.RawResource != null
                && _clinicalReferenceDuplicator.IsDuplicatableResourceType(response.Outcome.RawResourceElement.InstanceType))
            {
                (var source, var duplicate) = await _clinicalReferenceDuplicator.CreateResourceAsync(
                    response.Outcome.RawResourceElement,
                    cancellationToken);
                if (source != null)
                {
                    return new UpsertResourceResponse(
                        new SaveOutcome(new RawResourceElement(source), response.Outcome.Outcome));
                }
            }

            return response;
        }

        public async Task<UpsertResourceResponse> Handle(
            UpsertResourceRequest request,
            RequestHandlerDelegate<UpsertResourceResponse> next,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            EnsureArg.IsNotNull(next, nameof(next));

            var response = await next(cancellationToken);
            if (_coreFeatureConfiguration.EnableClinicalReferenceDuplication
                && response?.Outcome?.RawResourceElement?.InstanceType != null
                && response?.Outcome?.RawResourceElement?.RawResource != null
                && _clinicalReferenceDuplicator.IsDuplicatableResourceType(response.Outcome.RawResourceElement.InstanceType))
            {
                await _clinicalReferenceDuplicator.UpdateResourceAsync(
                    response.Outcome.RawResourceElement,
                    cancellationToken);
            }

            return response;
        }

        public async Task<DeleteResourceResponse> Handle(
            DeleteResourceRequest request,
            RequestHandlerDelegate<DeleteResourceResponse> next,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            EnsureArg.IsNotNull(next, nameof(next));

            var response = await next(cancellationToken);
            if (_coreFeatureConfiguration.EnableClinicalReferenceDuplication
                && response?.ResourceKey?.Id != null
                && response?.ResourceKey?.ResourceType != null
                && _clinicalReferenceDuplicator.IsDuplicatableResourceType(response.ResourceKey.ResourceType))
            {
                await _clinicalReferenceDuplicator.DeleteResourceAsync(
                    response.ResourceKey,
                    request.DeleteOperation,
                    cancellationToken);
            }

            return response;
        }
    }
}
