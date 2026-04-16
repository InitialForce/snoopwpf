# Scenario: set_text_value — type into a TextBox

Type a value into a TextBox and confirm via get_properties.

```set_text_value
{"locator": "//TextBox[@Name='SearchBox']", "value": "hello"}
```

```get_properties
{"locator": "//TextBox[@Name='SearchBox']", "properties": ["Text"]}
```
