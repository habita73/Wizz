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
    frame.style.right = '-10000px';
    frame.style.width = '0';
    frame.style.height = '0';
    frame.setAttribute('aria-hidden', 'true');
    document.body.appendChild(frame);
    const doc = frame.contentWindow.document;
    doc.open();
    doc.write(html);
    doc.close();

    const doPrint = function () {
        try {
            frame.contentWindow.focus();
            frame.contentWindow.print();
        } finally {
            setTimeout(() => frame.remove(), 1000);
        }
    };

    // ننتظر تحميل الصور قبل الطباعة إن وُجدت.
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
