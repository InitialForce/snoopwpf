# Scenario: mega composite — all tool families in one scenario

Exercises observe, act, sync, extract, and utility tools together.

```get_session_info
{}
```

```get_windows
{}
```

```find_elements
{"locator": "//TextBox"}
```

```set_text_value
{"locator": "//TextBox[1]", "value": "mega test"}
```

```pump_until_idle
{"timeoutMs": 500}
```

```wait_for_property
{"locator": "//TextBox[1]", "property": "Text", "expectedValue": "mega test", "timeoutMs": 1000}
```

```resolve_binding
{"locator": "//TextBox[1]", "property": "Text"}
```

```capture_screenshot
{"locator": "//Window[1]"}
```

```fetch_blob
{"uri": "$result.uri"}
```
