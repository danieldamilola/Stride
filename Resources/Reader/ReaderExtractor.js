// ReaderExtractor.js - scaffold stub.
// Real implementation clones the document, strips nav, aside, script, style, hidden nodes,
// ad selectors, and returns ArticleResult JSON via WebMessageRouter or ExecuteScriptAsync.
// This stub exists so the embedded resource pipeline and template rendering can be wired
// without shipping real heuristics before step 2.

(function () {
  function isReadable() {
    // Placeholder heuristic: treat pages with at least 3 paragraphs and 500 chars as readable
    var paras = document.querySelectorAll('p');
    var len = 0;
    for (var i = 0; i < paras.length; i++) len += (paras[i].textContent || '').length;
    return paras.length >= 3 && len >= 500;
  }

  function extract() {
    var title = document.title || '';
    var byline = '';
    var meta = document.querySelector('meta[name="author"]');
    if (meta) byline = meta.getAttribute('content') || '';

    // Prefer the most likely article container. Wikipedia uses #mw-content-text and .mw-parser-output.
    var candidates = [
      '#mw-content-text',
      '.mw-parser-output',
      'main',
      '[role="main"]',
      'article',
      '#content',
      '#bodyContent'
    ];
    var root = null;
    for (var c = 0; c < candidates.length; c++) {
      var el = document.querySelector(candidates[c]);
      if (el && (el.textContent || '').trim().length > 400) { root = el.cloneNode(true); break; }
    }
    if (!root) {
      var article = document.querySelector('article');
      root = article ? article.cloneNode(true) : document.body.cloneNode(true);
    }

    // Remove chrome that should never appear in reader, including Wikipedia nav and appearance panels.
    var dropSelectors = [
      'nav', 'aside', 'header', 'footer', 'script', 'style', 'noscript', 'template',
      '#mw-navigation', '#mw-panel', '#p-logo', '.vector-header', '.vector-sidebar',
      '.vector-page-toolbar', '.mw-indicators', '.infobox', '.navbox', '.vertical-navbox',
      '.sidebar', '.toc', '#toc', '.mw-editsection', '.noprint', '.catlinks',
      '[role="navigation"]', '[role="banner"]', '[role="contentinfo"]', 'form'
    ];
    for (var i = 0; i < dropSelectors.length; i++) {
      var nodes = root.querySelectorAll(dropSelectors[i]);
      for (var n = 0; n < nodes.length; n++) nodes[n].remove();
    }

    // Remove hidden nodes and ad selectors
    var all = root.querySelectorAll('*');
    for (var k = all.length - 1; k >= 0; k--) {
      var el = all[k];
      var style = window.getComputedStyle ? window.getComputedStyle(el) : null;
      if (style && (style.display === 'none' || style.visibility === 'hidden')) el.remove();
    }

    var html = root.innerHTML || '';
    // Trim excessive leading nav text that slipped through, keep first real heading or paragraph
    return JSON.stringify({
      title: title,
      byline: byline,
      excerpt: '',
      contentHtml: html,
      siteName: location.hostname,
      length: html.length,
      isReadable: isReadable()
    });
  }

  // Expose for ExecuteScriptAsync callers and for WebMessageRouter if later wired
  window.__strideReaderExtract = extract;
  window.__strideReaderIsReadable = isReadable;
})();
