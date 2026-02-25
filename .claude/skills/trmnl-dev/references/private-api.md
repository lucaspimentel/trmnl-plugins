# TRMNL Private API Reference

Full OpenAPI spec: https://trmnl.com/api-docs/index.html
Official Ruby client: https://github.com/usetrmnl/trmnl-api

---

## Authentication

Two auth schemes depending on endpoint type:

| Scheme | Header | Value | Used for |
|--------|--------|-------|----------|
| Device API | `Access-Token` | Device API key (from Devices > Edit) | Device/display endpoints |
| Account API | `Authorization` | `Bearer user_xxxxx` | Account-level endpoints (user_, plugins, playlists) |

Device API key: obtain from **trmnl.com/devices > Edit**.
Account API key: obtain from **trmnl.com/account settings**, prefixed with `user_`.

---

## Device Endpoints

### `GET /api/display`
Fetch the next screen image and advance the playlist queue.

**Headers:** `Access-Token` (required), plus optional device telemetry headers:
`Battery-Voltage`, `Percent-Charged`, `FW-Version`, `RSSI`, `Height`, `Width`

**Response:**
```json
{
  "status": 0,
  "image_url": "https://trmnl.s3.us-east-2.amazonaws.com/path-to-img.png",
  "image_name": "plugin-2024-01-05-T12-00-00Z-abc123",
  "update_firmware": false,
  "firmware_url": null,
  "refresh_rate": "1800",
  "reset_firmware": false
}
```
`status`: 0 = success, 202 = no user attached.

---

### `GET /api/display/current`
Fetch the currently displayed screen without advancing the queue.

**Headers:** `Access-Token` (required)

**Response:**
```json
{
  "status": 200,
  "refresh_rate": 1800,
  "image_url": "https://trmnl.com/rails/active_storage/blobs/...",
  "filename": "plugin-2024-01-05-T12-00-00Z-abc123",
  "rendered_at": null
}
```

---

### `POST /api/log`
Send device log entries.

**Headers:** `Access-Token` (required)
**Body:** `{ "logs": [...] }`
**Response:** 204 No Content

---

### `GET /api/setup`
Register/set up a new device.

**Headers:** `ID` (MAC address), `Model` (required)
**Response:** `{ "status": ..., "api_key": "...", "friendly_id": "...", "image_url": "...", "message": "..." }`

---

## Account Endpoints

All require `Authorization: Bearer user_xxxxx` header.

### `GET /api/me`
Get the authenticated user's account data.

**Response:** `{ "data": { ...User } }`

---

### `GET /api/devices`
List all devices for the authenticated user.

**Response:** `{ "data": [ ...Device ] }`
Each device includes: `id`, `name`, `friendly_id`, MAC address, `battery_voltage`, `wifi_strength` (RSSI).

---

### `GET /api/devices/{id}`
Get a single device by ID.

---

### `PATCH /api/devices/{id}`
Update device settings.

**Body fields:** `sleep_mode_enabled`, `sleep_start_time`, `sleep_end_time`, `percent_charged`

---

### `GET /api/playlists/items`
List all playlist items.

**Response:** `{ "data": [ ...PlaylistItem ] }`

---

### `PATCH /api/playlists/items/{id}`
Show or hide a playlist item (eyeball toggle).

**Body:** `{ "visible": true }`

---

## Plugin Settings Endpoints

### `GET /api/plugin_settings`
List plugin settings, optionally filtered by plugin.

**Query:** `plugin_id` (optional)
**Response:** `{ "data": [ ...PluginSetting ] }`

---

### `POST /api/plugin_settings`
Create a new plugin setting instance.

**Body:** `{ "name": "...", "plugin_id": 123 }`
**Response:** `{ "data": { ...PluginSetting } }`

---

### `DELETE /api/plugin_settings/{id}`
Delete a plugin setting instance.

**Response:** 204 No Content

---

### `GET /api/plugin_settings/{id}/data`
Get the current merge variables for a plugin setting.

**Auth:** Bearer token (optional for public recipes)
**Response:** `{ "data": { ...merge_variables } }`

---

### `POST /api/plugin_settings/{id}/data`
Push new data to a plugin setting (webhook equivalent).

**Auth:** Bearer token (optional if using UUID)
**Body:**
```json
{
  "merge_variables": { "key": "value" },
  "merge_strategy": "deep_merge"
}
```
**Response:** `{ "data": { ...updated_merge_variables } }`

Merge strategies:
- `deep_merge` (default) — merges nested key/value pairs
- `stream` — appends to top-level arrays; use `stream_limit` to cap array length

---

### `GET /api/plugin_settings/{id}/archive`
Download a plugin archive file.

---

### `POST /api/plugin_settings/{id}/archive`
Upload a plugin archive (multipart/form-data).

**Response:** `{ "data": { ...PluginSettingArchive } }`

---

### `POST /api/plugin_settings/{id}/image`
Upload a webhook image for display.

**Body:** image file
**Response:** `{ "data": { "message": "..." } }`

---

## Webhook Endpoint

### `POST /api/custom_plugins/{PLUGIN_SETTINGS_UUID}`
Push merge variables to a private plugin. UUID is found in the plugin's webhook URL field (must save the plugin first to generate one).

**Body:**
```json
{
  "merge_variables": {
    "temperature": 72,
    "sensor": { "humidity": 45 }
  }
}
```

**With deep merge:**
```json
{
  "merge_variables": { "sensor": { "temperature": 42 } },
  "merge_strategy": "deep_merge"
}
```

**With stream (append to array, cap at N):**
```json
{
  "merge_variables": { "temperatures": [40, 42] },
  "merge_strategy": "stream",
  "stream_limit": 10
}
```

**Rate limits:**
- Standard: 12 requests/hour, 2kb payload
- TRMNL+: 30 requests/hour, 5kb payload
- Exceeding limits returns `429`

### `GET /api/custom_plugins/{PLUGIN_SETTINGS_UUID}`
Retrieve current merge variables for a private plugin.

---

## Public Endpoints (no auth)

### `POST /api/markup`
Render a Liquid template server-side.

**Body:** `{ "markup": "<template string>", "variables": { "key": "value" } }`
**Response:** `{ "data": "<rendered HTML>" }`

Useful for testing template rendering without a full plugin.

---

### `GET /api/models`
List all supported device models.

### `GET /api/categories`
List plugin categories.

### `GET /api/palettes`
List color palettes.

### `GET /api/ips`
List TRMNL server IP addresses (ipv4 + ipv6).
