function updateUnsupportedSearchParameters() {
    var collection = getContext().getCollection();

    let updatedIdList = new Array();

    var isAccepted = collection.queryDocuments(
        collection.getSelfLink(),
        `SELECT * FROM root c WHERE c.partitionKey = "__searchparameterstatus__" AND c.status = 'Disabled'`,

    function (err, feed, options) {
        if (err) throw err;
        var response = getContext().getResponse();

        if (!feed || !feed.length) {
            
            response.setBody('no docs found');
        }
        else {

            tryUpdate(feed);

        }
    });

    if (!isAccepted) throw new Error('The query was not accepted by the server.');

    function tryUpdate(documents) {
        if (documents.length > 0) {
            updatedIdList.push(documents[0].id);
            documents[0].status = "Unsupported";

            // Replace the first item.
            var isAccepted = collection.replaceDocument(
                documents[0]._self,
                documents[0],
                { etag: documents[0]._etag },
                function (err, responseOptions) {
                    if (err) {
                        throw err;
                    }

                    // Successful, shift the current document and continue
                    documents.shift();
                    tryUpdate(documents);
                });

            if (!isAccepted) {
                // We ran out of time.
                throwTooManyRequestsError();
            }
        }
        else{
            getContext().getResponse().setBody("Modified "+ updatedIdList.length + " documents");
        }
    }

    function throwArgumentValidationError(message) {
        throw new Error(ErrorCodes.BadRequest, message);
    }

    function throwTooManyRequestsError() {
        throw new Error(ErrorCodes.RequestEntityTooLarge, `The request could not be completed.`);
    }
}
