// لوحة الإعدادات السريعة — تفضيلات عرض محلية، بلا اعتماديات وبلا مغادرة الصفحة.
// الهدف: تغيير المظهر/حجم الخط/الكثافة/التباين/الحركة/أهداف اللمس وحفظ السلة
// ورؤية معلومات المتجر دون فقدان عمل جارٍ (مثل سلة بيع غير مكتملة)، لأن أي
// انتقال إلى صفحة أخرى يعيد تحميل الصفحة.
// المفاتيح: wizz.theme · wizz.fontsize · wizz.density · wizz.contrast · wizz.motion
//           wizz.targets · wizz.cart.draft.enabled (مسودة السلة نفسها: wizz.cart.draft.v1)
window.wizzQuickSettings = {
    themeKey: 'wizz.theme',
    fontKey: 'wizz.fontsize',
    densityKey: 'wizz.density',
    contrastKey: 'wizz.contrast',
    motionKey: 'wizz.motion',
    targetsKey: 'wizz.targets',
    draftKey: 'wizz.cart.draft.enabled',
    draftCartKey: 'wizz.cart.draft.v1',

    _panel: null,
    _trigger: null,
    _lastFocus: null,
    _ready: false,

    // قراءة قيمة محفوظة مع بديل آمن عند تعطّل التخزين المحلي.
    _read: function (key, fallback) {
        try {
            var value = localStorage.getItem(key);
            return value === null || value === '' ? fallback : value;
        } catch (e) {
            return fallback;
        }
    },

    _write: function (key, value) {
        try {
            localStorage.setItem(key, value);
        } catch (e) {
            // التخزين المحلي معطّل: يبقى التغيير سارياً في هذه الصفحة فقط.
        }
    },

    // تطبيق التفضيلات على عنصر html (المصدر الوحيد للتنسيق).
    applyPreferences: function () {
        var api = window.wizzQuickSettings;
        var root = document.documentElement;
        root.setAttribute('data-theme', api._read(api.themeKey, 'system'));
        root.setAttribute('data-fontsize', api._read(api.fontKey, 'normal'));
        root.setAttribute('data-density', api._read(api.densityKey, 'comfortable'));
        root.setAttribute('data-contrast', api._read(api.contrastKey, 'normal'));
        root.setAttribute('data-motion', api._read(api.motionKey, 'normal'));
        root.setAttribute('data-targets', api._read(api.targetsKey, 'normal'));
        api._syncActive();
    },

    // إبراز الخيار المطابق للقيمة الحالية.
    _syncActive: function () {
        var api = window.wizzQuickSettings;
        var theme = api._read(api.themeKey, 'system');
        var font = api._read(api.fontKey, 'normal');
        var density = api._read(api.densityKey, 'comfortable');
        var contrast = api._read(api.contrastKey, 'normal');
        var motion = api._read(api.motionKey, 'normal');
        var targets = api._read(api.targetsKey, 'normal');
        var draft = api._read(api.draftKey, 'on');

        api._mark('[data-wizz-qs-theme]', 'wizz-qs-theme', theme);
        api._mark('[data-wizz-qs-font]', 'wizz-qs-font', font);
        api._mark('[data-wizz-qs-density]', 'wizz-qs-density', density);
        api._mark('[data-wizz-qs-contrast]', 'wizz-qs-contrast', contrast);
        api._mark('[data-wizz-qs-motion]', 'wizz-qs-motion', motion);
        api._mark('[data-wizz-qs-targets]', 'wizz-qs-targets', targets);
        api._mark('[data-wizz-qs-draft]', 'wizz-qs-draft', draft);
    },

    _mark: function (selector, attribute, current) {
        var nodes = document.querySelectorAll(selector);
        for (var i = 0; i < nodes.length; i++) {
            var isActive = nodes[i].getAttribute('data-' + attribute) === current;
            nodes[i].classList.toggle('is-active', isActive);
            nodes[i].setAttribute('aria-pressed', isActive ? 'true' : 'false');
        }
    },

    isOpen: function () {
        var panel = window.wizzQuickSettings._panel;
        return !!panel && !panel.hidden;
    },

    open: function () {
        var api = window.wizzQuickSettings;
        if (!api._panel || api.isOpen()) return;

        api._lastFocus = document.activeElement;
        api.applyPreferences();
        api._panel.hidden = false;
        var backdrop = document.querySelector('.wizz-qs-backdrop');
        if (backdrop) backdrop.hidden = false;
        if (api._trigger) api._trigger.setAttribute('aria-expanded', 'true');

        var first = api._panel.querySelector('.wizz-qs-option.is-active') || api._panel.querySelector('.btn-close');
        if (first) first.focus();
    },

    close: function () {
        var api = window.wizzQuickSettings;
        if (!api._panel || !api.isOpen()) return;

        api._panel.hidden = true;
        var backdrop = document.querySelector('.wizz-qs-backdrop');
        if (backdrop) backdrop.hidden = true;
        if (api._trigger) {
            api._trigger.setAttribute('aria-expanded', 'false');
            api._trigger.focus();
        } else if (api._lastFocus && api._lastFocus.focus) {
            api._lastFocus.focus();
        }
        api._lastFocus = null;
    },

    toggle: function () {
        if (window.wizzQuickSettings.isOpen()) {
            window.wizzQuickSettings.close();
        } else {
            window.wizzQuickSettings.open();
        }
    },

    setTheme: function (value) {
        window.wizzQuickSettings._write(window.wizzQuickSettings.themeKey, value);
        window.wizzQuickSettings.applyPreferences();
    },

    setFont: function (value) {
        window.wizzQuickSettings._write(window.wizzQuickSettings.fontKey, value);
        window.wizzQuickSettings.applyPreferences();
    },

    setDensity: function (value) {
        window.wizzQuickSettings._write(window.wizzQuickSettings.densityKey, value);
        window.wizzQuickSettings.applyPreferences();
    },

    setContrast: function (value) {
        window.wizzQuickSettings._write(window.wizzQuickSettings.contrastKey, value);
        window.wizzQuickSettings.applyPreferences();
    },

    setMotion: function (value) {
        window.wizzQuickSettings._write(window.wizzQuickSettings.motionKey, value);
        window.wizzQuickSettings.applyPreferences();
    },

    setTargets: function (value) {
        window.wizzQuickSettings._write(window.wizzQuickSettings.targetsKey, value);
        window.wizzQuickSettings.applyPreferences();
    },

    // إيقاف الحفظ يمسح أي مسودة سابقة كي لا تبقى سلة قديمة على الجهاز.
    setDraft: function (value) {
        var api = window.wizzQuickSettings;
        api._write(api.draftKey, value);
        if (value === 'off') {
            try {
                localStorage.removeItem(api.draftCartKey);
            } catch (e) {
                // لا شيء يعتمد على نجاح المسح.
            }
        }
        api._syncActive();
    },

    // استعادة الافتراضي: تفضيلات العرض فقط، دون المساس بالبيانات أو بالمسودة.
    reset: function () {
        var api = window.wizzQuickSettings;
        var keys = [
            api.themeKey,
            api.fontKey,
            api.densityKey,
            api.contrastKey,
            api.motionKey,
            api.targetsKey
        ];
        for (var i = 0; i < keys.length; i++) {
            try {
                localStorage.removeItem(keys[i]);
            } catch (e) {
                // تجاهل: الوضع الافتراضي يُطبَّق في كل الأحوال أدناه.
            }
        }
        api.applyPreferences();
    },

    // يربط الأزرار والاختصارات مرة واحدة لكل تحميل صفحة.
    init: function () {
        var api = window.wizzQuickSettings;
        if (api._ready) return;

        api._panel = document.getElementById('wizz-quick-settings');
        api._trigger = document.getElementById('wizz-qs-trigger');
        if (!api._panel) {
            document.addEventListener('DOMContentLoaded', function () { api.init(); }, { once: true });
            return;
        }
        api._ready = true;

        api.applyPreferences();

        api._panel.addEventListener('click', function (event) {
            var target = event.target;
            if (!target) return;

            var theme = target.closest ? target.closest('[data-wizz-qs-theme]') : null;
            if (theme) { api.setTheme(theme.getAttribute('data-wizz-qs-theme')); return; }

            var font = target.closest ? target.closest('[data-wizz-qs-font]') : null;
            if (font) { api.setFont(font.getAttribute('data-wizz-qs-font')); return; }

            var density = target.closest ? target.closest('[data-wizz-qs-density]') : null;
            if (density) { api.setDensity(density.getAttribute('data-wizz-qs-density')); return; }

            var contrast = target.closest ? target.closest('[data-wizz-qs-contrast]') : null;
            if (contrast) { api.setContrast(contrast.getAttribute('data-wizz-qs-contrast')); return; }

            var motion = target.closest ? target.closest('[data-wizz-qs-motion]') : null;
            if (motion) { api.setMotion(motion.getAttribute('data-wizz-qs-motion')); return; }

            var targets = target.closest ? target.closest('[data-wizz-qs-targets]') : null;
            if (targets) { api.setTargets(targets.getAttribute('data-wizz-qs-targets')); return; }

            var draft = target.closest ? target.closest('[data-wizz-qs-draft]') : null;
            if (draft) { api.setDraft(draft.getAttribute('data-wizz-qs-draft')); return; }

            var reset = target.closest ? target.closest('[data-wizz-qs-reset]') : null;
            if (reset) { api.reset(); return; }

            var closer = target.closest ? target.closest('[data-wizz-qs-close]') : null;
            if (closer) api.close();
        });

        document.addEventListener('click', function (event) {
            var trigger = event.target && event.target.closest ? event.target.closest('[data-wizz-qs-open]') : null;
            if (!trigger) return;
            event.preventDefault();
            api.toggle();
        });

        document.addEventListener('keydown', function (event) {
            if (event.defaultPrevented) return;

            // F1 أو «?» لفتح/إغلاق اللوحة — لا نعترض الكتابة داخل الحقول.
            var target = event.target;
            var tag = target && target.tagName ? target.tagName : '';
            var typing = tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || (target && target.isContentEditable);

            if ((event.key === 'F1' || (event.key === '?' && !typing)) && !event.ctrlKey && !event.altKey && !event.metaKey) {
                event.preventDefault();
                api.toggle();
                return;
            }

            if (event.key === 'Escape' && api.isOpen()) {
                event.preventDefault();
                api.close();
            }
        }, true);

        // مزامنة التفضيلات بين تبويبات المتصفح المفتوحة في الوقت نفسه.
        window.addEventListener('storage', function (event) {
            if (!event.key) return;
            if (event.key === api.draftKey && event.newValue === 'off') {
                try {
                    localStorage.removeItem(api.draftCartKey);
                } catch (e) {
                    // تجاهل.
                }
            }
            if (event.key === api.themeKey
                || event.key === api.fontKey
                || event.key === api.densityKey
                || event.key === api.contrastKey
                || event.key === api.motionKey
                || event.key === api.targetsKey
                || event.key === api.draftKey) {
                api.applyPreferences();
            }
        });
    }
};

window.wizzQuickSettings.init();
