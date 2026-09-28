// Caches the app shell (framework files, CSS, fonts, icons) so the PWA starts instantly and
// installs on Android. API calls and uploaded photos always go to the network.
// See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff2$/, /\.png$/, /\.jpe?g$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];
const networkOnly = [ /^\/api\//, /^\/images\// ];

const base = "/";
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
    // A new version takes over on the next launch instead of waiting for every tab to close.
    self.skipWaiting();
}

async function onActivate(event) {
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    const url = new URL(event.request.url);
    if (event.request.method !== 'GET' || url.origin !== self.origin || networkOnly.some(p => p.test(url.pathname))) {
        return fetch(event.request);
    }

    // Navigations (e.g. /soap/recipes/…) are served the cached app shell.
    const shouldServeIndexHtml = event.request.mode === 'navigate'
        && !manifestUrlList.some(u => u === event.request.url);

    const request = shouldServeIndexHtml ? 'index.html' : event.request;
    const cache = await caches.open(cacheName);
    return (await cache.match(request)) || fetch(event.request);
}
