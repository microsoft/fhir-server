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
                sourceCollection.DisableParallelization || IsExplicitCollection(sourceCollection),
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

        private static bool IsExplicitCollection(IXunitTestCollection sourceCollection)
        {
            // A collection is explicit when it has a [CollectionDefinition] class or a named
            // [Collection("...")] attribute (TestCollectionClassName is non-null for named collections,
            // null for the implicit per-class default collection).
            return sourceCollection.CollectionDefinition != null || sourceCollection.TestCollectionClassName != null;
        }
    }
}
