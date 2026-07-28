(function () {
    var toggle = document.getElementById('theme-toggle');
    if (!toggle) return;

    function updateLabel(theme) {
        toggle.textContent = theme === 'dark' ? '☀️ Light mode' : '🌙 Arrivo mode';
    }

    var initial = document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
    updateLabel(initial);

    toggle.addEventListener('click', function () {
        var html = document.documentElement;
        var current = html.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
        var next = current === 'dark' ? 'light' : 'dark';

        html.setAttribute('data-theme', next);
        document.cookie = 'hopon-theme=' + next + ';path=/;max-age=' + (60 * 60 * 24 * 365);
        updateLabel(next);
    });
})();