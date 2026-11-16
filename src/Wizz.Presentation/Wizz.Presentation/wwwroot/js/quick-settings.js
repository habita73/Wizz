// لوحة الإعدادات السريعة — تفضيلات عرض محلية، بلا اعتماديات وبلا مغادرة الصفحة.
// الهدف: تغيير المظهر/حجم الخط/الكثافة ورؤية معلومات المتجر دون فقدان عمل جارٍ
// (مثل سلة بيع غير مكتملة)، لأن أي انتقال إلى صفحة أخرى يعيد تحميل الصفحة.
// المفاتيح: wizz.theme (مشترك مع صفحة الإعدادات) · wizz.fontsize · wizz.density
window.wizzQuickSettings = {
    themeKey: 'wizz.theme',
    fontKey: 'wizz.fontsize',
    densityKey: 'wizz.density',

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
        var root = document.documentElement;
        root.setAttribute('data-theme', window.wizzQuickSettings._read(window.wizzQuickSettings.themeKey, 'system'));
        root.setAttribute('data-fontsize', window.wizzQuickSettings._read(window.wizzQuickSettings.fontKey, 'normal'));
        root.setAttribute('data-density', window.wizzQuickSettings._read(window.wizzQuickSettings.densityKey, 'comfortable'));
        window.wizzQuickSettings._syncActive();
    },

    // إبراز الخيار المطابق للقيمة الحالية.
    _syncActive: function () {
        var api = window.wizzQuickSettings;
        var theme = api._read(api.themeKey, 'system');
        var font = api._read(api.fontKey, 'normal');
        var density = api._read(api.densityKey, 'comfortable');

        api._mark('[data-wizz-qs-theme]', 'wizz-qs-theme', theme);
        api._mark('[data-wizz-qs-font]', 'wizz-qs-font', font);
        api._mark('[data-wizz-qs-density]', 'wizz-qs-density', density);
    },

    _mark: function (selector, attribute, current) {
        var nodes = document.querySelectorAll(selector);
        for (var i = 0; i < nodes.length; i++) {
            var isActive = nodes[i].getAttribute(attribute) === current;
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
            if (event.key === api.themeKey || event.key === api.fontKey || event.key === api.densityKey) {
                api.applyPreferences();
            }
        });
    }
};

window.wizzQuickSettings.init();
