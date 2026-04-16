# Scenario: toggle — flip a ToggleButton state

Toggle a ToggleButton and verify IsChecked flips.

```get_properties
{"locator": "//ToggleButton[@Name='FilterToggle']", "properties": ["IsChecked"]}
```

```toggle
{"locator": "//ToggleButton[@Name='FilterToggle']"}
```

```get_properties
{"locator": "//ToggleButton[@Name='FilterToggle']", "properties": ["IsChecked"]}
```
