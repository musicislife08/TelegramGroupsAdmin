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

// Format timestamp in user's local timezone with 12-hour format
window.formatLocalTimestamp = (utcTimestamp) => {
    const date = new Date(utcTimestamp);
    const now = new Date();

    // Check if same day
    const isSameDay = date.toDateString() === now.toDateString();

    // Check if yesterday
    const yesterday = new Date(now);
    yesterday.setDate(yesterday.getDate() - 1);
    const isYesterday = date.toDateString() === yesterday.toDateString();

    // Format time in 12-hour format with AM/PM
    const timeOptions = {
        hour: 'numeric',
        minute: '2-digit',
        hour12: true
    };
    const timeStr = date.toLocaleTimeString('en-US', timeOptions);

    if (isSameDay) {
        return timeStr; // Just time for today
    } else if (isYesterday) {
        return `Yesterday ${timeStr}`;
    } else if (date.getFullYear() === now.getFullYear()) {
        // Same year - show month, day, time
        const monthDay = date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
        return `${monthDay} ${timeStr}`;
    } else {
        // Different year - show full date with time
        const fullDate = date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
        return `${fullDate} ${timeStr}`;
    }
}

// Initialize timestamp formatting on page load and after Blazor updates
function initializeTimestamps() {
    // Only process timestamps that haven't been formatted yet
    const timestamps = document.querySelectorAll('.local-timestamp[data-utc]:not([data-formatted])');
    timestamps.forEach(element => {
        const utcTimestamp = element.getAttribute('data-utc');
        if (utcTimestamp) {
            element.textContent = window.formatLocalTimestamp(utcTimestamp);
            element.setAttribute('data-formatted', 'true'); // Mark as formatted to avoid reprocessing
        }
    });
}

// Run on initial load
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initializeTimestamps);
} else {
    initializeTimestamps();
}

// Debounce function to avoid excessive calls
function debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
        const later = () => {
            clearTimeout(timeout);
            func(...args);
        };
        clearTimeout(timeout);
        timeout = setTimeout(later, wait);
    };
}

// Re-run after Blazor renders (using MutationObserver for dynamic content)
// Debounced to avoid performance issues with frequent updates
const debouncedInitializeTimestamps = debounce(initializeTimestamps, 100);

const observer = new MutationObserver((mutations) => {
    // Only run if we actually added new nodes with timestamps
    let hasNewTimestamps = false;
    for (const mutation of mutations) {
        if (mutation.addedNodes.length > 0) {
            hasNewTimestamps = true;
            break;
        }
    }

    if (hasNewTimestamps) {
        debouncedInitializeTimestamps();
    }
});

// Start observing when DOM is ready
if (document.body) {
    observer.observe(document.body, { childList: true, subtree: true });
} else {
    document.addEventListener('DOMContentLoaded', () => {
        observer.observe(document.body, { childList: true, subtree: true });
    });
}

// Scroll to a specific message and highlight it
window.scrollToMessage = (messageId) => {
    // Find the message element by data-message-id attribute
    const element = document.querySelector(`[data-message-id="${messageId}"]`);

    if (!element) {
        console.warn(`Message element with ID ${messageId} not found`);
        return;
    }

    // Scroll to the element with smooth animation
    element.scrollIntoView({
        behavior: 'smooth',
        block: 'center', // Center the element vertically
        inline: 'nearest'
    });

    // Add highlight animation class
    element.classList.add('message-highlight');

    // Remove the highlight class after animation completes (2 seconds)
    setTimeout(() => {
        element.classList.remove('message-highlight');
    }, 2000);
}
