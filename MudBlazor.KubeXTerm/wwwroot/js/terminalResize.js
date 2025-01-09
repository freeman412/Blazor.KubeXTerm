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