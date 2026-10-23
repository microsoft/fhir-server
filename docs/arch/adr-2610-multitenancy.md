## MVP
-	Multiple Databases (DB), one Elastic Pool (EP) (see if we can get EP connection strings)
-	Separate out common search parameters (spec defined ones) so that we don’t need them in the cache. This will remove the ability to disable built in search parameters.
-	Unify caching logic. All tenant specific caches should be moved to a single cache management class. A tenant manager will then hold a dictionary of these cache managers.
-	Multitenant watchdogs. One instance of a watchdog need to run against all tenants simultaneously to avoid favoring one tenant.
-	Multitenant background jobs. Each instance has a limited number of threads for each background job type. It should cycle through the tenants, picking up jobs in a round robin pattern to avoid having one tenant hog all the background job slots.
-	Multitenant API requests. The tenant id will be part of the url. The API controllers need to be able to extract the id and add it to the context. This may be possible to do with a middleware to avoid modifying all the existing controllers.
-	Multitenant SQL requests. The SQL server services need to pull the tenant id out of the context and send the request to the correct database.

## Stories
Description (Pre-requisite)
A.	Create cache management class v1. For this version it only needs to return built in search parameters, it should not access the DB yet. It will also return an empty list of profiles and other system resources. 
B.	Create tenant management class. This will include adding a tenant configuration section to appsettings that will contain a list of tenants. At this time the configuration only needs to contain a tenant name and id (which will be the database name). (A)
C.	Enable SQL connections to work with EP connection strings. 
D.	Enable SQL to connect to tenant specific DBs. (B, C)
E.	Enable watchdogs to run on multiple tenants. (D)
F.	Enable background jobs to run on multiple tenants. (D)
G.	Create tenant catching middleware. This will add the tenant of the current request to the request context and strip it from the URL so the current controllers still work. (B)
H.	Enable SQL to connect to the DB of the request’s tenant. (D, G)
I.	Enable requests to use the cache of the request’s tenant. (G)
J.	Enhance cache management. Have it pull from tenant specific DBs so that it has correct SP and other system resources. (D)
