# Scenario: click — open ContextMenu via right-click gesture

Right-click a control to open its context menu.

```click
{"locator": "//ListBoxItem[@Name='Item1']", "button": "Right"}
```

```pump_until_idle
{"timeoutMs": 500}
```

```find_elements
{"locator": "//ContextMenu"}
```
