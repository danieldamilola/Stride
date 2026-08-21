// ReaderExtractor.js - wrapper around Mozilla Readability.js.
// Readability.js must be loaded before this file. The C# loader concatenates both.

(function () {
  function isReadable() {
    try {
      if (typeof Readability !== 'undefined') {
        var testDoc = document.cloneNode(true);
        var testArticle = new Readability(testDoc).parse();
        return !!testArticle && (testArticle.content || '').length > 500;
      }
    } catch (e) {}
    var paras = document.querySelectorAll('p');
    var len = 0;
    for (var i = 0; i < paras.length; i++) len += (paras[i].textContent || '').length;
    return paras.length >= 3 && len >= 500;
  }

  function extract() {
    var title = document.title || '';
    var byline = '';
    var siteName = location.hostname || '';
    try {
      if (typeof Readability !== 'undefined') {
        var docClone = document.cloneNode(true);
        var reader = new Readability(docClone);
        var article = reader.parse();
        if (article) {
          return JSON.stringify({
            title: article.title || title,
            byline: article.byline || byline,
            excerpt: article.excerpt || '',
            contentHtml: article.content || '',
            siteName: article.siteName || siteName,
            length: article.length || (article.content || '').length,
            isReadable: true
          });
        }
      }
    } catch (e) {}
    var meta = document.querySelector('meta[name="author"]');
    if (meta) byline = meta.getAttribute('content') || '';
    var html = document.body ? document.body.innerHTML : '';
    return JSON.stringify({
      title: title,
      byline: byline,
      excerpt: '',
      contentHtml: html,
      siteName: siteName,
      length: html.length,
      isReadable: isReadable()
    });
  }

  window.__strideReaderExtract = extract;
  window.__strideReaderIsReadable = isReadable;
})();
