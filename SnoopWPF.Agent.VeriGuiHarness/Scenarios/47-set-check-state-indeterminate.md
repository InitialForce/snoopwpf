# Scenario: set_check_state — set CheckBox to Indeterminate

Set a three-state CheckBox to Indeterminate.

```set_check_state
{"locator": "//CheckBox[@Name='PartialSelect']", "state": "Indeterminate"}
```

```get_properties
{"locator": "//CheckBox[@Name='PartialSelect']", "properties": ["IsChecked"]}
```
