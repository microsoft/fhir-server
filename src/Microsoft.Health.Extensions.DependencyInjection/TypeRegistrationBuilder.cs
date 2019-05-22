// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Health.Extensions.DependencyInjection
{
    public class TypeRegistrationBuilder
    {
        private readonly MethodInfo _factoryGenericMethod = typeof(TypeRegistrationExtensions).GetMethod(nameof(TypeRegistrationExtensions.AddFactory), BindingFlags.Public | BindingFlags.Static);
        private readonly IServiceCollection _serviceCollection;
        private readonly Type _type;
        private readonly Func<IServiceProvider, object> _delegateRegistration;
        private readonly TypeRegistration.RegistrationMode _registrationMode;

        private Func<IServiceProvider, object> _cachedResolver;
        private Type _firstRegisteredType;

        internal TypeRegistrationBuilder(
            IServiceCollection serviceCollection,
            Type type,
            Func<IServiceProvider, object> delegateRegistration,
            TypeRegistration.RegistrationMode registrationMode)
        {
            _serviceCollection = serviceCollection;
            _type = type;
            _delegateRegistration = delegateRegistration;
            _registrationMode = registrationMode;
        }

        /// <summary>
        /// Creates a service registration for the specified interface
        /// </summary>
        /// <typeparam name="T">Type of service to be registered</typeparam>
        /// <returns>The registration builder</returns>
        public TypeRegistrationBuilder AsService<T>() => AsService(typeof(T));

        /// <summary>
        /// Creates a service registration for the specified interface
        /// </summary>
        /// <param name="serviceType">The service type to be registered</param>
        /// <returns>The registration builder</returns>
        public TypeRegistrationBuilder AsService(Type serviceType)
        {
            EnsureArg.IsNotNull(serviceType, nameof(serviceType));

            Debug.Assert(serviceType != _type, $"The \"AsSelf()\" registration should be used instead of \"AsService<{_type.Name}>()\".");

            RegisterType(serviceType);

            return this;
        }

        /// <summary>
        /// Creates a service registration for the specified interface that can be resolved with Func
        /// </summary>
        /// <typeparam name="T">Type of service to be registered</typeparam>
        /// <returns>The registration builder</returns>
        public TypeRegistrationBuilder AsFactory<T>()
        {
            var factoryMethod = _factoryGenericMethod.MakeGenericMethod(typeof(T));

            factoryMethod.Invoke(null, new object[] { _serviceCollection });

            return this;
        }

        /// <summary>
        /// Creates a service registration for the specified interface that can be resolved with Func
        /// </summary>
        /// <returns>The registration builder</returns>
        public TypeRegistrationBuilder AsFactory()
        {
            var factoryMethod = _factoryGenericMethod.MakeGenericMethod(_type);

            factoryMethod.Invoke(null, new object[] { _serviceCollection });

            return this;
        }

        /// <summary>
        /// Replaces a service registration for the specified interface
        /// </summary>
        /// <typeparam name="T">Type of service to be registered</typeparam>
        /// <returns>The registration builder</returns>
        public TypeRegistrationBuilder ReplaceService<T>()
        {
            RegisterType(typeof(T), true);

            return this;
        }

        /// <summary>
        /// Creates a service registration for all interfaces implemented by the type
        /// </summary>
        /// <param name="interfaceFilter">A predicate specifying which interfaces to register.</param>
        /// <returns>The registration builder</returns>
        /// <exception cref="NotSupportedException">Throws when Type was not explicitly defined</exception>
        public TypeRegistrationBuilder AsImplementedInterfaces(Predicate<Type> interfaceFilter = null)
        {
            Debug.Assert(
                _firstRegisteredType != null || _registrationMode == TypeRegistration.RegistrationMode.Transient,
                $"Using \"AsImplementedInterfaces()\" without calling \"AsSelf()\" first in registration for \"{_type.Name}\" could have inconsistent results.");

            if (interfaceFilter == null)
            {
                interfaceFilter = x => x != typeof(IDisposable);
            }

            var interfaces = _type.GetInterfaces().Where(x => interfaceFilter(x));

            foreach (var typeInterface in interfaces)
            {
                RegisterType(typeInterface);
            }

            return this;
        }

        /// <summary>
        /// Create a service registration for the concrete type
        /// </summary>
        /// <returns>The registration builder</returns>
        /// <exception cref="NotSupportedException">Throws when Type was not explicitly defined</exception>
        public TypeRegistrationBuilder AsSelf()
        {
            Debug.Assert(_firstRegisteredType == null, $"The \"AsSelf()\" registration for \"{_type.Name}\" should come first.");

            RegisterType(_type);

            return this;
        }

        /// <summary>
        /// Replaces a service registration for the concrete type
        /// </summary>
        /// <returns>The registration builder</returns>
        /// <exception cref="NotSupportedException">Throws when Type was not explicitly defined</exception>
        public TypeRegistrationBuilder ReplaceSelf()
        {
            Debug.Assert(_firstRegisteredType == null, $"The \"ReplaceSelf()\" registration for \"{_type.Name}\" should come first.");

            RegisterType(_type, replace: true);

            return this;
        }

        private void RegisterType(Type serviceType, bool replace = false)
        {
            EnsureArg.IsNotNull(serviceType, nameof(serviceType));

            if (_firstRegisteredType == null && _registrationMode != TypeRegistration.RegistrationMode.Transient)
            {
                SetupRootRegistration(serviceType, replace);

                return;
            }

            ServiceDescriptor serviceDescriptor;

            switch (_registrationMode)
            {
                case TypeRegistration.RegistrationMode.Transient:

                    if (_delegateRegistration != null)
                    {
                        serviceDescriptor = ServiceDescriptor.Transient(serviceType, _delegateRegistration);
                    }
                    else
                    {
                        serviceDescriptor = ServiceDescriptor.Transient(serviceType, _type);
                    }

                    break;
                case TypeRegistration.RegistrationMode.Scoped:

                    serviceDescriptor = ServiceDescriptor.Scoped(serviceType, _cachedResolver);

                    break;
                case TypeRegistration.RegistrationMode.Singleton:

                    serviceDescriptor = ServiceDescriptor.Singleton(serviceType, _cachedResolver);

                    break;
                default:
                    throw new NotSupportedException();
            }

            serviceDescriptor = serviceDescriptor.WithMetadata(_type);

            if (replace)
            {
                _serviceCollection.Replace(serviceDescriptor);
            }
            else
            {
                _serviceCollection.Add(serviceDescriptor);
            }
        }

        private void SetupRootRegistration(Type serviceType, bool replace)
        {
            _firstRegisteredType = serviceType;

            ServiceDescriptor serviceDescriptor = null;

            if (_delegateRegistration == null)
            {
                if (_registrationMode == TypeRegistration.RegistrationMode.Scoped)
                {
                    serviceDescriptor = ServiceDescriptor.Scoped(serviceType, _type);
                }
                else if (_registrationMode == TypeRegistration.RegistrationMode.Singleton)
                {
                    serviceDescriptor = ServiceDescriptor.Singleton(serviceType, _type);
                }
            }
            else
            {
                if (_registrationMode == TypeRegistration.RegistrationMode.Scoped)
                {
                    serviceDescriptor = ServiceDescriptor.Scoped(serviceType, _delegateRegistration);
                }
                else if (_registrationMode == TypeRegistration.RegistrationMode.Singleton)
                {
                    serviceDescriptor = ServiceDescriptor.Singleton(serviceType, _delegateRegistration);
                }
            }

            if (serviceDescriptor != null)
            {
                serviceDescriptor = serviceDescriptor.WithMetadata(_type);

                if (replace)
                {
                    _serviceCollection.Replace(serviceDescriptor);
                }
                else
                {
                    _serviceCollection.Add(serviceDescriptor);
                }
            }

            _cachedResolver = provider => provider.GetService(_firstRegisteredType);
        }
    }
}
