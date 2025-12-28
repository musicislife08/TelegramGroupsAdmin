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

// Get user's IANA timezone (e.g., "America/Los_Angeles", "America/New_York")
window.getUserTimeZone = () => {
    try {
        return Intl.DateTimeFormat().resolvedOptions().timeZone;
    } catch (error) {
        console.warn('Failed to detect timezone, defaulting to UTC:', error);
        return 'UTC';
    }
};

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

// Scroll messages container to approximate position of message
// This triggers MudVirtualize to render items that are currently outside viewport
window.scrollMessageContainer = (messageIndex, totalCount) => {
    const container = document.querySelector('.messages-container');
    if (!container) {
        console.warn('Messages container not found');
        return;
    }

    // Messages are displayed in reverse order (newest first at bottom)
    // So index 0 is at the bottom, and index N is at the top
    // Calculate approximate scroll position
    const estimatedItemHeight = 100; // Approximate message bubble height
    const scrollHeight = estimatedItemHeight * messageIndex;

    // Scroll from bottom (messages are flex-reversed)
    container.scrollTop = container.scrollHeight - scrollHeight - container.clientHeight / 2;
};

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

// Insert text at cursor position in a specific input/textarea element
// Used by WelcomeSystemConfig for variable chip insertion
// Takes elementId to avoid focus issues when clicking buttons
window.insertTextAtCursor = (text, elementId) => {
    // If elementId is provided, use it; otherwise fall back to active element
    let targetElement;

    if (elementId) {
        targetElement = document.getElementById(elementId);
        if (!targetElement) {
            console.warn(`Element with ID '${elementId}' not found`);
            return;
        }
    } else {
        targetElement = document.activeElement;
        if (!targetElement || (targetElement.tagName !== 'INPUT' && targetElement.tagName !== 'TEXTAREA')) {
            console.warn('No input or textarea is currently focused');
            return;
        }
    }

    // For MudBlazor TextFields, the actual input/textarea is inside a div
    // Try to find it if we got a container element
    if (targetElement.tagName !== 'INPUT' && targetElement.tagName !== 'TEXTAREA') {
        const input = targetElement.querySelector('input, textarea');
        if (input) {
            targetElement = input;
        } else {
            console.warn('Could not find input or textarea element');
            return;
        }
    }

    const start = targetElement.selectionStart ?? targetElement.value?.length ?? 0;
    const end = targetElement.selectionEnd ?? start;
    const value = targetElement.value ?? '';

    // Insert text at cursor position
    const newValue = value.substring(0, start) + text + value.substring(end);
    targetElement.value = newValue;

    // Move cursor to end of inserted text
    const newCursorPos = start + text.length;
    targetElement.setSelectionRange(newCursorPos, newCursorPos);

    // Trigger input event to update Blazor binding
    targetElement.dispatchEvent(new Event('input', { bubbles: true }));

    // Keep focus on the element
    targetElement.focus();
};

// Capture scroll state before DOM updates (for preserving position when new messages arrive)
window.captureScrollState = (container) => {
    if (!container) {
        console.warn('[ScrollPreservation] Container element not found');
        return { scrollTop: 0, scrollHeight: 0, clientHeight: 0 };
    }

    const state = {
        scrollTop: container.scrollTop,
        scrollHeight: container.scrollHeight,
        clientHeight: container.clientHeight
    };

    return state;
};

// Restore scroll position after DOM updates (compensates for inserted content)
window.restoreScrollState = (container, previousState) => {
    if (!container || !previousState) {
        console.warn('[ScrollPreservation] Container or previous state not found');
        return;
    }

    // Calculate how much the content height changed
    const newScrollHeight = container.scrollHeight;
    const heightDifference = newScrollHeight - previousState.scrollHeight;

    if (heightDifference <= 0) {
        return;
    }

    // For flex-direction: column-reverse:
    // - scrollTop = 0 means scrolled to BOTTOM (newest messages visible)
    // - scrollTop > 0 OR scrollTop < 0 means scrolled UP (viewing older messages)
    // - CRITICAL: Some browsers use NEGATIVE scrollTop for column-reverse!
    // - New content appears at visual BOTTOM when inserted at DOM index 0
    // - When content grows, browser does NOT auto-adjust scrollTop

    // Calculate distance from bottom (works for both positive and negative scrollTop)
    // At bottom: scrollTop ≈ 0 (or very close to maxScroll in some browsers)
    const maxScroll = previousState.scrollHeight - previousState.clientHeight;
    const distanceFromBottom = Math.abs(previousState.scrollTop);
    const distanceFromMax = Math.abs(previousState.scrollTop - maxScroll);

    // User is at bottom if scrollTop is near 0 OR near maxScroll (browser-dependent)
    const wasAtBottom = distanceFromBottom <= 5 || distanceFromMax <= 5;

    if (wasAtBottom) {
        // User was watching conversation - let new message appear naturally
        // Browser will show it at bottom (scrollTop stays near 0)
    } else {
        // User was reading history - browser doesn't compensate in column-reverse
        // We need to adjust scrollTop to maintain visual position
        // For NEGATIVE scrollTop (some browsers): add height difference (becomes more negative)
        // For POSITIVE scrollTop (other browsers): subtract height difference

        // Adjust based on scrollTop polarity
        let newScrollTop;
        if (previousState.scrollTop < 0) {
            // Negative scrollTop browser - ADD height to make MORE negative
            newScrollTop = previousState.scrollTop - heightDifference;
        } else {
            // Positive scrollTop browser - SUBTRACT height
            newScrollTop = Math.max(0, previousState.scrollTop - heightDifference);
        }

        // Set scroll position instantly (no smooth scrolling to avoid visual blip)
        container.style.scrollBehavior = 'auto';
        container.scrollTop = newScrollTop;

        // Restore smooth scrolling for user interactions
        setTimeout(() => {
            container.style.scrollBehavior = '';
        }, 0);
    }
};

// Restore scroll position after render cycle completes
// Uses requestAnimationFrame to ensure browser has finished layout
let scrollRestorationPending = false;

window.restoreScrollStateAfterRender = (container, previousState) => {
    // Prevent multiple simultaneous restorations
    if (scrollRestorationPending) {
        return;
    }

    scrollRestorationPending = true;

    // Use double requestAnimationFrame to ensure render + layout are complete
    requestAnimationFrame(() => {
        requestAnimationFrame(() => {
            window.restoreScrollState(container, previousState);
            scrollRestorationPending = false;
        });
    });
};

// Generate QR code from URI string and return as base64 GIF data URL
// Uses qrcode-generator library - GIF is efficient for black/white QR codes
window.generateQrCode = (uri) => {
    try {
        const qr = qrcode(0, 'M');
        qr.addData(uri);
        qr.make();
        return qr.createDataURL(10, 4); // Returns "data:image/gif;base64,..."
    } catch (error) {
        console.error('Failed to generate QR code:', error);
        return '';
    }
};
