/**
* This stored procedure can be used to replace an existing document
*
* @constructor
* @param {any} doc - The CosmosResourceWrapper to save
* @param {string} matchVersionId - required etag to match against when replacing an existing document
*/
function replaceSingleResource(doc, matchVersionId) {
    const collection = getContext().getCollection();
    const collectionLink = collection.getSelfLink();
    const response = getContext().getResponse();


    // Validate input
    if (!doc) {
        throwArgumentValidationError("The document is undefined or null.");
    }

    if (doc instanceof Array) {
        throwArgumentValidationError("Input should not be an array.");
    }

    if (stringIsNullOrEmpty(matchVersionId)) {
        throwArgumentValidationError("Invalid VersionId provided.");
    }

    let query = {
        query: "select * from root r where r.id = @id",
        parameters: [{ name: "@id", value: doc.id }]
    };

    let isQueryAccepted = collection.queryDocuments(
        collection.getSelfLink(),
        query,
        function (err, documents) {
            if (err) {
                throw err;
            }

            let document = documents.length === 0 ? null : documents[0];

            if (document === null ||
                doc.isDeleted && document.isDeleted) { // don't create another version if already deleted
                throw new Error(ErrorCodes.NotFound, "Document not found.");
            }

            // Check that the version passed in matches with current document version
            if (document.version !== matchVersionId) {
                throwPreconditionFailedError();
            }

            // Replace the primary record
            doc.version = matchVersionId;
            let selfLink = document._self;
            let isAccepted = collection.replaceDocument(selfLink, doc, { disableAutomaticIdGeneration: true, etag: document._etag }, replacePrimaryCallback);

            if (!isAccepted) {
                throwRequestNotQueuedError();
            }
        });

    if (!isQueryAccepted) {
        throwRequestNotQueuedError();
    }

    function stringIsNullOrEmpty(str) {
        return str === undefined || str === null || str === "";
    }

    function replacePrimaryCallback(err, createdDoc) {
        if (err) {
            if (err.number === ErrorCodes.Conflict ||
                err.number === ErrorCodes.PreconditionFailed) {
                throw createPreconditionFailedError();
            } else {
                throw err;
            }
        }

        response.setBody(createdDoc);
    }

    function throwRequestNotQueuedError() {
        throw new Error(429, "Request could not be queued.");
    }

    function throwPreconditionFailedError() {
        throw new Error(ErrorCodes.PreconditionFailed, "One of the specified pre-conditions is not met.");
    }

    function throwArgumentValidationError(message) {
        throw new Error(ErrorCodes.BadRequest, message);
    }
}
