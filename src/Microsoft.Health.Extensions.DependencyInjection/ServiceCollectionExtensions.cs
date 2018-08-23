// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Extensions.DependencyInjection
{
    /// <summary>
    /// Extensions for IServiceCollection
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers assembly modules.
        /// </summary>
        /// <param name="collection">The collection.</param>
        /// <param name="assembly">The assembly.</param>
        /// <param name="constructorParams">An array of constructor parameters that can be injected into IStartupModule on creation.</param>
        public static void RegisterAssemblyModules(this IServiceCollection collection, Assembly assembly, params object[] constructorParams)
        {
            EnsureArg.IsNotNull(collection, nameof(collection));
            EnsureArg.IsNotNull(assembly, nameof(assembly));

            foreach (var moduleType in assembly.GetTypes()
                .Where(x => typeof(IStartupModule).IsAssignableFrom(x) && x.IsClass && !x.IsAbstract))
            {
                // For simplicity, only a single Module constructor is supported
                var constructor = moduleType
                    .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Single();

                // Attempts to match parameter arguments by type
                var constructorArguments = constructor
                    .GetParameters()
                    .Select(x =>
                    {
                        var param = constructorParams?.FirstOrDefault(y => x.ParameterType.IsInstanceOfType(y));
                        if (param == null)
                        {
                            throw new NotSupportedException($"Constructor parameter '{x.Name}' of type '{x.ParameterType.Name}' in class '{moduleType.Name}' couldn't be resolved.");
                        }

                        return param;
                    })
                    .ToArray();

                var module = (IStartupModule)constructor.Invoke(constructorArguments);
                module.Load(collection);
            }
        }
    }
}
