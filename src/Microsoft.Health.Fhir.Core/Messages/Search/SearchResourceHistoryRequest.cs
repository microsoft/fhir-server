// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Search
{
    public class SearchResourceHistoryRequest : IRequest<SearchResourceHistoryResponse>, IRequireCapability
    {
        private readonly string _capability;

        public SearchResourceHistoryRequest(PartialDateTime since = null, PartialDateTime before = null, PartialDateTime at = null, int? count = null, string continuationToken = null, string sort = null)
        {
            Since = since;
            Before = before;
            At = at;
            Count = count;
            ContinuationToken = continuationToken;
            Sort = sort;

            _capability = "CapabilityStatement.rest.interaction.where(code = 'history-system').exists()";
        }

        public SearchResourceHistoryRequest(string resourceType, PartialDateTime since = null, PartialDateTime before = null, PartialDateTime at = null, int? count = null, string continuationToken = null, string sort = null)
            : this(since, before, at, count, continuationToken, sort)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

            ResourceType = resourceType;

            _capability = $"CapabilityStatement.rest.resource.where(type = '{resourceType}').interaction.where(code = 'history-type').exists()";
        }

        public SearchResourceHistoryRequest(string resourceType, string resourceId, PartialDateTime since = null, PartialDateTime before = null, PartialDateTime at = null, int? count = null, string continuationToken = null, string sort = null)
            : this(resourceType, since, before, at, count, continuationToken, sort)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));
            EnsureArg.IsNotNullOrWhiteSpace(resourceId, nameof(resourceId));

            ResourceId = resourceId;

            _capability = $"CapabilityStatement.rest.resource.where(type = '{resourceType}').interaction.where(code = 'history-instance').exists()";
        }

        public string ResourceType { get; }

        public string ResourceId { get; }

        public PartialDateTime Since { get; }

        public PartialDateTime Before { get; }

        public PartialDateTime At { get; }

        public int? Count { get; }

        public string ContinuationToken { get; }

        public string Sort { get; }

        public IEnumerable<CapabilityQuery> RequiredCapabilities()
        {
            yield return new CapabilityQuery(_capability);
        }
    }
}
