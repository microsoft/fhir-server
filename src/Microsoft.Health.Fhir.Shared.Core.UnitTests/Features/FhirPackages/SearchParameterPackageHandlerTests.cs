// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.FhirPackages;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using SearchParamType = Microsoft.Health.Fhir.ValueSets.SearchParamType;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.FhirPackages
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.CustomSearch)]
    public class SearchParameterPackageHandlerTests
    {
        private const string UrlA = "http://example.org/fhir/SearchParameter/patient-a";
        private const string UrlB = "http://example.org/fhir/SearchParameter/patient-b";
        private const string CompositeUrl = "http://example.org/fhir/SearchParameter/patient-a-b";

        private static readonly DateTimeOffset ReferenceSetLastUpdated = new DateTimeOffset(2026, 10, 12, 18, 0, 0, TimeSpan.Zero);

        private readonly ISearchParameterDefinitionManager _definitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly ISearchParameterOperations _searchParameterOperations = Substitute.For<ISearchParameterOperations>();
        private readonly IModelInfoProvider _modelInfoProvider = Substitute.For<IModelInfoProvider>();
        private readonly ISearchParameterComparer<SearchParameterInfo> _comparer = Substitute.For<ISearchParameterComparer<SearchParameterInfo>>();
        private readonly ISearchParameterSupportResolver _supportResolver = Substitute.For<ISearchParameterSupportResolver>();
        private readonly IDataStoreSearchParameterValidator _dataStoreValidator = Substitute.For<IDataStoreSearchParameterValidator>();
        private readonly ISearchParameterPackageWriter _writer = Substitute.For<ISearchParameterPackageWriter>();
        private readonly FhirPackage _package = new FhirPackage("example.package", "1.0.0", "sha512-abc", Array.Empty<FhirPackageEntry>());

        public SearchParameterPackageHandlerTests()
        {
            _searchParameterOperations.SearchParamLastUpdated.Returns(ReferenceSetLastUpdated);
            _supportResolver.IsSearchParameterSupported(Arg.Any<SearchParameterInfo>()).Returns((true, false));
            _dataStoreValidator.ValidateSearchParameter(Arg.Any<SearchParameterInfo>(), out Arg.Any<string>()).Returns(true);
        }

        [Fact]
        public async Task GivenAValidSet_WhenValidating_ThenAPlanIsReturnedAndNothingIsWrittenUntilItIsApplied()
        {
            // Arrange
            var entries = new[]
            {
                Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a")),
                Entry("package/SearchParameter-b.json", SearchParameterJson(UrlB, "b", type: "string", expression: "Patient.name")),
            };

            // Act
            IFhirPackageInstallPlan plan = await CreateHandler().ValidateAsync(entries, _package, CancellationToken.None);

            // Assert
            await _searchParameterOperations.Received(1).GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>(), Arg.Any<bool>());
            Assert.Equal(KnownResourceTypes.SearchParameter, plan.ResourceType);
            Assert.Equal(new[] { UrlA, UrlB }, plan.CanonicalUrls);
            await _writer.DidNotReceiveWithAnyArgs().WriteAsync(default, default, default, default);

            await plan.ApplyAsync(CancellationToken.None);

            await _writer.Received(1).WriteAsync(
                _package,
                Arg.Is<IReadOnlyList<ResourceElement>>(x => x.Count == 2 && x.All(r => r.InstanceType == KnownResourceTypes.SearchParameter)),
                ReferenceSetLastUpdated,
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData("draft")]
        [InlineData("active")]
        [InlineData("retired")]
        [InlineData("unknown")]
        public async Task GivenAnyStatus_WhenValidating_ThenTheSearchParameterIsInstalled(string status)
        {
            // Arrange
            var entries = new[] { Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a", status: status)) };

            // Act
            IFhirPackageInstallPlan plan = await CreateHandler().ValidateAsync(entries, _package, CancellationToken.None);

            // Assert
            Assert.Equal(new[] { UrlA }, plan.CanonicalUrls);
        }

        [Fact]
        public async Task GivenOneInvalidSearchParameterAmongValidOnes_WhenValidating_ThenTheWholeSetIsRejectedAndNothingIsWritten()
        {
            // Arrange
            var entries = new[]
            {
                Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a")),
                Entry("package/SearchParameter-b.json", SearchParameterJson(UrlB, "b", expression: null)),
            };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("Required", issue.Code);
            Assert.Contains("package/SearchParameter-b.json", issue.Diagnostics, StringComparison.Ordinal);
            Assert.Contains("'expression'", issue.Diagnostics, StringComparison.Ordinal);
            await _writer.DidNotReceiveWithAnyArgs().WriteAsync(default, default, default, default);
        }

        [Fact]
        public async Task GivenSeveralInvalidSearchParameters_WhenValidating_ThenEveryIssueIsReportedTogether()
        {
            // Arrange
            var entries = new[]
            {
                Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, null)),
                Entry("package/SearchParameter-b.json", SearchParameterJson("relative/url", "b")),
                Entry("package/SearchParameter-c.json", "{ not json"),
            };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            Assert.Equal(3, exception.Issues.Count);
            Assert.Contains(exception.Issues, i => i.Diagnostics.Contains("package/SearchParameter-a.json", StringComparison.Ordinal));
            Assert.Contains(exception.Issues, i => i.Diagnostics.Contains("package/SearchParameter-b.json", StringComparison.Ordinal));
            Assert.Contains(exception.Issues, i => i.Diagnostics.Contains("package/SearchParameter-c.json", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("url")]
        [InlineData("code")]
        [InlineData("base")]
        [InlineData("type")]
        [InlineData("expression")]
        public async Task GivenAMissingRequiredElement_WhenValidating_ThenItIsRejected(string element)
        {
            // Arrange
            JsonObject json = SearchParameterNode(UrlA, "a");
            json.Remove(element);
            var entries = new[] { Entry("package/SearchParameter-a.json", json.ToJsonString()) };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            Assert.Contains(exception.Issues, i => i.Code == "Required" && i.Diagnostics.Contains($"'{element}'", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("name")]
        [InlineData("status")]
        [InlineData("description")]
        public async Task GivenAMissingElementRequiredByTheFhirModel_WhenValidating_ThenModelValidationRejectsIt(string element)
        {
            // Arrange
            JsonObject json = SearchParameterNode(UrlA, "a");
            json.Remove(element);
            var entries = new[]
            {
                Entry("package/SearchParameter-a.json", json.ToJsonString()),
                Entry("package/SearchParameter-b.json", SearchParameterJson(UrlB, "b")),
            };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("Invalid", issue.Code);
            Assert.Contains("package/SearchParameter-a.json", issue.Diagnostics, StringComparison.Ordinal);
            Assert.Contains(element, issue.Diagnostics, StringComparison.OrdinalIgnoreCase);
            await _writer.DidNotReceiveWithAnyArgs().WriteAsync(default, default, default, default);
        }

        [Theory]
        [InlineData("not-a-uri")]
        [InlineData("SearchParameter/relative")]
        public async Task GivenACompositeComponentWithAMalformedDefinition_WhenValidating_ThenItIsRejectedWithoutThrowing(string definition)
        {
            // Arrange
            var entries = new[]
            {
                Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a")),
                Entry("package/SearchParameter-ab.json", CompositeJson(CompositeUrl, "a-bad", UrlA, definition)),
            };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("Invalid", issue.Code);
            Assert.Contains("package/SearchParameter-ab.json", issue.Diagnostics, StringComparison.Ordinal);
            Assert.Contains(definition, issue.Diagnostics, StringComparison.Ordinal);
            await _writer.DidNotReceiveWithAnyArgs().WriteAsync(default, default, default, default);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenTwoPackageMembersWhoseCodesCollideThroughResource_WhenValidating_ThenBothAreRejected(bool reverseOrder)
        {
            // Arrange
            ConfigureResourceHierarchy();
            JsonObject onResource = SearchParameterNode(UrlA, "shared", expression: "Resource.id");
            onResource["base"] = new JsonArray("Resource");
            var entries = new List<FhirPackageEntry>
            {
                Entry("package/SearchParameter-a.json", onResource.ToJsonString()),
                Entry("package/SearchParameter-b.json", SearchParameterJson(UrlB, "shared", type: "string", expression: "Patient.name")),
            };

            if (reverseOrder)
            {
                entries.Reverse();
            }

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            Assert.Equal(2, exception.Issues.Count);
            Assert.All(exception.Issues, i => Assert.Equal("Duplicate", i.Code));
            Assert.Contains(exception.Issues, i => i.Diagnostics.Contains("package/SearchParameter-a.json", StringComparison.Ordinal));
            Assert.Contains(exception.Issues, i => i.Diagnostics.Contains("package/SearchParameter-b.json", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenTwoPackageMembersWhoseCodesCollideThroughDomainResource_WhenValidating_ThenBothAreRejected(bool reverseOrder)
        {
            // Arrange
            ConfigureResourceHierarchy();
            JsonObject onDomainResource = SearchParameterNode(UrlA, "shared", expression: "DomainResource.text");
            onDomainResource["base"] = new JsonArray("DomainResource");
            var entries = new List<FhirPackageEntry>
            {
                Entry("package/SearchParameter-a.json", onDomainResource.ToJsonString()),
                Entry("package/SearchParameter-b.json", SearchParameterJson(UrlB, "shared", type: "string", expression: "Patient.name")),
            };

            if (reverseOrder)
            {
                entries.Reverse();
            }

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            Assert.Equal(2, exception.Issues.Count);
            Assert.All(exception.Issues, i => Assert.Equal("Duplicate", i.Code));
        }

        [Fact]
        public async Task GivenTwoPackageMembersWithTheSameCodeOnUnrelatedBases_WhenValidating_ThenBothAreAccepted()
        {
            // Arrange
            ConfigureResourceHierarchy();
            JsonObject onObservation = SearchParameterNode(UrlB, "shared", expression: "Observation.code");
            onObservation["base"] = new JsonArray("Observation");
            var entries = new[]
            {
                Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "shared")),
                Entry("package/SearchParameter-b.json", onObservation.ToJsonString()),
            };

            // Act
            IFhirPackageInstallPlan plan = await CreateHandler().ValidateAsync(entries, _package, CancellationToken.None);

            // Assert
            Assert.Equal(new[] { UrlA, UrlB }, plan.CanonicalUrls);
        }

        [Theory]
        [InlineData("gender", true)]
        [InlineData("gender.(", false)]
        public async Task GivenACompositeComponentExpression_WhenValidatingWithTheRealSupportResolver_ThenTheComponentExpressionItselfIsParsed(string componentExpression, bool valid)
        {
            // Arrange
            var supportResolver = new SearchParameterSupportResolver(await SearchParameterFixtureData.GetFhirTypedElementToSearchValueConverterManagerAsync());
            var entries = new[]
            {
                Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a", expression: "Patient.identifier")),
                Entry("package/SearchParameter-b.json", SearchParameterJson(UrlB, "b", expression: "Patient.gender")),
                Entry("package/SearchParameter-ab.json", CompositeJsonWithExpressions(CompositeUrl, "a-b", (UrlA, "identifier"), (UrlB, componentExpression))),
            };
            SearchParameterPackageHandler handler = CreateHandler(supportResolver);

            // Act and assert
            if (valid)
            {
                IFhirPackageInstallPlan plan = await handler.ValidateAsync(entries, _package, CancellationToken.None);
                Assert.Contains(CompositeUrl, plan.CanonicalUrls);
            }
            else
            {
                FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                    () => handler.ValidateAsync(entries, _package, CancellationToken.None));
                OperationOutcomeIssue issue = Assert.Single(exception.Issues);
                Assert.Equal("Invalid", issue.Code);
                Assert.Contains("package/SearchParameter-ab.json", issue.Diagnostics, StringComparison.Ordinal);
                await _writer.DidNotReceiveWithAnyArgs().WriteAsync(default, default, default, default);
            }
        }

        [Theory]
        [InlineData("SearchParameter/relative")]
        [InlineData("http://example.org/fhir/SearchParameter/this-url-is-deliberately-longer-than-one-hundred-and-twenty-eight-characters-so-that-it-exceeds-the-limit")]
        public async Task GivenAnInvalidUrl_WhenValidating_ThenItIsRejected(string url)
        {
            // Arrange
            var entries = new[] { Entry("package/SearchParameter-a.json", SearchParameterJson(url, "a")) };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("Invalid", issue.Code);
        }

        [Theory]
        [InlineData("{ not json")]
        [InlineData("{\"resourceType\":\"SearchParameter\",\"url\":\"http://example.org/sp\",\"unknownElement\":true}")]
        public async Task GivenAnEntryThatIsNotAValidSearchParameter_WhenValidating_ThenItIsRejected(string json)
        {
            // Arrange
            var entries = new[] { Entry("package/SearchParameter-a.json", json) };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("Invalid", issue.Code);
            Assert.Contains("package/SearchParameter-a.json", issue.Diagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenTwoSearchParametersWithTheSameUrl_WhenValidating_ThenBothAreReportedAsDuplicates()
        {
            // Arrange
            var entries = new[]
            {
                Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a")),
                Entry("package/SearchParameter-a2.json", SearchParameterJson(UrlA, "a2")),
            };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            Assert.Equal(2, exception.Issues.Count(i => i.Code == "Duplicate"));
        }

        [Fact]
        public async Task GivenTwoSearchParametersWithTheSameCodeOnTheSameBase_WhenValidating_ThenBothAreReportedAsDuplicates()
        {
            // Arrange
            var entries = new[]
            {
                Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "same")),
                Entry("package/SearchParameter-b.json", SearchParameterJson(UrlB, "same")),
            };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            Assert.Equal(2, exception.Issues.Count(i => i.Code == "Duplicate" && i.Diagnostics.Contains("'same'", StringComparison.Ordinal)));
        }

        [Fact]
        public async Task GivenAUrlOwnedByASystemDefinedSearchParameter_WhenValidating_ThenItIsRejectedAsAConflict()
        {
            // Arrange
            var systemDefined = new SearchParameterInfo("a", "a", SearchParamType.Token, new Uri(UrlA)) { IsSystemDefined = true };
            _definitionManager.TryGetSearchParameter(UrlA, out Arg.Any<SearchParameterInfo>()).Returns(x =>
            {
                x[1] = systemDefined;
                return true;
            });
            var entries = new[] { Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a")) };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("Conflict", issue.Code);
        }

        [Fact]
        public async Task GivenACodeAlreadyUsedOnTheBaseByAnIncompatibleSearchParameter_WhenValidating_ThenItIsRejectedAsAConflict()
        {
            // Arrange
            var existing = new SearchParameterInfo("a", "a", SearchParamType.String, new Uri("http://example.org/other"), expression: "Patient.name");
            _definitionManager.TryGetSearchParameter("Patient", "a", Arg.Any<bool>(), out Arg.Any<SearchParameterInfo>()).Returns(x =>
            {
                x[3] = existing;
                return true;
            });
            var entries = new[] { Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a")) };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("Conflict", issue.Code);
        }

        [Fact]
        public async Task GivenACompositeWhoseComponentsAreInTheSamePackage_WhenValidating_ThenComponentsAreResolvedBeforeTheSupportCheck()
        {
            // Arrange
            var entries = new[]
            {
                Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a")),
                Entry("package/SearchParameter-b.json", SearchParameterJson(UrlB, "b")),
                Entry("package/SearchParameter-ab.json", CompositeJson(CompositeUrl, "a-b", UrlA, UrlB)),
            };

            // Act
            IFhirPackageInstallPlan plan = await CreateHandler().ValidateAsync(entries, _package, CancellationToken.None);

            // Assert
            Assert.Contains(CompositeUrl, plan.CanonicalUrls);
            _supportResolver.Received(1).IsSearchParameterSupported(Arg.Is<SearchParameterInfo>(i =>
                i.Url.OriginalString == CompositeUrl &&
                i.Component.Count == 2 &&
                i.Component[0].ResolvedSearchParameter.Url.OriginalString == UrlA &&
                i.Component[1].ResolvedSearchParameter.Url.OriginalString == UrlB));
        }

        [Theory]
        [InlineData(SearchParameterStatus.Enabled)]
        [InlineData(SearchParameterStatus.Supported)]
        public async Task GivenACompositeWhoseComponentIsAnExistingLiveSearchParameter_WhenValidating_ThenItIsAccepted(SearchParameterStatus status)
        {
            // Arrange
            const string existingUrl = "http://example.org/fhir/SearchParameter/customer-defined";
            var existing = new SearchParameterInfo("custom", "custom", SearchParamType.Token, new Uri(existingUrl), expression: "Patient.gender")
            {
                SearchParameterStatus = status,
            };
            _definitionManager.TryGetSearchParameter(existingUrl, out Arg.Any<SearchParameterInfo>()).Returns(x =>
            {
                x[1] = existing;
                return true;
            });
            var entries = new[]
            {
                Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a")),
                Entry("package/SearchParameter-ab.json", CompositeJson(CompositeUrl, "a-custom", UrlA, existingUrl)),
            };

            // Act
            IFhirPackageInstallPlan plan = await CreateHandler().ValidateAsync(entries, _package, CancellationToken.None);

            // Assert
            Assert.Equal(new[] { UrlA, CompositeUrl }, plan.CanonicalUrls);
            _supportResolver.Received(1).IsSearchParameterSupported(Arg.Is<SearchParameterInfo>(i =>
                i.Url.OriginalString == CompositeUrl && i.Component[1].ResolvedSearchParameter == existing));
        }

        [Theory]
        [InlineData(SearchParameterStatus.Disabled)]
        [InlineData(SearchParameterStatus.PendingDisable)]
        [InlineData(SearchParameterStatus.PendingDelete)]
        [InlineData(SearchParameterStatus.PendingHardDelete)]
        [InlineData(SearchParameterStatus.Deleted)]
        [InlineData(SearchParameterStatus.Unsupported)]
        public async Task GivenACompositeWhoseComponentIsAnExistingSearchParameterThatIsNotLive_WhenValidating_ThenItIsRejected(SearchParameterStatus status)
        {
            // Arrange
            const string existingUrl = "http://example.org/fhir/SearchParameter/customer-defined";
            var existing = new SearchParameterInfo("custom", "custom", SearchParamType.Token, new Uri(existingUrl), expression: "Patient.gender")
            {
                SearchParameterStatus = status,
            };
            _definitionManager.TryGetSearchParameter(existingUrl, out Arg.Any<SearchParameterInfo>()).Returns(x =>
            {
                x[1] = existing;
                return true;
            });
            var entries = new[]
            {
                Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a")),
                Entry("package/SearchParameter-ab.json", CompositeJson(CompositeUrl, "a-custom", UrlA, existingUrl)),
            };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("Invalid", issue.Code);
            Assert.Contains($"'{status}'", issue.Diagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenACompositeWhoseComponentCannotBeResolved_WhenValidating_ThenItIsRejected()
        {
            // Arrange
            var entries = new[]
            {
                Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a")),
                Entry("package/SearchParameter-ab.json", CompositeJson(CompositeUrl, "a-missing", UrlA, "http://example.org/fhir/SearchParameter/missing")),
            };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("Invalid", issue.Code);
            Assert.Contains("http://example.org/fhir/SearchParameter/missing", issue.Diagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenACompositeWhoseComponentIsAnotherComposite_WhenValidating_ThenItIsRejected()
        {
            // Arrange
            const string outerUrl = "http://example.org/fhir/SearchParameter/outer";
            var entries = new[]
            {
                Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a")),
                Entry("package/SearchParameter-b.json", SearchParameterJson(UrlB, "b")),
                Entry("package/SearchParameter-ab.json", CompositeJson(CompositeUrl, "a-b", UrlA, UrlB)),
                Entry("package/SearchParameter-outer.json", CompositeJson(outerUrl, "outer", UrlA, CompositeUrl)),
            };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Contains("package/SearchParameter-outer.json", issue.Diagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenACodeOnResourceThatAnInheritingTypeAlreadyUsesIncompatibly_WhenValidating_ThenItIsRejectedAsAConflict()
        {
            // Arrange
            _modelInfoProvider.GetResourceTypeNames().Returns(new[] { "Patient" });
            var existing = new SearchParameterInfo("shared", "shared", SearchParamType.String, new Uri("http://example.org/other"), expression: "Patient.name");
            _definitionManager.TryGetSearchParameter("Patient", "shared", Arg.Any<bool>(), out Arg.Any<SearchParameterInfo>()).Returns(x =>
            {
                x[3] = existing;
                return true;
            });
            JsonObject json = SearchParameterNode(UrlA, "shared", expression: "Resource.id");
            json["base"] = new JsonArray("Resource");
            var entries = new[] { Entry("package/SearchParameter-a.json", json.ToJsonString()) };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("Conflict", issue.Code);
        }

        [Fact]
        public async Task GivenACodeOnDomainResourceThatAnInheritingTypeAlreadyUsesIncompatibly_WhenValidating_ThenItIsRejectedAsAConflict()
        {
            // Arrange
            _modelInfoProvider.GetResourceTypeNames().Returns(new[] { "Patient" });
            _modelInfoProvider.GetTypeForFhirType("Patient").Returns(typeof(Hl7.Fhir.Model.Patient));
            _modelInfoProvider.GetFhirTypeNameForType(typeof(Hl7.Fhir.Model.Patient).BaseType).Returns(KnownResourceTypes.DomainResource);
            var existing = new SearchParameterInfo("shared", "shared", SearchParamType.String, new Uri("http://example.org/other"), expression: "Patient.name");
            _definitionManager.TryGetSearchParameter("Patient", "shared", Arg.Any<bool>(), out Arg.Any<SearchParameterInfo>()).Returns(x =>
            {
                x[3] = existing;
                return true;
            });
            JsonObject json = SearchParameterNode(UrlA, "shared", expression: "DomainResource.text");
            json["base"] = new JsonArray("DomainResource");
            var entries = new[] { Entry("package/SearchParameter-a.json", json.ToJsonString()) };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("Conflict", issue.Code);
        }

        [Fact]
        public async Task GivenACompositeListedBeforeItsComponents_WhenValidating_ThenComponentsStillResolve()
        {
            // Arrange
            var entries = new[]
            {
                Entry("package/SearchParameter-ab.json", CompositeJson(CompositeUrl, "a-b", UrlA, UrlB)),
                Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a")),
                Entry("package/SearchParameter-b.json", SearchParameterJson(UrlB, "b")),
            };

            // Act
            IFhirPackageInstallPlan plan = await CreateHandler().ValidateAsync(entries, _package, CancellationToken.None);

            // Assert
            Assert.Equal(new[] { CompositeUrl, UrlA, UrlB }, plan.CanonicalUrls);
            _supportResolver.Received(1).IsSearchParameterSupported(Arg.Is<SearchParameterInfo>(i =>
                i.Url.OriginalString == CompositeUrl && i.Component.All(c => c.ResolvedSearchParameter != null)));
        }

        [Fact]
        public async Task GivenACompositeComponentWithoutAnExpression_WhenValidating_ThenItIsRejected()
        {
            // Arrange
            JsonObject composite = JsonNode.Parse(CompositeJson(CompositeUrl, "a-b", UrlA, UrlB)).AsObject();
            composite["component"][1].AsObject().Remove("expression");
            var entries = new[]
            {
                Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a")),
                Entry("package/SearchParameter-b.json", SearchParameterJson(UrlB, "b")),
                Entry("package/SearchParameter-ab.json", composite.ToJsonString()),
            };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("Required", issue.Code);
            Assert.Contains("package/SearchParameter-ab.json", issue.Diagnostics, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        public async Task GivenASearchParameterTheServerCannotFullyIndex_WhenValidating_ThenItIsRejectedAsNotSupported(bool supported, bool partiallySupported)
        {
            // Arrange
            _supportResolver.IsSearchParameterSupported(Arg.Any<SearchParameterInfo>()).Returns((supported, partiallySupported));
            var entries = new[] { Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a")) };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("NotSupported", issue.Code);
        }

        [Fact]
        public async Task GivenASearchParameterTheDataStoreRejects_WhenValidating_ThenTheDataStoreReasonIsReported()
        {
            // Arrange
            _dataStoreValidator.ValidateSearchParameter(Arg.Any<SearchParameterInfo>(), out Arg.Any<string>()).Returns(x =>
            {
                x[1] = "Not supported by this data store.";
                return false;
            });
            var entries = new[] { Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a")) };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("NotSupported", issue.Code);
            Assert.Contains("Not supported by this data store.", issue.Diagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenAnExpressionThatCannotBeParsed_WhenValidating_ThenItIsRejectedAsInvalid()
        {
            // Arrange
            _supportResolver.IsSearchParameterSupported(Arg.Any<SearchParameterInfo>()).Returns(_ => throw new FormatException("bad expression"));
            var entries = new[] { Entry("package/SearchParameter-a.json", SearchParameterJson(UrlA, "a", expression: "Patient.(")) };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateHandler().ValidateAsync(entries, _package, CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("Invalid", issue.Code);
            Assert.Contains("bad expression", issue.Diagnostics, StringComparison.Ordinal);
        }

        private SearchParameterPackageHandler CreateHandler(ISearchParameterSupportResolver supportResolver = null) =>
            new SearchParameterPackageHandler(
                _definitionManager,
                _searchParameterOperations,
                _modelInfoProvider,
                _comparer,
                supportResolver ?? _supportResolver,
                _dataStoreValidator,
                new ModelAttributeValidator(),
                _writer,
                NullLogger<SearchParameterPackageHandler>.Instance);

        private void ConfigureResourceHierarchy()
        {
            _modelInfoProvider.GetResourceTypeNames().Returns(new[] { "Patient", "Observation" });
            _modelInfoProvider.GetTypeForFhirType("Patient").Returns(typeof(Hl7.Fhir.Model.Patient));
            _modelInfoProvider.GetTypeForFhirType("Observation").Returns(typeof(Hl7.Fhir.Model.Observation));
            _modelInfoProvider.GetFhirTypeNameForType(typeof(Hl7.Fhir.Model.DomainResource)).Returns(KnownResourceTypes.DomainResource);
        }

        private static FhirPackageEntry Entry(string path, string json) => new FhirPackageEntry(path, KnownResourceTypes.SearchParameter, json);

        private static string SearchParameterJson(string url, string code, string type = "token", string expression = "Patient.identifier", string status = "active")
        {
            JsonObject json = SearchParameterNode(url, code, type, expression, status);
            return json.ToJsonString();
        }

        private static JsonObject SearchParameterNode(string url, string code, string type = "token", string expression = "Patient.identifier", string status = "active")
        {
            var json = new JsonObject
            {
                ["resourceType"] = "SearchParameter",
                ["url"] = url,
                ["name"] = code ?? "unnamed",
                ["status"] = status,
                ["description"] = "Test search parameter.",
                ["code"] = code,
                ["base"] = new JsonArray("Patient"),
                ["type"] = type,
                ["expression"] = expression,
            };

            foreach (string key in json.Where(x => x.Value == null).Select(x => x.Key).ToList())
            {
                json.Remove(key);
            }

            return json;
        }

        private static string CompositeJson(string url, string code, params string[] componentUrls) =>
            CompositeJsonWithExpressions(url, code, componentUrls.Select(x => (x, "Patient")).ToArray());

        private static string CompositeJsonWithExpressions(string url, string code, params (string Definition, string Expression)[] componentDefinitions)
        {
            JsonObject json = SearchParameterNode(url, code, "composite", "Patient");
            var components = new JsonArray();
            foreach ((string componentUrl, string componentExpression) in componentDefinitions)
            {
#if Stu3
                JsonNode definition = new JsonObject { ["reference"] = componentUrl };
#else
                JsonNode definition = componentUrl;
#endif
                components.Add(new JsonObject { ["definition"] = definition, ["expression"] = componentExpression });
            }

            json["component"] = components;
            return json.ToJsonString();
        }
    }
}
