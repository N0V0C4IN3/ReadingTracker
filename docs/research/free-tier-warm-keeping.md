# What the free tier actually permits for keeping things warm

Research for [#71](https://github.com/N0V0C4IN3/ReadingTracker/issues/71), part of the wayfinder map [#69](https://github.com/N0V0C4IN3/ReadingTracker/issues/69).

**Researched 2026-09-10.** Every price and quota below is dated, because pricing pages change.
Sources are Microsoft Learn, the Azure Retail Prices API, Neon's own docs, and GitHub's own docs.
Where no primary source exists, this document says so instead of guessing.

---

## Headline: three of the map's working assumptions do not survive contact with the numbers

1. **The Container Apps free grant does not cover even one always-on replica.** One replica at the
   smallest allocation consumes **360% of the monthly grant** — the grant runs out after 8.3 days.
   Three always-on replicas exhaust it in **2.8 days**.
2. **Neon's free plan cannot be kept warm at all.** A 24/7 keep-alive needs 180 CU-hours/month
   against a **100 CU-hour** allowance. The compute would be force-suspended for the last ~13 days
   of every month — strictly worse than letting it auto-suspend.
3. **Pinging a scale-to-zero app is ~3.5x more expensive than `minReplicas: 1`.** Idle billing
   requires `minReplicas > 0`, so a pinged `minReplicas: 0` app pays the *active* vCPU rate for
   every second it is up. This is the opposite of the intuitive result.

The only unambiguously free lever found is the GitHub Actions pinger itself, and only because
ReadingTracker's repo is public.

---

## 1. Azure Container Apps free grant

### The grant

| Item | Value | Scope |
| --- | --- | --- |
| vCPU-seconds | **180,000** | per subscription, per calendar month |
| GiB-seconds | **360,000** | per subscription, per calendar month |
| HTTP requests | **2,000,000** | per subscription, per calendar month |

> "The following resources are free during each calendar month, per subscription: The first 180,000
> vCPU-seconds / The first 360,000 GiB-seconds / The first 2 million HTTP requests"
> — [Billing in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/billing) (page dated 2025-12-09)

**Per subscription, not per app.** This is the single most consequential fact on this page: the
Gateway, Library and Catalog container apps all draw on *one* shared grant, not three.

Health probe requests are not billable, and requests originating inside the Container Apps
environment are not billable — only requests from outside the environment count
([same source](https://learn.microsoft.com/en-us/azure/container-apps/billing)). For a
Gateway → Library → Catalog chain, only the Gateway's inbound request is billable.

### Smallest allocation Container Apps permits

**0.25 vCPU / 0.5 GiB.** The Consumption plan only accepts fixed CPU/memory pairs, and 0.25/0.5Gi
is the smallest row in the table. The ratio is fixed at 1 vCPU : 2 GiB throughout.

> | vCPUs (cores) | Memory |
> | --- | --- |
> | `0.25` | `0.5Gi` |
> | `0.5` | `1.0Gi` |
> | ... | ... |
>
> — [Containers in Azure Container Apps § vCPU and memory allocation requirements](https://learn.microsoft.com/en-us/azure/container-apps/containers) (page dated 2025-05-15)

### The arithmetic: one always-on replica over a 30-day month

A 30-day month is `30 x 24 x 3600 = 2,592,000` seconds.

One replica at 0.25 vCPU / 0.5 GiB, running continuously:

```
vCPU-seconds = 0.25 vCPU x 2,592,000 s =   648,000 vCPU-seconds
GiB-seconds  = 0.5 GiB   x 2,592,000 s = 1,296,000 GiB-seconds
```

Against the grant:

```
  648,000 /   180,000 = 3.6   ->  360% of the monthly vCPU grant
1,296,000 /   360,000 = 3.6   ->  360% of the monthly memory grant
```

**One always-on replica at the smallest allocation consumes 360% of the free grant — 3.6x over.**

Read the other way, the grant buys:

```
180,000 vCPU-s / 0.25 vCPU = 720,000 s = 200 hours = 8.33 days
200 h / 720 h in a 30-day month = 27.8% of the month
```

**The free grant covers one replica for 8.3 days a month, then stops.**

For the actual three-service topology (Gateway + Library + Catalog), all at 0.25 vCPU:

```
180,000 vCPU-s / (3 x 0.25 vCPU) = 240,000 s = 66.7 hours = 2.78 days
```

**Three always-on replicas exhaust the entire monthly grant in under three days.**

### Idle rate vs active rate

There *is* a reduced idle rate, but it is conditional and it is not free.

> "By default, replicas are charged at an *active* rate. However, in certain conditions, a replica
> can enter an *idle* state. While in an *idle* state, resources are billed at a reduced rate."

To be eligible for idle charges the revision must be **configured with `minReplicas` greater than
zero** and **scaled to that minimum**. A replica counts as idle only when *all* of these hold:

- the replica runs in a revision eligible for idle charges;
- all containers have started and are running;
- the replica **isn't processing any HTTP requests**;
- the replica is using **less than 0.01 vCPU cores**;
- the replica is receiving **less than 1,000 bytes per second** of network traffic.

— [Billing in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/billing)

Two consequences that matter here:

- A `minReplicas: 0` app that is kept alive by an external pinger is **never** eligible for the idle
  rate. It pays active rates for every second it is running.
- A .NET container idling on a GC heap plausibly clears the 0.01 vCPU bar, but this is not
  guaranteed and cannot be confirmed from documentation — it must be measured.

### Rates (West Europe, USD, retrieved 2026-09-10)

The public pricing page renders its rates client-side and returns `$-` to a plain fetch, so these
come from Microsoft's first-party
[Azure Retail Prices API](https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices)
(`https://prices.azure.com/api/retail/prices?currencyCode=USD&$filter=serviceName eq 'Azure Container Apps' and armRegionName eq 'westeurope'`):

| Meter | Unit | Rate (USD) |
| --- | --- | --- |
| Standard vCPU **Active** Usage | 1 second | `0.000034` |
| Standard vCPU **Idle** Usage | 1 second | `0.000004` |
| Standard Memory **Active** Usage | 1 GiB second | `0.000004` |
| Standard Memory **Idle** Usage | 1 GiB second | `0.000004` |
| Standard Requests | 1M | `0.56` |

Note the memory idle rate **equals** the memory active rate in West Europe. Only vCPU gets the
discount (8.5x cheaper). Meter effective start date `2022-06-01`.

### What exceeding the grant would actually cost

Excess for one always-on replica = `648,000 - 180,000 = 468,000` vCPU-seconds and
`1,296,000 - 360,000 = 936,000` GiB-seconds.

**Best case — `minReplicas: 1` and genuinely idle the whole month:**

```
vCPU:   468,000 x $0.000004 = $1.872
Memory: 936,000 x $0.000004 = $3.744
                              -------
                              $5.62 / month
```

**Pinged `minReplicas: 0` app (never idle-eligible, so active rates):**

```
vCPU:   468,000 x $0.000034 = $15.912
Memory: 936,000 x $0.000004 =  $3.744
                              --------
                              $19.66 / month
```

**Keeping a scale-to-zero app warm by pinging costs ~3.5x what `minReplicas: 1` costs.**

**All three services always-on at `minReplicas: 1`, all idle** (one shared grant):

```
vCPU:   (3 x 648,000)   - 180,000 = 1,764,000 x $0.000004 =  $7.056
Memory: (3 x 1,296,000) - 360,000 = 3,528,000 x $0.000004 = $14.112
                                                            --------
                                                            $21.17 / month
```

> **Unresolved, and it matters.** The billing doc states the grant in vCPU-seconds and GiB-seconds
> without saying whether seconds metered against the *idle* meters draw down the same grant as
> *active* seconds, or whether each meter has its own grant. The retail catalogue exposes them as
> four distinct meters. No primary source resolves this. The figures above assume conservatively
> that idle and active seconds draw on one shared grant. If each meter carried its own grant the
> idle-case numbers would be lower — but not zero, since 648,000 still exceeds 180,000. **The
> conclusion that the grant cannot cover an always-on replica holds under either reading.**

---

## 2. Cold start behaviour

### Documented cool-down

Yes, and it is 300 seconds by default.

> | Behavior | Value |
> | --- | --- |
> | Polling interval | 30 seconds |
> | Cool down period | 300 seconds |
> | Scale down stabilization window | 300 seconds |
>
> "**Cool down period** is how long after the last event KEDA waits before the application scales
> down to its minimum replica count."
>
> — [Scaling in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/scale-app) (page dated 2026-05-19)

The same page adds an important qualifier for custom scale rules:

> "The cool down period only takes effect when scaling in from the final replica to 0. The cool down
> period doesn't affect scaling as any other replicas are removed."

Polling interval "doesn't apply to HTTP and TCP scale rules" — for HTTP rules, "Every 15 seconds,
the number of concurrent requests is calculated as the number of requests in the past 15 seconds
divided by 15."

### Is the cool-down configurable?

**Yes.** `cooldownPeriod` and `pollingInterval` are first-class properties of the `scale` object.

> | Property | Description | Type |
> | --- | --- | --- |
> | `cooldownPeriod` | KEDA Cooldown Period in seconds. Defaults to 300 seconds if not set. | int |
> | `pollingInterval` | KEDA Polling Interval in seconds. Defaults to 30 seconds if not set. | int |
>
> — [Microsoft.App/containerApps template reference](https://learn.microsoft.com/en-us/azure/templates/microsoft.app/containerapps), API version `2026-01-01`

They were added in API version `2024-08-02-preview`
([change log](https://learn.microsoft.com/en-us/azure/templates/microsoft.app/change-log/containerapps):
"[Scale]: Added property 'cooldownPeriod'", "[Scale]: Added property 'pollingInterval'") and are
present in the GA `2026-01-01` version. No documented maximum value was found.

Note that the narrative article
[azure-resource-manager-api-spec](https://learn.microsoft.com/en-us/azure/container-apps/azure-resource-manager-api-spec)
does *not* list these two properties — only the generated template reference does. Trust the
template reference.

### Does anything short of `minReplicas: 1` keep a replica alive?

**No supported setting does.** The docs are unambiguous:

> "If you want to ensure that an instance of your revision is always running, set the minimum number
> of replicas to 1 or higher."
> — [Scaling in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/scale-app)

Two things come close but are not equivalent:

- **Raising `cooldownPeriod`.** This genuinely extends how long a replica survives after the last
  request without setting `minReplicas: 1`. But the app is still `minReplicas: 0`, so it is **never
  eligible for idle billing** — every second costs the active rate. Per §1 this is the most
  expensive way to stay warm.
- **An external pinger at an interval below the cool-down.** Same billing problem, plus each ping is
  a billable external HTTP request (health probe requests are exempt, but an external pinger's
  request is not a health probe).

Two documented cases where a replica refuses to scale to zero, neither useful here: Dapr actors
("scaling to zero isn't supported"), and — on Neon's side — logical replication subscribers.

**No primary source documents actual cold-start latency in seconds for Container Apps.** Microsoft
publishes no cold-start SLA or typical-duration figure. That number has to come from
[#70](https://github.com/N0V0C4IN3/ReadingTracker/issues/70)'s instrumentation, not from docs.

---

## 3. Neon free tier

### Auto-suspend

| Fact | Value | Source |
| --- | --- | --- |
| Auto-suspend threshold | **5 minutes** of inactivity | [Scale to Zero](https://neon.com/docs/introduction/scale-to-zero) |
| Configurable on Free plan? | **No** — always enabled, cannot be disabled | [Scale to Zero](https://neon.com/docs/introduction/scale-to-zero), [Free plan limits FAQ](https://neon.com/faqs/free-plan-limits-and-quotas) |
| Configurable on paid plans? | Yes — paid plans may disable it entirely | [Scale to Zero](https://neon.com/docs/introduction/scale-to-zero) |
| Documented resume time | reactivates "within a few hundred milliseconds" | [Scale to Zero](https://neon.com/docs/introduction/scale-to-zero) |

Neon's own wording on resume is "**a few hundred milliseconds**". Note this describes the *compute*
resuming, not end-to-end query latency from Azure; Neon is cross-cloud from Azure here, so network
RTT is additional and is not covered by that figure.

### The compute allowance

| Free plan item | Value |
| --- | --- |
| Compute | **100 CU-hours per project per month** |
| Minimum compute size | **0.25 CU** (~1 GB RAM) |
| Maximum compute size | 2 CU (~8 GB RAM) |
| Storage | 0.5 GB per project |
| Projects | 100 |
| Branches | 10 per project |
| Data transfer | 5 GB per project per month |

— [Free plan limits and quotas](https://neon.com/faqs/free-plan-limits-and-quotas), [Neon plans](https://neon.com/docs/introduction/plans)

What happens when it runs out is severe:

> "The project's compute is suspended until the next billing period or until you upgrade. Existing
> connections drop and new ones can't open."
> — [Free plan limits and quotas](https://neon.com/faqs/free-plan-limits-and-quotas)

Neon does not bill overages on the Free plan — it cuts you off instead.

### The arithmetic: can a keep-alive query be afforded?

A keep-alive that fires more often than every 5 minutes means the compute never suspends. At the
minimum 0.25 CU, over a 30-day month:

```
720 hours x 0.25 CU = 180 CU-hours needed
                      100 CU-hours available
                      -------
                       80 CU-hours short (180% of allowance)
```

Read the other way:

```
100 CU-hours / 0.25 CU = 400 hours = 16.7 days
```

**A 24/7 keep-alive exhausts Neon's free compute allowance after 16.7 days, and the database is
then hard-suspended — connections dropped, no new connections — for the remaining ~13.3 days of
the month.** A keep-alive on Neon free is not merely wasteful; it is self-defeating.

By contrast, letting auto-suspend do its job: each wake keeps the compute up for at least 5
minutes, so the 400 available hours buy roughly `400 x 60 / 5 = 4,800` isolated wake events per
month at 0.25 CU. For a portfolio demo that is not a binding constraint.

### Do Neon's terms permit a keep-alive query?

**There is no clause anywhere in Neon's published policies that names keep-alive queries, either to
permit or to forbid them.** That is the honest answer; anything stronger would be folklore.

Neon's [Acceptable Use Policy](https://neon.com/docs/security/acceptable-use-policy) now defers to
the Databricks AUP (Neon is part of the Databricks platform) and points to Neon's security overview
for "Neon-specific expectations on abuse of resources". That overview says, verbatim:

> "Users must not engage in activities that result in unintended or non-permitted use of Neon
> resources, or that disrupt or degrade the service for other users. Prohibited activities include,
> but are not limited to, intentional or unintentional denial-of-service attacks, exceeding rate
> limits, using Neon for distributed computing projects, using Neon as general-purpose file storage,
> or other usage that falls outside the intended resource usage and limits of the applicable Neon
> plan."
>
> — [Neon security overview](https://neon.com/docs/security/security-overview)

The catch-all "or other usage that falls outside the intended resource usage and limits of the
applicable Neon plan" is the only clause that could plausibly reach a keep-alive. It is not a
prohibition on keep-alives as such, and no Neon document interprets it that way.

In practice the question is moot: §3's arithmetic shows a keep-alive cannot fit inside the plan's
limits anyway, so a keep-alive would fall foul of the *limits* long before anyone had to argue
about the *terms*.

---

## 4. GitHub Actions scheduled workflows as a pinger

### Cost

**Free, with no minute cap, because `N0V0C4IN3/ReadingTracker` is a public repository** (verified
via `gh repo view` on 2026-09-10: `"visibility": "PUBLIC"`).

> "GitHub Actions usage is free for standard GitHub-hosted runners in public repositories and for
> self-hosted runners."
> — [About billing for GitHub Actions](https://docs.github.com/en/billing/concepts/product-billing/github-actions)

Larger runners are *not* free on public repos
([minute multipliers](https://docs.github.com/en/billing/reference/actions-minute-multipliers)) —
so the pinger must stay on a standard `ubuntu-latest` runner.

For contrast, had the repo been private: the Free plan includes 2,000 minutes/month, GitHub "rounds
the minutes and partial minutes each job uses up to the nearest whole minute", and a 5-minute
schedule is `12 x 24 x 30 = 8,640` runs/month, therefore **>= 8,640 billed minutes — 4.3x over the
2,000-minute allowance.** The pinger is only free because the repo is public.

### Minimum interval

> "The shortest interval you can run scheduled workflows is once every 5 minutes."
> — [Events that trigger workflows](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows)

Note the collision: **the minimum schedule interval (5 min) exactly equals both the Container Apps
default cool-down (300 s) and Neon's auto-suspend threshold (5 min).** A `*/5` schedule has zero
margin — combined with the delays below, it will routinely miss.

### Reliability

GitHub documents this as explicitly unreliable:

> "The `schedule` event can be delayed during periods of high loads of GitHub Actions workflow runs.
> High load times include the start of every hour."

and, under sufficient load, "**some queued jobs may be dropped**".
— [Events that trigger workflows](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows)

GitHub publishes **no SLA, no maximum delay bound, and no delivery guarantee** for `schedule`.
There is no primary source giving a typical delay in minutes. A cron pinger is therefore a
best-effort mechanism, not a guarantee that anything stays warm — the practical mitigation is to
avoid scheduling on the hour and to accept that some pings simply will not fire.

### Disabling on inactive repositories

> "In public repositories, scheduled workflows are automatically disabled when no repository
> activity has occurred in 60 days."
> — [Events that trigger workflows](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows)

Re-enabling is manual, via the workflow management UI. **This applies to public repos specifically
— i.e. to this one.** For a portfolio project that may sit untouched between bursts of work, a
keep-warm pinger silently switching itself off after 60 days is a real failure mode, and one that
produces exactly the cold start the pinger existed to prevent.

---

## 5. Azure Static Web Apps free tier

### Compression: yes, including pre-compressed `.br`

This is the clearest good news in this document, and it lands directly on the Blazor WASM payload.

> "For assets with file extensions of popular text formats, such as `.html`, `.css`, and `.js`,
> Azure Static Web Apps automatically serves Gzip- or Brotli-compressed versions of your static
> assets if the client supports it.
>
> For other file types, Static Web Apps allows you to include a Brotli-compressed version of your
> file with a `.br` extension. For example, if you have an uncompressed file named `app.wasm`, you
> can add a compressed version named `app.wasm.br` to your app. This version is automatically served
> if a client that supports Brotli requests `app.wasm`."
>
> — [Static Web Apps FAQ § How do I enable Gzip or Brotli compression?](https://learn.microsoft.com/en-us/azure/static-web-apps/faq) (page dated 2024-06-25)

So both halves are true, for different file types:

- `.html` / `.css` / `.js` — SWA compresses **on its own**, Brotli or Gzip, no action needed.
- everything else, **including `.wasm` and `.dll`** — SWA serves a **pre-compressed `.br` file you
  ship yourself**, transparently, when the client advertises Brotli.

A Blazor WASM publish emits `.br` and `.gz` siblings, which lines up with the second mechanism.
Two caveats worth flagging for whoever acts on this:

- The FAQ names only the **`.br`** extension for the shipped-file mechanism. It does **not** say
  `.gz` siblings are served the same way. No primary source confirms `.gz` fallback for non-text
  types.
- The FAQ's text-format list is "such as `.html`, `.css`, and `.js`" — illustrative, not exhaustive.
  There is no published complete list of auto-compressed extensions. Whether `.json`, `.wasm` or
  Blazor's `.dat`/`.dll` assets are auto-compressed cannot be determined from documentation and must
  be measured against the deployed site's response headers.

### Cache headers on fingerprinted assets

**No primary source documents what default `Cache-Control` headers Static Web Apps sets.** This gap
is worth stating plainly rather than filling in.

What the docs *do* say:

> "Azure Static Web Apps automatically handles cache invalidation. When a deployment completes, all
> requests are served the latest version of your files. However, files can still be cached in your
> users' browsers or in a CDN if you've configured one. To control how browsers and CDNs cache your
> content, configure the appropriate headers in your app's configuration file."
> — [Static Web Apps FAQ](https://learn.microsoft.com/en-us/azure/static-web-apps/faq)

So: deployment-time invalidation is handled, and **you are expected to set cache headers yourself**
via `staticwebapp.config.json`, using either `globalHeaders` or per-route `headers`. The
[configuration reference](https://learn.microsoft.com/en-us/azure/static-web-apps/configuration)
gives the mechanism and shows the idiom, e.g.:

```json
{
  "route": "/articles/*.html",
  "headers": { "Cache-Control": "public, max-age=604800, immutable" }
}
```

Route-specific headers override `globalHeaders` where they collide. The config file is capped at
**20 KB**.

The actual default headers on a fingerprinted Blazor asset must be observed, not looked up.

### Is there a CDN on the free plan?

**Yes — global static content distribution is included on Free.** But it is *not* the same thing as
the Standard plan's Azure Front Door integration.

> | Feature | Free plan | Standard plan |
> | --- | --- | --- |
> | Globally distributed static content | ✔ | ✔ |
> | Max app size | 250 MB per app | 500 MB per app |
> | Custom domains | 2 per app | 5 per app |
> | Private endpoints | ✗ | ✔ |
> | Service Level Agreement (SLA) | **None** | ✔ |
>
> — [Azure Static Web Apps hosting plans](https://learn.microsoft.com/en-us/azure/static-web-apps/plans) (page dated 2025-01-28)

> "Azure Static Web Apps is a global service. Your app's static assets are globally distributed."
> — [Static Web Apps FAQ](https://learn.microsoft.com/en-us/azure/static-web-apps/faq)

What Free does **not** get: `enterprise-edge` / Azure Front Door integration, `networking`
configuration and `forwardingGateway` configuration are all documented as **Standard plan only**
([configuration reference](https://learn.microsoft.com/en-us/azure/static-web-apps/configuration)),
and Free carries **no SLA at all**. You can put your own CDN in front of a Free app, but you cannot
use the managed enterprise edge.

Free plan quotas relevant to a WASM payload
([Quotas](https://learn.microsoft.com/en-us/azure/static-web-apps/quotas), page dated 2024-05-30):

| Quota | Free |
| --- | --- |
| Included bandwidth per month | 100 GB |
| Overage bandwidth | **Unavailable** (not billable — capped) |
| Storage, single environment | 250 MB |
| Total storage, all environments | 500 MB |
| File count | 15,000 |
| Preview environments | 3 |

Bandwidth overage being "Unavailable" on Free is the same shape of risk as Neon's: the free tier
does not bill you, it stops serving.

---

## What this means for the map

Restating the contradictions in the map's terms, since [#69](https://github.com/N0V0C4IN3/ReadingTracker/issues/69)
records "Keeping one replica warm is on the table; a bill is not" as a settled preference:

- **Keeping one replica warm is not on the table at zero cost.** The cheapest possible always-on
  configuration — one replica, 0.25 vCPU, idle-eligible all month — still runs 3.6x over the grant
  and costs ~$5.62/month. The map's two clauses are in direct conflict; one of them has to give.
- **The 80/20 "cold start over caching" preference gets *stronger*, not weaker.** If warmth cannot
  be bought, the only remaining lever on the cold path is making the boot itself cheaper —
  ReadyToRun, image size, moving EF migrations off the start-up path, trimming the WASM payload.
  Those are already listed under "Not yet specified" and this research promotes them from optional
  to load-bearing.
- **Partial warmth is worth pricing.** Nothing forces an all-or-nothing choice. `minReplicas: 1` on
  the **Gateway alone** costs 1/3 of the three-service figure and collapses the serial
  Gateway → Library → Catalog chain to two cold starts instead of three. Or `minReplicas: 1` could
  be scheduled only during waking hours; the grant covers 200 replica-hours/month, which is roughly
  **6.6 hours a day for one replica**, free. That is a genuine, unexplored option this research
  surfaces.
- **The Neon keep-alive idea should be dropped outright**, not merely deprioritised — it makes
  availability worse, not better.
- **The SWA Brotli finding is free upside** and is independent of everything above. It costs
  nothing, needs no keep-warm decision, and acts on the Blazor payload, which the map explicitly
  counts toward the cold number ("The reader's clock starts at the URL").

## Open questions no primary source answers

1. Does idle-metered usage draw on the same free grant as active-metered usage? (§1)
2. Actual Container Apps cold-start latency in seconds — undocumented by Microsoft; needs [#70](https://github.com/N0V0C4IN3/ReadingTracker/issues/70). (§2)
3. Does a .NET container idling on a GC heap actually stay under the 0.01 vCPU idle threshold? (§1)
4. The complete list of file extensions SWA auto-compresses, and whether `.gz` siblings are served
   the way `.br` siblings are. (§5)
5. Default `Cache-Control` headers SWA emits on fingerprinted assets. (§5)

All five are measurement questions, not reading questions.

## Sources

- [Billing in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/billing)
- [Scaling in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/scale-app)
- [Containers in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/containers)
- [Microsoft.App/containerApps template reference](https://learn.microsoft.com/en-us/azure/templates/microsoft.app/containerapps)
- [Microsoft.App/containerApps API change log](https://learn.microsoft.com/en-us/azure/templates/microsoft.app/change-log/containerapps)
- [Azure Container Apps pricing](https://azure.microsoft.com/en-us/pricing/details/container-apps/) and the [Azure Retail Prices API](https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices)
- [Neon — Scale to Zero](https://neon.com/docs/introduction/scale-to-zero)
- [Neon — plans](https://neon.com/docs/introduction/plans)
- [Neon — Free plan limits and quotas](https://neon.com/faqs/free-plan-limits-and-quotas)
- [Neon — Acceptable Use Policy](https://neon.com/docs/security/acceptable-use-policy) and [security overview](https://neon.com/docs/security/security-overview)
- [GitHub — Events that trigger workflows](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows)
- [GitHub — About billing for GitHub Actions](https://docs.github.com/en/billing/concepts/product-billing/github-actions)
- [GitHub — Actions minute multipliers](https://docs.github.com/en/billing/reference/actions-minute-multipliers)
- [Azure Static Web Apps FAQ](https://learn.microsoft.com/en-us/azure/static-web-apps/faq)
- [Azure Static Web Apps hosting plans](https://learn.microsoft.com/en-us/azure/static-web-apps/plans)
- [Azure Static Web Apps quotas](https://learn.microsoft.com/en-us/azure/static-web-apps/quotas)
- [Azure Static Web Apps configuration](https://learn.microsoft.com/en-us/azure/static-web-apps/configuration)
