window.toggleFullscreen = (element) => {
    if (document.fullscreenElement) {
        document.exitFullscreen();
        return;
    }

    const target = element || document.documentElement;
    target.requestFullscreen();
};
