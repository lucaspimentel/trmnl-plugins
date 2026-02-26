---
name: block-manual-zip-deploy
enabled: true
event: bash
pattern: zip.*publish|zip.*deploy
action: block
---

**Use Azure Functions Core Tools to deploy, not manual zip**

Deploy Azure Functions with `func`:

```bash
cd plugins/weather/api/src/WeatherProxy
func azure functionapp publish trmnl-weather
```

Do not manually zip the publish folder and upload it. The `func` tooling handles build, packaging, and deployment in one step.
