# Weather

A [TRMNL](https://usetrmnl.com/) plugin that displays current conditions, a 24-hour temperature chart, and a multi-day forecast with weather icons.

![Weather screenshot](screenshot.png)

## Features

- Current conditions: temperature (°F and °C), feels like, humidity, wind speed/direction, weather icon
- 24-hour chart: temperature spline + precipitation probability bars (Highcharts)
- Weather icons on the hourly chart x-axis with day/night variants
- Sunrise and sunset times marked as dashed vertical lines on the chart
- Multi-day forecast with temperature range bars and weather icons
- Configurable latitude/longitude (defaults to Boston, MA)

## Data Source

[Open-Meteo API](https://open-meteo.com/) — free, no API key required.

## Setup

Install as a private plugin on [TRMNL](https://usetrmnl.com/). Configure your location by setting the **Latitude** and **Longitude** fields in the plugin settings. The plugin polls the API every 30 minutes.
