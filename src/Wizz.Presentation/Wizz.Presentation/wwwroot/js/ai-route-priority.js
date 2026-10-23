// ترتيب أولوية المسارات التلقائية في localStorage — محلي بالكامل، بلا اعتماديات خارجية.
window.wizzAiRoutePriority = {
    key: 'wizz.ai.routePriority.v1',
    validRoutes: ['BuiltIn', 'Local', 'Cloud'],

    // يقرأ الترتيب المحفوظ ['BuiltIn','Local','Cloud'] أو يعيد الترتيب الافتراضي.
    load: function () {
        const fallback = this.validRoutes.slice();
        try {
            const raw = localStorage.getItem(this.key);
            if (!raw) return fallback;

            const data = JSON.parse(raw);
            const order = Array.isArray(data) ? data : (data && Array.isArray(data.order) ? data.order : null);
            if (!order) return fallback;

            const cleaned = [];
            for (const route of order) {
                if (this.validRoutes.includes(route) && !cleaned.includes(route)) {
                    cleaned.push(route);
                }
            }

            for (const route of this.validRoutes) {
                if (!cleaned.includes(route)) {
                    cleaned.push(route);
                }
            }

            return cleaned;
        } catch {
            // بيانات تالفة أو localStorage معطّل — لا نُعطّل التطبيق.
            return fallback;
        }
    },

    // يحفظ الترتيب (مصفوفة أسماء المسارات).
    save: function (order) {
        try {
            const cleaned = [];
            for (const route of (Array.isArray(order) ? order : [])) {
                if (this.validRoutes.includes(route) && !cleaned.includes(route)) {
                    cleaned.push(route);
                }
            }

            for (const route of this.validRoutes) {
                if (!cleaned.includes(route)) {
                    cleaned.push(route);
                }
            }

            localStorage.setItem(this.key, JSON.stringify({ order: cleaned }));
            return true;
        } catch {
            return false;
        }
    },

    // مسح الترتيب المحفوظ (العودة للافتراضي).
    clear: function () {
        try { localStorage.removeItem(this.key); return true; }
        catch { return false; }
    }
};
