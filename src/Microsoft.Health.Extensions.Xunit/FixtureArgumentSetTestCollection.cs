// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    internal sealed class FixtureArgumentSetTestCollection : XunitTestCollection
    {
        public FixtureArgumentSetTestCollection(IXunitTestCollection sourceCollection, IReadOnlyList<SingleFlag> fixtureArguments)
            : base(
                EnsureArg.IsNotNull(sourceCollection, nameof(sourceCollection)).TestAssembly,
                sourceCollection.CollectionDefinition,
                ShouldDisableParallelization(sourceCollection),
                BuildDisplayName(sourceCollection.TestCollectionDisplayName, fixtureArguments),
                uniqueID: BuildUniqueId(sourceCollection, fixtureArguments))
        {
            EnsureArg.IsNotNull(fixtureArguments, nameof(fixtureArguments));
        }

#pragma warning disable CS0618 // Called by the de-serializer; should only be called by deriving classes for de-serialization purposes
        public FixtureArgumentSetTestCollection()
        {
        }
#pragma warning restore CS0618

        private static string BuildDisplayName(string baseDisplayName, IReadOnlyList<SingleFlag> fixtureArguments)
        {
            EnsureArg.IsNotNull(fixtureArguments, nameof(fixtureArguments));

            if (fixtureArguments.Count == 0)
            {
                return baseDisplayName;
            }

            var argsLabel = string.Join(", ", fixtureArguments.Select(v => v.EnumValue));
            return $"{baseDisplayName}({argsLabel})";
        }

        private static string BuildUniqueId(IXunitTestCollection sourceCollection, IReadOnlyList<SingleFlag> fixtureArguments)
        {
            var displayName = BuildDisplayName(sourceCollection.TestCollectionDisplayName, fixtureArguments);
            return UniqueIDGenerator.ForTestCollection(sourceCollection.TestAssembly.UniqueID, displayName, sourceCollection.TestCollectionClassName);
        }

        /// <summary>
        /// Determines whether the variants of a collection should be prevented from running concurrently.
        /// </summary>
        /// <remarks>
        /// Splitting a collection into one variant per fixture argument set would otherwise let the
        /// variants run concurrently. Any collection backed by a [CollectionDefinition] class stays
        /// serialized, because those group classes that were deliberately placed together.
        /// <para>
        /// This does not cover a name-only [Collection("...")] with no matching [CollectionDefinition]:
        /// TestCollectionClassName holds the definition class, so it is null for those and there is
        /// nothing here to detect them by.
        /// </para>
        /// </remarks>
        /// <param name="sourceCollection">The collection the variant was derived from.</param>
        /// <returns><c>true</c> if the variant collection should run serially.</returns>
        private static bool ShouldDisableParallelization(IXunitTestCollection sourceCollection)
        {
            return sourceCollection.DisableParallelization || sourceCollection.CollectionDefinition != null;
        }
    }
}
