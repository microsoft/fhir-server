Notes on subscription:
- Supports empty, id only, and full resource notifications for resource update
- Supports rest hooks
- Currently looks up subscriptions and subscription topics on every potential trigger. This is due to the subscription listener being a scopped instead of singleton service.
- Doesn't include the subscription status field 'events since subscription start count'
- Doesn't support event notifications (not in tooling)
- In subscription topic, uses the description field instead of resource type to determine which resource the topic works on (tooling limitiation)
- Doesn't add included resources
- Doesn't honor query criteria or filters
- Doesn't honor end date
- Doesn't do handshakes
- Doesn't do auth
- Subscriptions should start in 'requested' status and have the server change it to 'active' status once it performs a successful handshake
- Doesn't allow for resend of failed notifications
	
References:
- Reference implimentation: https://subscriptions.argo.run/
- Conectathon page: https://confluence.hl7.org/display/FHIR/2021-09+Subscriptions
- R4B pages:
	- http://build.fhir.org/branches/R4B/subscription.html
	- http://build.fhir.org/branches/R4B/subscriptiontopic.html
	- http://build.fhir.org/branches/R4B/subscriptionstatus.html
- R5 current pages:
	- http://hl7.org/fhir/2021May/subscription.html
	- http://hl7.org/fhir/2021May/subscriptiontopic.html
	- http://hl7.org/fhir/2021May/subscriptionstatus.html


Open Questions:
- For subscription status
    - Where is the persisted data stored? The subscription status is a resource generated at runtime, it is not persisted in whole. The only part that needs to be persisted is the "eventsSeinceSubscriptionStart" property.
