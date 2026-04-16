# Scenario: poll_changes — detect property change after action

Poll a TextBlock for Text changes after a button click.

```click
{"locator": "//Button[@Name='RefreshBtn']"}
```

```poll_changes
{"locator": "//TextBlock[@Name='ResultText']", "properties": ["Text"], "intervalMs": 200, "maxPollCount": 10}
```
