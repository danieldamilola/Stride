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
    var article = document.querySelector('article');
    var root = article ? article.cloneNode(true) : document.body.cloneNode(true);
    // Stub: return outerHTML. Real version will strip and sanitize here.
    var html = root.innerHTML || '';
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
