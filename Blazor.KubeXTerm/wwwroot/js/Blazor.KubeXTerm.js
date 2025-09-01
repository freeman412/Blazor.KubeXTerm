XtermBlazor.registerAddons({ "addon-fit": new FitAddon.FitAddon() });

let resizeCallback;

window.registerViewportChangeCallback = (dotnetHelper) => {
    // Save the callback to remove it later
    resizeCallback = () => {
        dotnetHelper.invokeMethodAsync('OnResize', window.innerWidth, window.innerHeight);
    };

    // Attach event listeners
    window.addEventListener('load', resizeCallback);
    window.addEventListener('resize', resizeCallback);
};  

window.unregisterViewportChangeCallback = () => {
    if (resizeCallback) {
        // Remove event listeners
        window.removeEventListener('load', resizeCallback);
        window.removeEventListener('resize', resizeCallback);

        // Clear the saved reference
        resizeCallback = null;
    }
};

// ---- Drawer / Terminal sizing integration ----
window.kxt = window.kxt || {};

window.kxt._observers = {};

window.kxt.registerDrawerSizeWatcher = (hostId) => {
    const el = document.getElementById(hostId);
    if (!el) return;
    if (window.kxt._observers[hostId]) return;

    const ro = new ResizeObserver(() => {
        // Dispatch a synthetic window resize so existing terminal listener runs
        window.dispatchEvent(new Event('resize'));
    });
    ro.observe(el);
    window.kxt._observers[hostId] = ro;

    // Also delay-fire once after potential open animation
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

// --- Drawer auto-fit with live + final debounce ---
window.kxt = window.kxt || {};
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
    // Reuse existing terminal OnResize handler
    window.dispatchEvent(new Event('resize'));
}

const liveFit = kxThrottle(dispatchTerminalFit, 50);
let finalFitTimer;

window.kxt._findResizableDrawer = (hostEl) => {
    // Walk up; adjust class filters to REAL classes you see in dev tools if desired
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
    if (window.kxt._drawerAutoFitObservers[hostId])
        return;

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
        // Final post-drag fit after user stops resizing
        finalFitTimer = setTimeout(() => dispatchTerminalFit(), 140);
    });

    ro.observe(container);
    window.kxt._drawerAutoFitObservers[hostId] = { ro, container };

    // Initial fits after open (multiple to catch animations)
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

// ---- Adaptive drawer -> xterm fit integration ----
window.kxt = window.kxt || {};
window.kxt._adaptive = window.kxt._adaptive || {};

(function() {
    const active = window.kxt._adaptive;
    const LIVE_THROTTLE_MS = 60;
    const FINAL_DEBOUNCE_MS = 140;

    function throttle(fn, ms){
        let last=0, t;
        return (...a)=>{
            const now=Date.now();
            if(now-last>=ms){ last=now; fn(...a); }
            else{
                clearTimeout(t);
                t=setTimeout(()=>{ last=Date.now(); fn(...a); }, ms-(now-last));
            }
        };
    }

    function dispatchFit(){
        window.dispatchEvent(new Event('resize'));
    }

    const liveFit = throttle(dispatchFit, LIVE_THROTTLE_MS);
    let polling = false;

    function findDrawerContainer(host){
        // Walk up; adjust if you see different classes
        let cur = host.parentElement;
        while(cur && cur!==document.body){
            if(cur.classList.contains('mudx-drawer') ||
               cur.classList.contains('mudx-bottom-drawer') ||
               cur.classList.contains('mud-drawer') ||
               cur.getAttribute('data-drawer') === 'bottom'){
                return cur;
            }
            cur = cur.parentElement;
        }
        return host.parentElement || host;
    }

    function startTransformPolling(container){
        if(polling) return;
        polling = true;
        let lastRect = container.getBoundingClientRect();
        function step(){
            if(!polling) return;
            const rect = container.getBoundingClientRect();
            if(Math.abs(rect.height - lastRect.height) > 0.5){
                liveFit();
                lastRect = rect;
            }
            requestAnimationFrame(step);
        }
        requestAnimationFrame(step);
    }

    function stopTransformPolling(){
        polling = false;
    }

    active.registerDrawerAdaptiveFit = (hostId) => {
        if(active[hostId]) return;

        const host = document.getElementById(hostId);
        if(!host){
            setTimeout(()=>active.registerDrawerAdaptiveFit(hostId), 50);
            return;
        }
        const container = findDrawerContainer(host);
        if(!container) return;

        let finalTimer;
        let pointerDown = false;

        const pointerDownHandler = e => {
            if(e.buttons !== 1) return;
            pointerDown = true;
            startTransformPolling(container);
        };
        const pointerUpHandler = () => {
            if(!pointerDown) return;
            pointerDown = false;
            stopTransformPolling();
            clearTimeout(finalTimer);
            finalTimer = setTimeout(()=>dispatchFit(), FINAL_DEBOUNCE_MS);
        };

        // Observe actual size changes (if height animates)
        const ro = new ResizeObserver(() => {
            if(!pointerDown){
                liveFit();
                clearTimeout(finalTimer);
                finalTimer = setTimeout(()=>dispatchFit(), FINAL_DEBOUNCE_MS);
            }
        });
        ro.observe(container);

        // Attach to whole container (or refine to specific handle if you have its selector)
        container.addEventListener('pointerdown', pointerDownHandler, true);
        window.addEventListener('pointerup', pointerUpHandler, true);
        window.addEventListener('pointercancel', pointerUpHandler, true);

        // Multiphase initial fits
        [40, 180, 400].forEach(d => setTimeout(()=>dispatchFit(), d));

        active[hostId] = {
            ro,
            container,
            pointerDownHandler,
            pointerUpHandler
        };
    };

    active.unregisterDrawerAdaptiveFit = (hostId) => {
        const entry = active[hostId];
        if(!entry) return;
        entry.ro.disconnect();
        entry.container.removeEventListener('pointerdown', entry.pointerDownHandler, true);
        window.removeEventListener('pointerup', entry.pointerUpHandler, true);
        window.removeEventListener('pointercancel', entry.pointerUpHandler, true);
        delete active[hostId];
        if(Object.keys(active).length === 0){
            stopTransformPolling();
        }
    };
})();

// Public API wrappers called from .razor
window.kxt.registerDrawerAdaptiveFit = (hostId) => window.kxt._adaptive.registerDrawerAdaptiveFit(hostId);
window.kxt.unregisterDrawerAdaptiveFit = (hostId) => window.kxt._adaptive.unregisterDrawerAdaptiveFit(hostId);

// Minimal per-terminal resize observation
window.kxterm = window.kxterm || {};

(function(){
    const map = new WeakMap();

    // Throttle helper
    function throttle(fn, ms){
        let last = 0, t;
        return (...a)=>{
            const now = Date.now();
            if (now - last >= ms){
                last = now;
                fn(...a);
            } else {
                clearTimeout(t);
                t = setTimeout(()=>{ last = Date.now(); fn(...a); }, ms - (now - last));
            }
        };
    }

    function findResizeTarget(host){
        // Host may stay 100% height while parent resizes; climb until a non-100% ancestor actually changes pixel height.
        let cur = host;
        while (cur && cur !== document.body){
            const style = getComputedStyle(cur);
            if (style.position !== 'static' || style.height !== '100%') {
                // Good candidate; still return first scroll container if present
                return cur;
            }
            cur = cur.parentElement;
        }
        return host;
    }

    window.kxterm.register = (hostEl, dotnetRef) => {
        if (!hostEl || map.has(hostEl)) return;

        const target = findResizeTarget(hostEl);
        const invoke = throttle(() => {
            dotnetRef.invokeMethodAsync('FitNow').catch(()=>{});
        }, 50);

        const ro = new ResizeObserver(() => invoke());
        ro.observe(target);

        // Initial staged fits to handle animations
        [20, 120, 300].forEach(d => setTimeout(()=>invoke(), d));

        map.set(hostEl, { ro, dotnetRef, target });
    };

    window.kxterm.unregister = (hostEl) => {
        const entry = map.get(hostEl);
        if (!entry) return;
        entry.ro.disconnect();
        map.delete(hostEl);
    };
})();