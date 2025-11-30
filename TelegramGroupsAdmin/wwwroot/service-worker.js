// Service Worker for Web Push Notifications
// This runs in a separate thread and can receive push messages even when the app is not open

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

    const title = data.title || 'TelegramGroupsAdmin';
    const options = {
        body: data.body || '',
        icon: data.icon || '/icon-192.png',
        badge: '/icon-72.png',
        tag: data.tag || 'default',
        data: {
            url: data.url || '/'
        },
        // Vibration pattern for mobile devices
        vibrate: [100, 50, 100],
        // Keep notification visible until user interacts
        requireInteraction: data.requireInteraction || false
    };

    event.waitUntil(
        self.registration.showNotification(title, options)
    );
});

// Handle notification click
self.addEventListener('notificationclick', event => {
    event.notification.close();

    const urlToOpen = event.notification.data?.url || '/';

    event.waitUntil(
        // Check if a window is already open
        self.clients.matchAll({ type: 'window', includeUncontrolled: true })
            .then(windowClients => {
                // If a window is already open, focus it and navigate
                for (const client of windowClients) {
                    if (client.url.includes(self.location.origin)) {
                        client.focus();
                        return client.navigate(urlToOpen);
                    }
                }
                // Otherwise open a new window
                return self.clients.openWindow(urlToOpen);
            })
    );
});

// Handle notification close (for analytics if needed)
self.addEventListener('notificationclose', event => {
    // Could send analytics here if needed
    console.log('Notification closed:', event.notification.tag);
});
