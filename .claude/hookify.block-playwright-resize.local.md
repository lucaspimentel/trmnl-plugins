---
name: block-playwright-resize
enabled: true
event: bash
pattern: playwright-cli\s+resize
action: block
---

**Use build-preview.sh instead of playwright-cli resize**

This project uses `tools/build-preview.sh` which handles viewport sizing automatically:

```bash
bash tools/build-preview.sh plugins/<name> --screenshot
```

Do not call `playwright-cli resize` manually. The build script runs trmnlp build, wraps the output, and takes a correctly-sized screenshot in one step.
