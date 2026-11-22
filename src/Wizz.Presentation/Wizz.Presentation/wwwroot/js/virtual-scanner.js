// جسر محاكاة القراءة بين تبويب المحاكي وتبويب البيع.
// لا يتصل بأي جهاز، ولا يولّد أو يقرأ QR؛ يمرّر رسائل نصية داخل نفس المتصفح فقط.
(function () {
    'use strict';

    const CHANNEL_NAME = 'wizz-virtual-scanner';
    const FALLBACK_KEY = 'wizz.scanner.message.v1';
    const subscribers = new Map();
    let channel = null;
    let channelReady = false;

    function ensureChannel() {
        if (channelReady) return channel;
        channelReady = true;
        if (typeof BroadcastChannel !== 'undefined') {
            try {
                channel = new BroadcastChannel(CHANNEL_NAME);
                channel.onmessage = function (event) {
                    deliver(event.data);
                };
            } catch (e) {
                channel = null;
            }
        }
        return channel;
    }

    function deliver(message) {
        if (!message || typeof message !== 'object') return;
        const target = message.target;
        subscribers.forEach(function (reference, role) {
            if (target && target !== '*' && target !== role) return;
            try {
                reference.invokeMethodAsync('Receive', JSON.stringify(message));
            } catch (e) {
                // تبويب أُغلق أثناء الإرسال؛ نتجاهله.
            }
        });
    }

    window.addEventListener('storage', function (event) {
        if (event.key !== FALLBACK_KEY || !event.newValue) return;
        try {
            deliver(JSON.parse(event.newValue));
        } catch (e) {
            // رسالة تالفة؛ نتجاهلها.
        }
    });

    window.wizzScanner = {
        subscribe: function (role, reference) {
            if (!role || !reference) return;
            subscribers.set(role, reference);
            ensureChannel();
        },

        unsubscribe: function (role) {
            subscribers.delete(role);
        },

        publish: function (message) {
            if (!message || typeof message !== 'object') return;
            const payload = Object.assign({}, message, {
                ts: Date.now(),
                source: message.source || 'simulator'
            });
            const activeChannel = ensureChannel();
            if (activeChannel) {
                activeChannel.postMessage(payload);
            } else {
                // متصفح قديم بلا BroadcastChannel: الحدث storage يصل للتبويبات الأخرى.
                try {
                    localStorage.setItem(FALLBACK_KEY, JSON.stringify(payload));
                } catch (e) {
                    // التخزين المحلي معطّل؛ لا يوجد بديل ثالث.
                }
            }
        }
    };
})();
