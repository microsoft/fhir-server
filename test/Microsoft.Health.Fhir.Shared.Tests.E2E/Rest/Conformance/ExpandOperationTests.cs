// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Web;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Models;
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
    public class ExpandOperationTests : IClassFixture<ExpandOperationTestFixture>
    {
        private static readonly HashSet<string> ValidParameterNames = new HashSet<string>(
            TerminologyOperationParameterNames.Expand.Names,
            StringComparer.OrdinalIgnoreCase);

        private readonly ExpandOperationTestFixture _fixture;

        public ExpandOperationTests(ExpandOperationTestFixture fixture)
        {
            _fixture = fixture;
        }

        private TestFhirClient Client => _fixture.TestFhirClient;

        private TestFhirServer Server => _fixture.TestFhirServer;

        [SkippableTheory]
        [InlineData("?url=0", null)]
        [InlineData("?offset=10", null)] // Invalid parameter (missing required parameter)
        [InlineData("?valueSet={'bogusResource'}", "valueSet")] // Invalid parameter value
        [InlineData("?url=0&unknown=unknown", "unknown")] // Invalid parameter (unknown parameter)
        public async Task GivenQuery_WhenExpanding_ThenValueSetShouldBeExpanded(
            string query,
            string invalid)
        {
            var valid = false;
            try
            {
                var expandEnabled = Server.Metadata.SupportsOperation(OperationsConstants.ValueSetExpand);
                Skip.IfNot(expandEnabled, "The $expand operation is disabled");

                var parameters = ParseQuery(query, null, out valid);
                var url = $"{KnownResourceTypes.ValueSet}/{KnownRoutes.Expand}{ToQueryString(query, parameters.Collection)}";
                var pUrl = parameters.Collection.GetValues(TerminologyOperationParameterNames.Expand.Url)?.FirstOrDefault();
                var response = await Client.ReadAsync<Resource>(url);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Validate(response.Resource, pUrl, parameters.Collection, invalid);
                Assert.True(valid);
            }
            catch (FhirClientException ex) when ((int)ex.StatusCode >= 400 && (int)ex.StatusCode <= 499)
            {
                Assert.False(valid);
                if (string.IsNullOrEmpty(invalid))
                {
                    Assert.Contains(Api.Resources.ExpandMissingRequiredParameter, ex.Message);
                }
                else
                {
                    Validate(ex, invalid);
                }
            }
        }

        [SkippableTheory]
        [InlineData(0, null, null)]
        [InlineData(1, "?url=0", null)] // The url parameter should be ignored.
        [InlineData(0, "?offset=10", null)]
        [InlineData(1, "?valueSet={'bogusResource'}", null)] // Invalid parameter value should be replaced by a resource from the store.
        [InlineData(0, "?unknown=unknown", "unknown")] // Invalid parameter (unknown parameter)
        public async Task GivenResourceIdAndQuery_WhenExpanding_ThenValueSetShouldBeExpanded(
            int index,
            string query,
            string invalid)
        {
            var valid = false;
            try
            {
                var expandEnabled = Server.Metadata.SupportsOperation(OperationsConstants.ValueSetExpand);
                Skip.IfNot(expandEnabled, "The $expand operation is disabled");

                var resource = GetResource(index);
                var parameters = ParseQuery(query, resource, out valid);
                var url = $"{KnownResourceTypes.ValueSet}/{resource.Id}/{KnownRoutes.Expand}{ToQueryString(query, parameters.Collection)}";
                var response = await Client.ReadAsync<Resource>(url);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Validate(response.Resource, resource.Url, parameters.Collection, invalid);
                Assert.True(valid);
            }
            catch (FhirClientException ex) when ((int)ex.StatusCode >= 400 && (int)ex.StatusCode <= 499)
            {
                Assert.False(valid);
                if (string.IsNullOrEmpty(invalid))
                {
                    Assert.Contains(Api.Resources.ExpandMissingRequiredParameter, ex.Message);
                }
                else
                {
                    Validate(ex, invalid);
                }
            }
        }

        [SkippableTheory]
        [InlineData("?url=0", null)]
        [InlineData("?valueSet=0", null)]
        [InlineData("?url=0&valueSet=1", null)] // LocalTerminologyService takes 'valueSet' over 'url' parameter.
        [InlineData("?offset=10", null)] // Invalid parameter (missing required parameter)
        [InlineData("?valueSet={'bogusResource'}", "valueSet")] // Invalid parameter value
        [InlineData("?url=0&unknown=unknown", "unknown")] // Invalid parameter (unknown parameter)
        public async Task GivenParameters_WhenExpanding_ThenValueSetShouldBeExpanded(
            string query,
            string invalid)
        {
            var valid = false;
            try
            {
                var expandEnabled = Server.Metadata.SupportsOperation(OperationsConstants.ValueSetExpand);
                Skip.IfNot(expandEnabled, "The $expand operation is disabled");

                var parameters = ParseQuery(query, null, out valid);
                var url = $"{KnownResourceTypes.ValueSet}/{KnownRoutes.Expand}";
                var pUrl = GetUrl(query);
                var response = await Client.CreateAsync<Resource>(
                    url,
                    parameters.Resource);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Validate(response.Resource, pUrl, parameters.Collection, invalid);
                Assert.True(valid);
            }
            catch (FhirClientException ex) when ((int)ex.StatusCode >= 400 && (int)ex.StatusCode <= 499)
            {
                Assert.False(valid);
                if (string.IsNullOrEmpty(invalid))
                {
                    Assert.Contains(Api.Resources.ExpandMissingRequiredParameter, ex.Message);
                }
                else
                {
                    Validate(ex, invalid);
                }
            }
        }

        private ValueSet GetResource(int index)
        {
            Assert.InRange(index, 0, _fixture.ValueSets.Count - 1);

            return _fixture.ValueSets[index];
        }

        private string GetUrl(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return null;
            }

            string url = null;
            var parameters = HttpUtility.ParseQueryString(query);
            if (parameters.AllKeys.Any(x => string.Equals(x, TerminologyOperationParameterNames.Expand.Url, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var v in parameters.GetValues(TerminologyOperationParameterNames.Expand.Url))
                {
                    if (int.TryParse(v, out var intValue))
                    {
                        url = GetResource(intValue).Url;
                    }
                }
            }

            if (parameters.AllKeys.Any(x => string.Equals(x, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var v in parameters.GetValues(TerminologyOperationParameterNames.Expand.ValueSet))
                {
                    if (int.TryParse(v, out var intValue))
                    {
                        url = GetResource(intValue).Url;
                    }
                }
            }

            return url;
        }

        private (NameValueCollection Collection, Parameters Resource) ParseQuery(string query, Resource resource, out bool valid)
        {
            valid = false;
            if (string.IsNullOrEmpty(query))
            {
                valid = resource != null;
                return (new NameValueCollection(), new Parameters());
            }

            var parameterCollection = HttpUtility.ParseQueryString(query);
            if (parameterCollection.AllKeys.Any(x => string.Equals(x, TerminologyOperationParameterNames.Expand.Url, StringComparison.OrdinalIgnoreCase)))
            {
                var values = new List<string>();
                foreach (var v in parameterCollection.GetValues(TerminologyOperationParameterNames.Expand.Url))
                {
                    if (int.TryParse(v, out var intValue))
                    {
                        values.Add(GetResource(intValue).Url);
                    }
                    else
                    {
                        values.Add(v);
                    }
                }

                parameterCollection.Remove(TerminologyOperationParameterNames.Expand.Url);
                foreach (var v in values)
                {
                    parameterCollection.Add(TerminologyOperationParameterNames.Expand.Url, v);
                }
            }

            if (parameterCollection.AllKeys.Any(x => string.Equals(x, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase)))
            {
                var values = new List<string>();
                foreach (var v in parameterCollection.GetValues(TerminologyOperationParameterNames.Expand.ValueSet))
                {
                    if (int.TryParse(v, out var intValue))
                    {
                        values.Add(GetResource(intValue).ToJson());
                    }
                    else
                    {
                        values.Add(v);
                    }
                }

                parameterCollection.Remove(TerminologyOperationParameterNames.Expand.ValueSet);
                foreach (var v in values)
                {
                    parameterCollection.Add(TerminologyOperationParameterNames.Expand.ValueSet, v);
                }
            }

            var parametersResource = new Parameters();
            foreach (var k in parameterCollection.AllKeys)
            {
                foreach (var v in parameterCollection.GetValues(k))
                {
                    parametersResource.Parameter.Add(
                        new Parameters.ParameterComponent()
                        {
                            Name = k,
                            Value = new FhirString(v),
                        });
                }
            }

            if (resource != null)
            {
                parametersResource.Remove(TerminologyOperationParameterNames.Expand.ValueSet);
                parametersResource.Parameter.Add(
                    new Parameters.ParameterComponent()
                    {
                        Name = TerminologyOperationParameterNames.Expand.ValueSet,
                        Resource = resource,
                    });
            }

            valid = (resource != null
                || parameterCollection.AllKeys.Any(
                        x => string.Equals(x, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(x, TerminologyOperationParameterNames.Expand.Url, StringComparison.OrdinalIgnoreCase)))
                && parameterCollection.AllKeys.All(x => ValidParameterNames.Contains(x));
            return (parameterCollection, parametersResource);
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

        private static void Validate(
            Resource resource,
            string url,
            NameValueCollection parameters,
            string invalidParameters)
        {
            Assert.NotNull(resource);

            if (string.IsNullOrEmpty(invalidParameters))
            {
                Assert.IsType<ValueSet>(resource);
                var valueSet = (ValueSet)resource;

                if (!string.IsNullOrEmpty(url))
                {
                    Assert.Equal(url, valueSet.Url);
                }

                // NOTE: Just checking if the Expansion property is not null or empty for now.
                // (LocalTerminologyService doesn't seem to support basic parameters like 'offset' and 'count'.)
                Assert.NotNull(valueSet.Expansion);
                Assert.NotEmpty(valueSet.Expansion);
            }
            else
            {
                Assert.IsType<OperationOutcome>(resource);
                var operationOutcome = (OperationOutcome)resource;
                Assert.NotEmpty(operationOutcome.Issue);

                var invalid = invalidParameters?.Split(
                    new char[] { ',' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    ?? new string[0];
                Assert.Contains(
                    operationOutcome.Issue,
                    x => x.Severity == OperationOutcome.IssueSeverity.Error
                        && x.Code == OperationOutcome.IssueType.Invalid
                        && !string.IsNullOrEmpty(x.Diagnostics)
                        && invalid.Any(y => x.Diagnostics.Contains(y)));
            }
        }

        private void Validate(
            FhirClientException exception,
            string invalidParameters)
        {
            Assert.NotNull(exception);
            Assert.IsType<OperationOutcome>(exception.OperationOutcome);
            Assert.NotEmpty(exception.OperationOutcome.Issue);

            var invalid = invalidParameters?.Split(
                new char[] { ',' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? new string[0];
            Assert.Contains(
                exception.OperationOutcome.Issue,
                x => x.Severity == OperationOutcome.IssueSeverity.Error
                    && x.Code == OperationOutcome.IssueType.Invalid
                    && !string.IsNullOrEmpty(x.Diagnostics)
                    && invalid.Any(y => x.Diagnostics.Contains(y)));
        }
    }
}
