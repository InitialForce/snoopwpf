# Scenario: wait_for_property — wait until a property reaches a value

Trigger load, then wait for IsLoading to become False.

```click
{"locator": "//Button[@Name='LoadDataButton']"}
```

```wait_for_property
{"locator": "//Window[1]", "property": "IsLoading", "expectedValue": false, "timeoutMs": 5000}
```
