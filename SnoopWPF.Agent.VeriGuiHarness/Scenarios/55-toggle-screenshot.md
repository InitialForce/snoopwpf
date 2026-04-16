# Scenario: toggle → screenshot — toggle dark mode then capture

Toggle dark mode switch and capture updated window.

```toggle
{"locator": "//ToggleButton[@Name='DarkModeToggle']"}
```

```pump_until_idle
{"timeoutMs": 500}
```

```capture_screenshot
{"locator": "//Window[1]"}
```
