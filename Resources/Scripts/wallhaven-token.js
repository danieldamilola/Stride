(function() {
    // Host gate only, no secret. The previous version published the per-session IPC token as
    // window.__T on the allowlisted origins, which meant any script running on wallhaven.cc,
    // including third-party ad code or an XSS on that site, could read the session token and
    // then drive the whole privileged route table. The bridge authorises the download request
    // by checking the message source origin instead, so nothing secret needs to reach the page.
    var allowedHosts = [__ALLOWED_HOSTS__];
    if (allowedHosts.indexOf(window.location.hostname) !== -1) {
        window.__STRIDE_WALLHAVEN = true;
    }
})();