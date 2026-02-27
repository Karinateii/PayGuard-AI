/**
 * PayGuard AI – Safe JS Interop Helpers
 * Replaces all eval()-based calls with named, parameterised functions.
 * Reference these from Blazor via  JS.InvokeVoidAsync("PayGuard.xxx", ...)
 */
window.PayGuard = window.PayGuard || {};

/**
 * Download a file from a byte-array (Base64-encoded on the .NET side).
 * @param {string}  base64       Base64-encoded file content
 * @param {string}  fileName     Suggested download filename
 * @param {string}  contentType  MIME type, e.g. "text/csv" or "application/pdf"
 */
PayGuard.downloadFileFromBase64 = function (base64, fileName, contentType) {
    var byteCharacters = atob(base64);
    var byteNumbers = new Array(byteCharacters.length);
    for (var i = 0; i < byteCharacters.length; i++) {
        byteNumbers[i] = byteCharacters.charCodeAt(i);
    }
    var byteArray = new Uint8Array(byteNumbers);
    var blob = new Blob([byteArray], { type: contentType });
    var link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(link.href);
};

/**
 * Download a file using a Data-URI (e.g. "data:text/csv;base64,...").
 * @param {string}  dataUri   Full data URI
 * @param {string}  fileName  Suggested download filename
 */
PayGuard.downloadFileFromDataUri = function (dataUri, fileName) {
    var link = document.createElement('a');
    link.href = dataUri;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};

/**
 * Trigger a download by navigating to a URL (e.g. an API endpoint that
 * returns a file with Content-Disposition: attachment).
 * @param {string}  url       The URL to download from
 * @param {string}  fileName  Suggested download filename
 */
PayGuard.downloadFromUrl = function (url, fileName) {
    var link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};

/**
 * Returns true when the viewport width is below the given breakpoint.
 * @param {number} breakpoint  Width in pixels (default 960)
 * @returns {boolean}
 */
PayGuard.isMobile = function (breakpoint) {
    return window.innerWidth < (breakpoint || 960);
};
