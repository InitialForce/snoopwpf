# Scenario: set_text_value → wait_for_property — type and wait for validation

Type into a search box and wait for the results panel to become visible.

```set_text_value
{"locator": "//TextBox[@Name='SearchBox']", "value": "test query"}
```

```wait_for_property
{"locator": "//ListView[@Name='Results']", "property": "Visibility", "expectedValue": "Visible", "timeoutMs": 3000}
```
