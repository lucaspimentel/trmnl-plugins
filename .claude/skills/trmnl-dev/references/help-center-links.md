# TRMNL Help Center: general plugin-development articles

Curated from the [Plugin Guides collection](https://help.trmnl.com/en/collections/7820559-plugin-guides).
Kept here: articles about general plugin-development concepts and techniques. Left out: setup guides
for one specific third-party integration (Shopify, Notion, Weather, etc.), which are worth reaching
for from the collection when that integration is actually the task.

Append `.md` to any `help.trmnl.com/en/articles/...` URL for a leaner Markdown version. Every link
below was checked and resolves; a help article can be a *stub* that defers to `docs.trmnl.com`, and
those are marked.

`SKILL.md` routes here from its source map. Two entries also appear there directly, because they
answer a question often enough to be worth naming at the top level: the Form Builder and the
custom filters.

## Plugin architecture & workflow

- Private Plugins — https://help.trmnl.com/en/articles/9510536-private-plugins.md
- Compare Custom Plugin Types — https://help.trmnl.com/en/articles/10546870-compare-custom-plugin-types.md
- Importing and Exporting Private Plugins — https://help.trmnl.com/en/articles/10542599-importing-and-exporting-private-plugins.md
- Import: zip file does not contain settings.yml — https://help.trmnl.com/en/articles/12687284-import-the-zip-file-does-not-contain-settings-yml.md
- Syncing Plugins with GitHub (the trmnlp CLI workflow) — https://help.trmnl.com/en/articles/13465101-syncing-plugins-with-github.md
- GitHub Sync (the web editor's own versioning) — https://help.trmnl.com/en/articles/15977899-github-sync.md
- Plugin Markup Version Control (browser-local undo/redo) — https://help.trmnl.com/en/articles/14136474-plugin-markup-version-control.md
- Serverless (`serverless_language` in settings.yml) — https://help.trmnl.com/en/articles/14130649-serverless.md
- Demo Data for Publishing Plugins — https://help.trmnl.com/en/articles/12772238-demo-data-for-publishing-plugins.md
- Plugin Composer — https://help.trmnl.com/en/articles/12881005-plugin-composer.md

## Liquid & templating

- Liquid 101 — https://help.trmnl.com/en/articles/10671186-liquid-101.md
- Advanced Liquid — https://help.trmnl.com/en/articles/10693981-advanced-liquid.md
- Custom Plugin Filters — https://help.trmnl.com/en/articles/10347358-custom-plugin-filters.md
- Custom Plugin Form Builder — https://help.trmnl.com/en/articles/10513740-custom-plugin-form-builder.md
- Reusing Markup with Shared — https://help.trmnl.com/en/articles/13216853-reusing-markup-with-shared.md
- Skipping Screens within Plugin Markup — https://help.trmnl.com/en/articles/13615138-skipping-screens-within-plugin-markup.md
- Parsing plugins with the Sandbox Runtime — https://help.trmnl.com/en/articles/12996946-parsing-plugins-with-the-sandbox-runtime.md (stub; see the "Sandbox Transform (advanced)" section of `settings-yml.md`)
- Using Google Sheets with Private Plugins — https://help.trmnl.com/en/articles/11400219-using-google-sheets-with-private-plugins.md
  — an exception to the rule above: it is a technique for polling a sheet as a data source, not a setup guide

## Debugging & polling

- Debugging Native Plugins — https://help.trmnl.com/en/articles/11135276-debugging-native-plugins.md
- Debugging Private Plugins — https://help.trmnl.com/en/articles/11586187-debugging-private-plugins.md
- Testing Your Alias or Redirect Plugin — https://help.trmnl.com/en/articles/11628971-testing-your-alias-or-redirect-plugin.md
- Plugin in a Degraded State (Reset) — https://help.trmnl.com/en/articles/12384091-plugin-in-a-degraded-state-reset.md
- Missing Data in Multiple Polling URLs — https://help.trmnl.com/en/articles/12385769-missing-data-in-multiple-polling-urls.md
- Plugin Not Receiving Data from Polling URL — https://help.trmnl.com/en/articles/12386583-plugin-not-receiving-data-from-polling-url.md
- Plugins::Base.process! → StandardError: private_plugin — https://help.trmnl.com/en/articles/12814634-plugins-base-process-standarderror-private_plugin.md

## Framework/CSS & design

- Framework Design Docs — https://help.trmnl.com/en/articles/12410486-framework-design-docs.md
- What fonts are used in Framework? — https://help.trmnl.com/en/articles/12494341-what-fonts-are-used-in-framework.md
- Grayscale: 1-bit, 2-bit, 4-bit in Framework — https://help.trmnl.com/en/articles/12386214-grayscale-1-bit-2-bit-4-bit-in-framework.md
- Recipe Best Practices — https://help.trmnl.com/en/articles/11395668-recipe-best-practices.md
- Weather Icons — https://help.trmnl.com/en/articles/11823386-weather-icons.md
- Creating Inline Images for Plugins — https://help.trmnl.com/en/articles/12391781-creating-inline-images-for-plugins.md
- Image Display — https://help.trmnl.com/en/articles/11479051-image-display.md
- Mashups — https://help.trmnl.com/en/articles/10168132-mashups.md
- Understanding Color Palettes — https://help.trmnl.com/en/articles/12985974-understanding-color-palettes.md
- Using Data Mode to Redesign a Native Plugin — https://help.trmnl.com/en/articles/12306729-using-data-mode-to-redesign-a-native-plugin.md
  — **stub**: it defers to the Plugin Data API docs, which `docs.trmnl.com/go/llms-full.txt` already carries
- Screen Wiper — https://help.trmnl.com/en/articles/16693695-screen-wiper.md

## Discovery

- API Catalog — https://help.trmnl.com/en/articles/14136603-api-catalog.md
- Is there a plugin for... — https://help.trmnl.com/en/articles/12814732-is-there-a-plugin-for.md
