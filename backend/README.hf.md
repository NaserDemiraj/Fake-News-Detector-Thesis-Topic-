---
title: Fake News Detector API
emoji: 📰
colorFrom: blue
colorTo: indigo
sdk: docker
app_port: 7860
pinned: false
---

# Fake News Detector — Backend API

.NET 8 ASP.NET Core backend for the Fake News Detector.

This Space runs the API as a Docker container. Configure these in
**Settings → Variables and secrets** (do NOT commit them):

Variables (public):
- `PORT` = `7860`
- `ASPNETCORE_ENVIRONMENT` = `Production`
- `AllowedOrigins` = your Vercel frontend URL

Secrets (hidden):
- `Jwt__Key` (random, ≥32 chars)
- `Neon__HttpUrl`, `Neon__ConnectionUri`
- `Groq__ApiKey`, `Tavily__ApiKey`
- `Gemini__ApiKey`, `FactCheck__ApiKey` (optional)

Health check: `/health`
