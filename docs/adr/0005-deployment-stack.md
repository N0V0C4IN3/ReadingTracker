# Deployment: Azure Container Apps + Neon + Azure Static Web Apps

*Superseded by [ADR-0012](0012-self-host-on-a-raspberry-pi-behind-tailscale-funnel.md) for the
services and [ADR-0016](0016-the-pi-serves-the-frontend-too.md) for the frontend. Nothing from it
is deployed any more; the Azure resources were deleted on 8 October 2026.*

Services deploy to Azure Container Apps (genuine always-free monthly grant, scale-to-zero), Postgres to Neon (free tier has no hard expiry or inactivity pause, unlike Render/Supabase), and the Blazor WASM frontend to Azure Static Web Apps (permanent free tier, custom domain+SSL). Fly.io, Railway, and Render were considered and rejected: their current free tiers are either discontinued, too thin for continuous hosting, or have a database expiry that doesn't suit a persistent portfolio demo.
