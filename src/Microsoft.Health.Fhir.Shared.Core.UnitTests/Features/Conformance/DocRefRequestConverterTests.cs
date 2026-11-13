// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Conformance;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using DateTime = System.DateTime;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Conformance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    public class DocRefRequestConverterTests
    {
        private readonly DocRefRequestConverter _converter;
        private readonly IMediator _mediator;
        private readonly IBundleFactory _bundleFactory;
        private readonly ILogger<DocRefRequestConverter> _logger;

        public DocRefRequestConverterTests()
        {
            _mediator = Substitute.For<IMediator>();
            _mediator.SendAsync(
                Arg.Any<SearchResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new SearchResourceResponse(new Bundle().ToResourceElement())));

            _bundleFactory = Substitute.For<IBundleFactory>();
            _bundleFactory.CreateSearchBundle(
                Arg.Any<SearchResult>())
                .Returns(new Bundle().ToResourceElement());

            _logger = Substitute.For<ILogger<DocRefRequestConverter>>();
            _converter = new DocRefRequestConverter(
                _mediator,
                _bundleFactory,
                _logger);
        }

        [Theory]
        [MemberData(nameof(GetParametersTestData))]
        public async Task GivenParameters_WhenConverting_ThenSearchResourceShouldBeCalledSuccessfully(
            List<Tuple<string, string>> parameters)
        {
            var parametersToValidate = parameters?
                .GroupBy(x => x.Item1)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, List<Tuple<string, string>>>();
            var valid = parametersToValidate.TryGetValue(DocRefRequestConverter.PatientParameterName, out var patientParameters) && patientParameters.Count == 1
                && (!parametersToValidate.TryGetValue(DocRefRequestConverter.StartParameterName, out var startParameters)
                    || (startParameters.Count == 1 && !(startParameters[0]?.Item2?.Contains(',', StringComparison.Ordinal) ?? true)))
                && (!parametersToValidate.TryGetValue(DocRefRequestConverter.EndParameterName, out var endParameters)
                    || (endParameters.Count == 1 && !(endParameters[0]?.Item2?.Contains(',', StringComparison.Ordinal) ?? true)));
            var unsupported = parametersToValidate.ContainsKey(DocRefRequestConverter.OnDemandParameterName)
                || parametersToValidate.ContainsKey(DocRefRequestConverter.ProfileParameterName);

            _mediator.SendAsync(
                Arg.Any<SearchResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        Assert.True(valid && !unsupported);

                        var request = (SearchResourceRequest)x[0];
                        Assert.NotNull(request);
                        Assert.Equal(KnownResourceTypes.DocumentReference, request.ResourceType, true);

                        ValidateParameters(parameters, request.Queries);
                        return Task.FromResult(new SearchResourceResponse(new Bundle().ToResourceElement()));
                    });
            _bundleFactory.CreateSearchBundle(
                Arg.Any<SearchResult>())
                .Returns(
                    x =>
                    {
                        Assert.True(unsupported);

                        var p = parameters.Any(x => string.Equals(x.Item1, DocRefRequestConverter.OnDemandParameterName, StringComparison.OrdinalIgnoreCase))
                            ? DocRefRequestConverter.OnDemandParameterName : DocRefRequestConverter.ProfileParameterName;
                        var result = (SearchResult)x[0];
                        Assert.NotNull(result);
                        Assert.Equal(1, result.SearchIssues?.Count ?? 0);

                        var issue = result.SearchIssues.FirstOrDefault();
                        Assert.NotNull(issue);
                        Assert.Equal(OperationOutcomeConstants.IssueSeverity.Error, issue.Severity);
                        Assert.Equal(OperationOutcomeConstants.IssueType.NotSupported, issue.Code);
                        Assert.Contains(p, issue.Diagnostics, StringComparison.OrdinalIgnoreCase);

                        return new Bundle().ToResourceElement();
                    });

            try
            {
                await _converter.ConvertAsync(
                    parameters,
                    CancellationToken.None);
                Assert.True(valid);
            }
            catch (RequestNotValidException)
            {
                Assert.False(valid);
            }

            await _mediator.Received(valid && !unsupported ? 1 : 0).SendAsync(
                Arg.Any<SearchResourceRequest>(),
                Arg.Any<CancellationToken>());
            _bundleFactory.Received(unsupported ? 1 : 0).CreateSearchBundle(
                Arg.Any<SearchResult>());
        }

        private static void ValidateParameters(
            IReadOnlyList<Tuple<string, string>> parameters,
            IReadOnlyList<Tuple<string, string>> parametersConverted)
        {
            Assert.NotNull(parametersConverted);

            if (parameters != null)
            {
                // Excluding unsupported parameters from validation.
                var count = parameters
                    .Where(x => !string.Equals(x.Item1, DocRefRequestConverter.OnDemandParameterName, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(x.Item1, DocRefRequestConverter.ProfileParameterName, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(x => DocRefRequestConverter.ConvertParameterMap.TryGetValue(x.Item1, out var n) ? n : x.Item1)
                    .Count();
                var source = parameters
                    .Where(x => !string.Equals(x.Item1, DocRefRequestConverter.OnDemandParameterName, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(x.Item1, DocRefRequestConverter.ProfileParameterName, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(x => x.Item1)
                    .ToDictionary(x => x.Key, x => x.Select(y => y.Item2).ToList());
                var converted = parametersConverted
                    .GroupBy(x => x.Item1)
                    .ToDictionary(x => x.Key, x => x.SelectMany(y => y.Item2?.Split(',')).ToList());
                Assert.Equal(count, converted.Count);
                foreach (var p in source)
                {
                    var name = p.Key;
                    if (DocRefRequestConverter.ConvertParameterMap.TryGetValue(p.Key, out var n))
                    {
                        name = n;
                    }

                    Assert.Contains(name, converted);
                    if (string.Equals(p.Key, DocRefRequestConverter.StartParameterName, StringComparison.OrdinalIgnoreCase))
                    {
                        Assert.Contains(
                            converted[name],
                            x => string.Equals(x, $"ge{p.Value[0]}", StringComparison.Ordinal));
                    }
                    else if (string.Equals(p.Key, DocRefRequestConverter.EndParameterName, StringComparison.OrdinalIgnoreCase))
                    {
                        Assert.Contains(
                            converted[name],
                            x => string.Equals(x, $"le{p.Value[0]}", StringComparison.Ordinal));
                    }
                    else
                    {
                        Assert.All(
                            p.Value,
                            x => Assert.Contains(converted[name], y => string.Equals(x, y, StringComparison.Ordinal)));
                    }
                }
            }
        }

        public static IEnumerable<object[]> GetParametersTestData()
        {
            var data = new[]
            {
                new object[]
                {
                    // Valid set of parameters.
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(DocRefRequestConverter.PatientParameterName, "test-patient"),
                        Tuple.Create(DocRefRequestConverter.StartParameterName, DateTime.UtcNow.Subtract(TimeSpan.FromDays(7)).ToString("o")),
                        Tuple.Create(DocRefRequestConverter.EndParameterName, DateTime.UtcNow.ToString("o")),
                        Tuple.Create(DocRefRequestConverter.TypeParameterName, "https://loinc.org|34133-9"),
                    },
                },
                new object[]
                {
                    // Valid set of parameters with required parameter only.
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(DocRefRequestConverter.PatientParameterName, "test-patient"),
                    },
                },
                new object[]
                {
                    // Missing required parameter "patient".
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(DocRefRequestConverter.StartParameterName, DateTime.UtcNow.Subtract(TimeSpan.FromDays(7)).ToString("o")),
                        Tuple.Create(DocRefRequestConverter.EndParameterName, DateTime.UtcNow.ToString("o")),
                        Tuple.Create(DocRefRequestConverter.TypeParameterName, "https://loinc.org|34133-9"),
                    },
                },
                new object[]
                {
                    // Unsupported parameter "on-demand".
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(DocRefRequestConverter.PatientParameterName, "test-patient"),
                        Tuple.Create(DocRefRequestConverter.StartParameterName, DateTime.UtcNow.Subtract(TimeSpan.FromDays(7)).ToString("o")),
                        Tuple.Create(DocRefRequestConverter.EndParameterName, DateTime.UtcNow.ToString("o")),
                        Tuple.Create(DocRefRequestConverter.OnDemandParameterName, "true"),
                    },
                },
                new object[]
                {
                    // Unsupported parameter "profile".
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(DocRefRequestConverter.PatientParameterName, "test-patient"),
                        Tuple.Create(DocRefRequestConverter.StartParameterName, DateTime.UtcNow.Subtract(TimeSpan.FromDays(7)).ToString("o")),
                        Tuple.Create(DocRefRequestConverter.EndParameterName, DateTime.UtcNow.ToString("o")),
                        Tuple.Create(DocRefRequestConverter.ProfileParameterName, "https://canonical"),
                    },
                },
                new object[]
                {
                    // More than one "patient" parameter.
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(DocRefRequestConverter.PatientParameterName, "test-patient"),
                        Tuple.Create(DocRefRequestConverter.PatientParameterName, "test-patient1"),
                    },
                },
                new object[]
                {
                    // More than one "start" parameter.
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(DocRefRequestConverter.PatientParameterName, "test-patient"),
                        Tuple.Create(DocRefRequestConverter.StartParameterName, DateTime.UtcNow.Subtract(TimeSpan.FromDays(3)).ToString("o")),
                        Tuple.Create(DocRefRequestConverter.StartParameterName, DateTime.UtcNow.Subtract(TimeSpan.FromDays(7)).ToString("o")),
                    },
                },
                new object[]
                {
                    // More than one "end" parameter.
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(DocRefRequestConverter.PatientParameterName, "test-patient"),
                        Tuple.Create(DocRefRequestConverter.EndParameterName, DateTime.UtcNow.Subtract(TimeSpan.FromDays(3)).ToString("o")),
                        Tuple.Create(DocRefRequestConverter.EndParameterName, DateTime.UtcNow.Subtract(TimeSpan.FromDays(7)).ToString("o")),
                    },
                },
                new object[]
                {
                    // Missing required parameter "patient".
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(DocRefRequestConverter.StartParameterName, DateTime.UtcNow.Subtract(TimeSpan.FromDays(7)).ToString("o")),
                        Tuple.Create(DocRefRequestConverter.TypeParameterName, "https://loinc.org|34133-7"),
                        Tuple.Create(DocRefRequestConverter.TypeParameterName, "https://loinc.org|34133-8"),
                        Tuple.Create(DocRefRequestConverter.TypeParameterName, "https://loinc.org|34133-9"),
                    },
                },
                new object[]
                {
                    // More than one "start" parameter value.
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(DocRefRequestConverter.PatientParameterName, "test-patient"),
                        Tuple.Create(
                            DocRefRequestConverter.StartParameterName,
                            $"{DateTime.UtcNow.Subtract(TimeSpan.FromDays(3)).ToString("o")},{DateTime.UtcNow.Subtract(TimeSpan.FromDays(7)).ToString("o")}"),
                    },
                },
                new object[]
                {
                    // More than one "end" parameter value.
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(DocRefRequestConverter.PatientParameterName, "test-patient"),
                        Tuple.Create(
                            DocRefRequestConverter.EndParameterName,
                            $"{DateTime.UtcNow.Subtract(TimeSpan.FromDays(3)).ToString("o")},{DateTime.UtcNow.Subtract(TimeSpan.FromDays(7)).ToString("o")}"),
                    },
                },
                new object[]
                {
                    // Missing required parameter "patient".
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(DocRefRequestConverter.PatientParameterName, "test-patient"),
                        Tuple.Create(DocRefRequestConverter.TypeParameterName, "https://loinc.org|34133-7"),
                        Tuple.Create(DocRefRequestConverter.TypeParameterName, "https://loinc.org|34133-8"),
                        Tuple.Create(DocRefRequestConverter.TypeParameterName, "https://loinc.org|34133-9"),
                    },
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }
    }
}
