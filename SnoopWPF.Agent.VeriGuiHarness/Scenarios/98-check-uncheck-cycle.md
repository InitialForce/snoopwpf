# Scenario: set_check_state × 3 — full check/uncheck/indeterminate cycle

Cycle a three-state CheckBox through all three states.

```set_check_state
{"locator": "//CheckBox[@Name='TristateBox']", "state": "Checked"}
```

```set_check_state
{"locator": "//CheckBox[@Name='TristateBox']", "state": "Indeterminate"}
```

```set_check_state
{"locator": "//CheckBox[@Name='TristateBox']", "state": "Unchecked"}
```

```get_properties
{"locator": "//CheckBox[@Name='TristateBox']", "properties": ["IsChecked"]}
```
