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
        public FixtureArgumentSetTestCollection(IXunitTestCollection sourceCollection, IReadOnlyList<SingleFlag> fixtureArguments, string testClassName = null)
            : base(
                EnsureArg.IsNotNull(sourceCollection, nameof(sourceCollection)).TestAssembly,
                sourceCollection.CollectionDefinition,
                sourceCollection.DisableParallelization,
                BuildDisplayName(sourceCollection.TestCollectionDisplayName, fixtureArguments, testClassName),
                uniqueID: BuildUniqueId(sourceCollection, fixtureArguments, testClassName))
        {
            EnsureArg.IsNotNull(fixtureArguments, nameof(fixtureArguments));
        }

#pragma warning disable CS0618 // Called by the de-serializer; should only be called by deriving classes for de-serialization purposes
        public FixtureArgumentSetTestCollection()
        {
        }
#pragma warning restore CS0618

        private static string BuildDisplayName(string baseDisplayName, IReadOnlyList<SingleFlag> fixtureArguments, string testClassName)
        {
            EnsureArg.IsNotNull(fixtureArguments, nameof(fixtureArguments));

            if (fixtureArguments.Count == 0)
            {
                return string.IsNullOrEmpty(testClassName) ? baseDisplayName : testClassName;
            }

            var argsLabel = string.Join(", ", fixtureArguments.Select(v => v.EnumValue));
            return string.IsNullOrEmpty(testClassName)
                ? $"{baseDisplayName}({argsLabel})"
                : $"{testClassName}({argsLabel})";
        }

        private static string BuildUniqueId(IXunitTestCollection sourceCollection, IReadOnlyList<SingleFlag> fixtureArguments, string testClassName)
        {
            var displayName = BuildDisplayName(sourceCollection.TestCollectionDisplayName, fixtureArguments, testClassName);
            return UniqueIDGenerator.ForTestCollection(sourceCollection.TestAssembly.UniqueID, displayName, sourceCollection.TestCollectionClassName);
        }
    }
}
