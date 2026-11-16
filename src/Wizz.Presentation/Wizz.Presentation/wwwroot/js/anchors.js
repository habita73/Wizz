// التمرير إلى قسم محدد بعد رسم المحتوى.
// Blazor يرسم بعض الصفحات بعد تحميل المستند، فلا يجد المتصفح الهدف عند فتح رابط فيه #قسم.
(function () {
  'use strict';

  function scrollToHash() {
    var hash = window.location.hash;
    if (!hash || hash.length < 2) {
      return;
    }
    var id;
    try {
      id = decodeURIComponent(hash.slice(1));
    } catch (e) {
      return;
    }
    var target = document.getElementById(id);
    if (target) {
      target.scrollIntoView({ block: 'start' });
    }
  }

  function schedule() {
    var tries = 0;
    function attempt() {
      scrollToHash();
      tries += 1;
      if (tries < 12) {
        window.requestAnimationFrame(attempt);
      }
    }
    attempt();
  }

  window.addEventListener('load', schedule);
  window.addEventListener('hashchange', schedule);
  document.addEventListener('enhancedload', schedule);

  if (document.readyState === 'complete') {
    schedule();
  }
})();
