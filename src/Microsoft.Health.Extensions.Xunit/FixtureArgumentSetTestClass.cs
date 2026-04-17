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
    internal sealed class FixtureArgumentSetTestClass : XunitTestClass
    {
        private static readonly FieldInfo UniqueIdField = typeof(XunitTestClass).GetField("uniqueID", BindingFlags.Instance | BindingFlags.NonPublic);
        private Type _testClassType;
        private IReadOnlyList<SingleFlag> _fixtureArguments;

        public FixtureArgumentSetTestClass(Type testClassType, IXunitTestCollection testCollection, IReadOnlyList<SingleFlag> fixtureArguments, string uniqueId)
            : base(testClassType, testCollection, uniqueId)
        {
            EnsureArg.IsNotNull(testClassType, nameof(testClassType));
            EnsureArg.IsNotNull(testCollection, nameof(testCollection));
            EnsureArg.IsNotNull(fixtureArguments, nameof(fixtureArguments));

            _testClassType = testClassType;
            _fixtureArguments = fixtureArguments;
            UpdateUniqueId();
        }

#pragma warning disable CS0618 // Called by the de-serializer; should only be called by deriving classes for de-serialization purposes
        public FixtureArgumentSetTestClass()
        {
            _fixtureArguments = Array.Empty<SingleFlag>();
        }
#pragma warning restore CS0618

#pragma warning disable SA1100 // Do not prefix calls with base unless local implementation exists
        public new Type Class => _testClassType ?? base.Class;
#pragma warning restore SA1100

        internal IReadOnlyList<SingleFlag> GetFixtureArguments()
        {
            return _fixtureArguments;
        }

        internal void ApplyFixtureArguments(IReadOnlyList<SingleFlag> fixtureArguments)
        {
            if (fixtureArguments == null)
            {
                return;
            }

            _fixtureArguments = fixtureArguments;
            UpdateUniqueId();
        }

        private void UpdateUniqueId()
        {
            if (_fixtureArguments.Count == 0)
            {
                return;
            }

            var className = $"{TestClassName}({string.Join(", ", _fixtureArguments.Select(v => v.EnumValue))})";
            UniqueIdField?.SetValue(this, UniqueIDGenerator.ForTestClass(TestCollection.UniqueID, className));
        }
    }
}
