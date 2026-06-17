// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Policy;
using System.Threading;
using AngleSharp.Dom;
using FluentValidation;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Specification.Terminology;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Shared.Core.Features.Conformance;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Conformance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    public class FirelyTerminologyServiceProxyTests
    {
        private readonly FirelyTerminologyServiceProxy _proxy;
        private readonly ITerminologyService _terminologyService;
        private readonly IAsyncResourceResolver _resourceResolver;

        public FirelyTerminologyServiceProxyTests()
        {
            _terminologyService = Substitute.For<ITerminologyService>();
            _terminologyService.Expand(
                Arg.Any<Parameters>(),
                Arg.Any<string>(),
                Arg.Any<bool>())
                .Returns(Task.FromResult<Resource>(new ValueSet() { Status = PublicationStatus.Active }));

            _resourceResolver = Substitute.For<IAsyncResourceResolver>();
            _resourceResolver.ResolveByCanonicalUriAsync(
                Arg.Any<string>())
                .Returns(Task.FromResult<Resource>(null));
            _proxy = new FirelyTerminologyServiceProxy(
                _terminologyService,
                _resourceResolver,
                Substitute.For<ILogger<FirelyTerminologyServiceProxy>>());
        }

        [Theory]
        [MemberData(nameof(GetExpandTestData))]
        public async Task GivenParameters_WhenExpanding_ThenProxyShouldCallServiceWithCorrectParameters(
            IReadOnlyList<Tuple<string, string>> parameterList,
            string resourceId)
        {
            Parameters parametersArg = null;
            string resourceIdArg = null;
            _terminologyService.Expand(
                Arg.Do<Parameters>(x => parametersArg = x),
                Arg.Do<string>(x => resourceIdArg = x),
                Arg.Any<bool>())
                .Returns(Task.FromResult<Resource>(new ValueSet() { Status = PublicationStatus.Active }));

            var resourceElement = await _proxy.ExpandAsync(
                parameterList,
                resourceId,
                CancellationToken.None);
            Assert.NotNull(resourceElement);

            var resource = resourceElement.ToPoco();
            Assert.IsType<ValueSet>(resource);
            Assert.NotNull(parametersArg);
            Assert.Equal(resourceId, resourceIdArg);
            Assert.All(
                parameterList.Where(x => !string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase)),
                x =>
                {
                    var ps = parametersArg.Parameter.Where(y => string.Equals(x.Item1, y.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                    Assert.Single(ps);

                    var p = ps[0];
                    switch (x.Item1)
                    {
                        case TerminologyOperationParameterNames.Expand.ActiveOnly:
                        case TerminologyOperationParameterNames.Expand.ExcludeNested:
                        case TerminologyOperationParameterNames.Expand.ExcludeNotForUI:
                        case TerminologyOperationParameterNames.Expand.ExcludePostCoordinated:
                        case TerminologyOperationParameterNames.Expand.IncludeDefinition:
                        case TerminologyOperationParameterNames.Expand.IncludeDesignations:
                            Assert.IsType<FhirBoolean>(p.Value);
                            Assert.Equal(x.Item2, ((FhirBoolean)p.Value).Value?.ToString(), true);
                            break;

                        case TerminologyOperationParameterNames.Expand.CheckSystemVersion:
                        case TerminologyOperationParameterNames.Expand.ExcludeSystem:
                        case TerminologyOperationParameterNames.Expand.ForceSystemVersion:
                        case TerminologyOperationParameterNames.Expand.SystemVersion:
                            Assert.IsType<Canonical>(p.Value);
                            Assert.Equal(x.Item2, ((Canonical)p.Value).Value);
                            break;

                        case TerminologyOperationParameterNames.Expand.Context:
                        case TerminologyOperationParameterNames.Expand.Url:
                            Assert.IsType<FhirUri>(p.Value);
                            Assert.Equal(x.Item2, ((FhirUri)p.Value).Value);
                            break;

                        case TerminologyOperationParameterNames.Expand.ContextDirection:
                        case TerminologyOperationParameterNames.Expand.DisplayLanguage:
                            Assert.IsType<Code>(p.Value);
                            Assert.Equal(x.Item2, ((Code)p.Value).Value);
                            break;

                        case TerminologyOperationParameterNames.Expand.Count:
                        case TerminologyOperationParameterNames.Expand.Offset:
                            Assert.IsType<Integer>(p.Value);
                            Assert.Equal(x.Item2, ((Integer)p.Value).Value?.ToString());
                            break;

                        case TerminologyOperationParameterNames.Expand.Date:
                            Assert.IsType<FhirDateTime>(p.Value);
                            Assert.Equal(x.Item2, ((FhirDateTime)p.Value).Value);
                            break;

                        case TerminologyOperationParameterNames.Expand.Designation:
                        case TerminologyOperationParameterNames.Expand.Filter:
                        case TerminologyOperationParameterNames.Expand.ValueSetVersion:
                            Assert.IsType<FhirString>(p.Value);
                            Assert.Equal(x.Item2, ((FhirString)p.Value).Value);
                            break;

                        default:
                            Assert.False(false, $"Unknown parameter name: {x.Item1}");
                            break;
                    }
                });

            if (parameterList.Any(x => string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase)))
            {
                var ps = parametersArg.Parameter.Where(x => string.Equals(x.Name, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase)).ToList();
                Assert.Single(ps);

                var p = ps[0];
                Assert.NotNull(p?.Resource);
                Assert.IsType<ValueSet>(p?.Resource);

                var json = parameterList.Single(x => string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase))?.Item2;
                Assert.Equal(json, p.Resource.ToJson());
            }

            await _terminologyService.Received(1).Expand(
                Arg.Any<Parameters>(),
                Arg.Any<string>(),
                Arg.Any<bool>());
        }

        [Theory]
        [MemberData(nameof(GetExpandFailureTestData))]
        public async Task GivenParameters_WhenExpandingFails_ThenProxyShouldHandleErrorFromServiceCorrectly(
            IReadOnlyList<Tuple<string, string>> parameterList,
            Exception exception,
            OperationOutcome.IssueSeverity severity,
            OperationOutcome.IssueType type,
            string message)
        {
            _terminologyService.Expand(
                Arg.Any<Parameters>(),
                Arg.Any<string>(),
                Arg.Any<bool>())
                .Throws(exception);

            var resourceElement = await _proxy.ExpandAsync(
                parameterList,
                null,
                CancellationToken.None);
            Assert.NotNull(resourceElement);

            var resource = resourceElement.ToPoco() as OperationOutcome;
            Assert.NotNull(resource);
            Assert.IsType<OperationOutcome>(resource);
            Assert.NotEmpty(resource.Issue);
            Assert.Equal(severity, resource.Issue[0].Severity);
            Assert.Equal(type, resource.Issue[0].Code);
            Assert.Contains(message, resource.Issue[0].Diagnostics);

            await _terminologyService.Received(exception == null ? 0 : 1).Expand(
                Arg.Any<Parameters>(),
                Arg.Any<string>(),
                Arg.Any<bool>());
        }

        [Theory]
        [MemberData(nameof(GetExpandContextTestData))]
        public async Task GivenContextParameter_WhenExpanding_ThenProxyShouldCallServiceWithCorrectParameters(
            IReadOnlyList<Tuple<string, string>> parameterList,
            string valueSetUrl)
        {
            parameterList = parameterList ?? new List<Tuple<string, string>>();
            var shouldUseContext = !parameterList
                .Any(x => string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.Url, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase));
            var context = parameterList
                .SingleOrDefault(x => string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.Context, StringComparison.OrdinalIgnoreCase))?
                .Item2;
            Assert.NotNull(context);

            var contextUri = new Uri(context);
            var definitionUrl = $"{contextUri.Scheme}://{contextUri.Authority}{contextUri.AbsolutePath}";
            var definitionPath = contextUri.Fragment?.TrimStart('#');
            var validContext = !string.IsNullOrEmpty(definitionUrl) && !string.IsNullOrEmpty(definitionPath);
            if (shouldUseContext && validContext)
            {
                _resourceResolver.ResolveByCanonicalUriAsync(
                    Arg.Is<string>(x => string.Equals(x, definitionUrl, StringComparison.OrdinalIgnoreCase)))
                    .Returns(Task.FromResult<Resource>(
                        new StructureDefinition()
                        {
                            Name = Guid.NewGuid().ToString(),
                            Url = definitionUrl,
                            Status = PublicationStatus.Active,
                            Snapshot = new StructureDefinition.SnapshotComponent()
                            {
                                Element = new List<ElementDefinition>
                                {
                                    new ElementDefinition()
                                    {
                                        Path = definitionPath,
                                        Binding = new ElementDefinition.ElementDefinitionBindingComponent()
                                        {
#if !Stu3
                                            ValueSet = valueSetUrl,
#else
                                            ValueSet = new FhirUri(valueSetUrl),
#endif
                                        },
                                    },
                                },
                            },
                        }));
            }

            Parameters parametersArg = null;
            string resourceIdArg = null;
            _terminologyService.Expand(
                Arg.Do<Parameters>(x => parametersArg = x),
                Arg.Do<string>(x => resourceIdArg = x),
                Arg.Any<bool>())
                .Returns(Task.FromResult<Resource>(new ValueSet() { Status = PublicationStatus.Active }));

            var resourceElement = await _proxy.ExpandAsync(
                parameterList,
                null,
                CancellationToken.None);
            Assert.NotNull(resourceElement);

            var resource = resourceElement.ToPoco();
            if (shouldUseContext)
            {
                if (validContext && !string.IsNullOrEmpty(valueSetUrl))
                {
                    Assert.IsType<ValueSet>(resource);
                    Assert.NotNull(parametersArg);
                    Assert.All(
                        parameterList.Where(
                            x => !string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.Context, StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase)),
                        x =>
                        {
                            var ps = parametersArg.Parameter.Where(y => string.Equals(x.Item1, y.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                            Assert.Single(ps);

                            var p = ps[0];
                            Assert.Equal(x.Item2, p.Value?.ToString());
                        });

                    Assert.Contains(
                        parametersArg.Parameter,
                        x =>
                        {
                            return string.Equals(x.Name, TerminologyOperationParameterNames.Expand.Url, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.Value?.ToString(), valueSetUrl);
                        });
                }
                else
                {
                    Assert.IsType<OperationOutcome>(resource);
                }
            }
            else
            {
                Assert.IsType<ValueSet>(resource);
                Assert.NotNull(parametersArg);
                Assert.All(
                    parameterList.Where(x => !string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase)),
                    x =>
                    {
                        var ps = parametersArg.Parameter.Where(y => string.Equals(x.Item1, y.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                        Assert.Single(ps);

                        var p = ps[0];
                        Assert.Equal(x.Item2, p.Value?.ToString());
                    });
            }

            if (parameterList.Any(x => string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase)))
            {
                var ps = parametersArg.Parameter.Where(x => string.Equals(x.Name, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase)).ToList();
                Assert.Single(ps);

                var p = ps[0];
                Assert.NotNull(p?.Resource);
                Assert.IsType<ValueSet>(p?.Resource);

                var json = parameterList.Single(x => string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase))?.Item2;
                Assert.Equal(json, p.Resource.ToJson());
            }

            await _resourceResolver.Received(shouldUseContext && validContext ? 1 : 0).ResolveByCanonicalUriAsync(
                Arg.Any<string>());
            await _terminologyService.Received(!shouldUseContext || (validContext && !string.IsNullOrEmpty(valueSetUrl)) ? 1 : 0).Expand(
                Arg.Any<Parameters>(),
                Arg.Any<string>(),
                Arg.Any<bool>());
        }

        private static ValueSet CreateValueSet(string id = default)
        {
            return new ValueSet()
            {
                Id = id,
                Status = PublicationStatus.Active,
            };
        }

        public static IEnumerable<object[]> GetExpandTestData()
        {
            var data = new[]
            {
                new object[]
                {
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Url, "http://acme.com/fhir/ValueSet/23"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.ValueSet, CreateValueSet(Guid.NewGuid().ToString()).ToJson()),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.ValueSetVersion, "1.1"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Context, "http://hl7.org/fhir/StructureDefinition/observation-hspc-height-hspcheight#Observation"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.ContextDirection, "outgoing"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Filter, "acut ast"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Date, DateTime.UtcNow.ToString("o")),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Offset, "200"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Count, "5000"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.IncludeDesignations, "true"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Designation, "http://loinc.org|74155-3"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.IncludeDefinition, "true"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.ActiveOnly, "false"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.ExcludeNested, "true"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.ExcludeNotForUI, "false"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.ExcludePostCoordinated, "true"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.DisplayLanguage, "english"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.ExcludeSystem, "http://loinc.org|1.01"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.SystemVersion, "http://loinc.org|2.56"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.CheckSystemVersion, "http://loinc.org|3.1"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.ForceSystemVersion, "http://loinc.org|4.99"),
                    },
                    Guid.NewGuid().ToString(),
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }

        public static IEnumerable<object[]> GetExpandFailureTestData()
        {
            var data = new[]
            {
                new object[]
                {
                    // BadRequestException expected
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Url, "http://acme.com/fhir/ValueSet/23"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.ExcludeNested, "not-boolean"),
                    },
                    null,
                    OperationOutcome.IssueSeverity.Error,
                    OperationOutcome.IssueType.Invalid,
                    $"'{TerminologyOperationParameterNames.Expand.ExcludeNested}'",
                },
                new object[]
                {
                    // BadRequestException expected
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Url, "http://acme.com/fhir/ValueSet/23"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Offset, "not-number"),
                    },
                    null,
                    OperationOutcome.IssueSeverity.Error,
                    OperationOutcome.IssueType.Invalid,
                    $"'{TerminologyOperationParameterNames.Expand.Offset}'",
                },
                new object[]
                {
                    // FhirOperationException thrown by a terminology service
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Url, "http://acme.com/fhir/ValueSet/23"),
                    },
                    new FhirOperationException(
                        "A terminology service failed.",
                        HttpStatusCode.BadRequest,
                        new OperationOutcome()
                        {
                            Issue = new List<OperationOutcome.IssueComponent>
                            {
                                new OperationOutcome.IssueComponent()
                                {
                                    Severity = OperationOutcome.IssueSeverity.Error,
                                    Code = OperationOutcome.IssueType.Invalid,
                                    Diagnostics = "Something is invalid.",
                                },
                            },
                        }),
                    OperationOutcome.IssueSeverity.Error,
                    OperationOutcome.IssueType.Invalid,
                    "Something is invalid.",
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }

        public static IEnumerable<object[]> GetExpandContextTestData()
        {
            var data = new[]
            {
                new object[]
                {
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Context, "http://hl7.org/fhir/us/core/StructureDefinition/us-core-diagnosticreport-note#DiagnosticReport.presentedForm.contentType"),
                    },
                    "http://acme.org/fhir/ValueSet/in-mimetypes-1",
                },
                new object[]
                {
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Url, "http://acme.org/fhir/ValueSet/in-mimetypes-1"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Context, "http://hl7.org/fhir/us/core/StructureDefinition/us-core-diagnosticreport-note#DiagnosticReport.presentedForm.contentType"),
                    },
                    "http://acme.org/fhir/ValueSet/in-mimetypes-1",
                },
                new object[]
                {
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Context, "http://hl7.org/fhir/us/core/StructureDefinition/us-core-diagnosticreport-note#DiagnosticReport.presentedForm.contentType"),
                        Tuple.Create(TerminologyOperationParameterNames.Expand.ValueSet, CreateValueSet(Guid.NewGuid().ToString()).ToJson()),
                    },
                    "http://acme.org/fhir/ValueSet/in-mimetypes-1",
                },
                new object[]
                {
                    // Invalid context value without fragment
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Context, "http://hl7.org/fhir/us/core/StructureDefinition/us-core-diagnosticreport-note"),
                    },
                    "http://acme.org/fhir/ValueSet/in-mimetypes-1",
                },
                new object[]
                {
                    // Valid context value without matching value set
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Context, "http://hl7.org/fhir/us/core/StructureDefinition/us-core-diagnosticreport-note#DiagnosticReport.presentedForm.contentType"),
                    },
                    null,
                },
                new object[]
                {
                    // Valid context value with the context url with port number.
                    new List<Tuple<string, string>>
                    {
                        Tuple.Create(TerminologyOperationParameterNames.Expand.Context, "http://hl7.org:1234/fhir/us/core/StructureDefinition/us-core-diagnosticreport-note#DiagnosticReport.presentedForm.contentType"),
                    },
                    "http://acme.org/fhir/ValueSet/in-mimetypes-1",
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }
    }
}
