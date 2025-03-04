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


//XtermBlazor.registerAddons({ "xterm-addon-fit": new FitAddon.FitAddon() });

//window.serverTerminal = {
//    registerResize: function (id) {
//        window.serverTerminal.terminalId = id;

//        window.addEventListener("resize", this.handle);
//    },
//    handle: function (a) {
//        XtermBlazor.invokeAddonFunction(window.serverTerminal.terminalId, "xterm-addon-fit", "fit");
//    },
//    unregisterResize: function (id) {
//        window.removeEventListener("resize", this.handle);
//    }
//}
