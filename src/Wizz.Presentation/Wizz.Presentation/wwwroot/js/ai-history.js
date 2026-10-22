// تخزين تاريخ أسئلة المساعد في localStorage — محلي بالكامل، بلا اعتماديات خارجية.
// المفتاح العام ثابت ليبقى التاريخ بين الجلسات.
window.wizzAiHistory = {
    key: 'wizz.ai.history.v1',

    // يقرأ قائمة الأسئلة المسجّلة [{question, count, lastAskedAt}] أو [] عند غيابها/تلفها.
    load: function () {
        try {
            const raw = localStorage.getItem(this.key);
            if (!raw) return [];
            const data = JSON.parse(raw);
            if (!Array.isArray(data)) return [];
            return data.filter(e =>
                e && typeof e.question === 'string' &&
                typeof e.count === 'number' &&
                typeof e.lastAskedAt === 'string');
        } catch {
            // بيانات تالفة أو localStorage معطّل — لا نُعطّل التطبيق.
            return [];
        }
    },

    // يحفظ القائمة كاملة.
    save: function (entries) {
        try {
            const clean = (entries || []).map(e => ({
                question: String(e.question || ''),
                count: Number(e.count) || 1,
                lastAskedAt: String(e.lastAskedAt || new Date().toISOString())
            }));
            localStorage.setItem(this.key, JSON.stringify(clean));
            return true;
        } catch {
            // الحصة ممتلئة أو التخزين معطّل — نتجاهل بهدوء.
            return false;
        }
    },

    clear: function () {
        try { localStorage.removeItem(this.key); return true; }
        catch { return false; }
    }
};
