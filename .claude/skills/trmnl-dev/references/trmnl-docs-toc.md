# TRMNL Documentation — Table of Contents

Full docs index: https://docs.trmnl.com/go/llms.txt
Append `.md` to any URL for a lean Markdown response (e.g. `https://docs.trmnl.com/go/readme.md`).

---

## Overview & Getting Started

| Page | URL | Summary |
|------|-----|---------|
| Welcome / README | [/go/readme.md](https://docs.trmnl.com/go/readme.md) | Introduction to TRMNL — building plugins, connecting hardware |
| How it Works | [/go/how-it-works.md](https://docs.trmnl.com/go/how-it-works.md) | Architectural overview of the TRMNL platform |

---

## Screen Design & Templates

| Page | URL | Summary |
|------|-----|---------|
| Screen Templating | [/go/private-plugins/templates.md](https://docs.trmnl.com/go/private-plugins/templates.md) | TRMNL's native design system for e-ink friendly screens; layout classes, CSS/JS resources |
| Screen Templating (Graphics) | [/go/private-plugins/templates-advanced.md](https://docs.trmnl.com/go/private-plugins/templates-advanced.md) | Custom styling, third-party libraries (Highcharts, Chartkick), data visualization |
| Webhooks | [/go/private-plugins/webhooks.md](https://docs.trmnl.com/go/private-plugins/webhooks.md) | Submit payload data with merge variables to generate custom screens; merge strategies, rate limits |
| Reusing Markup | [/go/reusing-markup.md](https://docs.trmnl.com/go/reusing-markup.md) | DRY markup with `{% template %}` / `{% render %}` tags |

---

## Private API (Developer Edition)

| Page | URL | Summary |
|------|-----|---------|
| Introduction | [/go/private-api/introduction.md](https://docs.trmnl.com/go/private-api/introduction.md) | Authentication (device API key via `Access-Token` header) |
| Display API | [/go/private-api/screens.md](https://docs.trmnl.com/go/private-api/screens.md) | Fetch screen image data without a physical device; `GET /api/display`, `GET /api/display/current` |
| Plugin Data API | [/go/private-api/plugin-data.md](https://docs.trmnl.com/go/private-api/plugin-data.md) | Access parsed plugin JSON for "data only" mode via Plugin Merge strategy |
| Account API | [/go/private-api/account.md](https://docs.trmnl.com/go/private-api/account.md) | Manage devices, plugins, playlists via `Bearer user_xxxxx` auth; OpenAPI spec at trmnl.com/api-docs |
| More Endpoints | [/go/private-api/more-endpoints.md](https://docs.trmnl.com/go/private-api/more-endpoints.md) | Additional customization endpoints; see Swagger docs |

---

## Public API (no auth required)

| Page | URL | Summary |
|------|-----|---------|
| Introduction | [/go/public-api/introduction.md](https://docs.trmnl.com/go/public-api/introduction.md) | Open endpoints requiring no authentication |
| Recipes API | [/go/public-api/recipes-api.md](https://docs.trmnl.com/go/public-api/recipes-api.md) | Search and filter community plugins |
| Categories API | [/go/public-api/categories-api.md](https://docs.trmnl.com/go/public-api/categories-api.md) | Plugin categories for discoverability |

---

## DIY Hardware

| Page | URL | Summary |
|------|-----|---------|
| Introduction | [/go/diy/introduction.md](https://docs.trmnl.com/go/diy/introduction.md) | Running TRMNL on self-hosted hardware |
| BYOD | [/go/diy/byod.md](https://docs.trmnl.com/go/diy/byod.md) | Bring your own device to TRMNL |
| BYOD/S | [/go/diy/byod-s.md](https://docs.trmnl.com/go/diy/byod-s.md) | Run your own server alongside your device |
| BYOS | [/go/diy/byos.md](https://docs.trmnl.com/go/diy/byos.md) | Use TRMNL hardware with a self-hosted server |
| ImageMagick Guide | [/go/diy/imagemagick-guide.md](https://docs.trmnl.com/go/diy/imagemagick-guide.md) | Create device-compatible images using ImageMagick |

---

## Plugin Marketplace

| Page | URL | Summary |
|------|-----|---------|
| Introduction | [/go/plugin-marketplace/introduction.md](https://docs.trmnl.com/go/plugin-marketplace/introduction.md) | Community plugin ecosystem and sharing platform |
| Plugin Creation | [/go/plugin-marketplace/plugin-creation.md](https://docs.trmnl.com/go/plugin-marketplace/plugin-creation.md) | OAuth client setup for marketplace plugins |
| Installation Flow | [/go/plugin-marketplace/plugin-installation-flow.md](https://docs.trmnl.com/go/plugin-marketplace/plugin-installation-flow.md) | OAuth flow between TRMNL and plugin servers |
| Management Flow | [/go/plugin-marketplace/plugin-management-flow.md](https://docs.trmnl.com/go/plugin-marketplace/plugin-management-flow.md) | User plugin management features |
| Screen Generation Flow | [/go/plugin-marketplace/plugin-screen-generation-flow.md](https://docs.trmnl.com/go/plugin-marketplace/plugin-screen-generation-flow.md) | How TRMNL POSTs to your server to get markup; `trmnl` metadata object (user, device, plugin_settings) |
| Uninstallation Flow | [/go/plugin-marketplace/plugin-uninstallation-flow.md](https://docs.trmnl.com/go/plugin-marketplace/plugin-uninstallation-flow.md) | Handle user uninstall requests |
| Going Live | [/go/plugin-marketplace/going-live.md](https://docs.trmnl.com/go/plugin-marketplace/going-live.md) | Submit plugins for public availability |

---

## Partners API

| Page | URL | Summary |
|------|-----|---------|
| Introduction | [/go/partners-api/introduction.md](https://docs.trmnl.com/go/partners-api/introduction.md) | Device provisioning for business partners |
| Getting Started | [/go/partners-api/getting-started.md](https://docs.trmnl.com/go/partners-api/getting-started.md) | Partnership program enrollment |
| Provisioning Devices | [/go/partners-api/provisioning-devices.md](https://docs.trmnl.com/go/partners-api/provisioning-devices.md) | Create devices and discount codes |
