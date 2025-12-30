// Push Notifications JS Interop for Blazor WASM
// Handles browser push subscription management

/**
 * Check if push notifications are supported by the browser
 * @returns {boolean} True if supported
 */
export function isPushSupported() {
    return 'serviceWorker' in navigator && 'PushManager' in window;
}

/**
 * Get the current notification permission state
 * @returns {string} 'granted', 'denied', or 'default'
 */
export function getPermissionState() {
    if (!('Notification' in window)) {
        return 'unsupported';
    }
    return Notification.permission;
}

/**
 * Request notification permission from the user
 * @returns {Promise<string>} Permission state after request
 */
export async function requestPermission() {
    if (!('Notification' in window)) {
        return 'unsupported';
    }
    return await Notification.requestPermission();
}

/**
 * Register service worker and subscribe to push notifications
 * @param {string} vapidPublicKey - VAPID public key for authentication
 * @returns {Promise<object|null>} Subscription data or null if failed
 */
export async function subscribeToPush(vapidPublicKey) {
    if (!isPushSupported()) {
        console.warn('Push notifications not supported');
        return null;
    }

    try {
        // Register service worker
        const registration = await navigator.serviceWorker.register('/service-worker.js');
        await navigator.serviceWorker.ready;

        // Check for existing subscription
        let subscription = await registration.pushManager.getSubscription();

        if (!subscription) {
            // Create new subscription
            const applicationServerKey = urlBase64ToUint8Array(vapidPublicKey);

            subscription = await registration.pushManager.subscribe({
                userVisibleOnly: true,
                applicationServerKey: applicationServerKey
            });
        }

        // Return subscription data for Blazor to save
        return {
            endpoint: subscription.endpoint,
            p256dh: arrayBufferToBase64(subscription.getKey('p256dh')),
            auth: arrayBufferToBase64(subscription.getKey('auth'))
        };
    } catch (error) {
        console.error('Failed to subscribe to push:', error);
        return null;
    }
}

/**
 * Unsubscribe from push notifications
 * @returns {Promise<boolean>} True if successfully unsubscribed
 */
export async function unsubscribeFromPush() {
    try {
        const registration = await navigator.serviceWorker.getRegistration();
        if (!registration) {
            return false;
        }

        const subscription = await registration.pushManager.getSubscription();
        if (!subscription) {
            return false;
        }

        await subscription.unsubscribe();
        return true;
    } catch (error) {
        console.error('Failed to unsubscribe from push:', error);
        return false;
    }
}

/**
 * Get current subscription endpoint (for checking if already subscribed)
 * @returns {Promise<string|null>} Endpoint URL or null if not subscribed
 */
export async function getCurrentSubscriptionEndpoint() {
    try {
        const registration = await navigator.serviceWorker.getRegistration();
        if (!registration) {
            return null;
        }

        const subscription = await registration.pushManager.getSubscription();
        return subscription?.endpoint || null;
    } catch (error) {
        console.error('Failed to get subscription:', error);
        return null;
    }
}

// Helper: Convert URL-safe base64 to Uint8Array
function urlBase64ToUint8Array(base64String) {
    const padding = '='.repeat((4 - base64String.length % 4) % 4);
    const base64 = (base64String + padding)
        .replace(/-/g, '+')
        .replace(/_/g, '/');

    const rawData = window.atob(base64);
    const outputArray = new Uint8Array(rawData.length);

    for (let i = 0; i < rawData.length; ++i) {
        outputArray[i] = rawData.charCodeAt(i);
    }
    return outputArray;
}

// Helper: Convert ArrayBuffer to base64
function arrayBufferToBase64(buffer) {
    if (!buffer) return '';
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.byteLength; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    return window.btoa(binary);
}
