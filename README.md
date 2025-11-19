# trmnl-plugins

Plugins for [TRMNL](https://usetrmnl.com/), an e-ink display device.

## Plugins

- **[mbta-alerts](./mbta-alerts)** - Displays service alerts from the Massachusetts Bay Transportation Authority (MBTA)

## Plugin Structure

Each plugin directory contains:
- `settings.yml` - Configuration (API endpoint, refresh interval, metadata)
- `shared.liquid` - Reusable Liquid templates
- Layout templates (`full.liquid`, `half_horizontal.liquid`, `half_vertical.liquid`, `quadrant.liquid`)
- `fields.txt` - Documentation of API data fields

## Resources

- [TRMNL Website](https://usetrmnl.com/)
- [TRMNL Documentation](https://docs.usetrmnl.com/)
