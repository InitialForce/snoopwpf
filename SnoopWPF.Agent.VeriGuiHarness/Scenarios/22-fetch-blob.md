# Scenario: fetch_blob — download a resource blob

Fetch a blob by the URI returned from a prior screenshot call.

```capture_screenshot
{"locator": "//Window[1]"}
```

```fetch_blob
{"uri": "$result.uri"}
```
