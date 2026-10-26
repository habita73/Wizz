// سلوكيات لوحة المفاتيح العامة — محلي بالكامل، بلا اعتماديات.
// Esc يغلق أي نافذة منبثقة مفتوحة (تعتمد نوافذ المشروع زر .btn-close في الرأس).
window.wizzKeyboard = {
    _ready: false,

    init: function () {
        if (window.wizzKeyboard._ready) return;
        window.wizzKeyboard._ready = true;

        document.addEventListener('keydown', function (event) {
            if (event.key !== 'Escape' || event.defaultPrevented) return;

            var modal = document.querySelector('.modal.show');
            if (!modal) return;

            var close = modal.querySelector('.modal-header .btn-close');
            if (!close) return;

            event.preventDefault();
            close.click();
        });
    }
};

window.wizzKeyboard.init();
