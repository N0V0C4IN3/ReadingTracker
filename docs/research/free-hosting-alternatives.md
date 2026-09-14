# Free hosting that could beat Container Apps on cold start

Answers [#78](https://github.com/N0V0C4IN3/ReadingTracker/issues/78), part of the
[#69](https://github.com/N0V0C4IN3/ReadingTracker/issues/69) map. Re-tests
[ADR-0005](../adr/0005-deployment-stack.md).

**Every figure below was retrieved from the provider's own documentation on 2026-09-10.** Free
tiers churn; anything not carrying a link and that date is not a fact. Where a primary source
could not be retrieved, the entry says **unverified** rather than guessing. Prices are USD.

Companion research: [free-tier warm-keeping](https://github.com/N0V0C4IN3/ReadingTracker/issues/71)
established the current stack's numbers (180,000 vCPU-s + 360,000 GiB-s per subscription per
month; one always-on replica = 3.6x the grant = ~$5.62/mo; Microsoft publishes no cold-start
figure). Those are not re-derived here.

---

## The short answer

**Yes — one arrangement removes cold start rather than shrinking it, and it is a VM, not a PaaS.**

**Oracle Cloud Always Free** grants 1,500 Arm OCPU-hours and 9,000 GB-hours per month, which is
exactly enough to run **2 OCPUs and 12 GB of RAM continuously, forever**, in a single home region.
That fits the entire system — three .NET services, Postgres and RabbitMQ, the same `docker-compose.yml`
this repo already has — on one machine that never scales to zero and never crosses a cloud boundary
to reach its database. Cold start stops being a number to optimise and becomes a number that does
not exist.

It has three real catches, all documented and none fatal: **idle instances may be reclaimed**, **Arm
capacity is not guaranteed**, and **it is a VM you administer**, with no managed ingress, TLS or
deployment pipeline.

Among the platforms that keep the current shape (managed, scale-to-zero, git-push deploys),
**Google Cloud Run** is the only one that genuinely improves on Container Apps, and for a specific,
sourced reason: under its default request-based billing it **keeps an instance idle for up to 15
minutes at no charge**. Container Apps cannot do that at any price below $5.62/mo. But its free
compute grant is *identical* to Container Apps' — the same 180,000 vCPU-s / 360,000 GiB-s — and
Neon has no GCP regions at all, so moving there fixes the idle window and leaves the cross-cloud
database hop exactly where it is.

**ADR-0005 still holds on the three platforms it named.** Fly.io, Railway and Render have all got
*worse* since it was written, not better. What ADR-0005 did not consider — a VM you run yourself —
is the option that wins.

---

## Comparison

| Candidate | 1. Scale to zero / cold start | 2. Durable free tier | 3. Colocated free Postgres | 4. Runs 3 .NET containers + AMQP | 5. Cost of being wrong |
| --- | --- | --- | --- | --- | --- |
| **Oracle Cloud Always Free (Arm A1)** | **Never idles.** No cold start at all. Risk: idle reclamation | **Yes** — "never expire", "for the life of the account". Card at signup, not charged unless you upgrade | **Postgres on the same box** — 0 network hops. No *managed* free Postgres on OCI | Yes — 2 OCPU / 12 GB runs all five containers; RabbitMQ self-hosted | Medium. Needs arm64 images + ingress/TLS + a deploy path. Escape hatch cheap: ACA config untouched |
| **Google Cloud Run** | Scales to zero, but **idle instances kept up to 15 min free**. No published .NET cold-start figure | Free tier "has no end date"; 30 days' notice of change. Billing account required | **None.** Neon has no GCP regions; Cloud SQL free tier is a 30-day trial | Yes | Low-medium. Container-native; but egress outside North America is not in the free tier |
| **Azure Container Apps (today)** | Scales to zero; 300s cooldown; billed at active rates the whole time | Yes (grant is per subscription) | No — Neon left Azure regions April 2026 | Yes (current) | n/a |
| **Azure App Service F1** | Unloaded after **20 min** idle; **Always On not available**; **60 CPU-minutes/day** cap | Yes | No | Marginal — 60 CPU-min/day shared across cold starts of 3 services | Low, but it is a downgrade |
| **Fly.io** | Machines can auto-stop | **No.** No free allowances for new organisations; credit card required | Managed Postgres is paid | Yes | n/a — fails (2) |
| **Railway** | Per-second billing | **No.** Trial = $5 once, expires in 30 days. Free plan = $1/month of credit against $10/GB-mo + $20/vCPU-mo | Paid | Yes | n/a — fails (2) |
| **Render** | Spins down after **15 min**, spin-up "about one minute" — *worse* than today | **No.** Free Postgres **expires 30 days after creation** | Expires | Yes | n/a — fails (2) |
| Koyeb | — | **No free compute plan** (lowest is $29/mo) | Postgres free tier = 5 h/month | — | dismissed |
| Cloudflare Containers | Sleeps automatically | **No.** Requires Workers Paid, $5/month | — | — | dismissed |
| AWS | — | **No.** New accounts: $100–200 credits, **account closed after 6 months** | — | — | dismissed (trial) |
| Scaleway Serverless Containers | Scales to zero | Free allowance published (400,000 GB-s + 200,000 vCPU-s/mo) but **permanence unverified** | No free Postgres found | Probably | promising, **unverified** |
| IBM Cloud Code Engine | Scales to zero | Lite plans "never expire and you can't be charged for them—ever" — but **Code Engine's free allowance figures could not be retrieved** | — | Probably | **unverified** |

---

## 1. Oracle Cloud Always Free — the only candidate that removes cold start

### What is actually granted

> "All tenancies get the first 1,500 OCPU hours and 9,000 GB hours per month for free for VM
> instances using the VM.Standard.A1.Flex shape, which has an Arm processor. For Always Free
> tenancies, this is equivalent to 2 OCPUs and 12 GB of memory."

— [Always Free Resources](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm),
checked 2026-09-10.

The arithmetic that matters, against a 30-day month (720 hours):

```
2 OCPU  x 720 h = 1,440 OCPU-hours   vs 1,500 granted   -> fits, 96% used
12 GB   x 720 h = 8,640 GB-hours     vs 9,000 granted   -> fits, 96% used
```

**An always-on 2-OCPU / 12-GB machine fits inside the grant with ~4% to spare.** This is the
opposite of the Container Apps arithmetic, where one 0.25-vCPU replica is 360% of the grant.

Also always free: **200 GB block volume**, **10 TB/month of outbound data transfer**, one flexible
load balancer, two VCNs. Same source.

### Is it durable?

> "Oracle Cloud Infrastructure's Free Tier includes a free time-limited promotional trial ... and a
> set of **Always Free offers that never expire**."

> "After your trial ends, your account remains active. There is no interruption to the availability
> of the Always Free Resources you have provisioned."

> "For security purposes, most users need a mobile phone number and **a credit card to create an
> account. Your credit card will not be charged unless you upgrade your account**."

— [Oracle Cloud Infrastructure Free Tier](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier.htm),
checked 2026-09-10.

So: no expiry, no trial dependency, no bill — but **a card on file at signup**. Under the strictest
reading of criterion 2 ("no card required to stay free") that is a mark against it; under the
reading that matters ("no bill can arrive"), Oracle states plainly that the card is not charged
without an explicit upgrade. Azure required a card too, so this is not a regression.

One more trial-boundary rule, worth knowing before signing up:

> "If you have more OCI Ampere A1 Compute instances provisioned than are available for an Always
> Free tenancy, all existing OCI Ampere A1 Compute instances are disabled and then deleted after 30
> days, unless you upgrade to a paid account."

Provision **within** Always Free limits from the start and this never fires.

### The catches, verbatim

**Idle reclamation.** This is the one that could bite a portfolio demo directly:

> "Idle Always Free compute instances may be reclaimed by Oracle. Oracle will deem virtual machine
> and bare metal compute instances as idle if, during a 7-day period, the following are true:
> - CPU utilization for the 95th percentile is less than 20%
> - Network utilization is less than 20%
> - Memory utilization is less than 20% *(applies to A1 shapes only)*"

— same page, checked 2026-09-10.

The three conditions are **AND-ed**: an instance is idle only if *all three* are under 20%. On a
12 GB A1 shape, 20% of memory is 2.4 GB, and three .NET services plus Postgres plus RabbitMQ
plausibly sit above that continuously — which would make the instance non-idle by Oracle's own
definition without any artificial keep-busy work. **That is a reasoned inference, not a verified
one**; it needs measuring on the real VM before it is relied on. Allocating fewer GB to stay
comfortably above 20% of a smaller number is the lever if it turns out to be tight.

What happens to a reclaimed instance — stopped or terminated, with or without notice — **is not
stated on the Always Free page**. Oracle's general compute docs cover
[stopping, starting and restarting an instance](https://docs.oracle.com/en-us/iaas/Content/Compute/Tasks/restartinginstance.htm),
and community threads describe reclaimed instances as stopped and restartable subject to capacity,
but **no primary source states the reclamation outcome**. Treat it as unknown.

**Arm capacity.** Also documented, and the single most common complaint about this offer:

> "If you receive an 'out of host capacity' error when trying to create a Compute instance, this
> indicates a temporary lack of Always Free shapes in your home region... You can also choose to
> upgrade your account to Pay as You Go... **Remember that Oracle doesn't charge for Always Free
> resources after you upgrade**."

Two consequences. First, you may not be able to create the A1 instance on the day you try, in the
region you want. Second, Oracle's documented workaround — upgrade to Pay As You Go, which does not
make Always Free resources billable — is a card-on-file arrangement where a *mistake* costs money,
which is a different risk posture from a free tier that cannot bill you.

**Home region.** "You must create the Always Free compute instances in your home region." The home
region is chosen at signup and is not casually changed, so pick the Frankfurt region up front to
match the current Neon/Azure geography.

**It is a VM.** No managed ingress, no managed TLS, no deployment pipeline, no revisions, no
`az containerapp update`. Caddy or nginx plus a systemd unit or Watchtower, and a workflow that
SSHes in. This is the honest cost of the option, and it is not small.

### Colocation — the strongest part of the case

OCI's Always Free list contains **Autonomous AI Database** (Oracle, not Postgres), **MySQL
HeatWave**, and **NoSQL**. There is **no managed free Postgres on OCI**. That sounds like a
weakness and is actually the reverse: the answer is `postgres:17-alpine` on the same VM, which is
already in this repo's `docker-compose.yml`. Distance from Library to its database goes from
Azure Germany West Central → AWS eu-central-1 (a cross-cloud round trip on every query, plus Neon's
5-minute auto-suspend and its "few hundred milliseconds" resume) to **localhost**. RabbitMQ moves
the same way, off CloudAMQP and onto the same box, which also retires
[ADR-0011](../adr/0011-rabbitmq-runs-as-a-managed-shared-instance.md)'s reasoning.

Durability trade: Neon's managed backups become your problem. 200 GB of block volume and 5 volume
backups are in the Always Free grant, so the tooling exists; the discipline does not come for free.

### Cost of being wrong

- **arm64 images.** The A1 shape is Arm. `deploy-services.yml` already uses
  `docker/setup-buildx-action` and `docker/build-push-action`, so this is a `platforms:
  linux/amd64,linux/arm64` line, not a rewrite. The three Dockerfiles are plain
  `mcr.microsoft.com/dotnet/{sdk,aspnet}:10.0`, which are multi-arch upstream.
- **The compose file already exists**, including Postgres and RabbitMQ with health checks and
  volumes. This migration is much closer to "run what dev already runs" than a typical replatform.
- **The escape hatch is cheap.** Nothing in the application changes — connection strings,
  `Catalog__BaseAddress` and the reverse-proxy addresses are all configuration. Leave the Container
  Apps resources and both workflows in place and the way back is a DNS change plus re-pointing
  `WEB_GATEWAY_BASE_ADDRESS`.
- **What does not come back**: managed TLS, the health-checked deploy, and Neon's backups.

---

## 2. Google Cloud Run — better idle behaviour, identical grant, worse colocation

### The free tier

> "Limits for request-based billing:
> - 2 million requests per month.
> - 360,000 GB-seconds of memory, 180,000 vCPU-seconds of compute time.
> - 1 GB of outbound data transfer from North America per month."

> "The Free Tier has no end date, but Google reserves the right to change the offering, including
> changing or eliminating usage limits, with 30 days' advance notice."

— [Free Google Cloud features](https://docs.cloud.google.com/free/docs/free-cloud-features),
checked 2026-09-10.

**That compute grant is the same number as Azure Container Apps'**, to the second. Whatever else is
true, moving to Cloud Run does not buy a bigger allowance, and an always-on instance is exactly as
unaffordable there as it is on Azure.

### Why it is still better on cold start

Two documented behaviours that Container Apps does not match:

> "Cloud Run instances are only charged when they process requests, when they start, and when they
> shut down."

— [Billing settings](https://docs.cloud.google.com/run/docs/configuring/billing-settings) (request-based
billing is the default), checked 2026-09-10.

> "Cloud Run might keep instances idle for a period of time after they finish handling requests (up
> to 15 minutes, or 10 minutes for GPUs)."

— [About instance autoscaling](https://docs.cloud.google.com/run/docs/about-instance-autoscaling),
checked 2026-09-10. The same page describes shutdown in two stages: instances under 10% utilisation
over a 1-minute window first, then remaining instances after a 15-minute idle timeout.

Put together: **up to 15 minutes of warm idle, free.** On Container Apps the equivalent window is
the 300-second cooldown, and per #71 an app with `minReplicas: 0` is never idle-billing-eligible,
so every second of that cooldown is charged at *active* rates against the grant. Cloud Run's idle
window is three times longer and costs nothing. For a portfolio demo where visits arrive in
clusters — a recruiter clicking twice in ten minutes — that is the difference between one cold
start and two.

`minInstances` is not a way out: "Instances kept running using the minimum instances feature do
incur billing costs"
([Minimum instances](https://docs.cloud.google.com/run/docs/configuring/min-instances), checked
2026-09-10). Same dead end as `minReplicas: 1`.

### Cold-start numbers

**Google publishes no cold-start latency figure**, for .NET or anything else.
[General development tips](https://docs.cloud.google.com/run/docs/tips/general) (checked 2026-09-10)
says only that "their startup time has impact on the latency of your service", offers **startup CPU
boost** ("temporarily increase CPU allocation during instance startup in order to reduce startup
latency") as the lever, and notes that "Requests will pend for up to 3.5 times average startup time
of container instances of this service, or 10 seconds, whichever is greater."

So Cloud Run's cold-start advantage over Container Apps is **reputational, not documented**. The
*idle-retention* advantage above is documented; the raw start-up-time advantage is not. Anyone
claiming a number for a .NET container on either platform is guessing.

### Colocation — the deal-breaker

- **Neon has no GCP regions at all.** Its
  [Regions](https://neon.com/docs/introduction/regions) page (checked 2026-09-10) lists AWS regions
  and deprecated Azure ones. GCP does not appear.
- **Cloud SQL has no free tier** — the free-features page offers only "Create a 30-day Cloud SQL
  free trial instance at no cost." A 30-day trial fails criterion 2 outright.
- Supabase is on AWS; Aiven's free plan "cannot select a specific cloud or region".

Moving to Cloud Run therefore **keeps the cross-cloud hop and gains nothing on criterion 3**. It
trades an Azure→AWS hop for a GCP→AWS hop.

### Two further cautions

- **Egress.** The free tier includes only "1 GB of outbound data transfer from North America per
  month". A European deployment's egress is not in the free tier at all. The volume here is small
  (JSON responses; the WASM payload is served by a static host, not by Cloud Run), but "small" is
  not "zero", and the constraint on this project is **zero**. The exact European egress rate could
  not be retrieved (see Unknowns).
- The `cloud.google.com/run/pricing` page renders its tables client-side and could not be read;
  **whether the free tier is restricted to particular regions, and what the free tier is under
  instance-based billing, are unverified.** Note that Google's free tier *is* region-restricted for
  other products (Compute Engine's e2-micro is `us-west1`/`us-central1`/`us-east1` only; Cloud
  Storage's free tier is those three regions), so this is a real question, not a formality.

---

## 3. The three ADR-0005 rejected — re-tested

ADR-0005 rejected Fly.io, Railway and Render as "either discontinued, too thin for continuous
hosting, or have a database expiry that doesn't suit a persistent portfolio demo." All three
verdicts hold, and two of the three have deteriorated further.

### Fly.io — "discontinued" was right, and it went further

> "Fly.io no longer offers plans to new customers. If you purchased a Launch or Scale plan before
> October 7, 2024, you can remain on those plans."

> "The following resources were included for free on the Hobby (deprecated), Launch, and Scale
> plans, and are **still honored for any organizations that were on these plans before we sunset
> them**: Up to 3 shared-cpu-1x 256mb VMs, 3GB persistent volume storage (total)."

— [Discontinued Plans](https://fly.io/docs/about/discontinued-plans/), checked 2026-09-10.

> "All organizations (except for Linked Organizations) require a credit card on file."

— [Fly.io Resource Pricing](https://fly.io/docs/about/pricing/), checked 2026-09-10. The
[billing page](https://fly.io/docs/about/billing/) (same date) adds that prepaid credits are the
alternative, minimum $25.

**A new organisation gets no free allowance.** The 3x256MB allowance survives only for organisations
grandfathered before 2024-10-07. Fails criterion 2. Managed Postgres is paid; the self-managed
option is quoted from "about $2/month". **Verdict: ADR-0005 was right and is more right now.**

### Railway — a free plan exists, and it is $1

- Trial: "a one-time grant of $5 in credits" which "expire in 30 days".
- Free plan: "$0/month with $1 of monthly usage credits", "1 vCPU / 0.5 GB per service, 1 replica".
- Rates: "RAM: $10 / GB / month", "CPU: $20 / vCPU / month", egress "$0.05 / GB".
- "As of March 30th, Railway requires the use of a post-paid card" (Hobby).
- "If you are using credits as a payment method and your credit balance reaches zero, your
  subscription will be cancelled. You will no longer be able to deploy to Railway and we will stop
  all of your workloads."

— [Railway pricing](https://railway.com/pricing) and
[Plans reference](https://docs.railway.com/reference/pricing/plans), checked 2026-09-10.

$1/month against $10/GB-month means the Free plan buys roughly **0.1 GB-month of RAM before any CPU
is counted** — about three days of a single 0.5 GB service, for one of three services, with the
documented consequence of exhaustion being that Railway stops all your workloads. "Too thin for
continuous hosting" was, if anything, generous. **Verdict: ADR-0005 holds.**

### Render — the database expiry ADR-0005 named is still there, verbatim

> "Render **spins down** a Free web service that goes 15 minutes without receiving any inbound
> traffic." Spin-up takes "about one minute."

> "Render grants **750 Free instance hours** to each workspace per calendar month."

> "**Free Render Postgres databases expire 30 days after creation.**" — with "a grace period of 14
> days to upgrade it to a paid compute plan", and 1 GB fixed storage.

— [Free instance types](https://render.com/docs/free), checked 2026-09-10.

Two independent failures. The database expiry is exactly the objection ADR-0005 recorded, unchanged.
And on criterion 1 Render is **worse than what we have**: a documented ~1-minute spin-up, against
Container Apps' unpublished but almost certainly shorter one. 750 instance hours also does not cover
three always-on services (3 x 720 = 2,160). **Verdict: ADR-0005 holds, emphatically.**

---

## 4. Azure App Service F1 — the option that changes least, and the one that helps least

From the [App Service limits table](https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/azure-subscription-service-limits)
(checked 2026-09-10), the Free tier row:

| | Free (F1) |
| --- | --- |
| Apps per plan | 10 |
| Storage | 1 GB (total across the plan) |
| CPU time (5 min) | 3 minutes |
| **CPU time (day)** | **60 minutes** |
| Memory (1 hour) | 1,024 MB per plan |
| Scale out | 1 shared instance |
| **Always On** | **not available** |
| Custom domain SSL | not supported (`*.azurewebsites.net` only) |

And the idle behaviour, from [Configure an App Service app](https://learn.microsoft.com/en-us/azure/app-service/configure-common)
(checked 2026-09-10):

> "When **Always On** is turned off (default), the app is unloaded after 20 minutes without any
> incoming requests. The unloaded app can cause high latency for new requests because of its warm-up
> time."

Always On is precisely what F1 does not have. So F1 **also** cold-starts — after 20 minutes rather
than 5 — and does so under a **60-CPU-minute daily budget** and 1 GB of memory for the whole plan.
Three .NET services cold-starting repeatedly inside 60 CPU-minutes/day is a quota you can actually
exhaust, and the documented consequence is the app being stopped until the quota resets.

Can it even run our containers? The Linux-container path of
[Run a custom container on App Service](https://learn.microsoft.com/en-us/azure/app-service/quickstart-custom-container)
(checked 2026-09-10) instructs: "To change to the Free tier, select **Change size** > **Dev/Test** >
**F1** > **Apply**" — so Microsoft's own quickstart puts a Linux custom container on F1. Whether
that is supported for sustained use rather than a quickstart is **not something the docs state**.

Colocation: unchanged and still cross-cloud — Neon deprecated its Azure regions on 2026-04-07.

**Verdict: F1 is a lateral move at best and a downgrade at worst.** It trades a 5-minute cooldown
for a 20-minute one — the only respect in which it beats Container Apps — and pays for it with a
hard daily CPU cap, 1 GB of shared memory, one shared instance, and no custom-domain TLS.

---

## 5. Everything else considered, and why it is dismissed

- **Koyeb** — [pricing](https://www.koyeb.com/pricing), checked 2026-09-10. No free compute plan;
  the lowest paid tier is $29/mo. Serverless Postgres has a "Free 5h" tier — five hours a month.
  Fails (1) and (2).
- **Cloudflare Containers** — [pricing](https://developers.cloudflare.com/containers/pricing/),
  checked 2026-09-10. The Free plan shows "N/A" for vCPU, memory and disk; containers require the
  Workers Paid plan at "$5 USD per month". Fails (2) — it is a $5/month bill, which is the same
  money `minReplicas: 1` costs on Azure.
- **AWS** — [aws.amazon.com/free](https://aws.amazon.com/free/), checked 2026-09-10. New accounts
  get "$100 in credits immediately... up to $200 over 6 months", with the account closing "6 months
  after you open it or when your credits run out, whichever comes first". That is a trial, not a
  free tier. The page still claims "30+ AWS services are always free within monthly usage limits"
  but does not mention the old 12-month t2/t3.micro EC2 offer at all. Fails (2).
- **Scaleway Serverless Containers** — [pricing](https://www.scaleway.com/en/pricing/serverless/),
  checked 2026-09-10. Publishes "400 000 GB-s Free Tier per account and per month" and "200 000
  vCPU-s Free Tier per account and per month" — **larger than both Azure's and Google's grants**,
  and it is an EU provider (Paris). But the page does not state whether the allowance is permanent
  or whether a card is required, and no free managed Postgres was found. **Genuinely promising and
  genuinely unverified**; the first thing to check if the Oracle route fails.
- **IBM Cloud Code Engine** — [IBM Cloud free](https://www.ibm.com/products/cloud/free), checked
  2026-09-10: "40+ always-free products with a Lite plan", "They never expire and you can't be
  charged for them—ever", but "Payment details are required up front". Code Engine scales to zero
  and IBM says it "includes a free tier"
  ([pricing docs](https://cloud.ibm.com/docs/codeengine?topic=codeengine-pricing)), **but the
  actual vCPU-second/GB-second figures could not be retrieved from IBM's own pages.** Unverified.
- **Zeabur** — [pricing](https://zeabur.com/pricing), checked 2026-09-10. A "$0 per month" Free tier
  exists but the page documents only dashboard access, build spec and log retention; no compute
  allowance, no statement on sleeping, no free Postgres. Cannot be assessed; not reportable as
  passing (1) or (2).
- **Supabase** (database only) — [pricing](https://supabase.com/pricing), checked 2026-09-10. "500
  MB database size", "2 active projects", and decisively: "**Free projects are paused after 1 week
  of inactivity.**" For a portfolio demo that idles between visits, that is worse than Neon's
  5-minute auto-suspend, because a paused project needs manual restoration. Runs on AWS, so it would
  not colocate with GCP or OCI anyway.
- **Aiven** (database only) — [pricing](https://aiven.io/pricing), checked 2026-09-10. Free
  PostgreSQL plan of "1 CPU / 1 GB RAM / 1 GB storage", but "**Cannot select a specific cloud or
  region**" — which disqualifies it on criterion 3 by construction. Aiven also reserves the right to
  shut down services "unused for an extended period of time". No RabbitMQ free plan is listed.

---

## Verdict on ADR-0005

**The ADR's rejections still hold. Its choice no longer does — for a reason the ADR could not have
known.**

Point by point:

1. **"Fly.io, Railway and Render ... discontinued, too thin, or a database expiry"** — **all three
   confirmed correct as of 2026-09-10**, and all three have moved further in that direction. Fly
   removed free allowances for new organisations entirely on 2024-10-07; Railway's free plan is
   $1/month of credit against $10/GB-month; Render's free Postgres still expires at 30 days and its
   free web services take "about one minute" to spin up. Nothing here needs revisiting.

2. **"Neon (free tier has no hard expiry or inactivity pause, unlike Render/Supabase)"** — the
   comparative claim is **still true and still well-judged**. Render's Postgres expires at 30 days;
   Supabase pauses free projects after 1 week of inactivity; Neon does neither. One nuance: Neon's
   [Regions](https://neon.com/docs/introduction/regions) page (checked 2026-09-10) warns that
   "Projects on the Free plan that have been inactive for 90 days or more are subject to deletion as
   of October 5, 2026" — **that notice sits inside the Azure-regions deprecation warning and applies
   to Azure-region projects**, which this project's `aws-eu-central-1` database is not. It is not a
   general Neon free-plan inactivity policy. Do not repeat it as one.

3. **"Azure Container Apps (genuine always-free monthly grant, scale-to-zero)"** — true, and still
   true. What has changed is that this was written as an argument for *durability*, and the project
   is now optimising for *cold start*, which the same grant cannot buy (#71: 3.6x short). Two
   facts arrived after the ADR that specifically weaken its choice:
   - **Neon left Azure's regions on 2026-04-07**, so ADR-0005's compute and database no longer share
     a cloud. The ADR chose a pairing that no longer exists as chosen.
   - Container Apps has **no free idle window**. Cloud Run keeps an instance for up to 15 minutes at
     no charge; Container Apps' 300-second cooldown is billed at active rates against the grant.

**So: ADR-0005 was not wrong, and re-litigating Fly/Railway/Render would waste the effort. But its
central pairing has been broken by Neon's Azure exit, and its platform is the weakest of the
credible options on the metric this effort now cares about.** If ADR-0005 is superseded, it should
be superseded by the Oracle VM — an option it never considered, because in 2025 the question was
durability and a VM you administer looks worse on durability than a managed PaaS. On cold start it
looks better than everything, because it never gets cold.

Recommendation for [#79](https://github.com/N0V0C4IN3/ReadingTracker/issues/79): **prototype the
Oracle Always Free A1 VM before committing.** Two things decide it, and both are cheap to test and
impossible to settle from documentation — whether an A1 instance can be created at all in a European
home region on the day you try, and whether the running stack stays above Oracle's 20% idle
thresholds. If either fails, **stay on Container Apps** and spend the effort on boot time instead;
Cloud Run's 15-minute idle window is real but it does not fix colocation, and no other candidate
passes both criterion 1 and criterion 2.

---

## What remains unknown

Documentation cannot settle these. Listed in the order they would change the recommendation.

1. **Whether an Arm A1 instance can actually be created in a European home region.** Oracle
   documents the "out of host capacity" error as a real, ongoing condition. There is no published
   availability signal. **The entire recommendation rests on this and it can only be tested by
   trying.**
2. **What Oracle does to a reclaimed instance.** The Always Free page says instances "may be
   reclaimed" and defines idle. It does not say whether reclamation stops or terminates the
   instance, whether notice is given, or whether a stopped instance can be restarted. No primary
   source found.
3. **Whether this workload clears the 20% idle thresholds.** The AND-ed criteria mean any one metric
   above 20% is enough. Five containers on a 12 GB machine plausibly clear the memory threshold;
   plausibly is not measured.
4. **Cold-start seconds for a .NET container on any of these platforms.** Neither Microsoft nor
   Google publishes a figure. Render publishes "about one minute" for its own free spin-up — the only
   published number found anywhere in this research, and it belongs to a candidate that fails on
   other grounds. Everything else must come from
   [#70](https://github.com/N0V0C4IN3/ReadingTracker/issues/70)'s instrumentation.
5. **Cloud Run's free tier by region, and under instance-based billing.**
   `cloud.google.com/run/pricing` renders client-side and could not be read; the free-features page
   documents only the request-based-billing limits and explicitly defers the rest. Given that
   Google's free tier *is* region-restricted for Compute Engine and Cloud Storage, this is not safe
   to assume away.
6. **Cloud Run egress pricing from European regions.** The free tier covers 1 GB from North America
   only. The European rate was not retrieved. Under a strict zero-spend constraint, an unquantified
   per-GB charge is a blocker until quantified.
7. **Scaleway's free tier permanence and card requirement**, and whether it has any free Postgres.
   Its published allowance is the largest of any serverless container platform found here.
8. **IBM Code Engine's actual free allowance.** IBM says Lite plans never expire and can never
   charge; the numbers behind that claim were not retrievable from IBM's own docs.
9. **CloudAMQP free-plan region and cloud availability.** The [plans page](https://www.cloudamqp.com/plans.html)
   (checked 2026-09-10) documents the Little Lemur limits (100 queues, 10,000 queued messages, 1M
   messages/month, 20 connections, "Max idle queue time 28 days") but marks free-plan region support
   with asterisks rather than listing regions. Moot under the Oracle recommendation, which
   self-hosts the broker; live under the Cloud Run one.
10. **Whether Azure App Service F1 supports a Linux custom container for sustained use.** Microsoft's
    quickstart selects F1 in the container flow, which is evidence but not a support statement.

---

## Sources

All checked **2026-09-10**.

| Source | Used for |
| --- | --- |
| [OCI Always Free Resources](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm) | A1 grant, idle reclamation, home region, 10 TB egress, capacity error |
| [OCI Free Tier overview](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier.htm) | "never expire", card at signup, post-trial behaviour |
| [OCI stopping/starting instances](https://docs.oracle.com/en-us/iaas/Content/Compute/Tasks/restartinginstance.htm) | instance restart mechanics |
| [Google Cloud free features](https://docs.cloud.google.com/free/docs/free-cloud-features) | Cloud Run free tier, no end date, Compute Engine/Cloud SQL entries |
| [Cloud Run billing settings](https://docs.cloud.google.com/run/docs/configuring/billing-settings) | request-based vs instance-based billing |
| [Cloud Run instance autoscaling](https://docs.cloud.google.com/run/docs/about-instance-autoscaling) | up-to-15-minute idle retention |
| [Cloud Run minimum instances](https://docs.cloud.google.com/run/docs/configuring/min-instances) | min instances are billed |
| [Cloud Run general tips](https://docs.cloud.google.com/run/docs/tips/general) | startup CPU boost; no published cold-start figure |
| [Fly.io discontinued plans](https://fly.io/docs/about/discontinued-plans/) | free allowances grandfathered only |
| [Fly.io pricing](https://fly.io/docs/about/pricing/) / [billing](https://fly.io/docs/about/billing/) | card required |
| [Railway pricing](https://railway.com/pricing) / [plans](https://docs.railway.com/reference/pricing/plans) | trial $5/30 days, Free $1/mo, rates, credit exhaustion |
| [Render free instances](https://render.com/docs/free) | 15-min spin-down, ~1-min spin-up, 750 hours, 30-day Postgres expiry |
| [Azure subscription limits](https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/azure-subscription-service-limits) | F1 quotas |
| [Azure App Service configuration](https://learn.microsoft.com/en-us/azure/app-service/configure-common) | 20-minute unload, Always On |
| [Azure custom container quickstart](https://learn.microsoft.com/en-us/azure/app-service/quickstart-custom-container) | F1 in the Linux container flow |
| [Neon regions](https://neon.com/docs/introduction/regions) | no GCP; Azure deprecation; the 90-day notice and its scope |
| [Neon plans](https://neon.com/docs/introduction/plans) / [free plan FAQ](https://neon.com/faqs/free-plan-limits-and-quotas) | 100 CU-hours, 0.5 GB, scale-to-zero |
| [Neon Azure deprecation](https://neon.com/docs/import/azure-regions-deprecation) | 2026-04-07 and 2026-10-05 dates |
| [Supabase pricing](https://supabase.com/pricing) | 1-week inactivity pause |
| [Aiven pricing](https://aiven.io/pricing) | free Postgres plan; no region choice |
| [Koyeb pricing](https://www.koyeb.com/pricing) | no free compute |
| [Cloudflare Containers pricing](https://developers.cloudflare.com/containers/pricing/) | requires Workers Paid |
| [AWS Free Tier](https://aws.amazon.com/free/) | credits-based, 6-month account closure |
| [Scaleway serverless pricing](https://www.scaleway.com/en/pricing/serverless/) | free GB-s and vCPU-s allowance |
| [IBM Cloud free](https://www.ibm.com/products/cloud/free) / [Code Engine pricing](https://cloud.ibm.com/docs/codeengine?topic=codeengine-pricing) | Lite plans never expire; allowance not published |
| [Zeabur pricing](https://zeabur.com/pricing) | free tier contents |
| [CloudAMQP plans](https://www.cloudamqp.com/plans.html) | Little Lemur limits |
