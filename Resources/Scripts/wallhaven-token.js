(function() {
    var allowedHosts = [__ALLOWED_HOSTS__];
    if (allowedHosts.indexOf(window.location.hostname) !== -1) {
        window.__T = '__IPC_TOKEN__:';
    }
})();