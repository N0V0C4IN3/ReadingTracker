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
    // The click has handed the file over by now; the URL only holds the text in memory.
    setTimeout(() => URL.revokeObjectURL(url), 0);
}
