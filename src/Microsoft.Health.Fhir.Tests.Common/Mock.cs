// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using EnsureThat;
using NSubstitute;
using NSubstitute.Core;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public class Mock
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> StaticPropertyConcurrency = new ConcurrentDictionary<string, SemaphoreSlim>();
        private const int MaxWaitTimeInSeconds = 5;

        public static IDisposable Property<T>(Expression<Func<T>> propertyExpr, T val)
        {
            EnsureArg.IsNotNull(propertyExpr);

            var disposable = Substitute.For<IDisposable>();

            if (propertyExpr.Body is MemberExpression propertyGetExpression)
            {
                object closureFieldValue = GetClosureFieldValue(propertyGetExpression);
                SemaphoreSlim semaphore = null;

                // Create a semaphore for changing the value of static properties
                // Two unit tests running concurrently can fail by overriding values
                if (closureFieldValue == null)
                {
                    var key = propertyGetExpression.ToString();
                    semaphore = StaticPropertyConcurrency.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
                    semaphore.Wait(TimeSpan.FromSeconds(MaxWaitTimeInSeconds));
                }

                if (propertyGetExpression.Member is PropertyInfo propertyInfo)
                {
                    var previous = propertyInfo.GetValue(closureFieldValue);
                    propertyInfo.SetValue(closureFieldValue, val);
                    disposable.When(x => x.Dispose()).Do(_ =>
                    {
                        propertyInfo.SetValue(closureFieldValue, previous);
                        semaphore?.Release();
                    });
                }
                else
                {
                    throw new NotSupportedException($"Failed to find property for {propertyExpr}");
                }
            }
            else
            {
                throw new NotSupportedException($"{propertyExpr} was not a property");
            }

            return disposable;
        }

        private static object GetClosureFieldValue(MemberExpression propertyGetExpression)
        {
            // In the expression "() => obj.Prop" obj is replaced by a field access on a compiler-generated class from the closure
            if (propertyGetExpression?.Expression is MemberExpression fieldOnClosureExpression &&
                fieldOnClosureExpression.Expression is ConstantExpression closureClassExpression &&
                fieldOnClosureExpression.Member is FieldInfo closureFieldInfo)
            {
                return closureFieldInfo.GetValue(closureClassExpression.Value);
            }

            return null;
        }

        /// <summary>
        /// Creates a Mock of the specified type with optional constructor arguments.
        /// </summary>
        /// <typeparam name="T">The type to mock</typeparam>
        /// <param name="argOverrides">The argument overrides.</param>
        public static T TypeWithArguments<T>(params object[] argOverrides)
            where T : class
        {
            return (T)TypeWithArguments(typeof(T), argOverrides);
        }

        public static object TypeWithArguments(Type type, params object[] argOverrides)
        {
            EnsureArg.IsNotNull(type, nameof(type));

            if (argOverrides.Any(x => x == null))
            {
                throw new ArgumentNullException(nameof(argOverrides), "Values for argument overrides should not be null");
            }

            var constructor = type.GetConstructors().OrderBy(x => x.GetParameters().Length).First();

            var arguments = new List<object>();
            foreach (var parameter in constructor.GetParameters())
            {
                var overridden = argOverrides.FirstOrDefault(x => parameter.ParameterType.IsAssignableFrom(x.GetType()));
                if (overridden != null)
                {
                    arguments.Add(overridden);
                }
                else
                {
                    if (parameter.ParameterType.IsClass && parameter.ParameterType.GetConstructors().Min(x => x.GetParameters().Length) > 0)
                    {
                        arguments.Add(TypeWithArguments(parameter.ParameterType, argOverrides));
                    }
                    else
                    {
                        object item = parameter.ParameterType.IsInterface ?
                            Substitute.For(new[] { parameter.ParameterType }, null) :
                            SubstitutionContext.Current.SubstituteFactory.CreatePartial(new[] { parameter.ParameterType }, null);
                        arguments.Add(item);
                    }
                }
            }

            return SubstitutionContext.Current.SubstituteFactory.CreatePartial(new[] { type }, arguments.ToArray());
        }
    }
}
