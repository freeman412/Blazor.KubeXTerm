// Xterm addon
XtermBlazor.registerAddons({ "addon-fit": new FitAddon.FitAddon() });

/* Namespaces */
window.kxt = window.kxt || {};
window.kxterm = window.kxterm || {};
const KXT_UTIL = (() => {
  function throttle(fn, ms){
    let last=0,t;
    return (...a)=>{
      const now=Date.now();
      if(now-last>=ms){ last=now; fn(...a); }
      else{
        clearTimeout(t);
        t=setTimeout(()=>{ last=Date.now(); fn(...a); }, ms-(now-last));
      }
    };
  }
  function dispatchFit(){ window.dispatchEvent(new Event('resize')); }
  function findDrawerContainer(host){
    let cur = host && host.parentElement;
    while(cur && cur!==document.body){
      if(cur.classList.contains('mudx-drawer') ||
         cur.classList.contains('mudx-bottom-drawer') ||
         cur.classList.contains('mud-drawer') ||
         cur.getAttribute('data-drawer') === 'bottom'){
        return cur;
      }
      cur = cur.parentElement;
    }
    return host && host.parentElement ? host.parentElement : host;
  }
  return { throttle, dispatchFit, findDrawerContainer };
})();

/* Viewport (WelcomeTerminal) */
let _viewportCb;
window.registerViewportChangeCallback = (dotnetHelper) => {
  _viewportCb = () => dotnetHelper.invokeMethodAsync('OnResize', window.innerWidth, window.innerHeight);
  window.addEventListener('load', _viewportCb);
  window.addEventListener('resize', _viewportCb);
};
window.unregisterViewportChangeCallback = () => {
  if(!_viewportCb) return;
  window.removeEventListener('load', _viewportCb);
  window.removeEventListener('resize', _viewportCb);
  _viewportCb = null;
};

/* Adaptive drawer fit (USED) */
(function(){
  const active = window.kxt._adaptive = window.kxt._adaptive || {};
  const LIVE_THROTTLE_MS = 60;
  const FINAL_DEBOUNCE_MS = 140;
  const BURST = [40,180,400];
  let polling=false;
  function startPolling(container, onChange){
    if(polling) return;
    polling=true;
    let lastRect = container.getBoundingClientRect();
    function step(){
      if(!polling) return;
      const rect = container.getBoundingClientRect();
      if(Math.abs(rect.height - lastRect.height) > .5){
        onChange();
        lastRect = rect;
      }
      requestAnimationFrame(step);
    }
    requestAnimationFrame(step);
  }
  function stopPolling(){ polling=false; }
  active.registerDrawerAdaptiveFit = (hostId) => {
    if(active[hostId]) return;
    const host = document.getElementById(hostId);
    if(!host){ setTimeout(()=>active.registerDrawerAdaptiveFit(hostId),50); return; }
    const container = KXT_UTIL.findDrawerContainer(host);
    if(!container) return;
    const liveFit = KXT_UTIL.throttle(KXT_UTIL.dispatchFit, LIVE_THROTTLE_MS);
    let finalTimer;
    let pointerDown=false;
    const pointerDownHandler = e=>{
      if(e.buttons!==1) return;
      pointerDown=true;
      startPolling(container, liveFit);
    };
    const pointerUpHandler = ()=>{
      if(!pointerDown) return;
      pointerDown=false;
      stopPolling();
      clearTimeout(finalTimer);
      finalTimer=setTimeout(KXT_UTIL.dispatchFit, FINAL_DEBOUNCE_MS);
    };
    const ro = new ResizeObserver(()=>{
      if(!pointerDown){
        liveFit();
        clearTimeout(finalTimer);
        finalTimer=setTimeout(KXT_UTIL.dispatchFit, FINAL_DEBOUNCE_MS);
      }
    });
    ro.observe(container);
    container.addEventListener('pointerdown', pointerDownHandler, true);
    window.addEventListener('pointerup', pointerUpHandler, true);
    window.addEventListener('pointercancel', pointerUpHandler, true);
    BURST.forEach(d=>setTimeout(KXT_UTIL.dispatchFit,d));
    active[hostId] = { ro, container, pointerDownHandler, pointerUpHandler };
  };
  active.unregisterDrawerAdaptiveFit = (hostId)=>{
    const entry = active[hostId];
    if(!entry) return;
    entry.ro.disconnect();
    entry.container.removeEventListener('pointerdown', entry.pointerDownHandler, true);
    window.removeEventListener('pointerup', entry.pointerUpHandler, true);
    window.removeEventListener('pointercancel', entry.pointerUpHandler, true);
    delete active[hostId];
    if(Object.keys(active).length===0) stopPolling();
  };
  // Public wrappers (used by Razor)
  window.kxt.registerDrawerAdaptiveFit = id => active.registerDrawerAdaptiveFit(id);
  window.kxt.unregisterDrawerAdaptiveFit = id => active.unregisterDrawerAdaptiveFit(id);
})();

/* Per-terminal resize (USED) */
(function(){
  const map = new WeakMap();
  const TERM_THROTTLE = 50;
  const BURST = [20,120,300];
  function findResizeTarget(host){
    let cur = host;
    while(cur && cur!==document.body){
      const style = getComputedStyle(cur);
      if(style.position!=='static' || style.height!=='100%')
        return cur;
      cur = cur.parentElement;
    }
    return host;
  }
  window.kxterm.register = (hostEl, dotnetRef)=>{
    if(!hostEl || map.has(hostEl)) return;
    const target = findResizeTarget(hostEl);
    const invoke = KXT_UTIL.throttle(()=>dotnetRef.invokeMethodAsync('FitNow').catch(()=>{}), TERM_THROTTLE);
    const ro = new ResizeObserver(()=>invoke());
    ro.observe(target);
    BURST.forEach(d=>setTimeout(invoke,d));
    map.set(hostEl,{ ro, target, invoke, dotnetRef });
  };
  window.kxterm.unregister = (hostEl)=>{
    const entry = map.get(hostEl);
    if(!entry) return;
    entry.ro.disconnect();
    map.delete(hostEl);
  };
})();

/* Deprecated / legacy APIs (unused – kept for compatibility) */
(function(){
  const once = f=>{ let done=false; return (...a)=>{ if(!done){ done=true; console.warn(f); } }; };
  function noop(){}
  // registerDrawerSizeWatcher / unregister / triggerWindowResize
  const legacy = {
    registerDrawerSizeWatcher: once('[Deprecated] kxt.registerDrawerSizeWatcher called'),
    unregisterDrawerSizeWatcher: once('[Deprecated] kxt.unregisterDrawerSizeWatcher called'),
    triggerWindowResize: once('[Deprecated] kxt.triggerWindowResize called'),
    registerDrawerAutoFit: once('[Deprecated] kxt.registerDrawerAutoFit called'),
    unregisterDrawerAutoFit: once('[Deprecated] kxt.unregisterDrawerAutoFit called')
  };
  // Keep old behavior minimally (just dispatch a fit once)
  legacy.registerDrawerSizeWatcher = (id)=>{
    console.warn('[Deprecated] kxt.registerDrawerSizeWatcher');
    KXT_UTIL.dispatchFit();
  };
  legacy.unregisterDrawerSizeWatcher = ()=>{};
  legacy.triggerWindowResize = ()=>KXT_UTIL.dispatchFit();
  legacy.registerDrawerAutoFit = (id)=>{
    console.warn('[Deprecated] kxt.registerDrawerAutoFit');
    KXT_UTIL.dispatchFit();
  };
  legacy.unregisterDrawerAutoFit = ()=>{};
  Object.assign(window.kxt, legacy);
})();