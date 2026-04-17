// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EnsureThat;
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    internal sealed class FixtureArgumentSetTestCollection : XunitTestCollection
    {
        private static readonly FieldInfo TestCollectionDisplayNameField = typeof(XunitTestCollection).GetField("testCollectionDisplayName", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo UniqueIdField = typeof(XunitTestCollection).GetField("uniqueID", BindingFlags.Instance | BindingFlags.NonPublic);

        private IReadOnlyList<SingleFlag> _fixtureArguments;
        private string _testClassName;

        public FixtureArgumentSetTestCollection(IXunitTestAssembly testAssembly, IReadOnlyList<SingleFlag> fixtureArguments, string testClassName = null)
            : base(testAssembly, collectionDefinition: null, disableParallelization: false, displayName: string.Empty, uniqueID: string.Empty)
        {
            EnsureArg.IsNotNull(testAssembly, nameof(testAssembly));
            EnsureArg.IsNotNull(fixtureArguments, nameof(fixtureArguments));

            _fixtureArguments = fixtureArguments;
            _testClassName = testClassName;

            UpdateDisplayAndUniqueId(testAssembly.UniqueID);
        }

#pragma warning disable CS0618 // Called by the de-serializer; should only be called by deriving classes for de-serialization purposes
        public FixtureArgumentSetTestCollection()
        {
            _fixtureArguments = Array.Empty<SingleFlag>();
        }
#pragma warning restore CS0618

        internal void ApplyFixtureArguments(IReadOnlyList<SingleFlag> fixtureArguments)
        {
            if (fixtureArguments == null)
            {
                return;
            }

            _fixtureArguments = fixtureArguments;
            UpdateDisplayAndUniqueId(TestAssembly.UniqueID);
        }

        private void UpdateDisplayAndUniqueId(string assemblyUniqueId)
        {
            if (_fixtureArguments.Count == 0)
            {
                return;
            }

            // Collection display name is either the variant label (shared-per-variant) or
            // test class + variant (per-class), depending on the caller's choice.
#pragma warning disable SA1100 // Do not prefix calls with base unless local implementation exists
            var argsLabel = string.Join(", ", _fixtureArguments.Select(v => v.EnumValue));
            var displayName = string.IsNullOrEmpty(_testClassName)
                ? $"{base.TestCollectionDisplayName}({argsLabel})"
                : $"{_testClassName}({argsLabel})";
            TestCollectionDisplayNameField?.SetValue(this, displayName);
            UniqueIdField?.SetValue(this, UniqueIDGenerator.ForTestCollection(assemblyUniqueId, displayName, base.TestCollectionClassName));
#pragma warning restore SA1100
        }
    }
}
