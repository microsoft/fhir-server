// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Shared.Core.Features.Conformance;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    public class DocRefControllerTests
    {
        private readonly DocRefController _controller;
        private readonly IDocRefRequestConverter _converter;
        private readonly USCoreConfiguration _configuration;

        public DocRefControllerTests()
        {
            _converter = Substitute.For<IDocRefRequestConverter>();
            _converter.ConvertAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new Bundle().ToResourceElement()));
            _configuration = new USCoreConfiguration();
            _controller = new DocRefController(
                _converter,
                Options.Create(_configuration));
            _controller.ControllerContext = new ControllerContext(
                new ActionContext(
                    Substitute.For<HttpContext>(),
                    new RouteData(),
                    new ControllerActionDescriptor()));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenGetRequest_WhenDocRefIsEnabled_ThenRequestShouldBeSentToConverter(
            bool enable)
        {
            _configuration.EnableDocRef = enable;

            try
            {
                await _controller.Search();
                Assert.True(enable);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enable);
            }

            await _converter.Received(enable ? 1 : 0).ConvertAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [MemberData(nameof(DocRefByPostTestData))]
        public async Task GivenPostRequest_WhenDocRefIsEnabled_ThenRequestShouldBeSentToConverter(
            bool enable,
            Parameters parameters)
        {
            var expectedParameters = new List<Tuple<string, string>>();
            if (parameters != null)
            {
                foreach (var p in parameters.Parameter)
                {
                    expectedParameters.Add(Tuple.Create(p.Name, p.Value.ToString()));
                }
            }

            _configuration.EnableDocRef = enable;
            _converter.ConvertAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        var p = (IReadOnlyList<Tuple<string, string>>)x[0];
                        Assert.Equal(expectedParameters.Count, p.Count);
                        if (expectedParameters.Any())
                        {
                            Assert.All(
                                p,
                                x => Assert.Contains(expectedParameters, y => y.Equals(x)));
                        }

                        return Task.FromResult(new Bundle().ToResourceElement());
                    });

            try
            {
                await _controller.Search(parameters);
                Assert.True(enable);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enable);
            }

            await _converter.Received(enable ? 1 : 0).ConvertAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>());
        }

        private static Parameters ToParameters(Dictionary<string, string> parameters)
        {
            if (parameters == null)
            {
                return null;
            }

            var resource = new Parameters();
            foreach (var p in parameters)
            {
                resource.Parameter.Add(
                    new Parameters.ParameterComponent()
                    {
                        Name = p.Key,
                        Value = new FhirString(p.Value),
                    });
            }

            return resource;
        }

        public static IEnumerable<object[]> DocRefByPostTestData()
        {
            var data = new[]
            {
                new object[]
                {
                    true,
                    ToParameters(
                        new Dictionary<string, string>
                        {
                            { "p1", "v1" },
                            { "p2", "v2" },
                            { "p3", "v3" },
                            { "p4", "v4" },
                            { "p5", "v5" },
                        }),
                },
                new object[]
                {
                    false,
                    ToParameters(new Dictionary<string, string>()),
                },
                new object[]
                {
                    true,
                    null,
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }
    }
}
