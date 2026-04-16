# Scenario: capture_screenshot → fetch_blob — screenshot and download

Capture the main window then fetch the PNG blob bytes.

```capture_screenshot
{"locator": "//Window[1]", "format": "png"}
```

```fetch_blob
{"uri": "$result.uri"}
```
