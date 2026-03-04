# Trmnl.Cli

Command-line tool that fetches your [TRMNL](https://usetrmnl.com) e-ink display's current screen image and renders it in the terminal using [Sixel](https://en.wikipedia.org/wiki/Sixel) graphics.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A terminal with Sixel support (e.g. Windows Terminal, WezTerm, foot, mlterm)

## Usage

```bash
cd tools/Trmnl.Cli
dotnet run -- [options]
```

### Options

| Option | Alias | Description | Default |
|---|---|---|---|
| `--device-id` | `-d` | Device ID (or set `TRMNL_DEVICE_ID` env var) | — |
| `--api-key` | `-k` | Device API key (or set `TRMNL_DEVICE_API_KEY` env var) | — |
| `--input` | `-i` | Display a local image file (no API calls) | — |
| `--output` | `-o` | Save image to file and exit (no display) | — |
| `--refresh` | `-r` | Refresh interval in minutes (display mode only) | 15 |

### Examples

```bash
# Display mode with env vars, refresh every 15 minutes
export TRMNL_DEVICE_ID=your-device-id
export TRMNL_DEVICE_API_KEY=your-api-key
dotnet run

# Display mode with explicit credentials and 5-minute refresh
dotnet run -- -d your-device-id -k your-api-key -r 5

# Save current screen to a file
dotnet run -- -d your-device-id -k your-api-key -o screen.png

# Display a local image file
dotnet run -- -i screen.png
```

### Display mode keybindings

| Key | Action |
|---|---|
| **N** | Toggle night mode (inverted colors) |
| **R** | Refresh image (re-fetch from API or re-read file) |
| **Q** / **Esc** / **Ctrl+C** | Exit |
