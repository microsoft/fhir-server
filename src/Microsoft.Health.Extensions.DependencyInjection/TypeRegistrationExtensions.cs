// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Extensions.DependencyInjection
{
    public static class TypeRegistrationExtensions
    {
        public static IEnumerable<TypeRegistration> TypesInSameAssemblyAs<T>(this IServiceCollection serviceCollection)
        {
            return TypesInSameAssembly(serviceCollection, typeof(T).Assembly);
        }

        public static IEnumerable<TypeRegistration> TypesInSameAssembly(this IServiceCollection serviceCollection, params Assembly[] assemblies)
        {
            EnsureArg.IsNotNull(serviceCollection, nameof(serviceCollection));
            EnsureArg.IsNotNull(assemblies, nameof(assemblies));

            return assemblies
                .SelectMany(x => x.GetTypes())
                .Where(x => x.IsClass && !x.IsAbstract && !x.ContainsGenericParameters)
                .Select(x => new TypeRegistration(serviceCollection, x))
                .ToArray();
        }

        public static TypeRegistration Add<T>(this IServiceCollection serviceCollection, Func<IServiceProvider, T> delegateRegistration)
        {
            EnsureArg.IsNotNull(serviceCollection, nameof(serviceCollection));
            EnsureArg.IsNotNull(delegateRegistration, nameof(delegateRegistration));

            return new TypeRegistration(serviceCollection, typeof(T), provider => delegateRegistration(provider));
        }

        public static TypeRegistration Add<T>(this IServiceCollection serviceCollection) => serviceCollection.Add(typeof(T));

        public static TypeRegistration Add(this IServiceCollection serviceCollection, Type type)
        {
            EnsureArg.IsNotNull(serviceCollection, nameof(serviceCollection));
            EnsureArg.IsNotNull(type, nameof(type));

            return new TypeRegistration(serviceCollection, type);
        }

        /// <summary>
        /// Adds a service that allows a factory to be injected that resolves the specified type (Func{T}).
        /// This is useful where the type being resolved should be as-needed, or multiple instances need to be created
        /// </summary>
        /// <typeparam name="T">Type of service to be registered</typeparam>
        /// <param name="serviceCollection">The service collection.</param>
        public static void AddFactory<T>(this IServiceCollection serviceCollection)
        {
            EnsureArg.IsNotNull(serviceCollection, nameof(serviceCollection));

            Type typeArguments = typeof(T);
            Type factoryFunc = typeof(Func<>).MakeGenericType(typeArguments);

            MethodInfo factoryMethod = typeof(TypeRegistrationExtensions).GetMethod(nameof(Factory), BindingFlags.NonPublic | BindingFlags.Static);

            Debug.Assert(factoryMethod != null, $"{nameof(Factory)} was not found.");

            MethodInfo implFactoryMethod = factoryMethod.MakeGenericMethod(typeArguments);
            Delegate implDelegate = implFactoryMethod.CreateDelegate(typeof(Func<IServiceProvider, object>), null);

            serviceCollection.AddTransient(factoryFunc, (Func<IServiceProvider, object>)implDelegate);
        }

        /// <summary>
        /// Register Lazy as an Open Generic, this can resolve any service with Lazy instantiation
        /// </summary>
        /// <param name="serviceCollection">The service collection.</param>
        public static void AddLazy(this IServiceCollection serviceCollection)
        {
            EnsureArg.IsNotNull(serviceCollection, nameof(serviceCollection));

            serviceCollection.AddTransient(typeof(Lazy<>), typeof(LazyProvider<>));
        }

        /// <summary>
        /// Register Scope as an Open Generic, this can resolve any service with an owned lifetime scope
        /// </summary>
        /// <param name="serviceCollection">The service collection.</param>
        public static void AddScoped(this IServiceCollection serviceCollection)
        {
            EnsureArg.IsNotNull(serviceCollection, nameof(serviceCollection));

            serviceCollection.AddTransient(typeof(IScoped<>), typeof(Scoped<>));
        }

        private static object Factory<T>(IServiceProvider provider)
        {
            Func<T> factory = provider.GetService<T>;
            return factory;
        }

        private static IEnumerable<T> Do<T>(this IEnumerable<T> registrations, Action<T> action)
        {
            EnsureArg.IsNotNull(registrations, nameof(registrations));
            EnsureArg.IsNotNull(action, nameof(action));

            var registrationArray = registrations.ToArray();

            foreach (var registration in registrationArray)
            {
                action(registration);
            }

            return registrationArray;
        }

        public static IEnumerable<TypeRegistration> AssignableTo<T>(this IEnumerable<TypeRegistration> registrations)
        {
            EnsureArg.IsNotNull(registrations, nameof(registrations));

            var type = typeof(T);
            return registrations.Where(x => type.IsAssignableFrom(x.Type));
        }

        public static IEnumerable<TypeRegistrationBuilder> Singleton(this IEnumerable<TypeRegistration> registrations)
        {
            EnsureArg.IsNotNull(registrations, nameof(registrations));

            return registrations.Select(x => x.Singleton());
        }

        public static IEnumerable<TypeRegistrationBuilder> Transient(this IEnumerable<TypeRegistration> registrations)
        {
            EnsureArg.IsNotNull(registrations, nameof(registrations));

            return registrations.Select(x => x.Transient());
        }

        public static IEnumerable<TypeRegistrationBuilder> Scoped(this IEnumerable<TypeRegistration> registrations)
        {
            EnsureArg.IsNotNull(registrations, nameof(registrations));

            return registrations.Select(x => x.Scoped());
        }

        /// <summary>
        /// Creates a service registration for the specified interface
        /// </summary>
        /// <typeparam name="T">Type of service to be registered</typeparam>
        /// <param name="registrations">The registrations.</param>
        /// <returns>The registration builder</returns>
        public static IEnumerable<TypeRegistrationBuilder> AsService<T>(this IEnumerable<TypeRegistrationBuilder> registrations)
        {
            EnsureArg.IsNotNull(registrations, nameof(registrations));

            return registrations.Do(x => x.AsService<T>());
        }

        /// <summary>
        /// Creates a service registration for all interfaces implemented by the type
        /// </summary>
        /// <param name="registrations">The registrations.</param>
        /// <param name="interfaceFilter">A predicate specifying which interfaces to register.</param>
        /// <returns>The registration builder</returns>
        public static IEnumerable<TypeRegistrationBuilder> AsImplementedInterfaces(this IEnumerable<TypeRegistrationBuilder> registrations, Predicate<Type> interfaceFilter = null)
        {
            EnsureArg.IsNotNull(registrations, nameof(registrations));

            return registrations.Do(x => x.AsImplementedInterfaces(interfaceFilter));
        }

        /// <summary>
        /// Create a service registration for the concrete type
        /// </summary>
        /// <param name="registrations">The registrations.</param>
        /// <returns>The registration builder</returns>
        public static IEnumerable<TypeRegistrationBuilder> AsSelf(this IEnumerable<TypeRegistrationBuilder> registrations)
        {
            EnsureArg.IsNotNull(registrations, nameof(registrations));

            return registrations.Do(x => x.AsSelf());
        }
    }
}
