// Spur Browser - YouTube Unhook
// Injects CSS rules and JS to hide distracting YouTube UI elements.
(function() {
    'use strict';

    // Inject CSS rules
    const css = `{{CSS}}`;
    if (css) {
        const style = document.createElement('style');
        style.textContent = css;
        document.head.appendChild(style);
    }

    // Execute JS-based hiding
    {{JS}}
})();
