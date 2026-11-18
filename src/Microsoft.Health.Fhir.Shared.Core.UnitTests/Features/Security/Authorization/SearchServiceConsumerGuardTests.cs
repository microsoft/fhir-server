// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Security.Authorization
{
    /// <summary>
    /// Pins the set of types that are constructed with direct access to <see cref="ISearchService"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="SearchResourceHandler"/> authorizes the caller before it searches. Any other type that receives
    /// <see cref="ISearchService"/> reaches stored resources without passing through that handler, so it must
    /// establish the caller's authority for the lookup itself. This test does not prove that the listed types do so;
    /// it makes the addition of a new direct consumer a deliberate, reviewed change instead of a silent one.
    /// </remarks>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Security)]
    public class SearchServiceConsumerGuardTests
    {
        private const string ReviewGuidance =
            "A type that receives ISearchService reaches stored resources without passing through SearchResourceHandler. " +
            "Before adding a type to the list in this test, follow .github/instructions/security-search.instructions.md: " +
            "show where the caller's DataActions are checked for the lookup, how SMART scope and compartment restrictions " +
            "in AccessControlContext reach the search (including background execution), and add a test with an allowed " +
            "and a refused caller.";

        /// <summary>
        /// Direct consumers of <see cref="ISearchService"/> when this guard was introduced, by full type name.
        /// Presence in this list records that the type existed then; it does not assert that the type was audited.
        /// </summary>
        private static readonly string[] BaselineConsumers =
        {
            "Microsoft.Health.Fhir.Core.Features.Conformance.USCoreInstantiateCapability",
            "Microsoft.Health.Fhir.Core.Features.Definition.SearchParameterDefinitionManager",
            "Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.BulkDeleteOrchestratorJob",
            "Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.BulkDeleteProcessingJob",
            "Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Handlers.CreateBulkDeleteHandler",
            "Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.BulkUpdateOrchestratorJob",
            "Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Handlers.CreateBulkUpdateHandler",
            "Microsoft.Health.Fhir.Core.Features.Operations.Everything.PatientEverythingService",
            "Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportJobTask",
            "Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportProcessingJob",
            "Microsoft.Health.Fhir.Core.Features.Operations.MemberMatch.MemberMatchService",
            "Microsoft.Health.Fhir.Core.Features.Operations.Reindex.ReindexOrchestratorJob",
            "Microsoft.Health.Fhir.Core.Features.Operations.Reindex.ReindexProcessingJob",
            "Microsoft.Health.Fhir.Core.Features.Persistence.BulkUpdateService",
            "Microsoft.Health.Fhir.Core.Features.Persistence.DeletionService",
            "Microsoft.Health.Fhir.Core.Features.Resources.ConditionalResourceHandler`2",
            "Microsoft.Health.Fhir.Core.Features.Resources.Create.ConditionalCreateResourceHandler",
            "Microsoft.Health.Fhir.Core.Features.Resources.Delete.ConditionalDeleteResourceHandler",
            "Microsoft.Health.Fhir.Core.Features.Resources.Get.GetResourceHandler",
            "Microsoft.Health.Fhir.Core.Features.Resources.Patch.ConditionalPatchResourceHandler",
            "Microsoft.Health.Fhir.Core.Features.Resources.ResourceReferenceResolver",
            "Microsoft.Health.Fhir.Core.Features.Resources.Upsert.ConditionalUpsertResourceHandler",
            "Microsoft.Health.Fhir.Core.Features.Search.Parameters.SearchParameterOperations",
            "Microsoft.Health.Fhir.Core.Features.Search.SearchCompartmentHandler",
            "Microsoft.Health.Fhir.Core.Features.Search.SearchResourceHandler",
            "Microsoft.Health.Fhir.Core.Features.Search.SearchResourceHistoryHandler",
            "Microsoft.Health.Fhir.Core.Features.Validation.ServerProvideProfileValidation",
        };

        [Fact]
        public void GivenTheCoreAssemblies_WhenATypeIsConstructedWithTheSearchService_ThenItIsAKnownConsumer()
        {
            string[] actual = GetSearchServiceConsumers();

            string[] unreviewed = actual.Except(BaselineConsumers, StringComparer.Ordinal).ToArray();
            string[] stale = BaselineConsumers.Except(actual, StringComparer.Ordinal).ToArray();

            Assert.True(
                unreviewed.Length == 0,
                $"New direct consumers of ISearchService: {string.Join(", ", unreviewed)}. {ReviewGuidance}");

            Assert.True(
                stale.Length == 0,
                $"These types no longer receive ISearchService; remove them from the list in this test: {string.Join(", ", stale)}.");
        }

        private static string[] GetSearchServiceConsumers()
        {
            // The first assembly holds the version-independent core; the second is the FHIR-version-specific
            // core assembly that the shared handlers are compiled into.
            Assembly[] assemblies =
            {
                typeof(ISearchService).Assembly,
                typeof(SearchResourceHandler).Assembly,
            };

            const BindingFlags DeclaredConstructors =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            return assemblies
                .Distinct()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.IsClass)
                .Where(type => type
                    .GetConstructors(DeclaredConstructors)
                    .SelectMany(constructor => constructor.GetParameters())
                    .Any(parameter => ExposesSearchService(parameter.ParameterType)))
                .Select(type => type.FullName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Returns true for <see cref="ISearchService"/> and for wrappers that hand it out, such as
        /// <c>IScoped&lt;ISearchService&gt;</c> and <c>Func&lt;IScoped&lt;ISearchService&gt;&gt;</c>.
        /// </summary>
        private static bool ExposesSearchService(Type type)
        {
            if (type == typeof(ISearchService))
            {
                return true;
            }

            IEnumerable<Type> typeArguments = type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes;

            return typeArguments.Any(ExposesSearchService);
        }
    }
}
