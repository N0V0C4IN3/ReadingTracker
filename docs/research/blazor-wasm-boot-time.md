# What actually moves Blazor WASM boot time in .NET 10

Research for [#72](https://github.com/N0V0C4IN3/ReadingTracker/issues/72), part of the
[Make ReadingTracker fast on the free tier](https://github.com/N0V0C4IN3/ReadingTracker/issues/69) map.

**Scope.** `src/ReadingTracker.Web` is a standalone Blazor WebAssembly app targeting `net10.0`,
published as static files to Azure Static Web Apps. The complaint is **start-up time**, not
steady-state execution speed. That distinction decides most of the answers below: a lever that
makes running code faster while making the download bigger is the wrong lever here, and is called
out as such.

**Sources.** Microsoft Learn (ASP.NET Core 10.0 moniker), the Azure Static Web Apps documentation,
and — where the prose is ambiguous about defaults — the `dotnet/sdk` MSBuild targets themselves.
No blog posts or Stack Overflow answers are load-bearing. Much of the Blazor material online
predates .NET 10; every place the current behaviour differs from older guidance is flagged as a
**trap**.

---

## TL;DR — what a plain `dotnet publish -c Release` already does

This is the single most useful thing in this document, because roughly half the advice online is to
switch on things that are already on.

| Behaviour | On by default in .NET 10? | Property | Verified at |
| --- | --- | --- | --- |
| IL trimming | **Yes**, on publish | `PublishTrimmed` = `true` | [SDK props](https://github.com/dotnet/sdk/blob/release/10.0.1xx/src/BlazorWasmSdk/Targets/Microsoft.NET.Sdk.BlazorWebAssembly.Current.props) |
| Trimmer granularity | **Yes**, `partial` | `TrimMode` = `partial` | same file |
| Brotli **and** Gzip artefacts | **Yes**, on publish | `PublishCompressionFormats` = `gzip;brotli` | [Compression targets](https://github.com/dotnet/sdk/blob/release/10.0.1xx/src/StaticWebAssetsSdk/Targets/Microsoft.NET.Sdk.StaticWebAssets.Compression.targets) |
| Compression at all | **Yes** for net6.0+ | `CompressionEnabled` = `true` | [Static web assets targets](https://github.com/dotnet/sdk/blob/release/10.0.1xx/src/StaticWebAssetsSdk/Targets/Sdk.StaticWebAssets.CurrentVersion.targets) |
| Webcil packaging (`.wasm` assemblies) | **Yes** | `WasmEnableWebcil` = `true` | [Host and deploy](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly/?view=aspnetcore-10.0#webcil-packaging-format-for-net-assemblies) |
| WebAssembly exception handling | **Yes** | `WasmEnableExceptionHandling` = `true` | [Build tools and AOT](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot?view=aspnetcore-10.0#exception-handling) |
| SIMD | **Yes** | `WasmEnableSIMD` = `true` | [Runtime performance](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/webassembly-runtime-performance?view=aspnetcore-10.0#single-instruction-multiple-data-simd) |
| Sharded ICU (culture subset, not full) | **Yes** | `BlazorWebAssemblyLoadAllGlobalizationData` = `false` | [Globalization](https://learn.microsoft.com/en-us/aspnet/core/blazor/globalization-localization?view=aspnetcore-10.0#net-globalization-and-international-components-for-unicode-icu-support-blazor-webassembly) |
| Invariant globalization | **No** | `InvariantGlobalization` = `false` | [Globalization](https://learn.microsoft.com/en-us/aspnet/core/blazor/globalization-localization?view=aspnetcore-10.0#invariant-globalization) |
| **Runtime relinking of `dotnet.wasm`** | **No** — needs the `wasm-tools` workload | n/a | [Runtime performance](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/webassembly-runtime-performance?view=aspnetcore-10.0#runtime-relinking) |
| AOT compilation | **No** | `RunAOTCompilation` = `false` | [Build tools and AOT](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot?view=aspnetcore-10.0#ahead-of-time-aot-compilation) |

The one default that matters most and is easiest to miss: **runtime relinking does not happen
unless the `wasm-tools` workload is installed on the build machine.** ReadingTracker's deploy
workflow runs a bare `dotnet publish --configuration Release`
(`.github/workflows/deploy-web.yml`) with no `dotnet workload install wasm-tools` step, so the
`dotnet.wasm` runtime shipped today is the un-relinked one — the largest single asset, untrimmed.

---

## 1. What Release publish does by default (trimming and globalization)

### Trimming is on, and it is `partial`

> Blazor WebAssembly performs [Intermediate Language (IL)](https://learn.microsoft.com/en-us/dotnet/standard/glossary#il) trimming to reduce the size of the published output. Trimming occurs when publishing an app.
>
> — [Configure the Trimmer for ASP.NET Core Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/configure-trimmer?view=aspnetcore-10.0)

Confirmed in the SDK rather than inferred from prose. `Microsoft.NET.Sdk.BlazorWebAssembly.Current.props`
sets, unconditionally (not gated on `Release`):

```xml
<!-- Trimmer defaults -->
<PublishTrimmed Condition="'$(PublishTrimmed)' == ''">true</PublishTrimmed>
<TrimMode Condition="'$(TrimMode)' == ''">partial</TrimMode>
```

The properties are always set; trimming only *runs* on publish.

**What `partial` strips.** Per the [trimmer doc](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/configure-trimmer?view=aspnetcore-10.0#default-trimmer-granularity):

> The default trimmer granularity for Blazor apps is `partial`, which means that only core framework libraries and libraries that have explicitly enabled trimming support are trimmed. **Full trimming isn't supported.**

So the base class library is trimmed; the app's own code and any library not marked
`<IsTrimmable>true</IsTrimmable>` is not. For a four-page app this is the right shape already —
the app's own IL is a rounding error next to the BCL and the runtime.

**What breaks under it.** Reflection is the whole story:

> In apps that use reflection, the IL Trimmer often can't determine the required types for runtime reflection and trims them away or trims away parameter names from methods. This can happen with complex framework types used for JS interop, JSON serialization/deserialization, and other operations.

The documented worked example is `JsonSerializer.Deserialize<List<Tuple<string, string>>>` throwing
`ConstructorContainsNullParameterNames` after publish. Two points worth internalising:

- The doc explicitly notes this happens **"even in spite of setting the `<PublishTrimmed>` property
  to `false`"** — turning trimming off is not a reliable escape hatch, because framework assemblies
  arrive pre-trimmed.
- The recommended fix is **custom types in non-trimmable assemblies**, not `[DynamicDependency]` or
  root descriptors, though .NET 10 documents both of those as fallbacks
  ([Root Descriptor](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/configure-trimmer?view=aspnetcore-10.0#use-a-root-descriptor) is new in the .NET 10 moniker).

Relevant to this app: DTOs crossing the Gateway boundary are deserialized with `System.Text.Json`.
As long as they are the project's own concrete classes — not framework generics like `Tuple<,>` —
they are safe. **Test the published output, not just `dotnet run`**; the doc is blunt that the
trimmer cannot see dynamic behaviour and that published output must be exercised regularly.

There is a second trimming hazard that only appears *once relinking is enabled*:

> Runtime relinking trims class instance JavaScript-invokable .NET methods unless they're protected.

The app has JS interop (`window.readingTracker`, cover-settling callbacks). Those are JS **called
from** .NET, which is unaffected; the risk is `[JSInvokable]` instance methods called **from** JS.
Worth a grep before switching relinking on.

### Globalization: the subset is already the default

The most repeated piece of stale advice in this area is "set
`BlazorWebAssemblyLoadAllGlobalizationData` to `false`". **That is already the default and always
has been** — the property exists to turn the full data set *on*:

> By default, Blazor loads a subset of globalization data that contains the app's culture. To load all globalization data, set `<BlazorWebAssemblyLoadAllGlobalizationData>` to `true`.

.NET 8 and later ship four sharded ICU files, and the app's culture selects one automatically:

- `icudt.dat` — full data
- `icudt_EFIGS.dat` — `en-*`, `fr-FR`, `es-ES`, `it-IT`, `de-DE`
- `icudt_CJK.dat` — `en-*`, `ja`, `ko`, `zh-*`
- `icudt_no_CJK.dat` — everything in `icudt.dat` except `ja`, `ko`, `zh-*`

> If a file isn't specified with `<BlazorIcuDataFileName>`, the app's culture is checked, and the corresponding ICU file is loaded for its culture. For example, the `en-US` culture results in loading the `icudt_EFIGS.dat` file.

So ReadingTracker today ships `icudt_EFIGS.dat` — already the small one, not the full one.
`BlazorIcuDataFileName` could not improve on that without going invariant.

**`InvariantGlobalization` still matters, and is still off by default.**

> If the app doesn't require localization, configure the app to support the invariant culture, which is generally based on United States English (`en-US`). **Using invariant globalization reduces the app's download size and results in faster app startup.**

This is one of only two documented levers Microsoft explicitly ties to *startup*, and it compounds
with relinking:

> The size reduction is particularly dramatic when disabling globalization.
> — [Runtime relinking](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/webassembly-runtime-performance?view=aspnetcore-10.0#runtime-relinking)

It is a **behaviour** change, not just a size change. Per
[`globalization-invariant-mode.md`](https://github.com/dotnet/runtime/blob/main/docs/design/features/globalization-invariant-mode.md)
in `dotnet/runtime`: `PredefinedCulturesOnly` defaults to `true`, so constructing any culture other
than invariant throws; `Compare`/`IndexOf`/`LastIndexOf` become **ordinal regardless of the
comparison options passed**; casing is restricted; string normalization and IDN handling degrade.
For an English-only reading tracker this is almost certainly fine, but "almost certainly" is a
thing to verify against the date and number formatting on the shelf, not assume.

A companion property, new in .NET 8 and still current:

> Adopting invariant globalization only results in using non-localized timezone names. To trim timezone code and data from the app, apply the `<InvariantTimezone>` MSBuild property with a value of `true`.

**Trap:** `<BlazorEnableTimeZoneSupport>` is the pre-.NET-8 spelling and the docs now say to delete
it — *"`<BlazorEnableTimeZoneSupport>` overrides an earlier `<InvariantTimezone>` setting. We
recommend removing the `<BlazorEnableTimeZoneSupport>` setting."* Likewise
`BlazorWebAssemblyPreserveCollationData` is .NET Core 3.1-only guidance and no longer applies.

---

## 2. Compression — what is emitted, and how Static Web Apps serves it

### What publish emits

> When a Blazor WebAssembly app is published, the output is statically compressed during publish to reduce the app's size and remove the overhead for runtime compression. The following compression algorithms are used:
> - Brotli (highest level)
> - Gzip
>
> — [Host and deploy Blazor WebAssembly § Compression](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly/?view=aspnetcore-10.0#compression)

Confirmed in the SDK, which also reveals a build/publish asymmetry the prose does not mention:

```xml
<EnableDefaultCompressionFormats Condition="'$(EnableDefaultCompressionFormats)' == ''">true</EnableDefaultCompressionFormats>
<BuildCompressionFormats   Condition="'$(EnableDefaultCompressionFormats)' == 'true'">$(BuildCompressionFormats);gzip</BuildCompressionFormats>
<PublishCompressionFormats Condition="'$(EnableDefaultCompressionFormats)' == 'true'">$(PublishCompressionFormats);gzip;brotli</PublishCompressionFormats>
```

`build` produces Gzip only; **`publish` produces both Gzip and Brotli**. Because the Blazor WASM SDK
sets `StaticWebAssetProjectMode` to `Root`, `StaticWebAssetPublishCompressAllAssets` defaults to
`true`, so *all* publish assets are compressed, not just discovered ones. Compression itself is
gated on `CompressionEnabled`, which defaults to true for net6.0 and later.

**Trap:** the property to disable compression is `CompressionEnabled` in .NET 8+. The older name
`BlazorEnableCompression` appears throughout pre-.NET-8 material and **has no effect on net10.0**.

### How it is meant to be served, and what Static Web Apps actually does

> Blazor relies on the host to serve the appropriate compressed files.

The Blazor docs then offer a heavy workaround for hosts that cannot do content negotiation:
disable autostart, ship Google's `decode.min.js`, and override `loadBootResource` to fetch `.br`
and Brotli-decode in JavaScript. **ReadingTracker does not need this**, and adopting it would be a
straight loss — it would move decompression onto the main thread during boot, which is precisely
the window being optimised.

The reason is in the Static Web Apps FAQ, which is the primary source for the hosting half:

> For assets with file extensions of popular text formats, such as `.html`, `.css`, and `.js`, Azure Static Web Apps automatically serves Gzip- or Brotli-compressed versions of your static assets if the client supports it.
>
> For other file types, Static Web Apps allows you to include a Brotli-compressed version of your file with a `.br` extension. For example, if you have an uncompressed file named `app.wasm`, you can add a compressed version named `app.wasm.br` to your app. This version is automatically served if a client that supports Brotli requests `app.wasm`.
>
> — [Azure Static Web Apps FAQ](https://learn.microsoft.com/en-us/azure/static-web-apps/faq)

That is an exact fit for what Blazor publishes. Under Webcil, assemblies *are* `.wasm` files, and
publish emits a `.br` beside each one. The `.wasm` + `.wasm.br` example in the FAQ is literally this
scenario. So on paper the pairing is already correct and needs no configuration.

Two caveats worth measuring rather than trusting:

1. The FAQ describes the **`.br` sidecar only**. It says nothing about serving a `.gz` sidecar for
   non-text types, so the Gzip artefacts publish emits are probably dead weight in the upload for
   `_framework` (harmless — SWA bills on bandwidth served, not stored).
2. The download-size doc gives the verification step, and it is the right one to fold into the
   baseline in [#70](https://github.com/N0V0C4IN3/ReadingTracker/issues/70):

   > After an app is deployed, verify that the app serves compressed files. Inspect the **Network** tab in a browser's developer tools and verify that the files are served with `Content-Encoding: br` (Brotli compression) or `Content-Encoding: gz` (Gzip compression).

**This is a measurement, not a change** — and it is the highest-value thing on this list, because if
`Content-Encoding: br` is missing on `_framework/*`, every other lever here is noise by comparison.

---

## 3. AOT — the wrong lever for this complaint

The trade-off is stated plainly and is not ambiguous:

> AOT compilation results in runtime performance improvements **at the expense of a larger app size**.
>
> The drawback to using AOT compilation is that AOT-compiled apps are generally larger than their IL-interpreted counterparts, so they usually **take longer to download to the client when first requested**.
>
> Although the size difference depends on the app, **most AOT-compiled apps are about twice the size of their IL-compiled versions. This means that using AOT compilation trades off load-time performance for runtime performance.** Whether this tradeoff is worth using AOT compilation depends on your app. Blazor WebAssembly apps that are CPU intensive generally benefit the most from AOT compilation.
>
> — [Build tools and AOT](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot?view=aspnetcore-10.0#ahead-of-time-aot-compilation)

**Does AOT help boot time? No — it actively hurts it.** The question in the ticket has a clean
answer: AOT speeds up *steady-state execution* of CPU-bound .NET code and makes the *download*
roughly twice as large. For an app whose complaint is start-up, it is the wrong direction.

The size increase has two documented causes, the second of which is counter-intuitive:

> - More code is required to represent high-level .NET IL instructions in native WebAssembly.
> - **AOT doesn't trim out managed DLLs when the app is published.** Blazor requires the DLLs for reflection metadata and to support certain .NET runtime features.

So AOT ships native WebAssembly *in addition to* the managed assemblies — it does not replace them.

Other costs:

- **Build time.** *"AOT compilation usually takes several minutes on small projects and potentially much longer for larger projects."* It runs on publish only, never in `Development`. On a free-tier GitHub Actions runner that is added minutes on every deploy.
- **Toolchain.** Requires the `wasm-tools` workload (Emscripten-based), which the CI does not install today.
- **Mitigation exists but only relative to AOT's own baseline.** `WasmStripILAfterAOT` *"enables removing the .NET IL for compiled methods after performing AOT compilation to WebAssembly, which reduces the size of the `_framework` folder"* ([runtime performance](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/webassembly-runtime-performance?view=aspnetcore-10.0#trim-net-il-after-ahead-of-time-aot-compilation)). It claws back part of the doubling; it does not make AOT smaller than not using AOT.

Without AOT the app runs on the interpreter plus *Jiterpreter* (partial JIT). Nothing about
ReadingTracker — rendering four pages and awaiting HTTP — is CPU-bound, so there is no runtime win
being forgone.

**Verdict: do not enable AOT.** Note the asymmetry, though: the *same* `wasm-tools` workload that
AOT requires is also what enables runtime relinking, which is unambiguously good for boot time.
Install the workload; set `RunAOTCompilation` nowhere.

---

## 4. Lazy loading assemblies — nothing to defer

`BlazorWebAssemblyLazyLoad` marks a developer assembly to be withheld at launch and fetched on
navigation via `LazyAssemblyLoader` and the `Router`'s `OnNavigateAsync`.

Two constraints decide this for ReadingTracker:

> **Lazy loading shouldn't be used for core runtime assemblies**, which might be trimmed on publish and unavailable on the client when the app loads.
>
> — [Lazy load assemblies](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-lazy-load-assemblies?view=aspnetcore-10.0)

and the mechanism is per-**assembly**, not per-page or per-component. Splitting a route out of the
boot payload means first moving it into its own Razor class library.

`src/ReadingTracker.Web` is a single assembly with four routable components (`Home`,
`Authentication`, `NotFound`, plus layout and components) and no project references to RCLs. There
is nothing to mark. Even inventing an RCL would defer only the app's own IL — the smallest part of
the payload — while leaving `dotnet.wasm`, the BCL assemblies and the ICU file, which is where the
bytes actually are, untouched. It would also add an `OnNavigateAsync` code path, a `<Navigating>`
state, and cancellation handling that must throw on a set token, for no measurable gain.

**Verdict: not worth it at this size.** This is a lever for apps with a heavy admin area or a
charting library behind one route. Revisit only if a genuinely large, route-isolated dependency is
ever added.

---

## 5. Where the boot seconds actually go

**Be honest about this: Microsoft does not publish a phase-by-phase timing breakdown.** There is no
document saying "N% download, N% runtime instantiation, N% app start". Anyone quoting such a split
is quoting a blog post. What the primary sources *do* define is the set of phases and the
instrumentation to measure them — which is exactly why
[#70](https://github.com/N0V0C4IN3/ReadingTracker/issues/70) exists and must land before any of
this is acted on.

### The phases, as documented

The boot resources are enumerated in [Blazor startup](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/startup?view=aspnetcore-10.0#load-client-side-boot-resources):

> When an app loads in the browser, the app downloads boot resources from the server:
> - JavaScript code to bootstrap the app
> - .NET runtime and assemblies
> - Locale specific data

and `loadBootResource` names the resource types: `assembly`, `pdb`, `dotnetjs`, `dotnetwasm`,
`timezonedata`. That yields three phases, in order:

1. **Download** — `blazor.webassembly.js`, then `dotnet.js`, `dotnet.wasm`, the Webcil assemblies, `icudt_EFIGS.dat`, timezone data.
2. **Runtime instantiation** — the browser compiles and instantiates the WebAssembly module and the Mono runtime starts. Marked by the `onRuntimeConfigLoaded(config)` and `onRuntimeReady(...)` callbacks.
3. **The app's own start-up** — `Program.cs`, DI construction, the root component's first render and its `OnInitializedAsync`.

### Which phases are addressable, and from where

| Phase | Addressable from `.csproj`? | How |
| --- | --- | --- |
| Download | **Yes, mostly** | Relinking, `InvariantGlobalization`, `InvariantTimezone`, trimming (already on), compression (already on), plus the host actually serving `.br` |
| Runtime instantiation | **Partly** | Relinking makes a smaller module to compile; `WasmEnableSIMD`/`WasmEnableExceptionHandling` affect it but are already at their good defaults |
| App's own start-up | **No** | This is `Program.cs` and the first render — the Gateway → Library → Catalog chain. Not a project-file concern at all |

That third row is the important one for the wider map. The `.csproj` cannot touch it, and it is
where ReadingTracker's own cold-start work already lives (the recent
"[Overlap the Catalog round trip with Library's own queries](https://github.com/N0V0C4IN3/ReadingTracker/commit/4906528)"
change, and the container cold starts in #70). **The frontend build and the backend cold start are
separate budgets and should be measured separately.**

### Measuring it

- **`--blazor-load-percentage`** — a CSS custom property Blazor updates during boot, described in [Blazor WebAssembly app loading progress](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/startup?view=aspnetcore-10.0#blazor-webassembly-app-loading-progress) as *"the percentage of app files loaded"*. The app already drives its splash bar from it. Note carefully: it measures **phase 1 only**. When the bar hits 100% the runtime has not started yet — so the tail the reader experiences after the bar fills is phases 2 and 3, invisible to that indicator.
- **Browser profiler integration** — `<WasmProfilers>browser</WasmProfilers>` with `WasmNativeStrip=false`, per [browser developer tools diagnostics](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/webassembly-browser-developer-tools-diagnostics?view=aspnetcore-10.0). The docs warn: *"Enabling profilers has negative size and performance impacts, so don't publish an app for production with profilers enabled."* Gate it behind a custom property, as the doc shows. Also: *"Setting the `Timing-Allow-Origin` HTTP header allows for more precise time measurements"* — cheap to add to `staticwebapp.config.json` for a measurement run.
- **Network tab** — for `Content-Encoding` verification (§2) and per-asset transfer sizes.

---

## 6. What is .NET 10 specific

Nothing in the [.NET 10 runtime](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/runtime)
or [.NET 10 SDK](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/sdk) release
notes is WebAssembly-specific — the JIT, stack-allocation and Arm64 work described there is
server-side. **The .NET 10 boot-time changes are all on the ASP.NET Core side**, in
[What's new in ASP.NET Core in .NET 10](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0).

### Changes that help boot time

**Boot configuration inlined** — removes a serialized round trip from the critical path:

> Blazor's boot configuration, which prior to the release of .NET 10 existed in a file named `blazor.boot.json`, has been inlined into the `dotnet.js` script.

Before .NET 10 the browser had to fetch `blazor.webassembly.js`, then `blazor.boot.json`, then act
on its contents. That middle hop is gone. This is a free win already banked by targeting net10.0.

**Framework asset preloading in standalone WASM** — starts the big downloads before the parser
reaches the script tag at the end of `<body>`:

> In standalone Blazor WebAssembly apps, framework assets are scheduled for high priority downloading and caching early in browser `index.html` page processing when:
> - The `OverrideHtmlAssetPlaceholders` MSBuild property in the app's project file (`.csproj`) is set to `true`
> - The following `<link>` element containing `rel="preload"` is present in the `<head>` content of `wwwroot/index.html`:
>
> ```html
> <link rel="preload" id="webassembly" />
> ```

**ReadingTracker already satisfies both.** `ReadingTracker.Web.csproj` sets
`<OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>` and
`wwwroot/index.html` contains `<link rel="preload" id="webassembly" />`. This lever is spent —
do not "discover" it again later.

**Client-side fingerprinting** — a fingerprint is computed into the script file name via the
`blazor.webassembly#[.{fingerprint}].js` placeholder plus an `<script type="importmap"></script>`
in `<head>`. ReadingTracker already has both. This is a *warm*-load lever (immutable caching), not
a cold one — it pairs with the existing `cache-control: public, max-age=31536000, immutable` on
`/_framework/*` in `staticwebapp.config.json`.

### Traps — .NET 10 removed or changed things older guidance still recommends

| Older guidance | Status in .NET 10 |
| --- | --- |
| `<BlazorCacheBootResources>` to control caching | **Removed from the framework.** *"Now that all Blazor client-side files are fingerprinted and cached by the browser, Blazor's custom caching mechanism and the `BlazorCacheBootResources` MSBuild property have been removed... If the client-side project's project file contains the MSBuild property, remove the property, as it no longer has any effect."* |
| `<BlazorEnableCompression>false</BlazorEnableCompression>` | Wrong name since .NET 8; it is `CompressionEnabled` |
| `<BlazorEnableTimeZoneSupport>` | Docs recommend removing it; use `InvariantTimezone` |
| Read/patch `blazor.boot.json` | The file no longer exists — inlined into `dotnet.js` |
| `Blazor-Environment` header / `launchSettings.json` to set environment | *"no longer used to control the environment in standalone Blazor WebAssembly apps"* — use `<WasmApplicationEnvironmentName>`; defaults are `Development` for build, `Production` for publish |
| `.dll` renaming tricks for firewalls | Obsolete since .NET 8 — Webcil already ships assemblies as `.wasm` |
| `BlazorWebAssemblyPreserveCollationData=false` | .NET Core 3.1-era only |

### One .NET 10 behavioural change to be aware of (not boot-related, but a live hazard)

> In prior Blazor releases, response streaming for `HttpClient` requests was opt-in. Now, response streaming is enabled by default.
>
> This is a breaking change because calling `HttpContent.ReadAsStreamAsync` for an `HttpResponseMessage.Content` returns a `BrowserHttpReadStream` and no longer a `MemoryStream`.

The app uses `Microsoft.Extensions.Http` against the Gateway. If any code path takes the response
stream and expects seekability, it will break. Opt out per request with
`SetBrowserResponseStreamingEnabled(false)` or globally with `<WasmEnableStreamingResponse>false</WasmEnableStreamingResponse>`.

---

## Ranked shortlist for an app THIS small

Ordered by (boot-time benefit ÷ cost). Nothing here should be *built* before
[#70](https://github.com/N0V0C4IN3/ReadingTracker/issues/70) produces a baseline.

### Worth it

1. **Verify Static Web Apps is serving `Content-Encoding: br` on `_framework/*`.**
   Cost: zero — one look at the Network tab. If this is broken, the app is shipping an uncompressed
   .NET runtime and nothing else on this list matters by comparison. If it works (the FAQ says it
   should), this is confirmed off the list forever. **Do this first.**

2. **Install the `wasm-tools` workload in the deploy workflow, enabling runtime relinking.**
   *"One of the largest parts of a Blazor WebAssembly app is the WebAssembly-based .NET runtime
   (`dotnet.wasm`)... Relinking the .NET WebAssembly runtime trims unused runtime code and thus
   improves download speed."* It happens automatically on Release publish once the workload is
   present. Cost: one CI step and added build minutes; **no application code and no `.csproj`
   change**. Risk: trims unprotected `[JSInvokable]` instance methods — grep first. This is the
   single biggest untapped lever, and it is untapped purely because nobody installed the workload.

3. **`InvariantGlobalization=true` (plus `InvariantTimezone=true`).**
   The only other lever Microsoft explicitly ties to *"faster app startup"*, and its size reduction
   is *"particularly dramatic"* in combination with #2. Drops `icudt_EFIGS.dat` entirely. Cost:
   a real behaviour change — ordinal-only string comparison, `PredefinedCulturesOnly`, non-localized
   timezone names. Requires an actual check of how the shelf renders dates and numbers before
   committing. Worth it for an English-only app; not a blind switch-on.

### Not worth it

4. **AOT compilation — actively harmful here.** Roughly doubles the download to speed up CPU-bound
   execution this app does not do. Adds minutes to every deploy. It is the textbook wrong lever for
   a start-up complaint, and the fact that it shares a workload with #2 makes it easy to enable by
   accident. Don't.

5. **Lazy loading assemblies — nothing to defer.** One assembly, four pages, no RCLs. Cannot touch
   the runtime, BCL or ICU file, which is where the bytes are.

6. **`BlazorWebAssemblyLoadAllGlobalizationData=false` — a no-op.** Already the default. Setting it
   changes nothing; setting it to `true` would make things worse.

7. **`BlazorIcuDataFileName` — subsumed.** The culture-matched shard (`icudt_EFIGS.dat`) is already
   selected automatically, and if #3 lands there is no ICU file at all.

8. **`BlazorCacheBootResources` — removed in .NET 10.** Has no effect.

9. **The `loadBootResource` Brotli-decoder workaround — counterproductive on SWA.** SWA serves `.br`
   sidecars natively; adding a JS decoder would move decompression onto the main thread during the
   exact window being optimised.

### Already done — do not rediscover

- `.NET 10` preload (`<link rel="preload" id="webassembly" />` + `OverrideHtmlAssetPlaceholders`) — present.
- Client-side fingerprinting + import map — present.
- `immutable` caching on `/_framework/*` — present in `staticwebapp.config.json`.
- IL trimming, Brotli+Gzip artefacts, Webcil, SIMD, sharded ICU — all on by default.
- `blazor.boot.json` round trip — eliminated by targeting net10.0.

---

## Bearing on the map's assumptions

The Notes in [#69](https://github.com/N0V0C4IN3/ReadingTracker/issues/69) say:

> `ReadingTracker.Web.csproj` does nothing to tune the WASM payload — no trimming config, no lazy loading.

Literally true, but it invites the wrong inference. **The absence of trimming configuration does
not mean the absence of trimming** — `PublishTrimmed` is `true` and `TrimMode` is `partial` by SDK
default, and Brotli, Gzip, Webcil, SIMD and culture-subset ICU are all on without a line of
configuration. The payload is already tuned; what is missing is narrower than "no tuning".

The one genuine gap the phrasing does not capture is not in the `.csproj` at all: **the CI does not
install `wasm-tools`, so the .NET runtime is shipped un-relinked.** That is a workflow gap, and it
is the largest single item on the list.

Also worth carrying back to the map: the reader's clock starting at the URL splits into two budgets
that need separate measurement — the WASM download and boot (this document) versus the app's own
start-up, which reaches through the Gateway to Library and Catalog. Only the first is addressable
from the frontend build. `--blazor-load-percentage` reaching 100% marks the end of the *download*
phase, not the point at which anything is on screen.
