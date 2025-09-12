// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Conformance
{
    public class DocRefRequestConverter : IDocRefRequestConverter
    {
        internal const string PatientParameterName = "patient";
        internal const string StartParameterName = "start";
        internal const string EndParameterName = "end";
        internal const string OnDemandParameterName = "on-demand";
        internal const string ProfileParameterName = "profile";
        internal const string TypeParameterName = "type";

        internal static readonly Dictionary<string, string> ConvertParameterMap = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            { PatientParameterName, "subject" },
            { StartParameterName, "period" },
            { EndParameterName, "period" },
            { OnDemandParameterName, "on-demand" },
            { ProfileParameterName, "format-canonical" },
            { TypeParameterName, "type" },
        };

        private readonly IMediator _mediator;
        private readonly IBundleFactory _bundleFactory;
        private readonly ILogger<DocRefRequestConverter> _logger;

        public DocRefRequestConverter(
            IMediator mediator,
            IBundleFactory bundleFactory,
            ILogger<DocRefRequestConverter> logger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(bundleFactory, nameof(bundleFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _mediator = mediator;
            _bundleFactory = bundleFactory;
            _logger = logger;
        }

        public Task<ResourceElement> ConvertAsync(
            IReadOnlyList<Tuple<string, string>> parameters,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(parameters, nameof(parameters));

            var parametersToValidate = parameters
                .GroupBy(x => x.Item1)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
            if (!parametersToValidate.ContainsKey(PatientParameterName))
            {
                throw new RequestNotValidException(
                    string.Format(Resources.DocRefMissingRequiredParameter, PatientParameterName));
            }

            var parametersOut = new List<Tuple<string, string>>[3];
            if ((!parametersToValidate.TryGetValue(PatientParameterName, out parametersOut[0]) || parametersOut[0]?.Count != 1)
                || (parametersToValidate.TryGetValue(StartParameterName, out parametersOut[1]) && parametersOut[1]?.Count != 1)
                || (parametersToValidate.TryGetValue(EndParameterName, out parametersOut[2]) && parametersOut[2]?.Count != 1))
            {
                throw new RequestNotValidException(
                    string.Format(
                        Resources.DocRefInvalidParameterCount,
                        (parametersOut[0] != null && parametersOut[0].Count != 1) ? PatientParameterName : ((parametersOut[1] != null && parametersOut[1].Count != 1) ? StartParameterName : EndParameterName)));
            }

            if (parametersToValidate.ContainsKey(OnDemandParameterName) || parametersToValidate.ContainsKey(ProfileParameterName))
            {
                return Task.FromResult(
                    CreateBundleForUnsupportedParameter(
                        parametersToValidate.ContainsKey(OnDemandParameterName) ? OnDemandParameterName : ProfileParameterName));
            }

            return _mediator.SearchResourceAsync(
                KnownResourceTypes.DocumentReference,
                ConvertParameters(parameters),
                cancellationToken);
        }

        private ResourceElement CreateBundleForUnsupportedParameter(string parameterName)
        {
            var operationOutcomeIssue = new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.NotSupported,
                string.Format(Resources.DocRefParameterNotSupported, parameterName));
            var searchResult = new SearchResult(
                new List<SearchResultEntry>(),
                null,
                null,
                new List<Tuple<string, string>>(),
                new List<OperationOutcomeIssue> { operationOutcomeIssue });
            return _bundleFactory.CreateSearchBundle(searchResult);
        }

        private List<Tuple<string, string>> ConvertParameters(IReadOnlyList<Tuple<string, string>> parameters)
        {
            var parametersConverted = new List<Tuple<string, string>>();
            if (parameters != null && parameters.Any())
            {
                foreach (var p in parameters)
                {
                    if (ConvertParameterMap.TryGetValue(p.Item1, out var val))
                    {
                        // NOTE: adding the prefix for the 1st value only for now. (e.g. we need to decide whether to allow multi-value like "start=<date1>,<date2>,...")
                        if (string.Equals(p.Item1, StartParameterName, StringComparison.OrdinalIgnoreCase))
                        {
                            parametersConverted.Add(Tuple.Create(val, $"ge{p.Item2}"));
                        }
                        else if (string.Equals(p.Item1, EndParameterName, StringComparison.OrdinalIgnoreCase))
                        {
                            parametersConverted.Add(Tuple.Create(val, $"le{p.Item2}"));
                        }
                        else
                        {
                            parametersConverted.Add(Tuple.Create(val, p.Item2));
                        }
                    }
                    else
                    {
                        var sanitizedName = p.Item1?
                            .Replace("\r", string.Empty, StringComparison.Ordinal)?
                            .Replace("\n", string.Empty, StringComparison.Ordinal);
                        _logger.LogWarning("Unknown parameter: {Name}", sanitizedName);
                        parametersConverted.Add(p);
                    }
                }
            }

            return parametersConverted;
        }
    }
}
