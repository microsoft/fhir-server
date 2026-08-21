// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Reflection;
using Xunit;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Covers how a class-level discovery failure chooses the methods it is reported against, and how
    /// it tells two of them apart.
    /// </summary>
    /// <remarks>
    /// Both decisions have the same failure mode. A test method that is not recognised as one gets no
    /// failure standing in for it, and two methods given the same identity get one failure between
    /// them, xunit keeping whichever arrived first. Either way the run reports success with tests
    /// missing and nothing in its output saying so, which is what this whole mechanism exists to
    /// prevent. Both are asserted directly rather than through a scenario because arranging them in a
    /// real test assembly needs an attribute xunit has no discoverer for.
    /// </remarks>
    public class FaultMethodSelectionTests
    {
        /// <summary>
        /// xunit discovers tests by the <see cref="IFactAttribute"/> interface, not by the
        /// <see cref="FactAttribute"/> class, so an attribute implementing the interface without
        /// deriving from the class still marks a test. Recognising only the class would leave such a
        /// method out of the methods a class-level failure is reported against.
        /// </summary>
        [Fact]
        public void GivenAMethodMarkedByAnAttributeImplementingTheFactInterface_WhenItIsInspected_ThenItCountsAsATest()
        {
            MethodInfo method = typeof(Subject).GetMethod(nameof(Subject.MarkedByInterfaceOnly));

            Assert.False(typeof(FactAttribute).IsAssignableFrom(typeof(InterfaceOnlyFactAttribute)));
            Assert.True(CustomXunitTestFrameworkDiscoverer.IsTestMethod(method));
        }

        /// <summary>
        /// A method carrying no fact attribute at all is not a test, so it is not one of the methods a
        /// failure is reported against - a failure named after a helper carries none of the traits a
        /// CI leg selects the class's tests by.
        /// </summary>
        [Fact]
        public void GivenAMethodCarryingNoFactAttribute_WhenItIsInspected_ThenItDoesNotCountAsATest()
        {
            MethodInfo method = typeof(Subject).GetMethod(nameof(Subject.NotATest));

            Assert.False(CustomXunitTestFrameworkDiscoverer.IsTestMethod(method));
        }

        /// <summary>
        /// Generic overloads differing only in arity share a name and both take no parameters, so a key
        /// built from the name and parameter types alone is the same for both. The two failures would
        /// then share a unique ID and xunit would keep one, losing the other method silently.
        /// </summary>
        [Fact]
        public void GivenGenericOverloadsDifferingOnlyInArity_WhenTheirFaultKeysAreBuilt_ThenTheKeysDiffer()
        {
            MethodInfo one = typeof(Subject).GetMethod(nameof(Subject.GenericOverload), 1, Type.EmptyTypes);
            MethodInfo two = typeof(Subject).GetMethod(nameof(Subject.GenericOverload), 2, Type.EmptyTypes);

            Assert.NotEqual(
                CustomXunitTestFrameworkDiscoverer.BuildFaultMethodKey(one),
                CustomXunitTestFrameworkDiscoverer.BuildFaultMethodKey(two));
        }

        /// <summary>
        /// Ordinary overloads differing only in parameter types must stay distinguishable too, so that
        /// widening the key for generic arity did not narrow it anywhere else.
        /// </summary>
        [Fact]
        public void GivenOverloadsDifferingOnlyInParameterTypes_WhenTheirFaultKeysAreBuilt_ThenTheKeysDiffer()
        {
            MethodInfo one = typeof(Subject).GetMethod(nameof(Subject.Overload), new[] { typeof(int) });
            MethodInfo two = typeof(Subject).GetMethod(nameof(Subject.Overload), new[] { typeof(string) });

            Assert.NotEqual(
                CustomXunitTestFrameworkDiscoverer.BuildFaultMethodKey(one),
                CustomXunitTestFrameworkDiscoverer.BuildFaultMethodKey(two));
        }

        /// <summary>
        /// A method whose attributes throw while being read is not known to be a test, and must not
        /// be reported as known not to be one: the two answers lead to opposite decisions below.
        /// </summary>
        [Fact]
        public void GivenAMethodWhoseAttributesCannotBeRead_WhenItIsInspected_ThenItsTestNessIsUnknown()
        {
            MethodInfo method = typeof(Subject).GetMethod(nameof(Subject.AttributesCannotBeRead));

            Assert.Null(CustomXunitTestFrameworkDiscoverer.IsTestMethod(method));
        }

        /// <summary>
        /// The attributes that cannot be read are very often the ones that caused the failure being
        /// reported, so a method whose test-ness is unknown is kept. Dropping it would leave a real
        /// test with no failure standing in for it, and a leg selecting by a trait only that method
        /// carried would pass green with it absent - the presence of a readable sibling is exactly
        /// what would hide it, because the fallback never fires.
        /// </summary>
        [Fact]
        public void GivenAMethodWhoseAttributesCannotBeReadAlongsideAReadableTest_WhenTheMethodsAreSelected_ThenItIsKept()
        {
            MethodInfo unreadable = typeof(Subject).GetMethod(nameof(Subject.AttributesCannotBeRead));
            MethodInfo readable = typeof(Subject).GetMethod(nameof(Subject.MarkedByInterfaceOnly));

            MethodInfo[] selected = CustomXunitTestFrameworkDiscoverer.SelectMethodsToReportAgainst(new[] { readable, unreadable });

            Assert.Equal(new[] { readable, unreadable }, selected);
        }

        /// <summary>
        /// With nothing that is, or may be, a test there is still a failure to report, so the first
        /// method the class declares stands in for it. Reporting nowhere would put the class back to
        /// vanishing from a green run.
        /// </summary>
        [Fact]
        public void GivenNoMethodIsATest_WhenTheMethodsAreSelected_ThenTheFirstMethodIsUsed()
        {
            MethodInfo first = typeof(Subject).GetMethod(nameof(Subject.NotATest));
            MethodInfo second = typeof(Subject).GetMethod(nameof(Subject.Overload), new[] { typeof(int) });

            MethodInfo[] selected = CustomXunitTestFrameworkDiscoverer.SelectMethodsToReportAgainst(new[] { first, second });

            Assert.Equal(new[] { first }, selected);
        }

        /// <summary>
        /// An attribute that marks a test the way xunit recognises one - by implementing the interface -
        /// without deriving from <see cref="FactAttribute"/>.
        /// </summary>
        [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
        private sealed class InterfaceOnlyFactAttribute : Attribute, IFactAttribute
        {
            public string DisplayName => null;

            public bool Explicit => false;

            public string Skip => null;

            public Type[] SkipExceptions => null;

            public Type SkipType => null;

            public string SkipUnless => null;

            public string SkipWhen => null;

            public string SourceFilePath => null;

            public int? SourceLineNumber => null;

            public int Timeout => 0;
        }

        /// <summary>
        /// An attribute whose construction throws, standing in for one whose type cannot be loaded -
        /// reading the attributes of a method carrying it throws rather than answering.
        /// </summary>
        [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
        private sealed class UnreadableAttribute : Attribute
        {
            public UnreadableAttribute()
            {
                throw new InvalidOperationException("This attribute cannot be constructed.");
            }
        }

        /// <summary>
        /// The methods the assertions above reflect over. None of them is discovered as a test: they
        /// are inspected directly.
        /// </summary>
        private sealed class Subject
        {
            [InterfaceOnlyFact]
            public void MarkedByInterfaceOnly()
            {
            }

            public void NotATest()
            {
            }

            [InterfaceOnlyFact]
            [Unreadable]
            public void AttributesCannotBeRead()
            {
            }

            public void GenericOverload<T>()
            {
            }

            public void GenericOverload<T, TOther>()
            {
            }

            public void Overload(int value)
            {
            }

            public void Overload(string value)
            {
            }
        }
    }
}
