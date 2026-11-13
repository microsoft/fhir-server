// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    /// <summary>
    /// Tests for the warning returned when a URI search value is too long for the URI search index.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlServerSearchServiceLongUriWarningTests
    {
        private static readonly SearchParameterInfo InstantiatesUri = new SearchParameterInfo(
            name: "instantiates-uri",
            code: "instantiates-uri",
            searchParamType: SearchParamType.Uri,
            url: new Uri("http://hl7.org/fhir/SearchParameter/CarePlan-instantiates-uri"));

        private readonly List<OperationOutcomeIssue> _bundleIssues = new List<OperationOutcomeIssue>();
        private readonly IFhirRequestContext _requestContext = Substitute.For<IFhirRequestContext>();
        private readonly CapturingLogger _logger = new CapturingLogger();

        public SqlServerSearchServiceLongUriWarningTests()
        {
            _requestContext.BundleIssues.Returns(_bundleIssues);
        }

        [Fact]
        public void GivenAShortUriSearchValue_WhenSearching_ThenNoWarningIsAdded()
        {
            // Arrange
            Expression expression = CreateUriExpression(255);

            // Act
            SqlServerSearchService.AddLongUriSearchValueWarning(expression, _requestContext, _logger);

            // Assert
            Assert.Empty(_bundleIssues);
            Assert.Empty(_logger.Messages);
        }

        [Fact]
        public void GivenAUriSearchValueOfTheIndexedLength_WhenSearching_ThenAWarningIsAddedWithoutTheValue()
        {
            // Arrange
            Expression expression = CreateUriExpression(256);
            string uri = GetUri(expression);

            // Act
            SqlServerSearchService.AddLongUriSearchValueWarning(expression, _requestContext, _logger);

            // Assert
            OperationOutcomeIssue issue = Assert.Single(_bundleIssues);
            Assert.Equal(OperationOutcomeConstants.IssueSeverity.Warning, issue.Severity);
            Assert.Equal(OperationOutcomeConstants.IssueType.Informational, issue.Code);
            Assert.Contains("instantiates-uri", issue.Diagnostics, StringComparison.Ordinal);
            Assert.Contains("256", issue.Diagnostics, StringComparison.Ordinal);
            Assert.DoesNotContain(uri, issue.Diagnostics, StringComparison.Ordinal);

            string logMessage = Assert.Single(_logger.Messages);
            Assert.Contains("instantiates-uri", logMessage, StringComparison.Ordinal);
            Assert.DoesNotContain(uri, logMessage, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenTheSameRequestSearchesTwice_WhenSearching_ThenTheWarningIsAddedOnce()
        {
            // Arrange
            Expression expression = CreateUriExpression(300);

            // Act
            SqlServerSearchService.AddLongUriSearchValueWarning(expression, _requestContext, _logger);
            SqlServerSearchService.AddLongUriSearchValueWarning(expression, _requestContext, _logger);

            // Assert
            Assert.Single(_bundleIssues);
            Assert.Single(_logger.Messages);
        }

        [Fact]
        public void GivenSearchesWithDifferentLongUriParameters_WhenSearching_ThenOneWarningNamesBoth()
        {
            // Arrange
            var profile = new SearchParameterInfo("_profile", "_profile", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/Resource-profile"));
            Expression profileExpression = Expression.SearchParameter(profile, Expression.StringEquals(FieldName.Uri, null, new string('p', 300), false));

            // Act
            SqlServerSearchService.AddLongUriSearchValueWarning(CreateUriExpression(300), _requestContext, _logger);
            SqlServerSearchService.AddLongUriSearchValueWarning(profileExpression, _requestContext, _logger);

            // Assert
            OperationOutcomeIssue issue = Assert.Single(_bundleIssues);
            Assert.Contains("'_profile, instantiates-uri'", issue.Diagnostics, StringComparison.Ordinal);
            Assert.Equal(2, _logger.Messages.Count);
        }

        [Fact]
        public void GivenNoRequestContextOrExpression_WhenSearching_ThenNothingHappens()
        {
            // Act
            SqlServerSearchService.AddLongUriSearchValueWarning(CreateUriExpression(300), null, _logger);
            SqlServerSearchService.AddLongUriSearchValueWarning(null, _requestContext, _logger);

            // Assert
            Assert.Empty(_bundleIssues);
            Assert.Empty(_logger.Messages);
        }

        private static Expression CreateUriExpression(int length)
        {
            const string prefix = "https://careplan.invalid/";
            string uri = prefix + new string('b', length - prefix.Length);
            return Expression.SearchParameter(InstantiatesUri, Expression.StringEquals(FieldName.Uri, null, uri, false));
        }

        private static string GetUri(Expression expression) =>
            ((StringExpression)((SearchParameterExpression)expression).Expression).Value;

        private sealed class CapturingLogger : ILogger
        {
            public List<string> Messages { get; } = new List<string>();

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }
}
