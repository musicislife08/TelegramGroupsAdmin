// Native Blazor file download using DotNetStreamReference
// More efficient than base64 encoding - uses binary streaming
window.downloadFileFromStream = async (fileName, contentStreamReference) => {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer]);
    const url = URL.createObjectURL(blob);
    const anchorElement = document.createElement('a');
    anchorElement.href = url;
    anchorElement.download = fileName ?? '';
    anchorElement.click();
    anchorElement.remove();
    URL.revokeObjectURL(url);
};

// Download file from base64 string (used for backup export)
window.downloadFile = (fileName, base64Data) => {
    const blob = base64ToBlob(base64Data, 'application/gzip');
    const url = URL.createObjectURL(blob);
    const anchorElement = document.createElement('a');
    anchorElement.href = url;
    anchorElement.download = fileName ?? '';
    anchorElement.click();
    anchorElement.remove();
    URL.revokeObjectURL(url);
};

// Helper to convert base64 to blob
function base64ToBlob(base64, contentType = '') {
    const byteCharacters = atob(base64);
    const byteArrays = [];
    for (let offset = 0; offset < byteCharacters.length; offset += 512) {
        const slice = byteCharacters.slice(offset, offset + 512);
        const byteNumbers = new Array(slice.length);
        for (let i = 0; i < slice.length; i++) {
            byteNumbers[i] = slice.charCodeAt(i);
        }
        const byteArray = new Uint8Array(byteNumbers);
        byteArrays.push(byteArray);
    }
    return new Blob(byteArrays, { type: contentType });
}

// Extract image from clipboard using Clipboard API
window.getClipboardImage = async () => {
    try {
        // Try modern Clipboard API first
        if (navigator.clipboard && navigator.clipboard.read) {
            const clipboardItems = await navigator.clipboard.read();

            for (const clipboardItem of clipboardItems) {
                for (const type of clipboardItem.types) {
                    if (type.startsWith('image/')) {
                        const blob = await clipboardItem.getType(type);

                        // Convert blob to base64 data URL
                        return new Promise((resolve, reject) => {
                            const reader = new FileReader();
                            reader.onloadend = () => resolve(reader.result);
                            reader.onerror = reject;
                            reader.readAsDataURL(blob);
                        });
                    }
                }
            }
        }
    } catch (err) {
        console.error('Failed to read clipboard:', err);
    }

    return null;
}

// Setup paste event listener for spam tester
window.setupPasteListener = (dotNetHelper) => {
    const MAX_IMAGE_SIZE = 10 * 1024 * 1024; // 10MB limit

    const handlePaste = async (event) => {
        const clipboardData = event.clipboardData || window.clipboardData;

        if (!clipboardData || !clipboardData.items) {
            return;
        }

        // Check if there's an image in the clipboard
        let imageItem = null;
        for (let i = 0; i < clipboardData.items.length; i++) {
            if (clipboardData.items[i].type.indexOf('image') !== -1) {
                imageItem = clipboardData.items[i];
                break;
            }
        }

        // Only handle paste if there's an image
        if (!imageItem) {
            return;
        }

        // We have an image - always intercept it (even in text fields)
        event.preventDefault(); // Prevent default paste behavior
        event.stopPropagation(); // Stop the event from bubbling to text inputs

        const blob = imageItem.getAsFile();

        // Check file size
        if (blob.size > MAX_IMAGE_SIZE) {
            console.error(`Image too large: ${blob.size} bytes (max ${MAX_IMAGE_SIZE} bytes)`);
            return;
        }

        // Convert blob to base64 data URL
        const reader = new FileReader();
        reader.onloadend = async () => {
            try {
                const result = reader.result;

                // Double-check result size (base64 is ~33% larger than binary)
                if (result.length > MAX_IMAGE_SIZE * 1.5) {
                    console.error(`Base64 image too large: ${result.length} characters`);
                    return;
                }

                await dotNetHelper.invokeMethodAsync('OnImagePasted', result);
            } catch (err) {
                console.error('Failed to invoke OnImagePasted:', err);
            }
        };
        reader.onerror = (err) => {
            console.error('Failed to read image:', err);
        };
        reader.readAsDataURL(blob);
    };

    document.addEventListener('paste', handlePaste);

    // Store cleanup function globally so Blazor can call it
    window.pasteListenerCleanup = () => {
        document.removeEventListener('paste', handlePaste);
        delete window.pasteListenerCleanup;
    };
}
