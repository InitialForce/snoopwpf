# Scenario: click → poll_changes — click and observe property changes

Click a counter button and poll for increment.

```click
{"locator": "//Button[@Name='IncrementBtn']"}
```

```poll_changes
{"locator": "//TextBlock[@Name='CounterText']", "properties": ["Text"], "intervalMs": 100, "maxPollCount": 5}
```
