/**
* This stored procedure provides the following functionality:
* - Completely delete the document and all of its histories.
* 
* @constructor
* @param {string} resourceTypeName - The resource type name.
* @param {string} resourceId - The resource id.
*/

function hardDelete(resourceTypeName, resourceId) {
    const collection = getContext().getCollection();
    const collectionLink = collection.getSelfLink();
    const response = getContext().getResponse();

    // Validate input
    if (!resourceTypeName) {
        throwArgumentValidationError("The resourceTypeName is undefined or null");
    }

    if (!resourceId) {
        throwArgumentValidationError("The resourceId is undefined or null");
    }

    let deletedResourceIdList = new Array();

    tryQueryAndHardDelete();

    function tryQueryAndHardDelete() {
        // Find the resource and all of its history.
        let query = {
            query: "SELECT r._self, r.id FROM ROOT r WHERE r.resourceTypeName = @resourceTypeName AND r.resourceId = @resourceId",
            parameters: [{ name: "@resourceTypeName", value: resourceTypeName }, { name: "@resourceId", value: resourceId }]
        };

        let isQueryAccepted = collection.queryDocuments(
            collectionLink,
            query,
            {},
            function (err, documents, responseOptions) {
                if (err) {
                    throw err;
                }

                if (documents.length > 0) {
                    // Delete the documents.
                    tryHardDelete(documents);
                } else {
                    // There is no more documents so we are finished.
                    response.setBody(deletedResourceIdList);
                }
            });

        if (!isQueryAccepted) {
            // We ran out of time.
            throwTooManyRequestsError();
        }
    }

    function tryHardDelete(documents) {
        if (documents.length > 0) {
            deletedResourceIdList.push(documents[0].id);

            // Delete the first item.
            var isAccepted = collection.deleteDocument(
                documents[0]._self,
                {},
                function (err, responseOptions) {
                    if (err) {
                        throw err;
                    }

                    // Successfully deleted the item, continue deleting.
                    documents.shift();
                    tryHardDelete(documents);
                });

            if (!isAccepted) {
                // We ran out of time.
                throwTooManyRequestsError();
            }
        } else {
            // If the documents are empty, query for more documents.
            tryQueryAndHardDelete();
        }
    }

    function throwArgumentValidationError(message) {
        throw new Error(ErrorCodes.BadRequest, message);
    }

    function throwTooManyRequestsError() {
        throw new Error(ErrorCodes.RequestEntityTooLarge, `The request could not be completed.`);
    }
}
