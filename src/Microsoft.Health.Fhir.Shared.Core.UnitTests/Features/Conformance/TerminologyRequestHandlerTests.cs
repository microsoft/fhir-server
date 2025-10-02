// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Conformance;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Conformance;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Conformance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    public class TerminologyRequestHandlerTests
    {
        private readonly FhirJsonParser _parser;
        private readonly TerminologyRequestHandler _handler;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ITerminologyServiceProxy _terminologyServiceProxy;
        private readonly IMediator _mediator;

        public TerminologyRequestHandlerTests()
        {
            _authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            _authorizationService.CheckAccess(
                Arg.Any<DataActions>(),
                Arg.Any<CancellationToken>())
                .Returns(DataActions.Read);

            _terminologyServiceProxy = Substitute.For<ITerminologyServiceProxy>();
            _terminologyServiceProxy.ExpandAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateValueSet(Guid.NewGuid().ToString()).ToResourceElement()));

            _mediator = Substitute.For<IMediator>();
            _mediator.Send(
                Arg.Any<GetResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateGetResourceResponse(CreateValueSet(Guid.NewGuid().ToString()))));

            _parser = new FhirJsonParser(
                new ParserSettings()
                {
                    PermissiveParsing = true,
                });
            _handler = new TerminologyRequestHandler(
                _authorizationService,
                _terminologyServiceProxy,
                _mediator,
                Substitute.For<ILogger<TerminologyRequestHandler>>());
        }

        [Theory]
        [MemberData(nameof(GetExpandRequestTestData))]
        public async Task GivenExpandRequest_WhenHandling_ThenProxyShouldBeCalledWithCorrectParameters(
            ExpandRequest request,
            Exception exception)
        {
            _mediator.Send(
                Arg.Any<GetResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        Assert.True(!string.IsNullOrEmpty(request.ResourceId));

                        var req = (GetResourceRequest)x[0];
                        Assert.NotNull(req);
                        Assert.Equal(KnownResourceTypes.ValueSet, req.ResourceKey?.ResourceType);
                        Assert.Equal(request.ResourceId, req.ResourceKey?.Id);

                        if (exception != null)
                        {
                            throw exception;
                        }

                        return Task.FromResult(CreateGetResourceResponse(CreateValueSet(request.ResourceId)));
                    });

            _terminologyServiceProxy.ExpandAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        Assert.Null(exception);

                        var parameters = (IReadOnlyList<Tuple<string, string>>)x[0];
                        var resourceId = (string)x[1];
                        Assert.NotNull(parameters);
                        Assert.Equal(request.ResourceId, resourceId);
                        Assert.All(
                            request.Parameters.Where(x => !string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase)),
                            x =>
                            {
                                Assert.Contains(
                                    parameters,
                                    y =>
                                    {
                                        return y.Equals(x);
                                    });
                            });

                        if (!string.IsNullOrEmpty(request.ResourceId)
                            || request.Parameters.Any(x => string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase)))
                        {
                            Assert.Contains(
                                parameters,
                                x =>
                                {
                                    if (!string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase))
                                    {
                                        return false;
                                    }

                                    var actual = _parser.Parse<Resource>(x.Item2);
                                    var rid = request.ResourceId;
                                    if (string.IsNullOrEmpty(rid))
                                    {
                                        var json = request.Parameters.Single(y => string.Equals(y.Item1, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase))?.Item2;
                                        if (!string.IsNullOrEmpty(json))
                                        {
                                            var expected = _parser.Parse<Resource>(json);
                                            rid = expected.Id;
                                        }
                                    }

                                    return string.Equals(rid, actual.Id, StringComparison.OrdinalIgnoreCase);
                                });
                        }
                        else
                        {
                            Assert.DoesNotContain(
                                parameters,
                                x => string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase));
                        }

                        return Task.FromResult(CreateValueSet().ToResourceElement());
                    });
            try
            {
                await _handler.Handle(
                    request,
                    CancellationToken.None);
                Assert.Null(exception);
            }
            catch
            {
                Assert.NotNull(exception);
            }

            await _mediator.Received(string.IsNullOrEmpty(request.ResourceId) ? 0 : 1).Send(
                Arg.Any<GetResourceRequest>(),
                Arg.Any<CancellationToken>());
        }

        private static GetResourceResponse CreateGetResourceResponse(Resource resource)
        {
            return new GetResourceResponse(
                new RawResourceElement(
                    new ResourceWrapper(
                        Guid.NewGuid().ToString(),
                        null,
                        KnownResourceTypes.ValueSet,
                        new RawResource(
                            resource.ToJson(),
                            FhirResourceFormat.Json,
                            false),
                        null,
                        DateTimeOffset.UtcNow,
                        false,
                        null,
                        null,
                        null)));
        }

        private static ValueSet CreateValueSet(string id = default)
        {
            return new ValueSet()
            {
                Id = id,
                Status = PublicationStatus.Active,
            };
        }

        public static IEnumerable<object[]> GetExpandRequestTestData()
        {
            var data = new[]
            {
                new object[]
                {
                    new ExpandRequest(
                        new List<Tuple<string, string>>(),
                        Guid.NewGuid().ToString()),
                    null,
                },
                new object[]
                {
                    new ExpandRequest(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(TerminologyOperationParameterNames.Expand.ValueSet, CreateValueSet(Guid.NewGuid().ToString()).ToJson()),
                        },
                        null),
                    null,
                },
                new object[]
                {
                    new ExpandRequest(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(TerminologyOperationParameterNames.Expand.ValueSet, CreateValueSet(Guid.NewGuid().ToString()).ToJson()),
                        },
                        Guid.NewGuid().ToString()),
                    null,
                },
                new object[]
                {
                    new ExpandRequest(
                        new List<Tuple<string, string>>(),
                        Guid.NewGuid().ToString()),
                    new Exception("Unexpected exception."),
                },
                new object[]
                {
                    new ExpandRequest(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(TerminologyOperationParameterNames.Expand.Url, "http://acme.com/fhir/ValueSet/23"),
                            Tuple.Create(TerminologyOperationParameterNames.Expand.Offset, "200"),
                            Tuple.Create(TerminologyOperationParameterNames.Expand.IncludeDefinition, "false"),
                            Tuple.Create(TerminologyOperationParameterNames.Expand.ValueSet, CreateValueSet(Guid.NewGuid().ToString()).ToJson()),
                        },
                        Guid.NewGuid().ToString()),
                    null,
                },
                new object[]
                {
                    new ExpandRequest(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(TerminologyOperationParameterNames.Expand.Url, "http://acme.com/fhir/ValueSet/23"),
                            Tuple.Create(TerminologyOperationParameterNames.Expand.Offset, "200"),
                            Tuple.Create(TerminologyOperationParameterNames.Expand.IncludeDefinition, "false"),
                            Tuple.Create(TerminologyOperationParameterNames.Expand.ValueSet, CreateValueSet(Guid.NewGuid().ToString()).ToJson()),
                        },
                        null),
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
