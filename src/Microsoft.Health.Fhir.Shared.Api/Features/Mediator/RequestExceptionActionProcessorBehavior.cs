// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using MediatR.Pipeline;

namespace Microsoft.Health.Fhir.Api.Features.Mediator
{
    /// <summary>
    /// Behavior for executing all <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/>
    ///     or <see cref="RequestExceptionHandler{TRequest,TResponse}"/> instances
    ///     after an exception is thrown by the following pipeline steps
    /// </summary>
    /// <typeparam name="TRequest">Request type</typeparam>
    /// <typeparam name="TResponse">Response type</typeparam>
    /// <remarks>
    /// This file is based on the original at: https://github.com/jbogard/MediatR/blob/master/src/MediatR/Pipeline/RequestExceptionActionProcessorBehavior.cs
    /// These changes have been submitted back in https://github.com/jbogard/MediatR/pull/587
    /// </remarks>
    public class RequestExceptionActionProcessorBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        private readonly ServiceFactory _serviceFactory;

        public RequestExceptionActionProcessorBehavior(ServiceFactory serviceFactory) => _serviceFactory = serviceFactory;

        public async Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<TResponse> next)
        {
            try
            {
                return await next().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                var actionsForException = GetActionsForException(exception.GetType(), out MethodInfo actionMethod);

                foreach (var actionForException in actionsForException)
                {
                    try
                    {
                        await ((Task)actionMethod.Invoke(actionForException, new object[] { request, exception, cancellationToken })).ConfigureAwait(false);
                    }
                    catch (TargetInvocationException invocationException) when (invocationException.InnerException != null)
                    {
                        // Unwrap reflection exception to throw the actual error
                        ExceptionDispatchInfo.Capture(invocationException.InnerException).Throw();
                    }
                }

                throw;
            }
        }

        private IList<object> GetActionsForException(Type exceptionType, out MethodInfo actionMethodInfo)
        {
            var exceptionActionInterfaceType = typeof(IRequestExceptionAction<,>).MakeGenericType(typeof(TRequest), exceptionType);
            var enumerableExceptionActionInterfaceType = typeof(IEnumerable<>).MakeGenericType(exceptionActionInterfaceType);
            actionMethodInfo = exceptionActionInterfaceType.GetMethod(nameof(IRequestExceptionAction<TRequest, Exception>.Execute));

            var actionsForException = (IEnumerable<object>)_serviceFactory.Invoke(enumerableExceptionActionInterfaceType);

            return actionsForException.ToList();
        }
    }
}
