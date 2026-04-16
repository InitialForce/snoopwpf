# Scenario: set_check_state — check a CheckBox

Set a CheckBox to checked state and confirm via get_properties.

```set_check_state
{"locator": "//CheckBox[@Name='EnableFeature']", "state": "Checked"}
```

```get_properties
{"locator": "//CheckBox[@Name='EnableFeature']", "properties": ["IsChecked"]}
```
