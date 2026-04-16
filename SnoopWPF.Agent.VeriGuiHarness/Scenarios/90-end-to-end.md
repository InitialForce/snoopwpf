# Scenario: end-to-end — session info through action and verify

Full pipeline: session, windows, visual tree, action, verify.

```get_session_info
{}
```

```get_windows
{}
```

```get_visual_tree
{"locator": "//Window[1]", "depth": 1}
```

```click
{"locator": "//Button[@Name='PrimaryAction']"}
```

```pump_until_idle
{"timeoutMs": 2000}
```

```get_properties
{"locator": "//TextBlock[@Name='ResultMsg']", "properties": ["Text"]}
```
