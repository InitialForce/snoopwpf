# Scenario: screenshot before and after — visual diff baseline

Capture before state, click, pump, capture after state.

```capture_screenshot
{"locator": "//Window[1]"}
```

```click
{"locator": "//Button[@Name='ActionBtn']"}
```

```pump_until_idle
{"timeoutMs": 1000}
```

```capture_screenshot
{"locator": "//Window[1]"}
```
