# Scenario: wait_for_property × 2 — wait on two sequential conditions

After navigation, wait for panel visible then loading done.

```click
{"locator": "//MenuItem[@Name='DashboardMenu']"}
```

```wait_for_property
{"locator": "//Grid[@Name='DashboardPanel']", "property": "Visibility", "expectedValue": "Visible", "timeoutMs": 3000}
```

```wait_for_property
{"locator": "//ProgressBar[@Name='DashboardLoader']", "property": "Visibility", "expectedValue": "Collapsed", "timeoutMs": 5000}
```
