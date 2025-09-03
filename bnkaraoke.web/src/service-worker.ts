import { precacheAndRoute, createHandlerBoundToURL } from 'workbox-precaching';
import { registerRoute } from 'workbox-routing';

declare const self: ServiceWorkerGlobalScope & { __WB_MANIFEST: any };

precacheAndRoute(self.__WB_MANIFEST);

// Fallback to index.html for navigation requests
const handler = createHandlerBoundToURL('/');
registerRoute(({ request }) => request.mode === 'navigate', handler);
