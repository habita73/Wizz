// اختصارات الإجراءات السريعة (Alt+1..4) + تذكير الاستخدام الأول — محلي بالكامل.
// يُستدعى من صفحة الرئيسية فقط؛ يُزال المستمع عند مغادرة الصفحة.
window.wizzQuickActions = {
    hintKey: 'wizz.hints.quick-actions.v1',
    _handler: null,

    // يربط Alt+1..4 بدالة .NET في الصفحة الحالية.
    init: function (dotNetRef) {
        window.wizzQuickActions.dispose();

        window.wizzQuickActions._handler = function (event) {
            if (!event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;

            var target = event.target;
            var tag = target && target.tagName ? target.tagName : '';
            if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || (target && target.isContentEditable)) return;

            var key = event.key;
            if (key !== '1' && key !== '2' && key !== '3' && key !== '4') return;

            event.preventDefault();
            try {
                dotNetRef.invokeMethodAsync('NavigateByShortcut', key);
            } catch {
                // انقطاع الاتصال لا يمنع بقية الصفحة من العمل.
            }
        };

        document.addEventListener('keydown', window.wizzQuickActions._handler);
    },

    // يزيل المستمع (عند مغادرة الصفحة أو إعادة الربط).
    dispose: function () {
        if (window.wizzQuickActions._handler) {
            document.removeEventListener('keydown', window.wizzQuickActions._handler);
            window.wizzQuickActions._handler = null;
        }
    },

    // هل يُعرض تذكير الاستخدام الأول؟ (يُخفى بعد الضغط على «فهمت»).
    shouldShowHint: function () {
        try {
            return localStorage.getItem(window.wizzQuickActions.hintKey) !== 'dismissed';
        } catch {
            return true;
        }
    },

    // يحفظ إخفاء التذكير بشكل دائم.
    dismissHint: function () {
        try {
            localStorage.setItem(window.wizzQuickActions.hintKey, 'dismissed');
            return true;
        } catch {
            return false;
        }
    }
};
