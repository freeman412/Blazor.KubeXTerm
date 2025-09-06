XtermBlazor.registerAddons({ "addon-fit": new FitAddon.FitAddon() });

let resizeCallback;

window.registerViewportChangeCallback = (dotnetHelper) => {
    resizeCallback = () => {
        dotnetHelper.invokeMethodAsync('OnResize', window.innerWidth, window.innerHeight);
    };
    window.addEventListener('load', resizeCallback);
    window.addEventListener('resize', resizeCallback);
};

window.unregisterViewportChangeCallback = () => {
    if (resizeCallback) {
        window.removeEventListener('load', resizeCallback);
        window.removeEventListener('resize', resizeCallback);
        resizeCallback = null;
    }
};

window.kxt = window.kxt || {};
window.kxt._observers = {};

window.kxt.registerDrawerSizeWatcher = (hostId) => {
    const el = document.getElementById(hostId);
    if (!el) return;
    if (window.kxt._observers[hostId]) return;

    const ro = new ResizeObserver(() => {
        window.dispatchEvent(new Event('resize'));
    });
    ro.observe(el);
    window.kxt._observers[hostId] = ro;
    setTimeout(() => window.dispatchEvent(new Event('resize')), 250);
};

window.kxt.unregisterDrawerSizeWatcher = (hostId) => {
    const ro = window.kxt._observers[hostId];
    if (ro) {
        ro.disconnect();
        delete window.kxt._observers[hostId];
    }
};

window.kxt.triggerWindowResize = () => {
    window.dispatchEvent(new Event('resize'));
};

window.kxt._drawerAutoFitObservers = {};

function kxThrottle(fn, ms) {
    let last = 0, timer;
    return (...args) => {
        const now = Date.now();
        if (now - last >= ms) {
            last = now;
            fn(...args);
        } else {
            clearTimeout(timer);
            timer = setTimeout(() => {
                last = Date.now();
                fn(...args);
            }, ms - (now - last));
        }
    };
}

function dispatchTerminalFit() {
    window.dispatchEvent(new Event('resize'));
}

const liveFit = kxThrottle(dispatchTerminalFit, 50);
let finalFitTimer;

window.kxt._findResizableDrawer = (hostEl) => {
    let cur = hostEl.parentElement;
    while (cur && cur !== document.body) {
        if (
            cur.classList.contains('mudx-drawer') ||
            cur.classList.contains('mudx-bottom-drawer') ||
            cur.classList.contains('mud-drawer') ||
            cur.getAttribute('data-drawer') === 'bottom'
        ) {
            return cur;
        }
        cur = cur.parentElement;
    }
    return hostEl.parentElement || hostEl;
};

window.kxt.registerDrawerAutoFit = (hostId) => {
    if (window.kxt._drawerAutoFitObservers[hostId]) return;

    const host = document.getElementById(hostId);
    if (!host) {
        setTimeout(() => window.kxt.registerDrawerAutoFit(hostId), 60);
        return;
    }

    const container = window.kxt._findResizableDrawer(host);
    if (!container) return;

    const ro = new ResizeObserver(() => {
        liveFit();
        clearTimeout(finalFitTimer);
        finalFitTimer = setTimeout(() => dispatchTerminalFit(), 140);
    });

    ro.observe(container);
    window.kxt._drawerAutoFitObservers[hostId] = { ro, container };

    setTimeout(dispatchTerminalFit, 40);
    setTimeout(dispatchTerminalFit, 180);
    setTimeout(dispatchTerminalFit, 400);
};

window.kxt.unregisterDrawerAutoFit = (hostId) => {
    const entry = window.kxt._drawerAutoFitObservers[hostId];
    if (entry) {
        entry.ro.disconnect();
        delete window.kxt._drawerAutoFitObservers[hostId];
    }
};

// Add missing aliases for the drawer host component
//window.kxt.registerDrawerAdaptiveFit = window.kxt.registerDrawerAutoFit;
//window.kxt.unregisterDrawerAdaptiveFit = window.kxt.unregisterDrawerAutoFit;

window.kxterm = window.kxterm || {};

(function () {
    const map = new WeakMap();

    function throttle(fn, ms) {
        let last = 0, t;
        return (...a) => {
            const now = Date.now();
            if (now - last >= ms) {
                last = now;
                fn(...a);
            } else {
                clearTimeout(t);
                t = setTimeout(() => { last = Date.now(); fn(...a); }, ms - (now - last));
            }
        };
    }

    function findResizeTarget(host) {
        // FIXED: Always observe the drawer container that actually resizes
        const drawerContainer = window.kxt._findResizableDrawer(host);
        if (drawerContainer) {
            return drawerContainer;
        }
        
        // Fallback to immediate parent
        return host.parentElement || host;
    }

    window.kxterm.register = (hostEl, dotnetRef) => {
        if (!hostEl || map.has(hostEl)) return;

        const target = findResizeTarget(hostEl);
        const invoke = throttle(() => {
            dotnetRef.invokeMethodAsync('FitNow').catch(() => { });
        }, 50);

        const ro = new ResizeObserver(() => invoke());
        ro.observe(target);

        [20, 120, 300].forEach(d => setTimeout(() => invoke(), d));

        map.set(hostEl, { ro, dotnetRef, target });
    };

    window.kxterm.unregister = (hostEl) => {
        const entry = map.get(hostEl);
        if (!entry) return;
        entry.ro.disconnect();
        map.delete(hostEl);
    };
})();