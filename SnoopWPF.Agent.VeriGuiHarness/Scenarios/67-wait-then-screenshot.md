# Scenario: wait_for_property → screenshot — wait for ready then capture

Wait for the loading indicator to disappear then screenshot.

```wait_for_property
{"locator": "//ProgressBar[@Name='Spinner']", "property": "Visibility", "expectedValue": "Collapsed", "timeoutMs": 5000}
```

```capture_screenshot
{"locator": "//Window[1]"}
```
