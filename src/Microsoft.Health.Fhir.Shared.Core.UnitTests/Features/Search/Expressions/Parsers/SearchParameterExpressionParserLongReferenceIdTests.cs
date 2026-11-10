// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Expressions.Parsers
{
    /// <summary>
    /// Tests for reference search values with resource ids longer than the maximum resource id length.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterExpressionParserLongReferenceIdTests
    {
        private static readonly SearchParameterInfo Performer = new SearchParameterInfo(
            name: "performer",
            code: "performer",
            searchParamType: SearchParamType.Reference,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-performer"));

        private static readonly SearchParameterInfo Subject = new SearchParameterInfo(
            name: "subject",
            code: "subject",
            searchParamType: SearchParamType.Reference,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));

        private readonly List<OperationOutcomeIssue> _bundleIssues = new List<OperationOutcomeIssue>();
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private readonly CapturingLogger _logger = new CapturingLogger();

        public SearchParameterExpressionParserLongReferenceIdTests()
        {
            IFhirRequestContext context = Substitute.For<IFhirRequestContext>();
            context.BaseUri.Returns(new Uri("https://localhost/"));
            context.BundleIssues.Returns(_bundleIssues);
            _contextAccessor.RequestContext.Returns(context);
        }

        [Theory]
        [InlineData(false, 65, 64)]
        [InlineData(false, 129, 64)]
        [InlineData(true, 129, 128)]
        public void GivenAReferenceWithAnOverlongId_WhenParsed_ThenOneWarningIsAddedAndLoggedWithoutTheValue(bool useLongResourceIds, int length, int maxLength)
        {
            string resourceId = CreateResourceId(length);
            SearchParameterExpressionParser parser = CreateParser(ResourceIdPolicy.From(useLongResourceIds));

            parser.Parse(Performer, null, $"Patient/{resourceId}");

            OperationOutcomeIssue issue = Assert.Single(_bundleIssues);
            Assert.True(LongReferenceIdIssue.IsMatch(issue));
            Assert.Equal(LongReferenceIdIssue.Create("performer", maxLength).Diagnostics, issue.Diagnostics);
            Assert.DoesNotContain(resourceId[..maxLength], issue.Diagnostics, StringComparison.Ordinal);

            (LogLevel level, string message) = Assert.Single(_logger.Entries);
            Assert.Equal(LogLevel.Warning, level);
            Assert.Contains("SearchParameter=performer", message, StringComparison.Ordinal);
            Assert.Contains($"Length={length}", message, StringComparison.Ordinal);
            Assert.Contains($"MaxLength={maxLength}", message, StringComparison.Ordinal);
            Assert.DoesNotContain(resourceId[..maxLength], message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(false, 64)]
        [InlineData(true, 65)]
        [InlineData(true, 128)]
        public void GivenAReferenceWithinTheIdLimit_WhenParsed_ThenNoWarningIsAdded(bool useLongResourceIds, int length)
        {
            SearchParameterExpressionParser parser = CreateParser(ResourceIdPolicy.From(useLongResourceIds));

            parser.Parse(Performer, null, $"Patient/{CreateResourceId(length)}");
            parser.Parse(Performer, null, CreateResourceId(length));

            Assert.Empty(_bundleIssues);
            Assert.Empty(_logger.Entries);
        }

        [Fact]
        public void GivenAnOverlongBareId_WhenParsedWithAndWithoutATypeModifier_ThenTheWarningIsAddedOnce()
        {
            SearchParameterExpressionParser parser = CreateParser(ResourceIdPolicy.Standard);
            string resourceId = CreateResourceId(65);

            parser.Parse(Performer, null, resourceId);
            parser.Parse(Performer, new SearchModifier(SearchModifierCode.Type, KnownResourceTypes.Patient), resourceId);

            Assert.True(LongReferenceIdIssue.IsMatch(Assert.Single(_bundleIssues)));
            Assert.Single(_logger.Entries);
        }

        [Fact]
        public void GivenTheSameOverlongReferenceParsedRepeatedly_WhenParsed_ThenTheWarningIsAddedAndLoggedOnce()
        {
            SearchParameterExpressionParser parser = CreateParser(ResourceIdPolicy.Standard);
            string value = $"Patient/{CreateResourceId(65)},Patient/{CreateResourceId(70)}";

            parser.Parse(Performer, null, value);
            parser.Parse(Performer, null, value);

            Assert.Single(_bundleIssues);
            Assert.Single(_logger.Entries);
        }

        [Fact]
        public void GivenOverlongReferencesForTwoSearchParameters_WhenParsed_ThenOneWarningIsAddedPerParameter()
        {
            SearchParameterExpressionParser parser = CreateParser(ResourceIdPolicy.Standard);

            parser.Parse(Performer, null, $"Patient/{CreateResourceId(65)}");
            parser.Parse(Subject, null, $"Patient/{CreateResourceId(65)}");

            Assert.Collection(
                _bundleIssues,
                issue => Assert.Equal(LongReferenceIdIssue.Create("performer", 64).Diagnostics, issue.Diagnostics),
                issue => Assert.Equal(LongReferenceIdIssue.Create("subject", 64).Diagnostics, issue.Diagnostics));
        }

        [Fact]
        public void GivenNoRequestContext_WhenAnOverlongReferenceIsParsed_ThenItIsLogged()
        {
            _contextAccessor.RequestContext.Returns(default(IFhirRequestContext));
            SearchParameterExpressionParser parser = CreateParser(ResourceIdPolicy.Standard);

            parser.Parse(Performer, null, $"Patient/{CreateResourceId(65)}");

            Assert.Empty(_bundleIssues);
            Assert.Single(_logger.Entries);
        }

        [Fact]
        public void GivenTheParserWithoutAContextAccessor_WhenAnOverlongReferenceIsParsed_ThenTheSearchIsBuilt()
        {
            var parser = new SearchParameterExpressionParser(new ReferenceSearchValueParser(_contextAccessor, Substitute.For<IFhirServerInstanceConfiguration>(), ResourceIdPolicy.Standard));

            Assert.NotNull(parser.Parse(Performer, null, $"Patient/{CreateResourceId(65)}"));
            Assert.Empty(_bundleIssues);
        }

        private static string CreateResourceId(int length)
            => new string('a', length);

        private SearchParameterExpressionParser CreateParser(ResourceIdPolicy policy)
            => new SearchParameterExpressionParser(
                new ReferenceSearchValueParser(_contextAccessor, Substitute.For<IFhirServerInstanceConfiguration>(), policy),
                _contextAccessor,
                policy,
                _logger);

        private sealed class CapturingLogger : ILogger<SearchParameterExpressionParser>
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new List<(LogLevel, string)>();

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
