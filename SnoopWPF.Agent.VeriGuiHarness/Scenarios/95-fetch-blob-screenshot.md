# Scenario: fetch_blob — retrieve screenshot bytes for comparison

Take a scoped screenshot and fetch its raw bytes.

```capture_screenshot
{"locator": "//Border[@Name='ChartArea']"}
```

```fetch_blob
{"uri": "$result.uri"}
```
