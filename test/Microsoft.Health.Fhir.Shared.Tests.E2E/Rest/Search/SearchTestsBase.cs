// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using static Hl7.Fhir.Model.OperationOutcome;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public abstract class SearchTestsBase<TFixture> : IClassFixture<TFixture>
        where TFixture : HttpIntegrationTestFixture
    {
        private Regex _continuationToken = new Regex("[?&]ct");
        private ITestOutputHelper _output;

        protected SearchTestsBase(TFixture fixture, ITestOutputHelper output = null)
        {
            Fixture = fixture;
            _output = output;
        }

        protected TFixture Fixture { get; }

        protected TestFhirClient Client => Fixture.TestFhirClient;

        protected async Task<Bundle> ExecuteAndValidateBundle(string searchUrl, Tuple<string, string> customHeader, params Resource[] expectedResources)
        {
            return await ExecuteAndValidateBundle(searchUrl, searchUrl, true, customHeader, pageSize: 10, expectedResources);
        }

        protected async Task<Bundle> ExecuteAndValidateBundle(string searchUrl, params Resource[] expectedResources)
        {
            return await ExecuteAndValidateBundle(searchUrl, searchUrl, true, null, pageSize: 10, expectedResources);
        }

        protected async Task<Bundle> ExecuteAndValidateBundle(string searchUrl, bool sort, params Resource[] expectedResources)
        {
            var actualDecodedUrl = WebUtility.UrlDecode(searchUrl);
            return await ExecuteAndValidateBundle(searchUrl, actualDecodedUrl, sort, null, pageSize: 10, expectedResources);
        }

        protected async Task<Bundle> ExecuteAndValidateBundle(
            string searchUrl,
            bool sort,
            bool invalidSortParameter,
            Tuple<string, string> customHeader,
            params Resource[] expectedResources)
        {
            var actualDecodedUrl = WebUtility.UrlDecode(searchUrl);
            return await ExecuteAndValidateBundle(searchUrl, actualDecodedUrl, sort, invalidSortParameter, customHeader, pageSize: 10, expectedResources);
        }

        protected async Task<Bundle> ExecuteAndValidateBundle(string searchUrl, bool sort, int pageSize, params Resource[] expectedResources)
        {
            var actualDecodedUrl = WebUtility.UrlDecode(searchUrl);
            return await ExecuteAndValidateBundle(searchUrl, actualDecodedUrl, sort, null, pageSize, expectedResources);
        }

        protected async Task<Bundle> ExecuteAndValidateBundle(
            string searchUrl,
            string selfLink,
            bool sort,
            Tuple<string, string> customHeader = null,
            int pageSize = 10,
            params Resource[] expectedResources)
        {
            return await ExecuteAndValidateBundle(searchUrl, selfLink, sort, false, customHeader, pageSize, expectedResources);
        }

        protected async Task<Bundle> ExecuteAndValidateBundle(
            string searchUrl,
            string selfLink,
            bool sort,
            bool invalidSortParameter,
            Tuple<string, string> customHeader = null,
            int pageSize = 10,
            params Resource[] expectedResources)
        {
            bool numberOfResourcesIsGreaterThanExpected = false;
            string queryUrl = searchUrl;

            List<Resource> allResourcesReturned = new List<Resource>();
            FhirResponse<Bundle> firstBundle = null;
            do
            {
                FhirResponse<Bundle> fhirBundleResponse = null;
                try
                {
                    fhirBundleResponse = await Client.SearchAsync(queryUrl, customHeader);
                }
                catch (Exception ex)
                {
                    if (_output != null)
                    {
                        WriteSearchAsync(fhirBundleResponse, "ExecuteAndValidateBundle", queryUrl, customHeader, ex);
                    }

                    throw ex;
                }

                if (_output != null)
                {
                    WriteSearchAsync(fhirBundleResponse, "ExecuteAndValidateBundle", queryUrl, customHeader);
                }

                if (firstBundle == null)
                {
                    firstBundle = fhirBundleResponse;
                }

                string validationSelfLink;
                if (_continuationToken.Match(queryUrl).Success)
                {
                    // Truncating host and appending continuation token
                    validationSelfLink = selfLink + queryUrl.Substring(_continuationToken.Match(queryUrl).Index);
                }
                else
                {
                    validationSelfLink = selfLink;
                }

                int numberOfResourcesReturned = fhirBundleResponse.Resource.Entry.Count;

                if (allResourcesReturned.Count + numberOfResourcesReturned > expectedResources.Length)
                {
                    numberOfResourcesIsGreaterThanExpected = true;
                }
                else
                {
                    Resource[] expectedFirstBundle = expectedResources.Length > numberOfResourcesReturned ? expectedResources[allResourcesReturned.Count..(allResourcesReturned.Count + numberOfResourcesReturned)] : expectedResources;
                    ValidateBundle(fhirBundleResponse, validationSelfLink, sort, invalidSortParameter, expectedFirstBundle);
                }

                allResourcesReturned.AddRange(fhirBundleResponse.Resource.Entry.Select(e => e.Resource));
                queryUrl = fhirBundleResponse.Resource.NextLink?.ToString();
            }
            while (queryUrl != null);

            if (numberOfResourcesIsGreaterThanExpected)
            {
                ThrowInvalidBundleResultXunitException(expectedResources, allResourcesReturned);
            }

            return firstBundle;
        }

        protected async Task<OperationOutcome> ExecuteAndValidateErrorOperationOutcomeAsync(
            string searchUrl,
            Tuple<string, string> customHeader,
            HttpStatusCode expectedStatusCode,
            OperationOutcome expectedOperationOutcome)
        {
            FhirClientException ex = await Assert.ThrowsAsync<FhirClientException>(() => Client.SearchAsync(searchUrl, customHeader));
            Assert.Equal(expectedStatusCode, ex.StatusCode);
            ex.OperationOutcome.Id = null;

            // This is needed because the Meta is set to an instant and will be different when deep compared.
            expectedOperationOutcome.Meta = ex.OperationOutcome.Meta;

            Assert.True(expectedOperationOutcome.IsExactly(ex.OperationOutcome), "Deep compare detected expected and actual OperationOutcome mismatch.");
            return ex.OperationOutcome;
        }

        protected void ValidateBundle(Bundle bundle, string selfLink, params Resource[] expectedResources)
        {
            ValidateBundle(bundle, selfLink, true, false, expectedResources);
        }

        protected void ValidateBundle(Bundle bundle, string selfLink, bool sort, bool invalidSortParameter, params Resource[] expectedResources)
        {
            string actualUrl;

            // checking if continuation token is present in the link
            if (_continuationToken.IsMatch(bundle.SelfLink.AbsoluteUri))
            {
                // avoiding url decode of continuation token
                int tokenIndex = _continuationToken.Match(bundle.SelfLink.AbsoluteUri).Index;
                actualUrl = WebUtility.UrlDecode(bundle.SelfLink.AbsoluteUri.Substring(0, tokenIndex)) + bundle.SelfLink.AbsoluteUri.Substring(tokenIndex);
            }
            else
            {
                actualUrl = WebUtility.UrlDecode(bundle.SelfLink.AbsoluteUri);
            }

            if (!invalidSortParameter)
            {
                Skip.If(selfLink.Contains("_sort") && !actualUrl.Contains("_sort"), "This server does not support the supplied _sort parameter.");

                Assert.Equal(Fixture.GenerateFullUrl(selfLink), actualUrl);
            }
            else
            {
                Assert.DoesNotContain("_sort", actualUrl);
            }

            ValidateBundle(bundle, sort, invalidSortParameter, expectedResources);
        }

        protected void ValidateBundle(Bundle bundle, params Resource[] expectedResources)
        {
            ValidateBundle(bundle, true, false, expectedResources);
        }

        protected void ValidateBundle(Bundle bundle, bool sort, bool invalidSortParameter, params Resource[] expectedResources)
        {
            Assert.NotNull(bundle);

            if (invalidSortParameter)
            {
                Bundle.EntryComponent entry = Assert.Single(bundle.Entry, e => e.Resource.TypeName == KnownResourceTypes.OperationOutcome); // Exactly one OperationOutcome is returned.
                Assert.Equal(KnownResourceTypes.OperationOutcome, bundle.Entry[0].Resource.TypeName); // OperationOutcome is the first resource.
                entry.Resource.Id = null; // Set OperationOutcome id returned by the server to null before comparing with the expected resources.
            }

            if (sort)
            {
                bundle.Entry.Sort((a, b) => string.CompareOrdinal(a.Resource.Id, b.Resource.Id));
                Array.Sort(expectedResources, (a, b) => string.CompareOrdinal(a.Id, b.Id));
            }

            try
            {
                Assert.Collection(
                    bundle.Entry.Select(e => e.Resource),
                    expectedResources.Select(er => new Action<Resource>(r => Assert.True(er.IsExactly(r)))).ToArray());
            }
            catch (XunitException)
            {
                ThrowInvalidBundleResultXunitException(expectedResources, bundle.Entry.Select(e => e.Resource).ToArray());
            }
        }

        private void ThrowInvalidBundleResultXunitException(IReadOnlyList<Resource> expectedResources, IReadOnlyList<Resource> actualResources)
        {
            var sb = new StringBuilder("Expected count to be ");
            sb.Append(expectedResources.Count);
            sb.Append(" but was ");
            sb.Append(actualResources.Count);
            sb.AppendLine(" . Contents:");

            var fhirJsonSerializer = new FhirJsonSerializer(new SerializerSettings() { AppendNewLine = false, Pretty = false });
            using var sw = new StringWriter(sb);

            sb.AppendLine("Actual collection as below -");
            foreach (var element in actualResources)
            {
                sb.AppendLine(fhirJsonSerializer.SerializeToString(element));
            }

            sb.AppendLine("Expected Collection as below -");
            foreach (var element in expectedResources)
            {
                sb.AppendLine(fhirJsonSerializer.SerializeToString(element));
            }

            throw new XunitException(sb.ToString());
        }

        protected OperationOutcome GetAndValidateOperationOutcome(Bundle bundle)
        {
            var outcomeEnity = bundle.Entry.FirstOrDefault(x => x.Resource.TypeName == KnownResourceTypes.OperationOutcome);
            Assert.NotNull(outcomeEnity);
            var outcome = outcomeEnity.Resource as OperationOutcome;
            Assert.NotNull(outcome);
            return outcome;
        }

        protected void ValidateOperationOutcome(string[] expectedDiagnostics, IssueSeverity[] expectedIsseSeverity, IssueType[] expectedCodeTypes, OperationOutcome operationOutcome)
        {
            Assert.NotNull(operationOutcome?.Id);
            Assert.NotEmpty(operationOutcome?.Issue);

            Assert.Equal(expectedCodeTypes.Length, operationOutcome.Issue.Count);
            Assert.Equal(expectedDiagnostics.Length, operationOutcome.Issue.Count);

            for (int iter = 0; iter < operationOutcome.Issue.Count; iter++)
            {
                Assert.Equal(expectedCodeTypes[iter], operationOutcome.Issue[iter].Code);
                Assert.Equal(expectedIsseSeverity[iter], operationOutcome.Issue[iter].Severity);
                Assert.Equal(expectedDiagnostics[iter], operationOutcome.Issue[iter].Diagnostics);
            }
        }

        protected void ValidateBundleUrl(Uri expectedBaseAddress, ResourceType expectedResourceType, string expectedQuery, string bundleUrl)
        {
            var uriBuilder = new UriBuilder(expectedBaseAddress);
            uriBuilder.Path = Path.Combine(uriBuilder.Path, expectedResourceType.ToString());
            uriBuilder.Query = expectedQuery;

            Assert.Equal(HttpUtility.UrlDecode(uriBuilder.Uri.ToString()), HttpUtility.UrlDecode(bundleUrl));
        }

        // Troubleshooting helper methods follow.
        // These methods can be used to debug test failures in the pipeline.
        // ExecuteAndValidateBundle method is partially instrumented with WriteSearchAsync, which can be enabled by calling constructor with the output parameter set.
        protected async System.Threading.Tasks.Task WriteSearchAsync(string title, string pathAndQuery, Tuple<string, string> header = null)
        {
            Exception exception = null;
            FhirResponse<Bundle> fhirResponse = null;
#pragma warning disable CA1031 // Do not catch general exception types
            try
            {
                fhirResponse = await Client.SearchAsync(pathAndQuery, header);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
#pragma warning restore CA1031 // Do not catch general exception types

            WriteSearchAsync(fhirResponse, title, pathAndQuery, header, exception);
        }

        protected void WriteSearchAsync<T>(FhirResponse<T> fhirResponse, string title, string requestPathAndQuery, Tuple<string, string> requestHeader = null, Exception exception = null)
            where T : Resource
        {
            _output.WriteLine($"<--------- {title}");
            _output.WriteLine($"REQUEST:");
            _output.WriteLine($"  {requestPathAndQuery}");
            if (requestHeader != null)
            {
                _output.WriteLine($"  {requestHeader.Item1}: {requestHeader.Item2}");
            }

            _output.WriteLine($"RESPONSE:");

            if (fhirResponse != null)
            {
                _output.WriteLine($"  {fhirResponse.StatusCode}");
                if (fhirResponse.Headers != null)
                {
                    string headerName = KnownHeaders.InstanceId;
                    IEnumerable<string> headerValues = fhirResponse.Headers.GetValues(headerName);
                    foreach (string headerValue in headerValues)
                    {
                        _output.WriteLine($"  {headerName}: {headerValue}");
                    }
                }
                else
                {
                    _output.WriteLine("  fhirResponse.Headers == null");
                }

                if (fhirResponse.Resource != null)
                {
                    if (fhirResponse.Resource is Bundle bundle)
                    {
                        _output.WriteLine($"  {ToString(bundle)}:");
                        if (bundle.Entry == null)
                        {
                            _output.WriteLine("    bundle.Entry == null");
                        }
                        else
                        {
                            _output.WriteLine($"    bundle.Entry.Count = {bundle.Entry.Count}");
                            foreach (Bundle.EntryComponent ec in bundle.Entry)
                            {
                                _output.WriteLine($"    {ToString(ec.Resource)}");
                            }
                        }
                    }
                    else
                    {
                        _output.WriteLine($"  {ToString(fhirResponse.Resource)}");
                    }
                }
                else
                {
                    _output.WriteLine("  Resource == null");
                }
            }
            else
            {
                _output.WriteLine("  fhirResponse == null");
            }

            if (exception != null)
            {
                _output.WriteLine("  EXCEPTION:");
                while (exception != null)
                {
                    _output.WriteLine($"    {exception.Message}");
                    exception = exception.InnerException;
                }
            }

            _output.WriteLine($">---------");
        }

        protected static string ToString(Resource resource)
        {
            // Should return single line string.

            if (resource is Patient patient)
            {
                string name = (patient.Name?.Count ?? 0) > 0 ? patient.Name[0].Family : null;
                string telecom = (patient.Telecom?.Count ?? 0) > 0 ? patient.Telecom[0].Value : null;
                string identifier = (patient.Identifier?.Count ?? 0) > 0 ? patient.Identifier[0].Value : null;
                return $"{resource.TypeName}, {resource.Id}, {name}, {patient.BirthDate}, {telecom}, {patient.ManagingOrganization?.Reference}, {patient.Id}, {identifier}";
            }
            else if (resource is OperationOutcome operationOutcome)
            {
                if (operationOutcome.Issue == null)
                {
                    return "o.Issue == null";
                }

                string ostr = null;
                for (int i = 0; i < operationOutcome.Issue.Count; i++)
                {
                    if (ostr == null)
                    {
                        ostr = $"{resource.TypeName}, {resource.Id} || ";
                    }
                    else
                    {
                        ostr += " | ";
                    }

                    ostr += $"{operationOutcome.Issue[i].Code}, {operationOutcome.Issue[i].Severity}, {operationOutcome.Issue[i].Diagnostics}";
                }

                return ostr;
            }
            else
            {
                if (resource != null)
                {
                    return $"{resource.TypeName}, {resource.Id}";
                }
                else
                {
                    return "resource == null";
                }
            }
        }
    }
}
