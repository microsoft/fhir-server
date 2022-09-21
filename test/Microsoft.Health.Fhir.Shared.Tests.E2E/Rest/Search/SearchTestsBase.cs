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
            FhirResponse<Bundle> fhirResponse = await Client.SearchAsync(searchUrl, customHeader);
            if (_output != null)
            {
                WriteSearchAsync(fhirResponse, "ExecuteAndValidateBundle", searchUrl, customHeader);
            }

            Bundle firstBundle = fhirResponse;

            var expectedFirstBundle = expectedResources.Length > pageSize ? expectedResources[0..pageSize] : expectedResources;

            ValidateBundle(firstBundle, selfLink, sort, invalidSortParameter, expectedFirstBundle);

            var nextLink = firstBundle.NextLink?.ToString();
            int pageNumber = 1;
            bool checkedAllResources = false;
            while (nextLink != null && !checkedAllResources)
            {
                Bundle nextBundle = await Client.SearchAsync(nextLink);

                // Truncating host and appending continuation token
                nextLink = selfLink + nextLink.Substring(_continuationToken.Match(nextLink).Index);
                var remainingResources = expectedResources[(pageSize * pageNumber)..];
                if (remainingResources.Length > pageSize)
                {
                    remainingResources = remainingResources[..pageSize];
                }
                else
                {
                    checkedAllResources = true;
                }

                ValidateBundle(nextBundle, nextLink, sort, invalidSortParameter, remainingResources);

                nextLink = nextBundle.NextLink?.ToString();
                pageNumber++;
            }

            return firstBundle;
        }

        protected async Task<OperationOutcome> ExecuteAndValidateErrorOperationOutcomeAsync(
            string searchUrl,
            Tuple<string, string> customHeader,
            HttpStatusCode expectedStatusCode,
            OperationOutcome expectedOperationOutcome)
        {
            FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.SearchAsync(searchUrl, customHeader));
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
                var sb = new StringBuilder("Expected count to be ").Append(expectedResources.Length).Append(" but was ").Append(bundle.Entry.Count).AppendLine(" . Contents:");
                var fhirJsonSerializer = new FhirJsonSerializer(new SerializerSettings() { AppendNewLine = false, Pretty = false });
                using var sw = new StringWriter(sb);

                sb.AppendLine("Actual collection as below -");
                foreach (var element in bundle.Entry.Select(e => e.Resource))
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

        protected async System.Threading.Tasks.Task WriteSearchAsync(string title, string pathAndQuery, Tuple<string, string> header = null)
        {
            FhirResponse<Bundle> fhirResponse = await Client.SearchAsync(pathAndQuery, header);
            WriteSearchAsync(fhirResponse, title, pathAndQuery, header);
        }

        protected void WriteSearchAsync(FhirResponse<Bundle> fhirResponse, string title, string pathAndQuery, Tuple<string, string> header)
        {
            _output.WriteLine($"<--------- {title}");
            _output.WriteLine($"REQUEST:");
            _output.WriteLine($"  {pathAndQuery}");
            if (header != null)
            {
                _output.WriteLine($"  {header.Item1}: {header.Item2}");
            }

            _output.WriteLine($"RESPONSE:");

            try
            {
                _output.WriteLine($"  {fhirResponse.StatusCode}");
                if (fhirResponse.Headers != null)
                {
                    string headerName = "X-Instance-Id";
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

                Bundle bundle = fhirResponse;
                if (bundle != null)
                {
                    _output.WriteLine("  BUNDLE:");
                    if (bundle.Entry == null)
                    {
                        _output.WriteLine("    bundle.Entry == null");
                    }
                    else
                    {
                        foreach (Bundle.EntryComponent ec in bundle.Entry)
                        {
                            _output.WriteLine($"    {ToString(ec.Resource)}");
                            FindInvalidSearchParam(ec);
                        }
                    }
                }
                else
                {
                    _output.WriteLine("  bundle == null");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine("  EXCEPTION:");
                while (ex != null)
                {
                    _output.WriteLine($"    {ex.Message}");
                    ex = ex.InnerException;
                }
            }

            _output.WriteLine($">---------");
        }

        private void FindInvalidSearchParam(Bundle.EntryComponent ec)
        {
            var operationOutcome = ec.Resource as OperationOutcome;
            if (operationOutcome != null && operationOutcome.Children.Count() > 0)
            {
                foreach (var child in operationOutcome.Children)
                {
                    var issueCompoment = child as OperationOutcome.IssueComponent;
                    if (issueCompoment != null)
                    {
                        _output.WriteLine($"  {issueCompoment.Severity}");
                        _output.WriteLine($"  {issueCompoment.Code}");
                        _output.WriteLine($"  {issueCompoment.Diagnostics}");
                    }
                }
            }
        }

        protected static string ToString(Resource r)
        {
            Patient p = r as Patient;
            OperationOutcome o = r as OperationOutcome;
            if (p != null)
            {
                string name = (p.Name?.Count ?? 0) > 0 ? p.Name[0].Family : null;
                string telecom = (p.Telecom?.Count ?? 0) > 0 ? p.Telecom[0].Value : null;
                string identifier = (p.Identifier?.Count ?? 0) > 0 ? p.Identifier[0].Value : null;
                return $"Patient: {name} ; {p.BirthDate} ; {telecom} ; {p.ManagingOrganization?.Reference} ; {p.Id} ; {identifier}";
            }
            else if (o != null)
            {
                if (o.Issue == null)
                {
                    return "o.Issue == null";
                }

                string ostr = null;
                for (int iter = 0; iter < o.Issue.Count; iter++)
                {
                    ostr += $"{o.Issue[iter].Code}, {o.Issue[iter].Severity}, {o.Issue[iter].Diagnostics} ||";
                }

                return $"OperationOutcome: {ostr}";
            }
            else
            {
                return "UNKNOWN";
            }
        }
    }
}
