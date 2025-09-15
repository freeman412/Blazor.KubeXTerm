window.sandboxSplitter = (function () {

    let moveHandler, upHandler;
    let leftEl, splitterEl, containerEl;
    let dragging = false;

    function init(containerId, leftId, splitterId) {
        containerEl = document.getElementById(containerId);
        leftEl = document.getElementById(leftId);
        splitterEl = document.getElementById(splitterId);
        if (!containerEl || !leftEl || !splitterEl) return;

        splitterEl.addEventListener('mousedown', onMouseDown);

        moveHandler = onMouseMove;
        upHandler = onMouseUp;

        window.addEventListener('mouseup', upHandler);
        window.addEventListener('mousemove', moveHandler);
    }

    function onMouseDown(e) {
        dragging = true;
        document.body.classList.add('resizing');
        e.preventDefault();
    }

    function onMouseMove(e) {
        if (!dragging) return;
        const rect = containerEl.getBoundingClientRect();
        let pct = ((e.clientX - rect.left) / rect.width) * 100;
        pct = Math.max(15, Math.min(60, pct)); // bounds
        leftEl.style.flex = `0 0 ${pct}%`;
    }

    function onMouseUp() {
        if (!dragging) return;
        dragging = false;
        document.body.classList.remove('resizing');
    }

    function dispose() {
        if (splitterEl)
            splitterEl.removeEventListener('mousedown', onMouseDown);
        window.removeEventListener('mouseup', upHandler);
        window.removeEventListener('mousemove', moveHandler);
    }

    return { init, dispose };
})();