/**
* This stored procedure provides the following functionality:
* - Completely deletes a role and all of its histories.
* 
* @constructor
* @param {string} roleName - Role to be deleted
*/

function hardDelete(roleName) {
    const collection = getContext().getCollection();
    const response = getContext().getResponse();
    const collectionLink = collection.getSelfLink();

    const errorMessages = {
        RoleNameNull: `${ErrorCodes.BadRequest}: The roleName is undefined or null.`,
        CannotDeleteAllRows: `${ErrorCodes.BadRequest}: Cannot delete all roles.`,
        RequestEntityTooLarge: `${ErrorCodes.RequestEntityTooLarge}: The request could not be completed.`,
        NotFound: `${ErrorCodes.NotFound}: There are no entries in db.`
    };

    // Validate input
    if (!roleName) {
        throw new Error(errorMessages.RoleNameNull);
    }

    let deletedResourceIdList = new Array();


    validate();

    function validate() {
        // count all resources
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

                if (!documents || !documents.length) { }
                else {
                    doc = documents[0];
                    if (doc.docCount === 0) {
                        throw new Error(errorMessages.NotFound);
                    }
                    else if (doc.docCount === 1) {
                        throw new Error(errorMessages.CannotDeleteAllRows);
                    }

                    tryQueryAndHardDelete();
                }                
            });

        if (!isQueryAccepted) {
            // We ran out of time.
            throw new Error(errorMessages.CannotDeleteAllRows);
        }

    }

    function tryQueryAndHardDelete() {
        // Find the resource and all of its history.
        let query = {
            query: "SELECT r._self, r.id FROM ROOT r WHERE r.id = @roleName",
            parameters: [{ name: "@roleName", value: roleName }]
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
                    tryHardDelete(documents);
                } else {
                    // There is no more documents so we are finished.
                    response.setBody(deletedResourceIdList);
                }
            });

        if (!isQueryAccepted) {
            // We ran out of time.
            throw new Error(errorMessages.RequestEntityTooLarge);
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
                throw new Error(errorMessages.RequestEntityTooLarge);
            }
        } else {
            // If the documents are empty, query for more documents.
            tryQueryAndHardDelete();
        }
    }
}
