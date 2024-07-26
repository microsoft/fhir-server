// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using MimeKit;
using System.Collections.Concurrent;
using Microsoft.Health.Fhir.Subscriptions.Models;

namespace Microsoft.Health.Fhir.Subscriptions.WebHookChannel.NotificationManager;

/// <summary>Manager for notifications.</summary>
public class NotificationManager
{
    /// <summary>The HTTP client for REST notifications.</summary>
    private HttpClient _httpClient = new();

    /// <summary>The logger.</summary>
    private ILogger _logger;

    /// <summary>The heartbeat timer.</summary>
    private Timer _heartbeatTimer = null!;

    private Timer _notificationTimer = null!;

    private record NotificationRequest
    {
        public required IFhirStore Store { get; init; }

        public required SubscriptionSendEventArgs Args { get; init; }
    }

    private ConcurrentQueue<NotificationRequest> _notificationRequestQ = new();

    /// <summary>Initializes a new instance of the <see cref="NotificationManager"/> class.</summary>
    /// <param name="fhirStoreManager">Manager for FHIR store.</param>
    /// <param name="logger">          The logger.</param>
    public NotificationManager(
        IFhirStoreManager fhirStoreManager,
        ILogger<NotificationManager> logger)
    {
        _logger = logger;
        _storeManager = fhirStoreManager;

    }

    /// <summary>Try notify via the appropriate channel type.</summary>
    /// <param name="store">The store.</param>
    /// <param name="e">    Subscription event information.</param>
    /// <returns>An asynchronous result that yields true if it succeeds, false if it fails.</returns>
    private async Task<bool> TryNotify(IFhirStore store, SubscriptionSendEventArgs e)
    {
        string contents;

        switch (e.NotificationType)
        {
            case NotificationTypeCodes.Handshake:
                {
                    contents = store.SerializeSubscriptionEvents(
                                    e.Subscription.Id,
                                    Array.Empty<long>(),
                                    "handshake",
                                    false);
                }
                break;

            case NotificationTypeCodes.Heartbeat:
                {
                    contents = store.SerializeSubscriptionEvents(
                                    e.Subscription.Id,
                                    Array.Empty<long>(),
                                    "heartbeat",
                                    false);
                }
                break;

            case NotificationTypeCodes.EventNotification:
                {
                    if (!e.NotificationEvents.Any())
                    {
                        return false;
                    }

                    contents = store.SerializeSubscriptionEvents(
                                    e.Subscription.Id,
                                    e.NotificationEvents.Select(ne => ne.EventNumber),
                                    "event-notification",
                                    false);
                }
                break;

            case NotificationTypeCodes.QueryStatus:
                throw new NotImplementedException("TryNotify <<< QueryStatus is not an implemented mode for notifications");
            //break;

            case NotificationTypeCodes.QueryEvent:
                throw new NotImplementedException("TryNotify <<< QueryEvent is not an implemented mode for notifications");
            //break;

            default:
                _logger.LogError($"TryNotify <<< Unknown notification type: {e.NotificationType}");
                return false;
        }

        bool success;

        switch (e.Subscription.ChannelCode.ToLowerInvariant())
        {
            case "rest-hook":
                success = await TryNotifyRestHook(store, e, contents);
                break;
        }

        if ((e.NotificationType == ParsedSubscription.NotificationTypeCodes.Handshake) &&
            success)
        {
            store.ChangeSubscriptionStatus(e.Subscription.Id, "active");
        }

        if (e.NotificationEvents.Any())
        {
            e.Subscription.RegisterSerializedSend(e.NotificationEvents.Select(ne => ne.EventNumber).Max(), contents);
        }
        else
        {
            e.Subscription.RegisterSerializedSend(0, contents);
        }

        return success;
    }

    /// <summary>Attempt to send a notification via REST Hook.</summary>
    /// <param name="store">   The store.</param>
    /// <param name="e">       Subscription event information.</param>
    /// <param name="contents">Serialized contents of the notification.</param>
    /// <returns>True if it succeeds, false if it fails.</returns>
    private async Task<bool> TryNotifyRestHook(
        IFhirStore store,
        SubscriptionSendEventArgs e,
        string contents)
    {
        // auto-pass any notifications to example.org
        if (e.Subscription.Endpoint.Contains("example.org", StringComparison.Ordinal))
        {
            return true;
        }

        HttpRequestMessage request = null!;

        // send the request to the endpoint
        try
        {
            // build our request
            request = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(e.Subscription.Endpoint),
                Content = new StringContent(contents, Encoding.UTF8, e.Subscription.ContentType),
            };

            // check for additional headers
            if ((e.Subscription.Parameters != null) && e.Subscription.Parameters.Any())
            {
                // add headers
                foreach ((string param, List<string> values) in e.Subscription.Parameters)
                {
                    if (string.IsNullOrEmpty(param) ||
                        (!values.Any()))
                    {
                        continue;
                    }

                    request.Headers.Add(param, values);
                }
            }

            // send our request
            HttpResponseMessage response = await _httpClient.SendAsync(request);

            // check the status code
            if ((response.StatusCode != System.Net.HttpStatusCode.OK) &&
                (response.StatusCode != System.Net.HttpStatusCode.Accepted) &&
                (response.StatusCode != System.Net.HttpStatusCode.NoContent))
            {
                // failure
                e.Subscription.RegisterError($"REST POST to {e.Subscription.Endpoint} failed: {response.StatusCode}");
                return false;
            }

            e.Subscription.LastCommunicationTicks = DateTime.Now.Ticks;

            if (e.NotificationEvents.Any())
            {
                _logger.LogInformation(
                    $" <<< Subscription/{e.Subscription.Id}" +
                    $" POST: {e.Subscription.Endpoint}" +
                    $" Events: {string.Join(',', e.NotificationEvents.Select(ne => ne.EventNumber))}");
            }
            else
            {
                _logger.LogInformation(
                    $" <<< Subscription/{e.Subscription.Id}" +
                    $" POST {e.NotificationType}: {e.Subscription.Endpoint}");
            }
        }
        catch (Exception ex)
        {
            e.Subscription.RegisterError($"REST POST {e.NotificationType} to {e.Subscription.Endpoint} failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (request != null)
            {
                request.Dispose();
            }
        }

        return true;
    }


    /// <summary>Triggered when the application host is ready to start the service.</summary>
    /// <param name="cancellationToken">Indicates that the start process has been aborted.</param>
    /// <returns>An asynchronous result.</returns>
    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting NotificationManager...");

        // traverse stores and initialize our event handlers
        foreach (IFhirStore store in _storeManager.Values)
        {
            // register our event handlers
            store.OnSubscriptionsChanged += Store_OnSubscriptionsChanged;
            store.OnSubscriptionSendEvent += Store_OnSubscriptionSendEvent;
        }

        // start our heartbeat timer
        _heartbeatTimer = new Timer(
            CheckAndSendHeartbeats,
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(2));

        _notificationTimer = new Timer(
            CheckNotificationQ,
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1));

        return Task.CompletedTask;
    }

    /// <summary>Check notification q.</summary>
    /// <exception cref="Exception">Thrown when an exception error condition occurs.</exception>
    /// <param name="state">The state.</param>
    private void CheckNotificationQ(object? state)
    {
        while (_notificationRequestQ.Any())
        {
            if ((!_notificationRequestQ.TryDequeue(out NotificationRequest? req)) ||
                (req == null))
            {
                return;
            }

            _ = TryNotify(req.Store, req.Args);
        }
    }

    /// <summary>Check and send heartbeats.</summary>
    /// <param name="state">The state.</param>
    private void CheckAndSendHeartbeats(object? state)
    {
        long currentTicks = DateTime.Now.Ticks;

        // traverse stores to check subscriptions
        foreach (IFhirStore store in _storeManager.Values)
        {
            // traverse active subscriptions
            foreach (ParsedSubscription sub in store.CurrentSubscriptions)
            {
                if ((!sub.CurrentStatus.Equals("active", StringComparison.Ordinal)) ||
                    (sub.HeartbeatSeconds == null) ||
                    (sub.HeartbeatSeconds <= 0))
                {
                    continue;
                }

                // wait one offset if the subscription is new
                if (sub.LastCommunicationTicks == 0)
                {
                    sub.LastCommunicationTicks = currentTicks + ((long)sub.HeartbeatSeconds - 1 * TimeSpan.TicksPerSecond);
                    continue;
                }

                long threshold = currentTicks - ((long)sub.HeartbeatSeconds! * TimeSpan.TicksPerSecond);

                if (sub.LastCommunicationTicks < threshold)
                {
                    sub.LastCommunicationTicks = currentTicks;

                    _notificationRequestQ.Enqueue(new()
                    {
                        Store = store,
                        Args = new()
                        {
                            Tenant = store.Config,
                            Subscription = sub,
                            NotificationType = ParsedSubscription.NotificationTypeCodes.Heartbeat,
                        },
                    });
                }
            }
        }
    }

    /// <summary>Event handler. Called by Store for on subscription events.</summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">     Subscription event information.</param>
    private void Store_OnSubscriptionSendEvent(object? sender, SubscriptionSendEventArgs e)
    {
        if (!_storeManager.ContainsKey(e.Tenant.ControllerName))
        {
            _logger.LogError($"Cannot send subscription for non-existing tenant: {e.Tenant.ControllerName}");
            return;
        }

        _notificationRequestQ.Enqueue(new()
        {
            Store = _storeManager[e.Tenant.ControllerName],
            Args = e,
        });
    }

    /// <summary>Event handler. Called by Store for on subscriptions changed events.</summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">     Subscription changed event information.</param>
    private void Store_OnSubscriptionsChanged(object? sender, SubscriptionChangedEventArgs e)
    {
        // make sure the store we want exists
        if (!_storeManager.ContainsKey(e.Tenant.ControllerName))
        {
            return;
        }

        // check for a deleted subscription
        if (!string.IsNullOrEmpty(e.RemovedSubscriptionId))
        {
            // TODO: Remove any existing heartbeat record
        }

        IFhirStore store = _storeManager[e.Tenant.ControllerName];

        // check for a new subscription
        if (e.SendHandshake)
        {
            if (e.ChangedSubscription == null)
            {
                return;
            }

            _notificationRequestQ.Enqueue(new()
            {
                Store = store,
                Args = new()
                {
                    Tenant = e.Tenant,
                    Subscription = e.ChangedSubscription!,
                    NotificationType = ParsedSubscription.NotificationTypeCodes.Handshake,
                }
            });
        }

        // check for a changed subscription
        if (e.ChangedSubscription != null)
        {
            // TODO: Check for changes to the heartbeat interval
        }
    }

    /// <summary>Triggered when the application host is performing a graceful shutdown.</summary>
    /// <param name="cancellationToken">Indicates that the shutdown process should no longer be
    ///  graceful.</param>
    /// <returns>An asynchronous result.</returns>
    Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        _heartbeatTimer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Releases the unmanaged resources used by the
    /// FhirModelComparer.Server.Services.FhirManagerService and optionally releases the managed
    /// resources.
    /// </summary>
    /// <param name="disposing">True to release both managed and unmanaged resources; false to
    ///  release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_hasDisposed)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
                _heartbeatTimer?.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _hasDisposed = true;
        }
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged
    /// resources.
    /// </summary>
    void IDisposable.Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
