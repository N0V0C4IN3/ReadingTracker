# Blazor WebAssembly, not Blazor Server

The frontend is Blazor WASM: it runs entirely in the browser and calls the Gateway over plain HTTP, like any other client. Blazor Server was rejected specifically because it depends on a persistent SignalR circuit to a single backend process — which is both awkward to square with a multi-service architecture and actively broken by scale-to-zero hosting (Azure Container Apps recycles idle instances, dropping the circuit).
