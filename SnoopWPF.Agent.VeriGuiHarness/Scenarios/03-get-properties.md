# Scenario: get_properties — read IsEnabled on a Button

Retrieve the IsEnabled property of a named button.

```find_elements
{"locator": "//Button[@Name='OkButton']"}
```

```get_properties
{"locator": "//Button[@Name='OkButton']", "properties": ["IsEnabled"]}
```
