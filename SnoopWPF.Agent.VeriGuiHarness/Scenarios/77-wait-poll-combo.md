# Scenario: wait_for_property → poll_changes — double-sync pattern

Wait for element to appear, then poll for its final value.

```wait_for_property
{"locator": "//TextBlock[@Name='AsyncResult']", "property": "Visibility", "expectedValue": "Visible", "timeoutMs": 4000}
```

```poll_changes
{"locator": "//TextBlock[@Name='AsyncResult']", "properties": ["Text"], "intervalMs": 200, "maxPollCount": 10}
```
