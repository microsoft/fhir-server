// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
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
    [Trait(Traits.Category, Categories.Search)]
    public class DocRefRequestProcessorTests
    {
        private readonly DocRefRequestProcessor _processor;
        private readonly IMediator _mediator;
        private readonly IBundleFactory _bundleFactory;
        private readonly ILogger<DocRefRequestProcessor> _logger;

        public DocRefRequestProcessorTests()
        {
            _mediator = Substitute.For<IMediator>();
            _mediator.SearchResourceAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new Bundle().ToResourceElement()));

            _bundleFactory = Substitute.For<IBundleFactory>();
            _bundleFactory.CreateSearchBundle(
                Arg.Any<SearchResult>())
                .Returns(new Bundle().ToResourceElement());

            _logger = Substitute.For<ILogger<DocRefRequestProcessor>>();
            _processor = new DocRefRequestProcessor(
                _mediator,
                _bundleFactory,
                _logger);
        }

        [Theory]
        [MemberData(nameof(ProcessParametersTestData))]
        public async Task GivenParameters_WhenProcessing_ThenSearchResourceShouldBeCalledSuccessfully(
            List<Tuple<string, string>> parameters)
        {
            var valid = parameters?.Any(
                x => string.Equals(x.Item1, DocRefRequestProcessor.PatientParameterName, StringComparison.OrdinalIgnoreCase)) ?? false;
            var unsupported = parameters?.Any(
                x => string.Equals(x.Item1, DocRefRequestProcessor.OnDemandParameterName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Item1, DocRefRequestProcessor.ProfileParameterName, StringComparison.OrdinalIgnoreCase)) ?? true;

            _mediator.SearchResourceAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        var resourceType = (string)x[0];
                        Assert.Equal(KnownResourceTypes.DocumentReference, resourceType, true);

                        var parametersConverted = (IReadOnlyList<Tuple<string, string>>)x[1];
                        ValidateParameters(parameters, parametersConverted);

                        return Task.FromResult(new Bundle().ToResourceElement());
                    });
            try
            {
                await _processor.ProcessAsync(
                    parameters,
                    CancellationToken.None);
                Assert.True(valid);
            }
            catch (RequestNotValidException)
            {
                Assert.False(valid);
            }

            await _mediator.Received(valid && !unsupported ? 1 : 0).SearchResourceAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
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
                var original = parameters
                    .Where(x => !string.Equals(x.Item1, DocRefRequestProcessor.OnDemandParameterName, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(x.Item1, DocRefRequestProcessor.ProfileParameterName, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(x => x.Item1, x => x.Item2);
                var converted = parametersConverted.ToDictionary(x => x.Item1, x => x.Item2);

                Assert.Equal(original.Count, converted.Count);
                Assert.Contains(
                    original,
                    x =>
                    {
                        var p = x.Key;
                        var v = x.Value;
                        if (DocRefRequestProcessor.ConvertParameterMap.TryGetValue(x.Key, out var pp))
                        {
                            if (string.Equals(p, DocRefRequestProcessor.PatientParameterName, StringComparison.OrdinalIgnoreCase))
                            {
                                v = $"{KnownResourceTypes.Patient}/{x.Value}";
                            }
                            else if (string.Equals(p, DocRefRequestProcessor.StartParameterName, StringComparison.OrdinalIgnoreCase))
                            {
                                v = $"ge{x.Value}";
                            }
                            else if (string.Equals(p, DocRefRequestProcessor.EndParameterName, StringComparison.OrdinalIgnoreCase))
                            {
                                v = $"le{x.Value}";
                            }

                            p = pp;
                        }

                        return converted.Any(
                            y => string.Equals(p, y.Key, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(v, y.Value, StringComparison.OrdinalIgnoreCase));
                    });
            }
        }

        public static IEnumerable<object[]> ProcessParametersTestData()
        {
            var data = new[]
            {
                new object[]
                {
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(DocRefRequestProcessor.PatientParameterName, "test-patient"),
                        Tuple.Create(DocRefRequestProcessor.StartParameterName, DateTime.UtcNow.Subtract(TimeSpan.FromDays(7)).ToString("o")),
                        Tuple.Create(DocRefRequestProcessor.EndParameterName, DateTime.UtcNow.ToString("o")),
                        Tuple.Create(DocRefRequestProcessor.TypeParameterName, "https://loinc.org|34133-9"),
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
