// وظائف تصدير/طباعة المخرجات (artifacts) — تعمل محلياً بلا اعتماديات خارجية.
window.wizzDownloadText = function (content, fileName, mime) {
    const blob = new Blob([content], { type: (mime || 'text/plain') + ';charset=utf-8' });
    triggerDownload(blob, fileName);
};

window.wizzDownloadBase64 = function (base64, fileName, mime) {
    const byteChars = atob(base64);
    const bytes = new Uint8Array(byteChars.length);
    for (let i = 0; i < byteChars.length; i++) bytes[i] = byteChars.charCodeAt(i);
    const blob = new Blob([bytes], { type: mime || 'application/octet-stream' });
    triggerDownload(blob, fileName);
};

window.wizzDownloadUrl = function (url, fileName) {
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName || '';
    a.target = '_blank';
    a.rel = 'noopener';
    document.body.appendChild(a);
    a.click();
    a.remove();
};

window.wizzOpenHtml = function (html) {
    const w = window.open('', '_blank');
    if (!w) { alert('يرجى السماح بالنوافذ المنبثقة لعرض المخرج.'); return; }
    w.document.open();
    w.document.write(html);
    w.document.close();
};

window.wizzPrint = function (html) {
    const frame = document.createElement('iframe');
    frame.style.position = 'fixed';
    frame.style.left = '-10000px';
    frame.style.top = '0';
    frame.style.width = '794px';
    frame.style.height = '1200px';
    frame.style.border = '0';
    frame.setAttribute('aria-hidden', 'true');
    document.body.appendChild(frame);
    const doc = frame.contentWindow.document;
    doc.open();
    doc.write(html);
    doc.close();

    const doPrint = function () {
        try {
            const root = doc.documentElement;
            const width = parseInt(root.getAttribute('data-page-width-px'), 10);
            const height = parseInt(root.getAttribute('data-page-height-px'), 10);
            if (Number.isFinite(width) && width > 0) frame.style.width = width + 'px';

            // ترقيم الصفحات يظهر فقط حين يتجاوز المحتوى صفحة واحدة.
            const content = doc.getElementById('wizz-content');
            const contentHeight = content ? content.scrollHeight : 0;
            if (content && Number.isFinite(height) && contentHeight <= height + 24) {
                const numbering = doc.getElementById('wizz-page-number');
                if (numbering) numbering.remove();
            }

            frame.contentWindow.focus();
            frame.contentWindow.print();
        } finally {
            setTimeout(() => frame.remove(), 1000);
        }
    };

    // ننتظر تحميل المحتوى وتخطيطه قبل قياس عدد الصفحات.
    setTimeout(doPrint, 300);
};

function triggerDownload(blob, fileName) {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName || 'wizz-artifact';
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1500);
}
