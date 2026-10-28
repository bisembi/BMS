export function saveBlobDownload(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.hidden = true;
    document.body.append(anchor);
    try {
        anchor.click();
    }
    finally {
        anchor.remove();
        URL.revokeObjectURL(url);
    }
}
