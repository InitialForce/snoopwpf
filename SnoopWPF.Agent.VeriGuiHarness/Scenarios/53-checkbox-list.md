# Scenario: composite checkbox list — check all options

Check three checkboxes and verify their state.

```set_check_state
{"locator": "//CheckBox[@Name='OptA']", "state": "Checked"}
```

```set_check_state
{"locator": "//CheckBox[@Name='OptB']", "state": "Checked"}
```

```set_check_state
{"locator": "//CheckBox[@Name='OptC']", "state": "Checked"}
```

```find_elements
{"locator": "//CheckBox[@IsChecked='True']"}
```
