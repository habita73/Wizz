// تخزين ذاكرة المحادثة وتفضيلات المستخدم في localStorage — محلي بالكامل، بلا اعتماديات خارجية.
// مفتاحان منفصلان: ذاكرة المحادثة (قابلة للمسح) وتفضيلات المستخدم (إعدادات دائمة).
window.wizzAiMemory = {
    memoryKey: 'wizz.ai.memory.v1',
    prefsKey: 'wizz.ai.prefs.v1',

    // يقرأ ذاكرة المحادثة المحفوظة {turns:[{question,answer,atUtc,subject,intent}], lastSubject} أو لقطة فارغة.
    loadMemory: function () {
        const empty = { turns: [], lastSubject: null };
        try {
            const raw = localStorage.getItem(this.memoryKey);
            if (!raw) return empty;
            const data = JSON.parse(raw);
            if (!data || typeof data !== 'object') return empty;
            const turns = Array.isArray(data.turns) ? data.turns.filter(t =>
                t && typeof t.question === 'string' && typeof t.answer === 'string') : [];
            return {
                turns: turns.map(t => ({
                    question: String(t.question || ''),
                    answer: String(t.answer || ''),
                    atUtc: String(t.atUtc || new Date().toISOString()),
                    subject: t.subject ? String(t.subject) : null,
                    intent: t.intent ? String(t.intent) : null
                })),
                lastSubject: data.lastSubject ? String(data.lastSubject) : null
            };
        } catch {
            // بيانات تالفة أو localStorage معطّل — لا نُعطّل التطبيق.
            return empty;
        }
    },

    // يحفظ ذاكرة المحادثة كاملة.
    saveMemory: function (memory) {
        try {
            const data = memory || {};
            const turns = (Array.isArray(data.turns) ? data.turns : []).map(t => ({
                question: String(t.question || ''),
                answer: String(t.answer || ''),
                atUtc: String(t.atUtc || new Date().toISOString()),
                subject: t.subject ? String(t.subject) : null,
                intent: t.intent ? String(t.intent) : null
            }));
            localStorage.setItem(this.memoryKey, JSON.stringify({
                turns: turns,
                lastSubject: data.lastSubject ? String(data.lastSubject) : null
            }));
            return true;
        } catch {
            // الحصة ممتلئة أو التخزين معطّل — نتجاهل بهدوء.
            return false;
        }
    },

    // يقرأ تفضيلات المستخدم المحفوظة {priceMode, preferredUnit, rememberContext, allowBuiltInInternet}.
    loadPrefs: function () {
        const empty = { priceMode: 'Retail', preferredUnit: '', rememberContext: true, allowBuiltInInternet: false };
        try {
            const raw = localStorage.getItem(this.prefsKey);
            if (!raw) return empty;
            const data = JSON.parse(raw);
            if (!data || typeof data !== 'object') return empty;
            return {
                priceMode: data.priceMode === 'Wholesale' ? 'Wholesale' : 'Retail',
                preferredUnit: typeof data.preferredUnit === 'string' ? data.preferredUnit : '',
                rememberContext: data.rememberContext !== false,
                // مغلق افتراضياً — لا يُفعّل إلا إذا اختاره المستخدم صراحةً.
                allowBuiltInInternet: data.allowBuiltInInternet === true
            };
        } catch {
            return empty;
        }
    },

    // يحفظ تفضيلات المستخدم.
    savePrefs: function (prefs) {
        try {
            const data = prefs || {};
            localStorage.setItem(this.prefsKey, JSON.stringify({
                priceMode: data.priceMode === 'Wholesale' ? 'Wholesale' : 'Retail',
                preferredUnit: typeof data.preferredUnit === 'string' ? data.preferredUnit : '',
                rememberContext: data.rememberContext !== false,
                allowBuiltInInternet: data.allowBuiltInInternet === true
            }));
            return true;
        } catch {
            return false;
        }
    },

    // مسح ذاكرة المحادثة فقط (التفضيلات تبقى كما هي).
    clearMemory: function () {
        try { localStorage.removeItem(this.memoryKey); return true; }
        catch { return false; }
    },

    // مسح التفضيلات أيضاً (إعادة كل الإعدادات للافتراضي).
    clearPrefs: function () {
        try { localStorage.removeItem(this.prefsKey); return true; }
        catch { return false; }
    },

    // قائمة اختيار الجهة المُجيبة في مساعد الذكاء الاصطناعي.
    // Bootstrap JS غير محمّل في التطبيق، لذا نتحكم بفتح/إغلاق القائمة يدوياً عبر صنف show.
    dropdown: {
        toggle: function (show) {
            try {
                document.querySelectorAll('.dropdown-menu').forEach(menu => {
                    if (show) {
                        menu.classList.add('show');
                    } else {
                        menu.classList.remove('show');
                    }
                });
            } catch {
                // تجاهل الأخطاء — لا نُعطّل الواجهة.
            }
        },
        closeAll: function () {
            try {
                document.querySelectorAll('.dropdown-menu.show').forEach(menu => {
                    menu.classList.remove('show');
                });
            } catch {
                // تجاهل الأخطاء
            }
        }
    },

    // يُسجَّل مرة واحدة فقط: إغلاق القائمة عند النقر خارجها أو الضغط على Escape.
    initDropdownCloseOnClickOutside: function () {
        if (window.wizzAiMemory._clickOutsideBound) return;
        window.wizzAiMemory._clickOutsideBound = true;

        document.addEventListener('click', function (event) {
            document.querySelectorAll('.dropdown').forEach(dropdown => {
                if (dropdown.contains(event.target)) return;
                const menu = dropdown.querySelector('.dropdown-menu');
                if (menu) menu.classList.remove('show');
            });
        });

        document.addEventListener('keydown', function (event) {
            if (event.key === 'Escape') {
                window.wizzAiMemory.dropdown.closeAll();
            }
        });
    }
};
