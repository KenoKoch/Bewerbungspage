// KenoKoch.com – kleine Progressive-Enhancement-Schicht.
// Alles funktioniert auch ohne dieses Skript; es ergänzt nur Komfort und Animationen.
// Wird nach blazor.web.js geladen und nach jeder Enhanced Navigation erneut initialisiert.
(() => {
    'use strict';

    const root = document.documentElement;
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const header = () => document.querySelector('[data-site-header]');

    // ---------- Farbschema ----------
    document.addEventListener('click', (event) => {
        if (!event.target.closest('[data-theme-toggle]')) {
            return;
        }

        const next = root.dataset.theme === 'light' ? 'dark' : 'light';
        root.dataset.theme = next;
        const secure = location.protocol === 'https:' ? '; secure' : '';
        document.cookie = `theme=${next}; path=/; max-age=31536000; samesite=lax${secure}`;
        document.querySelector('meta[name="theme-color"]')?.setAttribute('content', next === 'light' ? '#f6f7fb' : '#07080f');
    });

    // ---------- Mobile Navigation ----------
    function setNavigation(open) {
        const element = header();
        if (!element) {
            return;
        }

        element.classList.toggle('nav-open', open);
        const toggle = element.querySelector('[data-nav-toggle]');
        toggle?.setAttribute('aria-expanded', String(open));
        toggle?.setAttribute('aria-label', open ? 'Menü schließen' : 'Menü öffnen');
    }

    document.addEventListener('click', (event) => {
        if (event.target.closest('[data-nav-toggle]')) {
            setNavigation(!header()?.classList.contains('nav-open'));
        } else if (event.target.closest('.site-nav a')) {
            setNavigation(false);
        }
    });

    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && header()?.classList.contains('nav-open')) {
            setNavigation(false);
        }
    });

    // ---------- Header beim Scrollen ----------
    let headerTicking = false;
    function updateHeader() {
        header()?.classList.toggle('is-scrolled', window.scrollY > 8);
        headerTicking = false;
    }

    window.addEventListener('scroll', () => {
        if (!headerTicking) {
            headerTicking = true;
            requestAnimationFrame(updateHeader);
        }
    }, { passive: true });

    // ---------- Einblend-Animationen ----------
    let revealObserver = null;

    function initReveal() {
        if (reducedMotion.matches || !('IntersectionObserver' in window)) {
            return;
        }

        revealObserver ??= new IntersectionObserver((entries) => {
            for (const entry of entries) {
                if (entry.isIntersecting) {
                    entry.target.classList.add('is-visible');
                    revealObserver.unobserve(entry.target);
                }
            }
        }, { rootMargin: '0px 0px -6% 0px', threshold: 0.06 });

        const fold = window.innerHeight * 0.92;
        for (const element of document.querySelectorAll('[data-reveal]:not([data-reveal-ready])')) {
            element.setAttribute('data-reveal-ready', '');

            // Was schon sichtbar ist, wird nicht animiert – so flackert nichts.
            if (element.getBoundingClientRect().top < fold) {
                continue;
            }

            element.classList.add('reveal-init');
            revealObserver.observe(element);
        }
    }

    // Interaktive Komponenten rendern nach dem Laden nach – neue Elemente ebenfalls berücksichtigen.
    let revealScheduled = false;
    new MutationObserver(() => {
        if (!revealScheduled) {
            revealScheduled = true;
            requestAnimationFrame(() => {
                revealScheduled = false;
                initReveal();
            });
        }
    }).observe(document.body, { childList: true, subtree: true });

    // ---------- Spotlight-Effekt auf Karten ----------
    document.addEventListener('pointermove', (event) => {
        const card = event.target.closest?.('.spotlight');
        if (!card) {
            return;
        }

        const rect = card.getBoundingClientRect();
        card.style.setProperty('--mx', `${event.clientX - rect.left}px`);
        card.style.setProperty('--my', `${event.clientY - rect.top}px`);
    }, { passive: true });

    // ---------- Code kopieren ----------
    document.addEventListener('click', async (event) => {
        const button = event.target.closest('[data-copy-target]');
        if (!button) {
            return;
        }

        const code = document.getElementById(button.dataset.copyTarget);
        if (!code || !navigator.clipboard) {
            return;
        }

        try {
            await navigator.clipboard.writeText(code.textContent.replace(/\n$/, ''));
            const label = button.querySelector('.code-block__copy-label');
            button.classList.add('is-copied');
            if (label) {
                label.textContent = 'Kopiert';
            }

            setTimeout(() => {
                button.classList.remove('is-copied');
                if (label) {
                    label.textContent = 'Kopieren';
                }
            }, 1600);
        } catch {
            // Zwischenablage nicht verfügbar (z. B. ohne HTTPS) – dann eben nicht.
        }
    });

    // ---------- Scroll-Spy für die Startseite ----------
    let spyObserver = null;

    function initScrollSpy() {
        spyObserver?.disconnect();
        const links = [...document.querySelectorAll('.site-nav [data-spy]')];
        links.forEach((link) => link.classList.remove('is-active'));

        if (location.pathname !== '/' || !('IntersectionObserver' in window)) {
            return;
        }

        spyObserver = new IntersectionObserver((entries) => {
            for (const entry of entries) {
                if (entry.isIntersecting) {
                    links.forEach((link) => link.classList.toggle('is-active', link.dataset.spy === entry.target.id));
                }
            }
        }, { rootMargin: '-45% 0px -50% 0px' });

        links
            .map((link) => document.getElementById(link.dataset.spy))
            .filter(Boolean)
            .forEach((section) => spyObserver.observe(section));
    }

    // ---------- Trend-Fadenkreuz (SPS-Simulator) ----------
    // Blazor rendert das Overlay ohne Kinder; Linie und Tooltip werden hier per DOM-API erzeugt (textContent, kein innerHTML).
    const german = (value, digits = 1) => Number(value).toLocaleString('de-DE', { minimumFractionDigits: digits, maximumFractionDigits: digits });
    let activeTrend = null;

    function tooltipRow(value, label, key) {
        const row = document.createElement('div');
        row.className = 'trend-tip__row';
        const swatch = document.createElement('span');
        swatch.className = `trend-tip__key trend-tip__key--${key}`;
        const strong = document.createElement('strong');
        strong.textContent = value;
        const text = document.createElement('span');
        text.textContent = label;
        row.append(swatch, strong, text);
        return row;
    }

    function renderTrendTip(plot, clientX) {
        let samples;
        try {
            samples = JSON.parse(plot.dataset.samples || '[]');
        } catch {
            return;
        }

        const overlay = plot.querySelector('.trend__overlay');
        if (!overlay || samples.length === 0) {
            overlay?.classList.remove('is-active');
            return;
        }

        const rect = plot.getBoundingClientRect();
        const width = Number(plot.dataset.width) || 600;
        const left = Number(plot.dataset.left) || 0;
        const right = Number(plot.dataset.right) || 0;
        const span = Number(plot.dataset.window) || 60;
        const plotWidth = width - left - right;

        const viewX = (clientX - rect.left) / rect.width * width;
        const t = (viewX - left) / plotWidth * span - span;

        let nearest = samples[0];
        for (const sample of samples) {
            if (Math.abs(sample[0] - t) < Math.abs(nearest[0] - t)) {
                nearest = sample;
            }
        }

        const percent = (left + (nearest[0] + span) / span * plotWidth) / width * 100;

        let line = overlay.querySelector('.trend-crosshair');
        let tip = overlay.querySelector('.trend-tip');
        if (!line) {
            line = document.createElement('div');
            line.className = 'trend-crosshair';
            tip = document.createElement('div');
            tip.className = 'trend-tip';
            overlay.append(line, tip);
        }

        const footer = document.createElement('div');
        footer.className = 'trend-tip__foot';
        footer.textContent = nearest[0] > -0.25 ? 'jetzt' : `vor ${german(-nearest[0])} s`;

        tip.replaceChildren(
            tooltipRow(`${german(nearest[1])} %`, 'Füllstand', 'level'),
            tooltipRow(nearest[2] ? 'EIN' : 'AUS', 'Pumpe P1', 'pump'),
            tooltipRow(nearest[3] ? 'AUF' : 'ZU', 'Ventil V1', 'valve'),
            footer);

        line.style.left = `${percent}%`;
        tip.style.left = `${percent}%`;
        tip.classList.toggle('is-right', percent > 62);
        overlay.classList.add('is-active');
    }

    document.addEventListener('pointermove', (event) => {
        const plot = event.target.closest?.('[data-trend-plot]');
        if (plot) {
            activeTrend = { plot, x: event.clientX };
            renderTrendTip(plot, event.clientX);
        }
    }, { passive: true });

    document.addEventListener('pointerout', (event) => {
        const plot = event.target.closest?.('[data-trend-plot]');
        if (plot && !plot.contains(event.relatedTarget)) {
            plot.querySelector('.trend__overlay')?.classList.remove('is-active');
            activeTrend = null;
        }
    });

    // Werte aktualisieren sich alle 500 ms – Tooltip mitziehen, solange der Zeiger darauf steht.
    setInterval(() => {
        if (activeTrend?.plot.isConnected) {
            renderTrendTip(activeTrend.plot, activeTrend.x);
        }
    }, 500);

    // ---------- Hilfsfunktionen für WebAssembly-Komponenten (JS-Interop) ----------
    window.kk = Object.assign(window.kk || {}, {
        canvasSize(canvas) {
            const rect = canvas.getBoundingClientRect();
            return [rect.width, rect.height];
        },
        drawRows(canvas, y, width, rows, bytes) {
            const context = canvas.getContext('2d');
            const data = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
            const pixels = new Uint8ClampedArray(data.buffer, data.byteOffset, data.byteLength);
            context.putImageData(new ImageData(pixels, width, rows), 0, y);
        },
    });

    // ---------- Initialisierung ----------
    function init() {
        updateHeader();
        setNavigation(false);
        initReveal();
        initScrollSpy();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }

    window.Blazor?.addEventListener?.('enhancedload', init);
})();
