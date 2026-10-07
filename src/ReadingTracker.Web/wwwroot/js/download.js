// Saving text the page made as a file in the reader's downloads, without a trip to a server:
// a link to the text, clicked once and let go.
export function save(fileName, text, type) {
    const url = URL.createObjectURL(new Blob([text], { type }));
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    document.body.append(link);
    link.click();
    link.remove();
    // Let go of the text only once the browser has surely read it: Safari on iOS reads it after
    // the click returns, and fails the download if the URL is already gone.
    setTimeout(() => URL.revokeObjectURL(url), 40_000);
}
