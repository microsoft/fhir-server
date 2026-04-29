// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Search)]
public class QueryPlanReuseCheckerTests
{
    private readonly ISqlRetryService _sqlRetryService;
    private readonly ILogger<QueryPlanReuseChecker> _logger;

    public QueryPlanReuseCheckerTests()
    {
        _sqlRetryService = Substitute.For<ISqlRetryService>();
        _logger = NullLogger<QueryPlanReuseChecker>.Instance;
    }

    [Fact]
    public void GivenQueryPlanReuseChecker_WhenNotInitialized_ThenCanReuseQueryPlanReturnsTrue()
    {
        // Arrange
        var checker = new QueryPlanReuseChecker(_sqlRetryService, _logger);
        var searchOptions = CreateSearchOptions(new List<SearchParameterInfo>());

        // Act
        bool result = checker.CanReuseQueryPlan(searchOptions);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GivenQueryPlanReuseChecker_WhenInitializedWithNoSkewedParameters_ThenCanReuseQueryPlanReturnsTrue()
    {
        // Arrange
        var checker = CreateInitializedChecker(new List<IGrouping<string, (string Uri, string ResourceTypeId)>>());
        var searchParameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var searchOptions = CreateSearchOptions(new List<SearchParameterInfo> { searchParameter });

        // Act
        bool result = checker.CanReuseQueryPlan(searchOptions);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GivenQueryPlanReuseChecker_WhenSearchParameterIsSkewed_ThenCanReuseQueryPlanReturnsFalse()
    {
        // Arrange
        string skewedUri = "http://hl7.org/fhir/SearchParameter/Patient-name";
        var skewedParameters = CreateSkewedParameterGroups(skewedUri, "1");
        var checker = CreateInitializedChecker(skewedParameters);
        var searchParameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri(skewedUri));
        var searchOptions = CreateSearchOptions(new List<SearchParameterInfo> { searchParameter });

        // Act
        bool result = checker.CanReuseQueryPlan(searchOptions);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GivenQueryPlanReuseChecker_WhenSearchParameterIsNotSkewed_ThenCanReuseQueryPlanReturnsTrue()
    {
        // Arrange
        string skewedUri = "http://hl7.org/fhir/SearchParameter/Patient-name";
        string nonSkewedUri = "http://hl7.org/fhir/SearchParameter/Patient-gender";
        var skewedParameters = CreateSkewedParameterGroups(skewedUri, "1");
        var checker = CreateInitializedChecker(skewedParameters);
        var searchParameter = new SearchParameterInfo("gender", "gender", SearchParamType.Token, new Uri(nonSkewedUri));
        var searchOptions = CreateSearchOptions(new List<SearchParameterInfo> { searchParameter });

        // Act
        bool result = checker.CanReuseQueryPlan(searchOptions);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GivenQueryPlanReuseChecker_WhenMultipleParametersAndOneIsSkewed_ThenCanReuseQueryPlanReturnsFalse()
    {
        // Arrange
        string skewedUri = "http://hl7.org/fhir/SearchParameter/Patient-name";
        string nonSkewedUri = "http://hl7.org/fhir/SearchParameter/Patient-gender";
        var skewedParameters = CreateSkewedParameterGroups(skewedUri, "1");
        var checker = CreateInitializedChecker(skewedParameters);
        var searchParameters = new List<SearchParameterInfo>
        {
            new SearchParameterInfo("gender", "gender", SearchParamType.Token, new Uri(nonSkewedUri)),
            new SearchParameterInfo("name", "name", SearchParamType.String, new Uri(skewedUri)),
        };
        var searchOptions = CreateSearchOptions(searchParameters);

        // Act
        bool result = checker.CanReuseQueryPlan(searchOptions);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GivenQueryPlanReuseChecker_WhenMultipleParametersAndNoneAreSkewed_ThenCanReuseQueryPlanReturnsTrue()
    {
        // Arrange
        string skewedUri = "http://hl7.org/fhir/SearchParameter/Patient-address";
        var skewedParameters = CreateSkewedParameterGroups(skewedUri, "1");
        var checker = CreateInitializedChecker(skewedParameters);
        var searchParameters = new List<SearchParameterInfo>
        {
            new SearchParameterInfo("gender", "gender", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-gender")),
            new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name")),
        };
        var searchOptions = CreateSearchOptions(searchParameters);

        // Act
        bool result = checker.CanReuseQueryPlan(searchOptions);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GivenQueryPlanReuseChecker_WhenEmptySearchParameters_ThenCanReuseQueryPlanReturnsTrue()
    {
        // Arrange
        string skewedUri = "http://hl7.org/fhir/SearchParameter/Patient-name";
        var skewedParameters = CreateSkewedParameterGroups(skewedUri, "1");
        var checker = CreateInitializedChecker(skewedParameters);
        var searchOptions = CreateSearchOptions(new List<SearchParameterInfo>());

        // Act
        bool result = checker.CanReuseQueryPlan(searchOptions);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GivenQueryPlanReuseChecker_WhenHandleNotificationCalled_ThenStorageReadyIsSet()
    {
        // Arrange
        var checker = new QueryPlanReuseChecker(_sqlRetryService, _logger);
        var notification = new SearchParametersInitializedNotification();

        // Act
        await checker.Handle(notification, CancellationToken.None);

        // Assert - Verify by checking the private field through reflection
        var storageReadyField = typeof(QueryPlanReuseChecker)
            .GetField("_storageReady", BindingFlags.NonPublic | BindingFlags.Instance);
        bool storageReady = (bool)storageReadyField.GetValue(checker);
        Assert.True(storageReady);
    }

    [Fact]
    public void GivenQueryPlanReuseChecker_WhenMultipleSkewedParameters_ThenAllAreChecked()
    {
        // Arrange
        string skewedUri1 = "http://hl7.org/fhir/SearchParameter/Patient-name";
        string skewedUri2 = "http://hl7.org/fhir/SearchParameter/Patient-address";
        var skewedData = new List<(string Uri, string ResourceTypeId)>
        {
            (skewedUri1, "1"),
            (skewedUri2, "2"),
        };
        var skewedParameters = skewedData.GroupBy(x => x.Uri).ToList();
        var checker = CreateInitializedChecker(skewedParameters);

        // Test with first skewed parameter
        var searchOptions1 = CreateSearchOptions(new List<SearchParameterInfo>
        {
            new SearchParameterInfo("name", "name", SearchParamType.String, new Uri(skewedUri1)),
        });

        // Test with second skewed parameter
        var searchOptions2 = CreateSearchOptions(new List<SearchParameterInfo>
        {
            new SearchParameterInfo("address", "address", SearchParamType.String, new Uri(skewedUri2)),
        });

        // Act & Assert
        Assert.False(checker.CanReuseQueryPlan(searchOptions1));
        Assert.False(checker.CanReuseQueryPlan(searchOptions2));
    }

    [Fact]
    public async Task GivenSkewedParametersInDatabase_WhenInitialized_ThenSkewedParametersAreSetCorrectly()
    {
        // Arrange
        string skewedUri1 = "http://hl7.org/fhir/SearchParameter/Patient-name";
        string resourceTypeId1 = "1";
        string searchParamId1 = "5";

        string skewedUri2 = "http://hl7.org/fhir/SearchParameter/Patient-address";
        string resourceTypeId2 = "2";
        string searchParamId2 = "6";

        // Act

        bool firstCallMade = false;
        bool secondCallMade = false;

        _sqlRetryService.ExecuteReaderAsync(Arg.Any<SqlCommand>(), Arg.Any<Func<SqlDataReader, (string, string)>>(), Arg.Any<ILogger>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                if (!firstCallMade)
                {
                    // Return the expected results directly - simulating what the database would return
                    // The first call returns (ResourceTypeId, SearchParamId) tuples parsed from skewed stats
                    var results = new List<(string ResourceTypeId, string SearchParamId)>
                    {
                        (resourceTypeId1, searchParamId1),
                        (resourceTypeId2, searchParamId2),
                    };

                    firstCallMade = true;

                    return Task.FromResult<IReadOnlyList<(string ResourceTypeId, string SearchParamId)>>(results);
                }
                else
                {
                    var command = x.ArgAt<SqlCommand>(0);
                    Assert.Contains(searchParamId1, command.Parameters["@SearchParamIds"].Value.ToString());
                    Assert.Contains(searchParamId2, command.Parameters["@SearchParamIds"].Value.ToString());

                    // Return the expected results directly - simulating the URI lookup
                    // The second call returns (SearchParamId, Uri) tuples
                    var results = new List<(string SearchParamId, string Uri)>
                    {
                        (searchParamId1, skewedUri1),
                        (searchParamId2, skewedUri2),
                    };

                    secondCallMade = true;

                    return Task.FromResult<IReadOnlyList<(string SearchParamId, string Uri)>>(results);
                }
            });

        var checker = new QueryPlanReuseChecker(_sqlRetryService, _logger);
        var notification = new SearchParametersInitializedNotification();

        await checker.Handle(notification, CancellationToken.None);

        var wait = 0;
        while (!firstCallMade || !secondCallMade)
        {
            await Task.Delay(1000); // Wait for the refresh timer to execute
            wait += 1000;
            if (wait > 10000) // Timeout after 10 seconds
            {
                break;
            }
        }

        // Assert - Verify the private field through reflection

        Assert.True(firstCallMade, "Expected first call to ExecuteReaderAsync was not made.");
        Assert.True(secondCallMade, "Expected second call to ExecuteReaderAsync was not made.");

        var skewedParametersField = typeof(QueryPlanReuseChecker)
            .GetField("_skewedParameters", BindingFlags.NonPublic | BindingFlags.Instance);
        var actualSkewedParameters = (List<IGrouping<string, (string Uri, string ResourceTypeId)>>)skewedParametersField.GetValue(checker);
        Assert.Equal(2, actualSkewedParameters.Count);

        Assert.Equal(skewedUri1, actualSkewedParameters[0].Key);
        Assert.Single(actualSkewedParameters[0]);
        Assert.Equal(resourceTypeId1, actualSkewedParameters[0].First().ResourceTypeId);

        Assert.Equal(skewedUri2, actualSkewedParameters[1].Key);
        Assert.Single(actualSkewedParameters[1]);
        Assert.Equal(resourceTypeId2, actualSkewedParameters[1].First().ResourceTypeId);
    }

    /// <summary>
    /// Creates an initialized QueryPlanReuseChecker with the specified skewed parameters.
    /// </summary>
    private QueryPlanReuseChecker CreateInitializedChecker(List<IGrouping<string, (string Uri, string ResourceTypeId)>> skewedParameters)
    {
        var checker = new QueryPlanReuseChecker(_sqlRetryService, _logger);

        // Use reflection to set the private fields to simulate initialization
        var isInitializedField = typeof(QueryPlanReuseChecker)
            .GetField("_isInitialized", BindingFlags.NonPublic | BindingFlags.Instance);
        var skewedParametersField = typeof(QueryPlanReuseChecker)
            .GetField("_skewedParameters", BindingFlags.NonPublic | BindingFlags.Instance);

        isInitializedField.SetValue(checker, true);
        skewedParametersField.SetValue(checker, skewedParameters);

        return checker;
    }

    /// <summary>
    /// Creates a list of skewed parameter groups for testing.
    /// </summary>
    private List<IGrouping<string, (string Uri, string ResourceTypeId)>> CreateSkewedParameterGroups(string uri, string resourceTypeId)
    {
        var data = new List<(string Uri, string ResourceTypeId)> { (uri, resourceTypeId) };
        return data.GroupBy(x => x.Uri).ToList();
    }

    /// <summary>
    /// Creates a SearchOptions instance with the specified search parameters.
    /// </summary>
    private SearchOptions CreateSearchOptions(IReadOnlyList<SearchParameterInfo> searchParameters)
    {
        return new SearchOptions
        {
            Sort = Array.Empty<(SearchParameterInfo, Microsoft.Health.Fhir.Core.Features.Search.SortOrder)>(),
            SearchParameters = searchParameters,
            ResourceVersionTypes = ResourceVersionType.Latest,
        };
    }
}
