# Scenario: set_text_value — clear a TextBox

Clear a TextBox by setting value to empty string.

```set_text_value
{"locator": "//TextBox[@Name='FilterBox']", "value": ""}
```

```get_properties
{"locator": "//TextBox[@Name='FilterBox']", "properties": ["Text"]}
```
