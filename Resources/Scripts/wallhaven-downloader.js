(function () {
    if (!window.location.hostname.includes('wallhaven.cc')) return;

    function init() {
        // Add CSS for hover effect
        const style = document.createElement('style');
        style.textContent = `
            .stride-dl-btn { opacity: 0; transition: opacity 0.15s ease-in-out; }
            figure.thumb:hover .stride-dl-btn { opacity: 1; }
        `;
        document.head.appendChild(style);



        function getFullResUrl(element) {
            const img = element.querySelector('img');
            if (!img) return null;

            const src = img.getAttribute('data-src') || img.getAttribute('src') || '';
            if (!src) return null;

            let url = src
                .replace('//th.wallhaven.cc', '//w.wallhaven.cc')
                .replace(/\/small\//, '/full/')
                .replace(/\/large\//, '/full/')
                .replace(/(\/full\/[a-z0-9]+\/)([a-z0-9]+\.)/, '$1wallhaven-$2');

            const isPng = element.querySelector('.png') !== null;
            if (isPng) {
                url = url.replace(/\.jpg$/, '.png').replace(/\.jpeg$/, '.png');
            }

            return url;
        }

        function processThumb(element) {
            if (element.querySelector('.wbs_dl')) return;

            const downloadUrl = getFullResUrl(element);
            if (!downloadUrl) return;

            const downloadDiv = document.createElement('div');
            downloadDiv.className = 'wbs_dl wbs_unsafe stride-dl-btn';
            
            downloadDiv.style.position = 'absolute';
            downloadDiv.style.top = '10px';
            downloadDiv.style.left = '10px';
            downloadDiv.style.zIndex = '999';

            const downloadAnchor = document.createElement('a');
            downloadAnchor.className = 'icon-download';
            downloadAnchor.href = downloadUrl;
            downloadAnchor.title = 'Download via Stride';
            
            downloadAnchor.innerHTML = `<svg viewBox="0 0 24 24" width="20" height="20" style="background: rgba(0,0,0,0.6); border-radius: 4px; padding: 4px;"><path fill="none" stroke="white" stroke-width="2" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4"/></svg>`;

            downloadAnchor.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();

                if (window.__T) {
                    window.chrome.webview.postMessage(window.__T + 'download-request:' + downloadUrl);
                } else {
                    window.open(downloadUrl, '_blank');
                }

                downloadAnchor.innerHTML = `<svg viewBox="0 0 24 24" width="20" height="20" style="background: rgba(34,197,94,0.8); border-radius: 4px; padding: 4px;"><path fill="none" stroke="white" stroke-width="2" d="M5 13l4 4L19 7"/></svg>`;
            });

            downloadDiv.appendChild(downloadAnchor);
            element.appendChild(downloadDiv);
            
            element.style.position = 'relative';
        }

        function processAll() {
            document.querySelectorAll('figure.thumb').forEach(processThumb);
        }

        const observer = new MutationObserver(processAll);
        observer.observe(document.body, { childList: true, subtree: true });

        processAll();
        setTimeout(processAll, 500);
        setTimeout(processAll, 1500);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
