/**
* This stored procedure provides the following functionality:
* - Completely delete the identity provider document.
* 
* @constructor
* @param {string} id - The document id.
* @param {string} eTag - - optional etag to match against when deleting an existing document.
*/

function hardDelete(id, eTag) {
    const collection = getContext().getCollection();
    const collectionLink = collection.getSelfLink();
    const response = getContext().getResponse();

    const errorMessages = {
        IdNull: `${ErrorCodes.BadRequest}: The id is undefined or null.`,
        RequestEntityTooLarge: `${ErrorCodes.RequestEntityTooLarge}: The request could not be completed.`,
        NotFound: `${ErrorCodes.NotFound}: The id is not found.`,
        LastIdentity: `${ErrorCodes.BadRequest}: Cannot delete as only one identity provider exists.`,
        PreconditionFailed: `${ErrorCodes.PreconditionFailed}: One of the specified pre-condition is not met.`
    };

    // Validate input
    if (!id) {
        throw new Error(errorMessages.IdNull);
    }

    let deletedResourceIdList = new Array();

    getIdentityProviderCountAndDelete(eTag);

    function getIdentityProviderCountAndDelete(eTag) {

        var doc;
        // Find the resource and all of its history.
        let query = {
            query: "SELECT COUNT(r.id) docCount FROM ROOT r"
        };

        let isQueryAccepted = collection.queryDocuments(
            collection.getSelfLink(),
            query,
            {},
            function (err, documents, responseOptions) {
                if (err) {
                    throw err;
                }

                if (!documents || !documents.length) {
                    return;
                } else {
                    doc = documents[0];
                    if (doc.docCount === 0) {
                        throw new Error(errorMessages.NotFound);
                    }
                    else if (doc.docCount === 1) {
                        throw new Error(errorMessages.LastIdentity);
                    }
                    tryQueryAndHardDelete(eTag);
                }
            });

        if (!isQueryAccepted) {
            // We ran out of time.
            throw new Error(errorMessage.requestEntityTooLarge);
        }

    }

    function tryQueryAndHardDelete(eTag) {
        // Find the resource and all of its history.
        let query = {
            query: "SELECT * FROM ROOT r WHERE r.id = @id",
            parameters: [{ name: "@id", value: id }]
        };

        let isQueryAccepted = collection.queryDocuments(
            collection.getSelfLink(),
            query,
            {},
            function (err, documents, responseOptions) {
                if (err) {
                    throw err;
                }

                if (documents.length > 0) {
                    // Delete the documents.
                    tryHardDelete(documents, eTag);
                    deletedResourceIdList.push(documents[0].id);
                    response.setBody(deletedResourceIdList);
                }
            });

        if (!isQueryAccepted) {
            // We ran out of time.
            throw new Error(errorMessage.requestEntityTooLarge);
        }
    }

    function tryHardDelete(documents, eTag) {
        if (documents.length > 0) {
            let documentVersion = documents[0]._etag;
            // If an eTag was passed in, check it matches.
            if (!stringIsNullOrEmpty(eTag) && !stringIsNullOrEmpty(documentVersion)) {
                if (documentVersion !== eTag) {
                    throw new Error(errorMessages.PreconditionFailed);
                }
            }
            // Delete the first item.
            var isAccepted = collection.deleteDocument(
                documents[0]._self,
                {},
                function (err, responseOptions) {
                    if (err) {
                        throw err;
                    }

                    // Successfully deleted the item.
                });

            if (!isAccepted) {
                // We ran out of time.
                throw new Error(errorMessages.RequestEntityTooLarge);
            }
        }
    }

    function stringIsNullOrEmpty(str) {
        return str === undefined || str === null || str === "";
    }
}
