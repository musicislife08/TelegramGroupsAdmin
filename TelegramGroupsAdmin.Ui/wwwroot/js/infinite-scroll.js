// Infinite scroll functionality using IntersectionObserver
// Detects when scroll sentinel becomes visible and triggers loading of older messages

let observer = null;
let dotNetHelper = null;
let isLoading = false;

export function initializeIntersectionObserver(sentinelElement, dotNetObjectReference) {
    // Clean up existing observer if any
    if (observer) {
        observer.disconnect();
    }

    dotNetHelper = dotNetObjectReference;
    isLoading = false;

    // Create IntersectionObserver to watch the sentinel element
    observer = new IntersectionObserver(
        (entries) => {
            entries.forEach(entry => {
                // When sentinel becomes visible and we're not already loading
                if (entry.isIntersecting && !isLoading) {
                    console.log('[InfiniteScroll] Sentinel visible, loading older messages...');
                    isLoading = true;

                    // Call the Blazor component's LoadOlderMessagesAsync method
                    dotNetHelper.invokeMethodAsync('LoadOlderMessagesAsync')
                        .then(() => {
                            console.log('[InfiniteScroll] Finished loading older messages');
                            isLoading = false;
                        })
                        .catch(error => {
                            console.error('[InfiniteScroll] Error loading older messages:', error);
                            isLoading = false;
                        });
                }
            });
        },
        {
            // Trigger when sentinel is within 200px of viewport
            rootMargin: '200px',
            threshold: 0.1
        }
    );

    // Start observing the sentinel element
    if (sentinelElement) {
        observer.observe(sentinelElement);
        console.log('[InfiniteScroll] Observer initialized and watching sentinel');
    } else {
        console.warn('[InfiniteScroll] Sentinel element not found');
    }
}

export function dispose() {
    if (observer) {
        observer.disconnect();
        observer = null;
    }
    dotNetHelper = null;
    isLoading = false;
    console.log('[InfiniteScroll] Observer disposed');
}
