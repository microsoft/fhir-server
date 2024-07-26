namespace Microsoft.Health.Fhir.Subscriptions.Models
{        
    public enum NotificationTypeCodes
    {
        /// The status was generated as part of the setup or verification of a communications channel.
        Handshake,

        /// The status was generated to perform a heartbeat notification to the subscriber.
        Heartbeat,

        /// The status was generated for an event to the subscriber.
        EventNotification,
    }

}