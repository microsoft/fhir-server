Notes on subscription:
- Supports empty, id only, and full resource notifications for resource update
- Supports rest hooks
- Currently needs a server restart for new subscriptions/subscription topics
- Doesn't send subscription status
	-Doesn't support event notifications (not in tooling)
- In subscription topic, uses the description field instead of resource type to determine which resource the topic works on
- Doesn't add included resources
- Doesn't honor query criteria or filters
- Doesn't honor end date
- Doesn't do handshakes
- Doesn't do auth
- Subscriptions should start in 'requested' status and have the server change it to active status once it performs a successful handshake
	
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
