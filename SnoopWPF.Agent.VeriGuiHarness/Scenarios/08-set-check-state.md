# Scenario: set_check_state — check a CheckBox

Set a CheckBox to checked and verify IsChecked.

```set_check_state
{"locator": "//CheckBox[@Name='AgreeCheck']", "isChecked": true}
```

```get_properties
{"locator": "//CheckBox[@Name='AgreeCheck']", "properties": ["IsChecked"]}
```
