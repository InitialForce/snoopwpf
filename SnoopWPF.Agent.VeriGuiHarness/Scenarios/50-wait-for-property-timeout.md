# Scenario: wait_for_property — short timeout returns gracefully

Wait for an unreachable property value; expect graceful timeout.

```wait_for_property
{"locator": "//TextBlock[@Name='StatusText']", "property": "Text", "expectedValue": "never", "timeoutMs": 500}
```
