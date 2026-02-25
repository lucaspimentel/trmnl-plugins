# MBTA Alerts

A [TRMNL](https://usetrmnl.com/) plugin that displays current service alerts from the Massachusetts Bay Transportation Authority (MBTA), filtered to subway and light rail routes.

![MBTA Alerts screenshot](screenshot.png)

## Features

- Displays active MBTA service alerts sorted by severity
- Filtered to subway (Heavy Rail) and light rail routes only
- Shows service effect, timeframe, alert details, and last updated time
- Gracefully handles empty state ("No current alerts")
- Automatically truncates long lists with "and N more" indicator

## Data Source

[MBTA V3 API](https://api-v3.mbta.com/) — free, no API key required.

## Setup

Install as a private plugin on [TRMNL](https://usetrmnl.com/). The plugin polls the MBTA API every 30 minutes.
