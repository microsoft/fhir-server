// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Guidance;
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
        private readonly ILogger<DuplicateClinicalReferenceBehavior> _logger;

        public DuplicateClinicalReferenceBehavior(
            IOptions<CoreFeatureConfiguration> coreFeatureConfiguration,
            IClinicalReferenceDuplicator clinicalReferenceDuplicator,
            ILogger<DuplicateClinicalReferenceBehavior> logger)
        {
            EnsureArg.IsNotNull(coreFeatureConfiguration?.Value, nameof(coreFeatureConfiguration));
            EnsureArg.IsNotNull(clinicalReferenceDuplicator, nameof(clinicalReferenceDuplicator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _coreFeatureConfiguration = coreFeatureConfiguration.Value;
            _clinicalReferenceDuplicator = clinicalReferenceDuplicator;
            _logger = logger;
        }

        public async Task<UpsertResourceResponse> Handle(
            CreateResourceRequest request,
            RequestHandlerDelegate<UpsertResourceResponse> next,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            EnsureArg.IsNotNull(next, nameof(next));

            var response = await next(cancellationToken);
            var resource = response?.Outcome?.RawResourceElement?.RawResource?
                .ToITypedElement(ModelInfoProvider.Instance)?
                .ToResourceElement()?
                .ToPoco();
            if (_coreFeatureConfiguration.EnableClinicalReferenceDuplication && _clinicalReferenceDuplicator.ShouldDuplicate(resource))
            {
                await _clinicalReferenceDuplicator.CreateResourceAsync(
                    resource,
                    cancellationToken);
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
            var resource = response?.Outcome?.RawResourceElement?.RawResource?
                .ToITypedElement(ModelInfoProvider.Instance)?
                .ToResourceElement()?
                .ToPoco();
            if (_coreFeatureConfiguration.EnableClinicalReferenceDuplication && _clinicalReferenceDuplicator.ShouldDuplicate(resource))
            {
                var duplicateResources = await _clinicalReferenceDuplicator.UpdateResourceAsync(
                    resource,
                    cancellationToken);
                if (duplicateResources.Any())
                {
                    return response;
                }

                _logger.LogWarning("No duplicate resource found.");
                await _clinicalReferenceDuplicator.CreateResourceAsync(
                    resource,
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
            var resourceKey = request?.ResourceKey;
            if (_coreFeatureConfiguration.EnableClinicalReferenceDuplication && _clinicalReferenceDuplicator.CheckDuplicate(resourceKey))
            {
                await _clinicalReferenceDuplicator.DeleteResourceAsync(
                    resourceKey,
                    request.DeleteOperation,
                    cancellationToken);
            }

            return response;
        }
    }
}
