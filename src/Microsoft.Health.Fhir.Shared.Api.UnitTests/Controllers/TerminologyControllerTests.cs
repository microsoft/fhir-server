// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Web;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Messages.Conformance;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    public class TerminologyControllerTests
    {
        private readonly TerminologyController _controller;
        private readonly TerminologyConfiguration _configuration;
        private readonly IMediator _mediator;

        public TerminologyControllerTests()
        {
            _mediator = Substitute.For<IMediator>();
            _mediator.Send<ExpandResponse>(
                Arg.Any<ExpandRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new ExpandResponse(new ValueSet().ToResourceElement()));

            _configuration = new TerminologyConfiguration();
            _configuration.EnableExpand = true;

            _controller = new TerminologyController(
                _mediator,
                Options.Create(_configuration));
            _controller.ControllerContext = new ControllerContext(
                new ActionContext(
                    Substitute.For<HttpContext>(),
                    new RouteData(),
                    new ControllerActionDescriptor()));
        }

        [Theory]
        [InlineData(true, true, false)]
        [InlineData(true, false, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, true)]
        [InlineData(false, false, false)]
        public async Task GivenRequest_WhenExpanding_ThenRequestShouldBeSentToHandler(
            bool enable,
            bool useId,
            bool useParameters)
        {
            _configuration.EnableExpand = enable;

            try
            {
                if (useId)
                {
                    await _controller.Expand(Guid.NewGuid().ToString());
                }
                else if (useParameters)
                {
                    var p = new Parameters()
                    {
                        Parameter = new List<Parameters.ParameterComponent>
                        {
                            new Parameters.ParameterComponent()
                            {
                                Name = TerminologyOperationParameterNames.Expand.Url,
                                Value = new FhirUri("http://acme.com/fhir/ValueSet/23"),
                            },
                        },
                    };

                    await _controller.Expand(p);
                }
                else
                {
                    _controller.HttpContext.Request.QueryString = new QueryString("?url=http://acme.com/fhir/ValueSet/23");
                    await _controller.Expand();
                }

                Assert.True(enable);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enable);
            }

            await _mediator.Received(enable ? 1 : 0).Send<ExpandResponse>(
                Arg.Any<ExpandRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData("?url=http://acme.com/fhir/ValueSet/23", true, false)]
        [InlineData("?url=http://acme.com/fhir/ValueSet/23&offset=10", true, false)]
        [InlineData("?url=http://acme.com/fhir/ValueSet/23&valueSetVersion=1.1&filter=abdo", true, false)]
        [InlineData("?context=http://fhir.org/guides/argonaut-clinicalnotes/StructureDefinition/argo-diagnosticreport%23DiagnosticReport.category&url=http://acme.com/fhir/ValueSet/23&offset=10&date=2014-02-23", true, false)]
        [InlineData("?context=http://fhir.org/guides/argonaut-clinicalnotes/StructureDefinition/argo-diagnosticreport%23DiagnosticReport.category", true, false)]
        [InlineData("?offset=10", false, false)]
        [InlineData("?url=http://acme.com/fhir/ValueSet/23&unknown=10", false, false)]
        [InlineData("", false, false)]
        [InlineData("?url=http://acme.com/fhir/ValueSet/23", true, true)]
        [InlineData("?url=http://acme.com/fhir/ValueSet/23&offset=10", true, true)]
        [InlineData("?url=http://acme.com/fhir/ValueSet/23&valueSetVersion=1.1&filter=abdo", true, true)]
        [InlineData("?context=http://fhir.org/guides/argonaut-clinicalnotes/StructureDefinition/argo-diagnosticreport%23DiagnosticReport.category&url=http://acme.com/fhir/ValueSet/23&offset=10&date=2014-02-23", true, true)]
        [InlineData("?offset=10", true, true)]
        [InlineData("?url=http://acme.com/fhir/ValueSet/23&unknown=10", false, true)]
        [InlineData("", true, true)]
        public async Task GivenGetRequest_WhenExpanding_ThenCorrectRequestShouldBeSentToHandler(
            string query,
            bool valid,
            bool useId)
        {
            _mediator.Send<ExpandResponse>(
                Arg.Any<ExpandRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        Assert.True(valid);

                        var request = (ExpandRequest)x[0];
                        Assert.NotNull(request);

                        var expected = ParseQuery(query);
                        Assert.Equal(expected.Count, request.Parameters.Count);
                        Assert.All(
                            expected,
                            x => Assert.Contains(request.Parameters, y => y.Equals(x)));

                        return new ExpandResponse(new ValueSet().ToResourceElement());
                    });
            try
            {
                _controller.HttpContext.Request.QueryString = new QueryString(query);
                if (useId)
                {
                    await _controller.Expand(Guid.NewGuid().ToString());
                }
                else
                {
                    await _controller.Expand();
                }

                Assert.True(valid);
            }
            catch (RequestNotValidException)
            {
                Assert.False(valid);
            }

            await _mediator.Received(valid ? 1 : 0).Send<ExpandResponse>(
                Arg.Any<ExpandRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [MemberData(nameof(GetExpandByPostTestData))]
        public async Task GivenPostRequest_WhenExpanding_ThenCorrectRequestShouldBeSentToHandler(
            Parameters parameters,
            bool valid)
        {
            _mediator.Send<ExpandResponse>(
                Arg.Any<ExpandRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        Assert.True(valid);

                        var request = (ExpandRequest)x[0];
                        Assert.NotNull(request);

                        var expected = ParseParameters(parameters);
                        Assert.Equal(expected.Count, request.Parameters.Count);
                        Assert.All(
                            expected,
                            x => Assert.Contains(request.Parameters, y => y.Equals(x)));

                        return new ExpandResponse(new ValueSet().ToResourceElement());
                    });
            try
            {
                await _controller.Expand(parameters);
                Assert.True(valid);
            }
            catch (RequestNotValidException)
            {
                Assert.False(valid);
            }

            await _mediator.Received(valid ? 1 : 0).Send<ExpandResponse>(
                Arg.Any<ExpandRequest>(),
                Arg.Any<CancellationToken>());
        }

        private static List<Tuple<string, string>> ParseParameters(Parameters parameters)
        {
            var result = new List<Tuple<string, string>>();
            if (parameters?.Parameter != null)
            {
                foreach (var p in parameters.Parameter)
                {
                    if (p.Resource == null)
                    {
                        result.Add(Tuple.Create(p.Name, p.Value.ToString()));
                    }
                    else
                    {
                        result.Add(Tuple.Create(p.Name, p.Resource.ToJson()));
                    }
                }
            }

            return result;
        }

        private static List<Tuple<string, string>> ParseQuery(string query)
        {
            var result = new List<Tuple<string, string>>();
            if (!string.IsNullOrEmpty(query))
            {
                var parameters = HttpUtility.ParseQueryString(query);
                foreach (var k in parameters.AllKeys)
                {
                    foreach (var p in parameters.GetValues(k))
                    {
                        result.Add(Tuple.Create(k, p));
                    }
                }
            }

            return result;
        }

        public static IEnumerable<object[]> GetExpandByPostTestData()
        {
            var data = new[]
            {
                new object[]
                {
                    new Parameters()
                    {
                        Parameter = new List<Parameters.ParameterComponent>()
                        {
                            new Parameters.ParameterComponent()
                            {
                                Name = TerminologyOperationParameterNames.Expand.ValueSet,
                                Resource = new ValueSet()
                                {
                                    Status = PublicationStatus.Active,
                                },
                            },
                        },
                    },
                    true,
                },
                new object[]
                {
                    new Parameters()
                    {
                        Parameter = new List<Parameters.ParameterComponent>()
                        {
                            new Parameters.ParameterComponent()
                            {
                                Name = TerminologyOperationParameterNames.Expand.Url,
                                Value = new FhirUri("http://acme.com/fhir/ValueSet/23"),
                            },
                        },
                    },
                    true,
                },
                new object[]
                {
                    new Parameters()
                    {
                        Parameter = new List<Parameters.ParameterComponent>()
                        {
                            new Parameters.ParameterComponent()
                            {
                                Name = TerminologyOperationParameterNames.Expand.Url,
                                Value = new FhirUri("http://acme.com/fhir/ValueSet/23"),
                            },
                            new Parameters.ParameterComponent()
                            {
                                Name = TerminologyOperationParameterNames.Expand.Offset,
                                Value = new Integer(200),
                            },
                            new Parameters.ParameterComponent()
                            {
                                Name = TerminologyOperationParameterNames.Expand.Date,
                                Value = new FhirDateTime(DateTime.UtcNow.ToString("o")),
                            },
                            new Parameters.ParameterComponent()
                            {
                                Name = TerminologyOperationParameterNames.Expand.ValueSetVersion,
                                Value = new FhirString("1.1"),
                            },
                            new Parameters.ParameterComponent()
                            {
                                Name = TerminologyOperationParameterNames.Expand.ContextDirection,
                                Value = new Code("incoming"),
                            },
                            new Parameters.ParameterComponent()
                            {
                                Name = TerminologyOperationParameterNames.Expand.IncludeDesignations,
                                Value = new FhirBoolean(true),
                            },
                        },
                    },
                    true,
                },
                new object[]
                {
                    new Parameters()
                    {
                        Parameter = new List<Parameters.ParameterComponent>()
                        {
                            new Parameters.ParameterComponent()
                            {
                                Name = TerminologyOperationParameterNames.Expand.Offset,
                                Value = new Integer(200),
                            },
                        },
                    },
                    false,
                },
                new object[]
                {
                    new Parameters(),
                    false,
                },
                new object[]
                {
                    new Parameters()
                    {
                        Parameter = new List<Parameters.ParameterComponent>()
                        {
                            new Parameters.ParameterComponent()
                            {
                                Name = TerminologyOperationParameterNames.Expand.Url,
                                Value = new FhirUri("http://acme.com/fhir/ValueSet/23"),
                            },
                            new Parameters.ParameterComponent()
                            {
                                Name = "unknown",
                                Value = new FhirString("value"),
                            },
                        },
                    },
                    false,
                },
                new object[]
                {
                    new Parameters()
                    {
                        Parameter = new List<Parameters.ParameterComponent>()
                        {
                            new Parameters.ParameterComponent()
                            {
                                Name = TerminologyOperationParameterNames.Expand.Context,
                                Value = new FhirUri("http://hl7.org/fhir/us/core/StructureDefinition/us-core-diagnosticreport-note#DiagnosticReport.category#DiagnosticReport.code"),
                            },
                        },
                    },
                    true,
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }
    }
}
