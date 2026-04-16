# Scenario: poll_changes — baseline with no action (idle check)

Poll a static TextBlock expecting no changes.

```poll_changes
{"locator": "//TextBlock[@Name='VersionLabel']", "properties": ["Text"], "intervalMs": 200, "maxPollCount": 3}
```
