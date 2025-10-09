// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Conformance;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.Extensions;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Conformance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class DocRefOperationTests : IClassFixture<DocRefOperationTestFixture>
    {
        private const string UnknownParameterName = "unknown";

        private readonly DocRefOperationTestFixture _fixture;

        public DocRefOperationTests(DocRefOperationTestFixture fixture)
        {
            _fixture = fixture;
        }

        private TestFhirClient Client => _fixture.TestFhirClient;

        private TestFhirServer Server => _fixture.TestFhirServer;

        [SkippableTheory]
        [InlineData("?patient=0")]
        [InlineData("?patient=0,2")]
        [InlineData("?patient=nonexistent")]
        [InlineData("?patient=0&patient=1")] // Invalid parameter
        [InlineData("?patient=1&start=-03:01:00")]
        [InlineData("?patient=0,1,2&start=-01:01:00")]
        [InlineData("?patient=2&start=-01:01:00&start=-05:00:00")] // Invalid parameter
        [InlineData("?patient=1&start=00:03:00")]
        [InlineData("?patient=1&end=-04:01:00")]
        [InlineData("?patient=0,1&end=-10:01:00")]
        [InlineData("?patient=2&end=-10:01:00&end=-09:00:00")] // Invalid parameter
        [InlineData("?patient=1&start=-08:01:00&end=-02:01:00")]
        [InlineData("?patient=1&start=-04:00:00&end=-04:00:00")]
        [InlineData("?patient=1&start=-02:01:00&end=-08:01:00")]
        [InlineData("?patient=0&type=http://loinc.org|55107-7")]
        [InlineData("?patient=0&type=http://loinc.org|55107-7,http://loinc.org|34873-0")]
        [InlineData("?patient=0&type=http://loinc.org|74155-3&type=http://loinc.org|55107-7")]
        [InlineData("?patient=1&type=http://loinc.org|nonexistent")]
        [InlineData("?patient=2&start=-11:01:00&end=-01:01:00&type=http://loinc.org|55107-7")]
        [InlineData("?patient=0,2&start=-09:01:00&end=-03:01:00&type=http://loinc.org|55107-7,http://loinc.org|34873-0")]
        [InlineData("?patient=0,1,2&start=-1.09:01:00&end=1.03:01:00&type=http://loinc.org|74155-3&type=http://loinc.org|55107-7")]
        [InlineData("?start=-01:01:00")] // Invalid parameter (missing required parameter - "patient")
        [InlineData("?patient=0&on-demand=true")] // Unsupported parameter
        [InlineData("?patient=0&profile=http://loinc.org/55107-7")] // Unsupported parameter
        [InlineData("?patient=0&profile=http://loinc.org/55107-7&on-demand=true")] // Unsupported parameter
        [InlineData("?patient=0&unknown=unknownvalue")] // Unknown parameter
        [InlineData("?patient=1&start=-08:01:00,-02:01:00")] // Invalie parameter value
        [InlineData("?patient=1&end=-08:01:00,-02:01:00")] // Invalie parameter value
        public async Task GivenQuery_WhenInvokingDocRef_ThenDocumentReferenceShouldBeRetrieved(
            string query)
        {
            var valid = false;
            var unsupported = false;
            var unknown = false;
            try
            {
                var docrefEnabled = Server.Metadata.SupportsOperation(OperationsConstants.DocRef);
                Skip.IfNot(docrefEnabled, "The $docref operation is disabled");

                var parameters = ParseQuery(query, out valid, out unsupported, out unknown);
                var url = $"{KnownResourceTypes.DocumentReference}/{KnownRoutes.DocRef}{ToQueryString(query, parameters)}";
                var actual = await InvokeAsync(url);
                Assert.True(valid);

                Validate(GetExpected(parameters, valid, unsupported), actual.documentReferences);
                Validate(parameters, actual.operationOutcomes);
            }
            catch (FhirClientException ex) when ((int)ex.StatusCode >= 400 && (int)ex.StatusCode <= 499)
            {
                Assert.False(valid);
            }
        }

        [SkippableTheory]
        [MemberData(nameof(GetParametersTestData))]
        public async Task GivenParameters_WhenInvokingDocRef_ThenDocumentReferenceShouldBeRetrieved(
            Parameters parameters)
        {
            var valid = false;
            var unsupported = false;
            var unknown = false;
            try
            {
                var docrefEnabled = Server.Metadata.SupportsOperation(OperationsConstants.DocRef);
                Skip.IfNot(docrefEnabled, "The $docref operation is disabled");

                var parameterCollections = ParseParameters(parameters, out valid, out unsupported, out unknown);
                var url = $"{KnownResourceTypes.DocumentReference}/{KnownRoutes.DocRef}";
                var actual = await InvokeAsync(url, parameters);
                Assert.True(valid);

                Validate(GetExpected(parameterCollections, valid, unsupported), actual.documentReferences);
                Validate(parameterCollections, actual.operationOutcomes);
            }
            catch (FhirClientException ex) when ((int)ex.StatusCode >= 400 && (int)ex.StatusCode <= 499)
            {
                Assert.False(valid);
            }
        }

        private Dictionary<string, Dictionary<string, DocumentReference>> GetExpected(
            NameValueCollection parameters,
            bool valid,
            bool unsupported)
        {
            if (!valid || unsupported)
            {
                return new Dictionary<string, Dictionary<string, DocumentReference>>();
            }

            var patientIds = parameters.GetValues(DocRefRequestConverter.PatientParameterName)?
                .SelectMany(x => x.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .Where(x => _fixture.Patients.Any(y => string.Equals(y.Id, x, StringComparison.OrdinalIgnoreCase)))
                .ToList() ?? new List<string>();
            var start = parameters.GetValues(DocRefRequestConverter.StartParameterName)?
                .SelectMany(x => x.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .FirstOrDefault();
            var end = parameters.GetValues(DocRefRequestConverter.EndParameterName)?
                .SelectMany(x => x.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .FirstOrDefault();
            var types = parameters.GetValues(DocRefRequestConverter.TypeParameterName)?
                .Select(x => x.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .ToList() ?? new List<string[]>();
            var resources = patientIds
                .SelectMany(x => _fixture.GetDocumentReferences(x))
                .ToList();
            var startDate = !string.IsNullOrEmpty(start) && DateTime.TryParse(start, out var sdate) ? sdate : DateTime.MinValue;
            var endDate = !string.IsNullOrEmpty(end) && DateTime.TryParse(end, out var edate) ? edate : DateTime.MaxValue;
            resources = FilterByPeriod(resources, startDate, endDate);
            resources = FilterByTypes(resources, types);

            return resources.GroupBy(x => x.Subject?.Reference).ToDictionary(x => x.Key, x => x.ToDictionary(y => y.Id));
        }

        private async Task<(Dictionary<string, Dictionary<string, DocumentReference>> documentReferences, List<OperationOutcome> operationOutcomes)> InvokeAsync(
            string url)
        {
            List<DocumentReference> documentReferences = new List<DocumentReference>();
            List<OperationOutcome> operationOutcomes = new List<OperationOutcome>();
            while (!string.IsNullOrEmpty(url))
            {
                var response = await Client.SearchAsync(url);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var bundle = response.Resource;
                documentReferences.AddRange(
                    bundle.Entry.Where(x => x.Search?.Mode != Bundle.SearchEntryMode.Outcome).Select(x => (DocumentReference)x.Resource));
                operationOutcomes.AddRange(
                    bundle.Entry.Where(x => x.Search?.Mode == Bundle.SearchEntryMode.Outcome).Select(x => (OperationOutcome)x.Resource));
                url = bundle.NextLink?.OriginalString;
            }

            return (documentReferences
                .GroupBy(x => x.Subject?.Reference)
                .ToDictionary(x => x.Key, x => x.ToDictionary(y => y.Id)),
                operationOutcomes);
        }

        private async Task<(Dictionary<string, Dictionary<string, DocumentReference>> documentReferences, List<OperationOutcome> operationOutcomes)> InvokeAsync(
            string url,
            Parameters parameters)
        {
            List<DocumentReference> documentReferences = new List<DocumentReference>();
            List<OperationOutcome> operationOutcomes = new List<OperationOutcome>();
            while (!string.IsNullOrEmpty(url))
            {
                var response = await Client.CreateAsync<Resource>(
                    url,
                    parameters);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var bundle = response.Resource as Bundle;
                Assert.NotNull(bundle);

                documentReferences.AddRange(
                    bundle.Entry.Where(x => x.Search?.Mode != Bundle.SearchEntryMode.Outcome).Select(x => (DocumentReference)x.Resource));
                operationOutcomes.AddRange(
                    bundle.Entry.Where(x => x.Search?.Mode == Bundle.SearchEntryMode.Outcome).Select(x => (OperationOutcome)x.Resource));
                url = bundle.NextLink?.OriginalString;
            }

            return (documentReferences
                .GroupBy(x => x.Subject?.Reference)
                .ToDictionary(x => x.Key, x => x.ToDictionary(y => y.Id)),
                operationOutcomes);
        }

        private NameValueCollection ParseParameters(Parameters parameters, out bool valid, out bool unsupported, out bool unknown)
        {
            valid = false;
            unsupported = false;
            unknown = false;
            if (parameters?.Parameter == null || !parameters.Parameter.Any())
            {
                return new NameValueCollection();
            }

            var collection = new NameValueCollection();
            foreach (var p in parameters.Parameter)
            {
                collection.Add(p.Name, ((FhirString)p.Value).Value);
            }

            if (parameters.Parameter.Any(x => string.Equals(x.Name, DocRefRequestConverter.PatientParameterName)))
            {
                var values = new List<string>();
                var value = new StringBuilder();
                foreach (var p in parameters.Parameter
                    .Where(x => string.Equals(x.Name, DocRefRequestConverter.PatientParameterName, StringComparison.OrdinalIgnoreCase))
                    .Select(x => ((FhirString)x.Value).Value))
                {
                    foreach (var v in p.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (int.TryParse(v, out var i) && i >= 0 && i < _fixture.Patients.Count)
                        {
                            value.AppendFormat("{0},", _fixture.Patients[i].Id);
                        }
                        else
                        {
                            value.AppendFormat("{0},", v);
                        }
                    }

                    values.Add(value.ToString().TrimEnd(','));
                    value.Clear();
                }

                valid = values.Count == 1;
                parameters.Remove(DocRefRequestConverter.PatientParameterName);
                collection.Remove(DocRefRequestConverter.PatientParameterName);
                foreach (var v in values)
                {
                    parameters.Add(DocRefRequestConverter.PatientParameterName, new FhirString(v));
                    collection.Add(DocRefRequestConverter.PatientParameterName, v);
                }
            }

            if (parameters.Parameter.Any(x => string.Equals(x.Name, DocRefRequestConverter.StartParameterName)))
            {
                var values = new List<string>();
                var value = new StringBuilder();
                foreach (var p in parameters.Parameter
                    .Where(x => string.Equals(x.Name, DocRefRequestConverter.StartParameterName, StringComparison.OrdinalIgnoreCase))
                    .Select(x => ((FhirString)x.Value).Value))
                {
                    foreach (var v in p.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (TimeSpan.TryParse(v, out var ts))
                        {
                            value.AppendFormat("{0},", _fixture.BaseTime.Add(ts).ToString("o"));
                        }
                        else
                        {
                            value.AppendFormat("{0},", v);
                        }
                    }

                    values.Add(value.ToString().TrimEnd(','));
                    value.Clear();
                }

                valid = valid && values.Count == 1 && !values[0].Contains(',', StringComparison.Ordinal);
                parameters.Remove(DocRefRequestConverter.StartParameterName);
                collection.Remove(DocRefRequestConverter.StartParameterName);
                foreach (var v in values)
                {
                    parameters.Add(DocRefRequestConverter.StartParameterName, new FhirString(v));
                    collection.Add(DocRefRequestConverter.StartParameterName, v);
                }
            }

            if (parameters.Parameter.Any(x => string.Equals(x.Name, DocRefRequestConverter.EndParameterName)))
            {
                var values = new List<string>();
                var value = new StringBuilder();
                foreach (var p in parameters.Parameter
                    .Where(x => string.Equals(x.Name, DocRefRequestConverter.EndParameterName, StringComparison.OrdinalIgnoreCase))
                    .Select(x => ((FhirString)x.Value).Value))
                {
                    foreach (var v in p.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (TimeSpan.TryParse(v, out var ts))
                        {
                            value.AppendFormat("{0},", _fixture.BaseTime.Add(ts).ToString("o"));
                        }
                        else
                        {
                            value.AppendFormat("{0},", v);
                        }
                    }

                    values.Add(value.ToString().TrimEnd(','));
                    value.Clear();
                }

                valid = valid && values.Count == 1 && !values[0].Contains(',', StringComparison.Ordinal);
                parameters.Remove(DocRefRequestConverter.EndParameterName);
                collection.Remove(DocRefRequestConverter.EndParameterName);
                foreach (var v in values)
                {
                    parameters.Add(DocRefRequestConverter.EndParameterName, new FhirString(v));
                    collection.Add(DocRefRequestConverter.EndParameterName, v);
                }
            }

            unsupported = collection.AllKeys.Any(
                x => string.Equals(x, DocRefRequestConverter.OnDemandParameterName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x, DocRefRequestConverter.ProfileParameterName, StringComparison.OrdinalIgnoreCase));
            unknown = collection.AllKeys.Any(
                x => string.Equals(x, UnknownParameterName, StringComparison.OrdinalIgnoreCase));
            return collection;
        }

        private NameValueCollection ParseQuery(string query, out bool valid, out bool unsupported, out bool unknown)
        {
            valid = false;
            unsupported = false;
            unknown = false;
            if (string.IsNullOrEmpty(query))
            {
                return new NameValueCollection();
            }

            var parameters = HttpUtility.ParseQueryString(query);
            if (parameters.AllKeys.Any(x => string.Equals(x, DocRefRequestConverter.PatientParameterName, StringComparison.OrdinalIgnoreCase)))
            {
                var values = new List<string>();
                var value = new StringBuilder();
                foreach (var p in parameters.GetValues(DocRefRequestConverter.PatientParameterName))
                {
                    foreach (var v in p.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (int.TryParse(v, out var i) && i >= 0 && i < _fixture.Patients.Count)
                        {
                            value.AppendFormat("{0},", _fixture.Patients[i].Id);
                        }
                        else
                        {
                            value.AppendFormat("{0},", v);
                        }
                    }

                    values.Add(value.ToString().TrimEnd(','));
                    value.Clear();
                }

                valid = values.Count == 1;
                parameters.Remove(DocRefRequestConverter.PatientParameterName);
                foreach (var v in values)
                {
                    parameters.Add(DocRefRequestConverter.PatientParameterName, v);
                }
            }

            if (parameters.AllKeys.Any(x => string.Equals(x, DocRefRequestConverter.StartParameterName, StringComparison.OrdinalIgnoreCase)))
            {
                var values = new List<string>();
                var value = new StringBuilder();
                foreach (var p in parameters.GetValues(DocRefRequestConverter.StartParameterName))
                {
                    foreach (var v in p.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (TimeSpan.TryParse(v, out var ts))
                        {
                            value.AppendFormat("{0},", _fixture.BaseTime.Add(ts).ToString("o"));
                        }
                        else
                        {
                            value.AppendFormat("{0},", v);
                        }
                    }

                    values.Add(value.ToString().TrimEnd(','));
                    value.Clear();
                }

                valid = valid && values.Count == 1 && !values[0].Contains(',', StringComparison.Ordinal);
                parameters.Remove(DocRefRequestConverter.StartParameterName);
                foreach (var v in values)
                {
                    parameters.Add(DocRefRequestConverter.StartParameterName, v);
                }
            }

            if (parameters.AllKeys.Any(x => string.Equals(x, DocRefRequestConverter.EndParameterName, StringComparison.OrdinalIgnoreCase)))
            {
                var values = new List<string>();
                var value = new StringBuilder();
                foreach (var p in parameters.GetValues(DocRefRequestConverter.EndParameterName))
                {
                    foreach (var v in p.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (TimeSpan.TryParse(v, out var ts))
                        {
                            value.AppendFormat("{0},", _fixture.BaseTime.Add(ts).ToString("o"));
                        }
                        else
                        {
                            value.AppendFormat("{0},", v);
                        }
                    }

                    values.Add(value.ToString().TrimEnd(','));
                    value.Clear();
                }

                valid = valid && values.Count == 1 && !values[0].Contains(',', StringComparison.Ordinal);
                parameters.Remove(DocRefRequestConverter.EndParameterName);
                foreach (var v in values)
                {
                    parameters.Add(DocRefRequestConverter.EndParameterName, v);
                }
            }

            unsupported = parameters.AllKeys.Any(
                x => string.Equals(x, DocRefRequestConverter.OnDemandParameterName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x, DocRefRequestConverter.ProfileParameterName, StringComparison.OrdinalIgnoreCase));
            unknown = parameters.AllKeys.Any(
                x => string.Equals(x, UnknownParameterName, StringComparison.OrdinalIgnoreCase));
            return parameters;
        }

        private static void Validate(
            Dictionary<string, Dictionary<string, DocumentReference>> expected,
            Dictionary<string, Dictionary<string, DocumentReference>> actual)
        {
            Assert.Equal(expected?.Count, actual?.Count);

            if (expected != null && expected.Any())
            {
                expected.All(
                    x => actual.TryGetValue(x.Key, out var actualRefs)
                        && x.Value.All(y => actualRefs.TryGetValue(y.Key, out _)));
            }
        }

        private static void Validate(
            NameValueCollection parameters,
            List<OperationOutcome> operationOutcomes)
        {
            var ondemand = parameters.AllKeys.Any(
                x => string.Equals(x, DocRefRequestConverter.OnDemandParameterName, StringComparison.OrdinalIgnoreCase));
            var profile = parameters.AllKeys.Any(
                x => string.Equals(x, DocRefRequestConverter.ProfileParameterName, StringComparison.OrdinalIgnoreCase));
            if (ondemand || profile)
            {
                Assert.NotEmpty(operationOutcomes);
                Assert.Contains(
                    operationOutcomes,
                    x => x.Issue.Any(
                        y => y.Severity == OperationOutcome.IssueSeverity.Error
                            && y.Code == OperationOutcome.IssueType.NotSupported
                            && !string.IsNullOrEmpty(y.Diagnostics)
                            && y.Diagnostics.Contains(ondemand ? DocRefRequestConverter.OnDemandParameterName : DocRefRequestConverter.ProfileParameterName)));
            }
            else
            {
                Assert.DoesNotContain(
                    operationOutcomes,
                    x => x.Issue.Any(
                        y => y.Severity == OperationOutcome.IssueSeverity.Error
                            && y.Code == OperationOutcome.IssueType.NotSupported
                            && !string.IsNullOrEmpty(y.Diagnostics)
                            && (y.Diagnostics.Contains(DocRefRequestConverter.OnDemandParameterName)
                            || y.Diagnostics.Contains(DocRefRequestConverter.ProfileParameterName))));
            }

            var unknown = parameters.AllKeys.Any(
                x => string.Equals(x, UnknownParameterName, StringComparison.OrdinalIgnoreCase));
            if (unknown)
            {
                Assert.Contains(
                    operationOutcomes,
                    x => x.Issue.Any(
                        y => y.Severity == OperationOutcome.IssueSeverity.Warning
                            && y.Code == OperationOutcome.IssueType.NotSupported
                            && !string.IsNullOrEmpty(y.Diagnostics)
                            && y.Diagnostics.Contains(UnknownParameterName)));
            }
        }

        private static List<DocumentReference> FilterByPeriod(
            List<DocumentReference> resources,
            DateTime start,
            DateTime end)
        {
            resources = resources?
                .Where(
                    x =>
                    {
                        var s = DateTime.MinValue;
                        var e = DateTime.MaxValue;
#if R4 || R4B || Stu3
                        if (DateTime.TryParse(x.Context?.Period?.Start, out var ss))
#else
                        if (DateTime.TryParse(x.Period?.Start, out var ss))
#endif
                        {
                            s = ss;
                        }

#if R4 || R4B || Stu3
                        if (DateTime.TryParse(x.Context?.Period?.End, out var ee))
#else
                        if (DateTime.TryParse(x.Period?.End, out var ee))
#endif
                        {
                            e = ee;
                        }

                        return start <= s && s <= end && start <= e && e <= end;
                    })
                .ToList();

            return resources;
        }

        private static List<DocumentReference> FilterByTypes(
            List<DocumentReference> resources,
            List<string[]> types)
        {
            if (types == null || !types.Any())
            {
                return resources;
            }

            resources = resources?
                .Where(
                    x =>
                    {
                        foreach (var t in types)
                        {
                            if (t == null || !t.Any())
                            {
                                continue;
                            }

                            if (!t.Any(y => x.Type?.Coding?.Any(z => string.Equals($"{z.System}|{z.Code}", y, StringComparison.OrdinalIgnoreCase)) ?? false))
                            {
                                return false;
                            }
                        }

                        return true;
                    })
                .ToList();

            return resources;
        }

        private static string ToQueryString(
            string query,
            NameValueCollection parameters)
        {
            if (string.IsNullOrEmpty(query) || parameters == null)
            {
                return query;
            }

            return $"{(query.StartsWith('?') ? "?" : string.Empty)}{parameters}";
        }

        private static Parameters ToParameters(List<Tuple<string, string>> parameters)
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
                        Name = p.Item1,
                        Value = new FhirString(p.Item2),
                    });
            }

            return resource;
        }

        public static IEnumerable<object[]> GetParametersTestData()
        {
            var data = new[]
            {
                new object[]
                {
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "0"),
                        }),
                },
                new object[]
                {
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "0,2"),
                        }),
                },
                new object[]
                {
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "1"),
                            Tuple.Create(DocRefRequestConverter.StartParameterName, "-03:01:00"),
                        }),
                },
                new object[]
                {
                    // Invalid parameter
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "2"),
                            Tuple.Create(DocRefRequestConverter.StartParameterName, "-03:01:00"),
                            Tuple.Create(DocRefRequestConverter.StartParameterName, "-05:01:00"),
                        }),
                },
                new object[]
                {
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "1"),
                            Tuple.Create(DocRefRequestConverter.EndParameterName, "-08:01:00"),
                        }),
                },
                new object[]
                {
                    // Invalid parameter
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "2"),
                            Tuple.Create(DocRefRequestConverter.EndParameterName, "-06:01:00"),
                            Tuple.Create(DocRefRequestConverter.EndParameterName, "-09:01:00"),
                        }),
                },
                new object[]
                {
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "0"),
                            Tuple.Create(DocRefRequestConverter.StartParameterName, "-1.00:01:00"),
                            Tuple.Create(DocRefRequestConverter.EndParameterName, "00:01:00"),
                        }),
                },
                new object[]
                {
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "0"),
                            Tuple.Create(DocRefRequestConverter.TypeParameterName, "http://loinc.org|55107-7"),
                        }),
                },
                new object[]
                {
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "0"),
                            Tuple.Create(DocRefRequestConverter.TypeParameterName, "http://loinc.org|55107-7,http://loinc.org|34873-0"),
                        }),
                },
                new object[]
                {
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "0"),
                            Tuple.Create(DocRefRequestConverter.TypeParameterName, "http://loinc.org|74155-3"),
                            Tuple.Create(DocRefRequestConverter.TypeParameterName, "http://loinc.org|55107-7"),
                        }),
                },
                new object[]
                {
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "2"),
                            Tuple.Create(DocRefRequestConverter.StartParameterName, "-11:01:00"),
                            Tuple.Create(DocRefRequestConverter.EndParameterName, "-03:01:00"),
                            Tuple.Create(DocRefRequestConverter.TypeParameterName, "http://loinc.org|55107-7"),
                        }),
                },
                new object[]
                {
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "0,1,2"),
                            Tuple.Create(DocRefRequestConverter.StartParameterName, "-09:01:00"),
                            Tuple.Create(DocRefRequestConverter.EndParameterName, "-03:01:00"),
                            Tuple.Create(DocRefRequestConverter.TypeParameterName, "http://loinc.org|55107-7"),
                            Tuple.Create(DocRefRequestConverter.TypeParameterName, "http://loinc.org|74155-3"),
                        }),
                },
                new object[]
                {
                    // Invalid parameter (missing required parameter - "patient")
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.StartParameterName, "-09:01:00"),
                            Tuple.Create(DocRefRequestConverter.EndParameterName, "-03:01:00"),
                            Tuple.Create(DocRefRequestConverter.TypeParameterName, "http://loinc.org|55107-7"),
                            Tuple.Create(DocRefRequestConverter.TypeParameterName, "http://loinc.org|74155-3"),
                        }),
                },
                new object[]
                {
                    // Unsupported parameter
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "0,1,2"),
                            Tuple.Create(DocRefRequestConverter.OnDemandParameterName, "true"),
                        }),
                },
                new object[]
                {
                    // Unsupported parameter
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "0,1,2"),
                            Tuple.Create(DocRefRequestConverter.ProfileParameterName, "http://loinc.org/55107-7"),
                        }),
                },
                new object[]
                {
                    // Unsupported parameter
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "0,1,2"),
                            Tuple.Create(DocRefRequestConverter.ProfileParameterName, "http://loinc.org/55107-7"),
                            Tuple.Create(DocRefRequestConverter.OnDemandParameterName, "true"),
                        }),
                },
                new object[]
                {
                    // Invalid parameter
                    new Parameters(),
                },
                new object[]
                {
                    // Unknown parameter
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "0"),
                            Tuple.Create(UnknownParameterName, "unknownvalue"),
                        }),
                },
                new object[]
                {
                    // Invalid parameter value
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "0,1,2"),
                            Tuple.Create(DocRefRequestConverter.StartParameterName, "-09:01:00,-03:01:00"),
                        }),
                },
                new object[]
                {
                    // Invalid parameter value
                    ToParameters(
                        new List<Tuple<string, string>>
                        {
                            Tuple.Create(DocRefRequestConverter.PatientParameterName, "0,1,2"),
                            Tuple.Create(DocRefRequestConverter.EndParameterName, "-09:01:00,-03:01:00"),
                        }),
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }
    }
}
