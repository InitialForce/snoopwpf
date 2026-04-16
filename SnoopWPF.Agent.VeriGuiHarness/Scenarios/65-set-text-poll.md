# Scenario: set_text_value → poll_changes — type and watch validation

Type an invalid email and poll for validation error message.

```set_text_value
{"locator": "//TextBox[@Name='EmailBox']", "value": "not-an-email"}
```

```poll_changes
{"locator": "//TextBlock[@Name='ValidationMsg']", "properties": ["Text","Visibility"], "intervalMs": 150, "maxPollCount": 8}
```
