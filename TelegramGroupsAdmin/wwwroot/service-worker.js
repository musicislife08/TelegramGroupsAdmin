// Service Worker for Web Push Notifications
// This runs in a separate thread and can receive push messages even when the app is not open

// Configuration constants
const DEFAULT_TITLE = 'TelegramGroupsAdmin';
const DEFAULT_ICON = '/icon-192.png';
const BADGE_ICON = '/icon-72.png';
const VIBRATION_PATTERN = [100, 50, 100];  // Short-pause-short vibration

self.addEventListener('install', event => {
    // Activate immediately without waiting
    self.skipWaiting();
});

self.addEventListener('activate', event => {
    // Claim all clients immediately
    event.waitUntil(self.clients.claim());
});

// Handle incoming push notifications
self.addEventListener('push', event => {
    if (!event.data) {
        console.warn('Push event received but no data');
        return;
    }

    let data;
    try {
        data = event.data.json();
    } catch (e) {
        // Fallback for plain text messages
        data = {
            title: 'Notification',
            body: event.data.text()
        };
    }

    const title = data.title || DEFAULT_TITLE;
    const options = {
        body: data.body || '',
        icon: data.icon || DEFAULT_ICON,
        badge: BADGE_ICON,
        tag: data.tag || 'default',
        data: {
            url: data.url || '/'
        },
        vibrate: VIBRATION_PATTERN,
        requireInteraction: data.requireInteraction || false
    };

    event.waitUntil(
        self.registration.showNotification(title, options)
    );
});

// Handle notification click - just focus existing window or open app root
// TODO: Issue #113 - Add context-specific URL navigation per notification type
self.addEventListener('notificationclick', event => {
    event.notification.close();

    event.waitUntil(
        self.clients.matchAll({ type: 'window', includeUncontrolled: true })
            .then(windowClients => {
                // If a window is already open, just focus it (no navigation)
                for (const client of windowClients) {
                    if (client.url && client.url.includes(self.location.origin)) {
                        return client.focus();
                    }
                }
                // Otherwise open the app root
                return self.clients.openWindow('/');
            })
    );
});

// Handle notification close (for analytics if needed)
self.addEventListener('notificationclose', event => {
    // Could send analytics here if needed
    console.log('Notification closed:', event.notification.tag);
});
