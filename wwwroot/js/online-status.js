// Bridge tra eventi window.online/offline e il servizio C# OnlineStatusService.
// Esposto su window.ttOnline; il .NET invoca register(dotNetRef) una volta sola.
(function () {
    const state = {
        registered: false,
        dotNetRef: null
    };

    window.ttOnline = {
        register: function (dotNetRef) {
            if (state.registered) return navigator.onLine;
            state.registered = true;
            state.dotNetRef = dotNetRef;

            window.addEventListener('online', function () {
                if (state.dotNetRef) state.dotNetRef.invokeMethodAsync('OnOnlineChange', true);
            });
            window.addEventListener('offline', function () {
                if (state.dotNetRef) state.dotNetRef.invokeMethodAsync('OnOnlineChange', false);
            });

            return navigator.onLine;
        }
    };
})();
