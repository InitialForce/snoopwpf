# Scenario: pump_until_idle — wait for dispatcher idle after action

Click a button then pump until idle to ensure animations settle.

```click
{"locator": "//Button[@Name='LoadDataButton']"}
```

```pump_until_idle
{"timeoutMs": 2000}
```

```get_properties
{"locator": "//ProgressBar[@Name='LoadProgress']", "properties": ["Value"]}
```
