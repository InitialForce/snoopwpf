# Scenario: set_check_state — uncheck a CheckBox

Set a previously-checked CheckBox to unchecked.

```set_check_state
{"locator": "//CheckBox[@Name='EnableFeature']", "state": "Unchecked"}
```

```get_properties
{"locator": "//CheckBox[@Name='EnableFeature']", "properties": ["IsChecked"]}
```
